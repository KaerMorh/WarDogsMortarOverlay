param([Parameter(Mandatory=$true)][string]$Setup)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot
$testDir=Join-Path $projectRoot ('artifacts/install-test-'+[guid]::NewGuid().ToString('N'))
$log=Join-Path $projectRoot 'artifacts/installer-verification.txt'
$checks=[Collections.Generic.List[string]]::new()
function Check([bool]$ok,[string]$name){if(!$ok){throw $name};$checks.Add('PASS '+$name)}
function Install {
  $process=Start-Process $Setup -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/TASKS=""',('/DIR="'+$testDir+'"') -WindowStyle Hidden -Wait -PassThru
  Check ($process.ExitCode -eq 0) 'installer exits successfully'
}
try {
  Install
  Check (!(Test-Path "$testDir/UserData")) 'package excludes personal settings and cache'
  Check (!(Test-Path "$testDir/Verification")) 'package excludes developer verification files'
  Check ((Get-FileHash "$testDir/WarDogsOverlay.dll").Hash -eq (Get-FileHash "$projectRoot/app/WarDogsOverlay.dll").Hash) 'installed application matches release build'
  Check ((Get-ChildItem "$testDir/Web/maps/tiles" -File -Recurse).Count -eq 682) 'both offline maps installed completely'
  Start-Process "$testDir/WarDogsOverlay.exe" -ArgumentList '--verify' -WindowStyle Hidden
  $result="$testDir/Verification/integration.txt"
  for($n=0;$n -lt 200 -and !(Test-Path $result);$n++){Start-Sleep -Milliseconds 200}
  Check ((Test-Path $result) -and !(Select-String -Path $result -Pattern '^FAIL' -Quiet) -and (Select-String -Path $result -Pattern 'native WPF and WebView captures exported' -Quiet)) 'installed application integration checks'
  Get-Process WarDogsOverlay -ErrorAction SilentlyContinue | Where-Object Path -eq "$testDir\WarDogsOverlay.exe" | Stop-Process
  $settings="$testDir/UserData/settings.json"
  Set-Content $settings '{"hudOpacity":0.42,"bubbleReadout":false,"bubbleTextColor":"#FFBD87"}'
  $before=(Get-FileHash $settings).Hash
  Install
  Check ((Get-FileHash $settings).Hash -eq $before) 'upgrade preserves saved settings'
  $resolved=(Resolve-Path $testDir).Path
  Check ($resolved.StartsWith((Join-Path $projectRoot 'artifacts')+'\',[StringComparison]::OrdinalIgnoreCase)) 'uninstall test directory stays inside project artifacts'
  $process=Start-Process "$resolved/unins000.exe" -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -WindowStyle Hidden -Wait -PassThru
  Check ($process.ExitCode -eq 0 -and !(Test-Path "$testDir/WarDogsOverlay.exe")) 'uninstaller removes application'
  Check ((Test-Path $settings) -and (Get-FileHash $settings).Hash -eq $before) 'uninstaller preserves user-created settings'
} catch {$checks.Add('FAIL '+$_.Exception.Message);throw} finally {$checks | Set-Content $log;Write-Output ($checks -join "`n")}
