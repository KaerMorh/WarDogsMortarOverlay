param([Parameter(Mandatory=$true)][string]$JobPath)
$ErrorActionPreference='Stop'
$job=$null
$installerLock=$null
try {
    $job=Get-Content -LiteralPath $JobPath -Raw -Encoding UTF8 | ConvertFrom-Json
    [IO.File]::WriteAllText((Join-Path (Split-Path -Parent $JobPath) 'helper.ready'),'ready')
    $parentProcess=Get-Process -Id $job.processId -ErrorAction SilentlyContinue
    if ($parentProcess -and !$parentProcess.WaitForExit(30000)) { throw '软件尚未退出，请关闭后重试。' }
    $appExe=Join-Path $job.installDir 'WarDogsOverlay.exe'
    $other=Get-Process -Name WarDogsOverlay -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $appExe }
    if ($other) { throw '此目录还有其他软件实例正在运行，请全部退出后重试。' }
    if (!(Test-Path -LiteralPath (Join-Path $job.installDir 'unins000.exe'))) { throw '找不到原安装目录，请使用安装包手动修复。' }
    $installerLock=[IO.File]::Open($job.setup,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    $sha=[Security.Cryptography.SHA256]::Create()
    try { $hash=([BitConverter]::ToString($sha.ComputeHash($installerLock))).Replace('-','') } finally { $sha.Dispose() }
    if ($installerLock.Length -ne $job.size -or $hash -ne $job.sha256) { throw '安装包校验失败，请重新下载。' }
    $settings=Join-Path $job.installDir 'UserData\settings.json'
    if (Test-Path -LiteralPath $settings) { Copy-Item -LiteralPath $settings -Destination (Join-Path (Split-Path -Parent $JobPath) 'settings.backup.json') }
    $log=Join-Path (Split-Path -Parent $JobPath) 'install.log'
    # Paths come from JSON, never evaluated as PowerShell code. Windows paths cannot contain double quotes.
    $arguments='/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCLOSEAPPLICATIONS /RESTARTEXITCODE=3010 /DIR="'+$job.installDir+'" /LOG="'+$log+'"'
    $installer=Start-Process -FilePath $job.setup -ArgumentList $arguments -PassThru -Wait -WindowStyle Hidden
    if ($installer.ExitCode -ne 0) { throw ('安装未完成（代码 '+$installer.ExitCode+'）。请查看 '+$log+'，可使用安装包手动修复。') }
    Start-Process -FilePath $appExe -WorkingDirectory $job.installDir -WindowStyle Hidden
} catch {
    $message=$_.Exception.Message
    [IO.File]::WriteAllText((Join-Path (Split-Path -Parent $JobPath) 'update-error.log'),$message)
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show($message,'WarDogs 更新失败') | Out-Null
} finally {
    if ($installerLock) { $installerLock.Dispose() }
}
