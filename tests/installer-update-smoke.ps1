param([Parameter(Mandatory=$true)][string]$OldPayload)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$smokeRoot=Join-Path $projectRoot ('artifacts/update-smoke-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $smokeRoot | Out-Null
$installDir=Join-Path $smokeRoot '安装 路径'
$scriptPath=Join-Path $smokeRoot 'Smoke.iss'
$source=[IO.File]::ReadAllText((Join-Path $projectRoot 'installer/WarDogsOverlay.iss'))
# Separate application identity keeps the user's installed product and shortcuts untouched.
$source=$source.Replace('C5940880-26AF-493E-B639-8535D0981B54',[guid]::NewGuid().ToString().ToUpperInvariant())
$source=$source.Replace('AppName=WarDogs Overlay','AppName=WarDogs Update Smoke Test')
$source=$source.Replace('DefaultGroupName=WarDogs Overlay','DefaultGroupName=WarDogs Update Smoke Test')
$source=$source.Replace('Compression=lzma2','Compression=none').Replace('SolidCompression=yes','SolidCompression=no')
[IO.File]::WriteAllText($scriptPath,$source,[Text.UTF8Encoding]::new($true))
$sdkCompiler=Join-Path $projectRoot '.tools/inno/ISCC.exe'
$bootstrap=Join-Path $projectRoot '.tools/MicrosoftEdgeWebview2Setup.exe'
function Compile-Smoke([string]$version,[string]$payload) {
    & $sdkCompiler /Q "/DAppVersion=$version" "/DPayloadDir=$payload" "/DWebViewBootstrapper=$bootstrap" "/O$smokeRoot" $scriptPath
    if($LASTEXITCODE -ne 0) { throw 'Smoke installer compilation failed' }
    return Join-Path $smokeRoot "WarDogsOverlay-$version-win-x64-Setup.exe"
}
$oldSetup=Compile-Smoke '0.5.0' (Resolve-Path -LiteralPath $OldPayload).Path
# Use the release payload, which already excludes private user data and debugging output.
$newPayload=Get-ChildItem -LiteralPath (Join-Path $projectRoot 'artifacts') -Directory -Filter 'payload-0.6.0-*' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if(!$newPayload) { throw 'Build the 0.6.0 release installer first.' }
$newSetup=Compile-Smoke '0.6.0' $newPayload.FullName
$installed=Start-Process -FilePath $oldSetup -ArgumentList ('/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOICONS /DIR="'+$installDir+'" /LOG="'+(Join-Path $smokeRoot 'old-install.log')+'"') -PassThru -Wait -WindowStyle Hidden
if($installed.ExitCode -ne 0) { throw 'Old-version installation failed' }
$appExe=Join-Path $installDir 'WarDogsOverlay.exe'
if((Get-Item -LiteralPath $appExe).VersionInfo.ProductVersion -notlike '0.5.0*') { throw 'Old version not installed' }
New-Item -ItemType Directory -Path (Join-Path $installDir 'UserData') -Force | Out-Null
$settings=Join-Path $installDir 'UserData/settings.json'
# Disable real network checks and retain a distinctive user setting through the upgrade.
$settingsText='{"autoCheckUpdates":false,"bubbleTextColor":"#ABCDEF","hudForm":"compact"}'
[IO.File]::WriteAllText($settings,$settingsText,[Text.UTF8Encoding]::new($false))
$sentinel=Join-Path $installDir 'UserData/keep-me.txt'
[IO.File]::WriteAllText($sentinel,'preserve this user file')
$parent=Start-Process -FilePath (Join-Path $PSHOME 'pwsh.exe') -ArgumentList '-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 5' -PassThru -WindowStyle Hidden
$jobPath=Join-Path $smokeRoot 'job.json'
$job=@{ processId=$parent.Id; setup=$newSetup; installDir=$installDir; size=(Get-Item -LiteralPath $newSetup).Length; sha256=(Get-FileHash -LiteralPath $newSetup -Algorithm SHA256).Hash }
[IO.File]::WriteAllText($jobPath,($job|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
$helper=Start-Process -FilePath (Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe') -ArgumentList ('-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+(Join-Path $projectRoot 'src/ApplyUpdate.ps1')+'" -JobPath "'+$jobPath+'"') -PassThru -WindowStyle Hidden
if(!$helper.WaitForExit(60000)) { throw "Update helper timed out; inspect $smokeRoot" }
if(Test-Path -LiteralPath (Join-Path $smokeRoot 'update-error.log')) { throw (Get-Content -LiteralPath (Join-Path $smokeRoot 'update-error.log') -Raw) }
if((Get-Item -LiteralPath $appExe).VersionInfo.ProductVersion -notlike '0.6.0*') { throw 'Updated version missing' }
if([IO.File]::ReadAllText((Join-Path $smokeRoot 'settings.backup.json')) -ne $settingsText) { throw 'Settings backup differs' }
if(!(Test-Path -LiteralPath $sentinel)) { throw 'UserData was removed' }
$deadline=[DateTime]::UtcNow.AddSeconds(10)
do { $restarted=Get-Process -Name WarDogsOverlay -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $appExe }; if(!$restarted){Start-Sleep -Milliseconds 200} } while(!$restarted -and [DateTime]::UtcNow -lt $deadline)
if(!$restarted) { throw 'Updated application was not restarted' }
Start-Sleep -Milliseconds 1200
if((Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json).bubbleTextColor -ne '#ABCDEF') { throw 'User setting not preserved' }
# Only stop the test process from this exact isolated path; leave other installations running.
$restarted | Stop-Process -Force
$uninstall=Start-Process -FilePath (Join-Path $installDir 'unins000.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -PassThru -Wait -WindowStyle Hidden
if($uninstall.ExitCode -ne 0) { throw 'Smoke-test uninstallation failed' }
$result='PASS 0.5.0 -> 0.6.0: Chinese/space install path, process wait, locked hash verification, settings backup, UserData retention, automatic restart, isolated uninstall.'
[IO.File]::WriteAllText((Join-Path $smokeRoot 'result.txt'),$result)
Write-Output $result
Write-Output "Evidence: $smokeRoot"
