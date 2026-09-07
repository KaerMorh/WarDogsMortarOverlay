param([string]$NotesPath='', [string]$SigningKeyPath='')
$ErrorActionPreference='Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'Use PowerShell 7 (pwsh).' }
[xml]$project=Get-Content "$PSScriptRoot/src/WarDogs.csproj"
$version=$project.Project.PropertyGroup.Version
if (!$NotesPath) { $NotesPath=Join-Path $PSScriptRoot "installer/release-notes-$version.md" }
if (!$SigningKeyPath) { $SigningKeyPath=Join-Path $PSScriptRoot '.tools/update-signing/private.pem' }
$setup=Join-Path $PSScriptRoot "artifacts/releases/WarDogsOverlay-$version-win-x64-Setup.exe"
$manifest=Join-Path $PSScriptRoot 'artifacts/releases/update.json'
$rsa=[Security.Cryptography.RSA]::Create()
$public=[Security.Cryptography.RSA]::Create()
try {
    $rsa.ImportFromPem([IO.File]::ReadAllText($SigningKeyPath))
    $public.ImportFromPem([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'src/update-public.pem')))
    if ([Convert]::ToBase64String($rsa.ExportSubjectPublicKeyInfo()) -ne [Convert]::ToBase64String($public.ExportSubjectPublicKeyInfo())) { throw 'Signing key does not match the embedded update public key.' }
    $data=[ordered]@{
        version=$version
        notes=[IO.File]::ReadAllText($NotesPath).Trim()
        url="https://github.com/KaerMorh/WarDogsMortarOverlay/releases/download/v$version/WarDogsOverlay-$version-win-x64-Setup.exe"
        size=(Get-Item -LiteralPath $setup).Length
        sha256=(Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    [IO.File]::WriteAllText($manifest,($data | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
    $signature=$rsa.SignData([IO.File]::ReadAllBytes($manifest),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
    [IO.File]::WriteAllBytes(($manifest+'.sig'),$signature)
    Write-Output "Signed manifest: $manifest"
} finally { $rsa.Dispose(); $public.Dispose() }
