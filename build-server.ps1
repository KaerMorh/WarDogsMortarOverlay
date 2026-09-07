param([switch]$Linux)
$ErrorActionPreference='Stop'
$projectRoot=$PSScriptRoot
$env:GOPATH=Join-Path $projectRoot '.tools/gopath'
$env:GOCACHE=Join-Path $projectRoot '.tools/gocache'
$go=Join-Path $projectRoot '.tools/go/bin/go.exe'
if (!(Test-Path -LiteralPath $go)) { $go=(Get-Command go -ErrorAction Stop).Source }
Push-Location (Join-Path $projectRoot 'server')
try {
    & $go vet ./...
    if ($LASTEXITCODE -ne 0) { throw 'Go vet failed' }
    & $go test ./... -count=1
    if ($LASTEXITCODE -ne 0) { throw 'Go tests failed' }
    $previousGoOs=$env:GOOS
    $previousGoArch=$env:GOARCH
    $previousCgo=$env:CGO_ENABLED
    try {
        $env:GOARCH='amd64'
        $env:CGO_ENABLED='0'
        if ($Linux) {
            $env:GOOS='linux'
            $target=Join-Path $projectRoot 'artifacts/multiplayer/linux/wardogs-server'
        } else {
            $env:GOOS='windows'
            $target=Join-Path $projectRoot 'artifacts/multiplayer/wardogs-server.exe'
        }
        & $go build -trimpath '-ldflags=-s -w' -o $target ./cmd/wardogs-server
        if ($LASTEXITCODE -ne 0) { throw 'Go server build failed' }
        Get-FileHash -LiteralPath $target -Algorithm SHA256
    } finally {
        $env:GOOS=$previousGoOs
        $env:GOARCH=$previousGoArch
        $env:CGO_ENABLED=$previousCgo
    }
} finally { Pop-Location }
