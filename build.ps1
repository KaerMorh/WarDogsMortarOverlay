$ErrorActionPreference='Stop'
$projectRoot=$PSScriptRoot
$env:DOTNET_CLI_HOME=Join-Path $projectRoot '.tools/cli'
$env:NUGET_PACKAGES=Join-Path $projectRoot '.tools/packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$sdk=Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
& $sdk run --project "$projectRoot/tests/CoreTests.csproj" -- "$projectRoot/src/Data/weapons.json"
if($LASTEXITCODE -ne 0){throw 'Core tests failed'}
& $sdk run --project "$projectRoot/update-tests/UpdateTests.csproj" -c Release
if($LASTEXITCODE -ne 0){throw 'Update tests failed'}
node "$projectRoot/tests/upstream-parity.cjs" "$projectRoot/src/Data"
if($LASTEXITCODE -ne 0){throw 'Upstream parity failed'}
& $sdk publish "$projectRoot/src/WarDogs.csproj" -c Release -r win-x64 --self-contained true -o "$projectRoot/app" --nologo
if($LASTEXITCODE -ne 0){throw 'Publish failed'}
