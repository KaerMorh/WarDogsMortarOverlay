param([string]$Iscc='', [switch]$SkipBuild)
$ErrorActionPreference='Stop'
$projectRoot=$PSScriptRoot
if(!$SkipBuild){& "$projectRoot/build.ps1"}
if(!$Iscc){$Iscc=Join-Path $projectRoot '.tools/inno/ISCC.exe'}
if(!(Test-Path $Iscc)){throw 'Install Inno Setup 6 and supply -Iscc path/to/ISCC.exe'}
[xml]$project=Get-Content "$projectRoot/src/WarDogs.csproj"
$version=$project.Project.PropertyGroup.Version
$payload=Join-Path $projectRoot ('artifacts/payload-'+$version+'-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $payload -Force | Out-Null
$appPath=Join-Path $projectRoot 'app'
foreach($file in Get-ChildItem $appPath -File -Recurse){
  $relative=[IO.Path]::GetRelativePath($appPath,$file.FullName)
  if($relative -match '^(UserData|Verification)[\\/]' -or $relative -match '(\.pdb$|parity-fixtures\.json$)'){continue}
  $destination=Join-Path $payload $relative
  New-Item -ItemType Directory ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
  Copy-Item -LiteralPath $file.FullName -Destination $destination
}
Copy-Item "$projectRoot/README.md" $payload
if(Test-Path "$payload/UserData"){throw 'Private runtime data must never be packaged'}
$bootstrapper=Join-Path $projectRoot '.tools/MicrosoftEdgeWebview2Setup.exe'
if(!(Test-Path $bootstrapper)){Invoke-WebRequest 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $bootstrapper}
$signature=Get-AuthenticodeSignature $bootstrapper
if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation'){throw 'WebView2 bootstrapper signature invalid'}
& $Iscc /Q "/DAppVersion=$version" "/DPayloadDir=$payload" "/DWebViewBootstrapper=$bootstrapper" "$projectRoot/installer/WarDogsOverlay.iss"
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
$setup=Join-Path $projectRoot "artifacts/releases/WarDogsOverlay-$version-win-x64-Setup.exe"
$hash=(Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path ($setup+'.sha256') -Value ($hash+'  '+[IO.Path]::GetFileName($setup)) -Encoding ascii
Write-Output "Installer: $setup"
