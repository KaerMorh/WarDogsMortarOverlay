param([switch]$SkipBuild)
$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
function Assert-Native([string]$action) { if ($LASTEXITCODE -ne 0) { throw "$action failed" } }
[xml]$project=Get-Content 'src/WarDogs.csproj'
$version=$project.Project.PropertyGroup.Version
$tag="v$version"
$repo='KaerMorh/WarDogsMortarOverlay'
$notes=Join-Path $PSScriptRoot "installer/release-notes-$version.md"
$changes=git status --porcelain --untracked-files=no
Assert-Native 'Git status'
if ($changes) { throw 'Commit tracked changes before publishing.' }
gh auth status
Assert-Native 'GitHub authentication'
if (!$SkipBuild) { & "$PSScriptRoot/build-installer.ps1" }
& "$PSScriptRoot/create-update-manifest.ps1" -NotesPath $notes
$commit=git rev-parse HEAD
Assert-Native 'Read commit'
git push origin HEAD
Assert-Native 'Push source'
$existing=gh release list --repo $repo --limit 100 --json tagName | ConvertFrom-Json
Assert-Native 'List releases'
if ($existing.tagName -contains $tag) { throw "Release $tag already exists; inspect it instead of overwriting." }
$setup=Join-Path $PSScriptRoot "artifacts/releases/WarDogsOverlay-$version-win-x64-Setup.exe"
# Upload all assets while draft. Only then advertise the version as latest.
gh release create $tag $setup ($setup+'.sha256') "$PSScriptRoot/artifacts/releases/update.json" "$PSScriptRoot/artifacts/releases/update.json.sig" --repo $repo --target $commit --title "WarDogs Overlay $version" --notes-file $notes --draft
Assert-Native 'Create draft release'
gh release edit $tag --repo $repo --draft=false --latest
Assert-Native 'Publish release'
gh release view $tag --repo $repo --json url,assets
Assert-Native 'Verify release'
