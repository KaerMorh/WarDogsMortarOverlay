using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace WarDogs;

public sealed class UpdateService
{
    readonly Controller controller;
    readonly HttpClient http;
    readonly string? publicKey;
    readonly string cacheRoot;
    CancellationTokenSource? cancellation;
    string? downloaded;
    public event Action? Changed;
    public static string CurrentVersion => typeof(UpdateService).Assembly.GetName().Version!.ToString(3);
    public UpdateManifest? Available { get; private set; }
    public bool HasUpdate => Available != null;
    public bool Busy { get; private set; }
    public bool IsDownloading => cancellation != null;
    public bool Ready => downloaded != null;
    public double Progress { get; private set; }
    public string Status { get; private set; } = "尚未检查更新";
    public bool Installed => File.Exists(Path.Combine(Controller.Root, "unins000.exe"));
    public UpdateService(Controller controller) : this(controller, new HttpClient { Timeout = Timeout.InfiniteTimeSpan }, null) { }
    internal UpdateService(Controller controller, HttpClient http, string? publicKey, string? cacheRoot = null)
    {
        this.controller = controller;
        this.http = http; this.publicKey = publicKey;
        this.cacheRoot = cacheRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WarDogsOverlay", "Updates");
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WarDogsOverlay/" + CurrentVersion);
    }
    void Refresh() => Changed?.Invoke();
    public void Cancel() => cancellation?.Cancel();
    public async Task CheckAsync()
    {
        if (Busy) return;
        Busy = true; Status = "正在检查更新…"; Refresh();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            // Both files can change during publication; signature failure is safe and retryable.
            var bytes = await GetSmallAsync(UpdateManifest.Feed, timeout.Token);
            var signature = await GetSmallAsync(UpdateManifest.Feed + ".sig", timeout.Token);
            var key = publicKey;
            if (key == null)
            {
                using var keyStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WarDogs.UpdatePublicKey")!;
                using var reader = new StreamReader(keyStream);
                key = await reader.ReadToEndAsync(timeout.Token);
            }
            var manifest = UpdateManifest.Verify(bytes, signature, key);
            if (UpdateManifest.ParseVersion(manifest.Version) > UpdateManifest.ParseVersion(CurrentVersion))
            {
                if (Available != manifest) downloaded = null;
                Available = manifest; Status = Ready ? "下载完成，可以重启安装" : "发现新版本 " + manifest.Version;
            }
            else { Available = null; downloaded = null; Status = "当前已是最新版本"; }
        }
        catch (OperationCanceledException) { Status = "检查超时，稍后可重试；当前版本可继续使用"; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or System.Security.Cryptography.CryptographicException or JsonException)
        { Status = "检查失败：" + ex.Message + "；可稍后重试"; }
        finally { Busy = false; Refresh(); }
    }
    async Task<byte[]> GetSmallAsync(string url, CancellationToken token)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(chunk, token)) > 0)
        { if (buffer.Length + read > 65536) throw new InvalidDataException("更新清单过大"); await buffer.WriteAsync(chunk.AsMemory(0, read), token); }
        return buffer.ToArray();
    }
    public async Task DownloadAsync()
    {
        if (Busy || Available is not {} manifest) return;
        if (controller.Demo) { Status = "演示模式不下载或安装更新"; Refresh(); return; }
        if (!Installed) { Status = "当前为便携版，请先通过发布页安装一次安装版"; Refresh(); return; }
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30)); cancellation = timeout;
        Busy = true; downloaded = null; Progress = 0; Status = "正在下载安装包…"; Refresh();
        var directory = Path.Combine(cacheRoot, Guid.NewGuid().ToString("N"));
        var partial = Path.Combine(directory, "Setup.exe.part");
        try
        {
            Directory.CreateDirectory(directory);
            using var response = await http.GetAsync(manifest.Url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is {} length && length != manifest.Size) throw new InvalidDataException("安装包大小不匹配");
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var chunk = new byte[81920]; long total = 0; int read; var last = Stopwatch.StartNew();
                while ((read = await input.ReadAsync(chunk, timeout.Token)) > 0)
                {
                    total += read; if (total > manifest.Size) throw new InvalidDataException("安装包超出预期大小");
                    await output.WriteAsync(chunk.AsMemory(0, read), timeout.Token); Progress = 100d * total / manifest.Size;
                    if (last.ElapsedMilliseconds > 150) { Refresh(); last.Restart(); }
                }
            }
            Status = "正在校验安装包…"; Refresh();
            await manifest.VerifyFileAsync(partial, timeout.Token);
            var target = Path.Combine(directory, "Setup.exe"); File.Move(partial, target);
            downloaded = target; Progress = 100; Status = "下载完成，点击“重启并安装”完成更新";
        }
        catch (OperationCanceledException) { Status = "下载已取消或超时，可重新下载"; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException)
        { Status = "下载失败：" + ex.Message; }
        finally
        {
            try { if (File.Exists(partial)) File.Delete(partial); } catch (IOException) { }
            cancellation = null; Busy = false; Refresh();
        }
    }
    public async Task InstallAsync()
    {
        if (Busy || downloaded is not {} setup || Available is not {} manifest || controller.Demo) return;
        Busy = true; Status = "正在准备安装…"; Refresh();
        try
        {
            await manifest.VerifyFileAsync(setup);
            var directory = Path.GetDirectoryName(setup)!;
            var script = Path.Combine(directory, "ApplyUpdate.ps1");
            File.Copy(Path.Combine(Controller.Root, "ApplyUpdate.ps1"), script, true);
            var job = Path.Combine(directory, "job.json");
            File.WriteAllText(job, JsonSerializer.Serialize(new { processId = Environment.ProcessId, setup, installDir = Path.TrimEndingDirectorySeparator(Controller.Root), sha256 = manifest.Sha256, size = manifest.Size }));
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe")) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-JobPath", job }) start.ArgumentList.Add(arg);
            var ready = Path.Combine(directory, "helper.ready");
            if (File.Exists(ready)) File.Delete(ready);
            using var process = Process.Start(start) ?? throw new IOException("无法启动更新助手");
            for (int attempt = 0; attempt < 50 && !File.Exists(ready); attempt++)
            {
                if (process.HasExited) throw new IOException("更新助手未能启动，可从发布页手动安装");
                await Task.Delay(100);
            }
            if (!File.Exists(ready)) throw new IOException("更新助手启动超时，请稍后重试");
            controller.Quit();
        }
        catch (Exception ex) { Status = "无法安装：" + ex.Message; Busy = false; Refresh(); }
    }
}
