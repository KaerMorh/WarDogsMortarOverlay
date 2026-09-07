param([string]$ServerUrl='')
$ErrorActionPreference='Stop'
$projectRoot=$PSScriptRoot
$env:DOTNET_CLI_HOME=Join-Path $projectRoot '.tools/cli'
$env:NUGET_PACKAGES=Join-Path $projectRoot '.tools/packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:GOPATH=Join-Path $projectRoot '.tools/gopath'
$env:GOCACHE=Join-Path $projectRoot '.tools/gocache'
$sdk=Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
$go=Join-Path $projectRoot '.tools/go/bin/go.exe'
if (!(Test-Path -LiteralPath $go)) { $go=(Get-Command go -ErrorAction Stop).Source }
Push-Location (Join-Path $projectRoot 'server')
try {
    & $go test ./...
    if ($LASTEXITCODE -ne 0) { throw 'Go tests failed' }
    & $go build -trimpath -o (Join-Path $projectRoot 'artifacts/multiplayer/wardogs-server.exe') ./cmd/wardogs-server
    if ($LASTEXITCODE -ne 0) { throw 'Go build failed' }
} finally { Pop-Location }
& $sdk run --project "$projectRoot/tests/CoreTests.csproj" -- "$projectRoot/src/Data/weapons.json"
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }
& $sdk run --project "$projectRoot/update-tests/UpdateTests.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw 'Update tests failed' }
node "$projectRoot/tests/upstream-parity.cjs" "$projectRoot/src/Data"
if ($LASTEXITCODE -ne 0) { throw 'Upstream parity failed' }
if ($ServerUrl) { & $sdk run --project "$projectRoot/multiplayer-tests/MultiplayerTests.csproj" -- $ServerUrl }
else { & $sdk run --project "$projectRoot/multiplayer-tests/MultiplayerTests.csproj" }
if ($LASTEXITCODE -ne 0) { throw 'Multiplayer tests failed' }
& $sdk run --project "$projectRoot/multiplayer-tests/MultiplayerTests.csproj" -- --restart "$projectRoot/artifacts/multiplayer/wardogs-server.exe"
if ($LASTEXITCODE -ne 0) { throw 'Server restart test failed' }
& $sdk publish "$projectRoot/src/WarDogs.csproj" -c Release -r win-x64 --self-contained true -o "$projectRoot/artifacts/multiplayer/client" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Client publish failed' }
