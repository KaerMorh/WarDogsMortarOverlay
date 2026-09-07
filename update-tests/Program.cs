using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using WarDogs;

try
{
if (args.Length == 2 && args[0] == "--instance-probe")
{
    using var probe = new SingleInstance(() => {}, args[1]);
    Environment.ExitCode = probe.IsOwner ? 0 : 2;
    return;
}
int assertions = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); assertions++; }
void Reject(Action action, string message)
{
    try { action(); } catch (InvalidDataException) { assertions++; return; }
    throw new Exception(message);
}
using var signing = RSA.Create(2048);
var key = signing.ExportSubjectPublicKeyInfoPem();
var payload = new byte[128 * 1024 + 13]; RandomNumberGenerator.Fill(payload);
var manifest = new UpdateManifest("0.7.0", "测试更新", UpdateManifest.Repository + "/releases/download/v0.7.0/WarDogsOverlay-0.7.0-win-x64-Setup.exe", payload.Length, Convert.ToHexString(SHA256.HashData(payload)));
byte[] Encode(UpdateManifest value) => JsonSerializer.SerializeToUtf8Bytes(value, UpdateManifest.Json);
byte[] Sign(byte[] bytes) => signing.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
UpdateManifest Verify(UpdateManifest value) { var bytes = Encode(value); return UpdateManifest.Verify(bytes, Sign(bytes), key); }
Check(Verify(manifest) == manifest, "valid signed manifest");
var bytes = Encode(manifest); var signed = Sign(bytes); bytes[^2] ^= 1;
Reject(() => UpdateManifest.Verify(bytes, signed, key), "tampered manifest accepted");
using var otherKey = RSA.Create(2048);
Reject(() => UpdateManifest.Verify(Encode(manifest), Sign(Encode(manifest)), otherKey.ExportSubjectPublicKeyInfoPem()), "wrong signing key accepted");
foreach (var version in new[] { "v0.7.0", "0.7.0-beta", "0.7", "../0.7.0", "0.7.0.1", "999999999999.1.0" })
    Reject(() => UpdateManifest.ParseVersion(version), "invalid version " + version);
Check(UpdateManifest.ParseVersion("0.10.0") > UpdateManifest.ParseVersion("0.9.0"), "numeric version comparison");
foreach (var bad in new[] {
    manifest with { Url = "https://example.com/evil.exe" },
    manifest with { Url = manifest.Url.Replace("https:", "http:") },
    manifest with { Url = manifest.Url + "?payload=1" },
    manifest with { Size = -1 }, manifest with { Size = 3L*1024*1024*1024 },
    manifest with { Sha256 = "bad" }, manifest with { Notes = new string('a', 20001) } })
    Reject(() => Verify(bad), "invalid manifest accepted");

Directory.CreateDirectory(Controller.Root);
var fixture = Path.Combine(Controller.Root, "fixture.bin");
await File.WriteAllBytesAsync(fixture, payload);
await manifest.VerifyFileAsync(fixture); assertions++;
await File.WriteAllBytesAsync(fixture, new byte[payload.Length]);
try { await manifest.VerifyFileAsync(fixture); throw new Exception("damaged payload accepted"); } catch (InvalidDataException) { assertions++; }
await File.WriteAllBytesAsync(fixture, new byte[10]);
try { await manifest.VerifyFileAsync(fixture); throw new Exception("truncated payload accepted"); } catch (InvalidDataException) { assertions++; }

var handler = new FeedHandler();
void SetFeed(UpdateManifest value, byte[]? installer = null)
{
    var content = Encode(value);
    handler.Respond = request => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(
        request.RequestUri!.AbsoluteUri == UpdateManifest.Feed ? content :
        request.RequestUri.AbsoluteUri == UpdateManifest.Feed + ".sig" ? Sign(content) : installer ?? payload) };
}
var service = new UpdateService(new Controller(), new HttpClient(handler), key, Path.Combine(Controller.Root, "Downloads"));
SetFeed(manifest);
await service.CheckAsync();
Check(service.HasUpdate && service.Available == manifest && !service.Busy, "new version available");
await service.DownloadAsync();
Check(!service.Ready && service.Status.Contains("便携版"), "portable installs must not silently switch directory");
File.WriteAllText(Path.Combine(Controller.Root, "unins000.exe"), "installed-test-marker");
SetFeed(manifest, new byte[7]);
await service.DownloadAsync();
Check(!service.Ready && !service.Busy && service.Status.Contains("大小"), "bad download length rejected");
SetFeed(manifest, new byte[payload.Length]);
await service.DownloadAsync();
Check(!service.Ready && service.Status.Contains("校验失败"), "bad download hash rejected");
SetFeed(manifest);
await service.DownloadAsync();
Check(service.Ready && service.Progress == 100 && !service.Busy, "valid download becomes ready");
await service.CheckAsync();
Check(service.Ready, "same feed retains verified download");
handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
await service.CheckAsync();
Check(service.HasUpdate && service.Ready && !service.Busy && service.Status.Contains("检查失败"), "network error preserves known available update");
var same = manifest with { Version = "0.6.0", Url = manifest.Url.Replace("0.7.0", "0.6.0") };
SetFeed(same); await service.CheckAsync();
Check(!service.HasUpdate && !service.Ready, "same version clears notification");
SetFeed(same with { Version = "0.5.0", Url = same.Url.Replace("0.6.0", "0.5.0") }); await service.CheckAsync();
Check(!service.HasUpdate, "downgrade not offered");
handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[65537]) };
await service.CheckAsync(); Check(!service.Busy && service.Status.Contains("过大"), "oversized manifest refused");
SetFeed(manifest); await service.CheckAsync();
handler.Delay = true;
var pending = service.DownloadAsync(); await Task.Delay(20); service.Cancel(); await pending;
Check(!service.Busy && !service.Ready && service.Status.Contains("取消"), "cancel download returns to retryable state");
handler.Delay = false; SetFeed(manifest); await service.DownloadAsync();
Check(service.Ready, "retry after cancellation succeeds");
var cacheRoot = Path.Combine(Controller.Root, "Downloads");
var offline = new FeedHandler { Respond = _ => throw new HttpRequestException("offline") };
var restored = new UpdateService(new Controller(), new HttpClient(offline), key, cacheRoot);
await restored.RestoreAsync();
Check(restored.Ready && restored.Available == manifest, "restart restores signed package without a network request");
await restored.CheckAsync();
Check(restored.Ready, "offline check keeps restored package installable");
var cachedFile = Directory.GetFiles(cacheRoot, "Setup.exe", SearchOption.AllDirectories).Single();
await File.WriteAllBytesAsync(cachedFile, new byte[payload.Length]);
var corrupted = new UpdateService(new Controller(), new HttpClient(offline), key, cacheRoot);
await corrupted.RestoreAsync();
Check(!corrupted.Ready && !File.Exists(cachedFile), "restart rejects and cleans tampered cache");
var legacyDir = Path.Combine(cacheRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(legacyDir);
var legacyFile = Path.Combine(legacyDir, "Setup.exe"); await File.WriteAllBytesAsync(legacyFile, payload);
var legacy = new UpdateService(new Controller(), new HttpClient(handler), key, cacheRoot);
await legacy.RestoreAsync(); Check(!legacy.Ready && File.Exists(legacyFile), "unsigned legacy cache waits for signed online manifest");
await legacy.CheckAsync(); Check(legacy.Ready && File.Exists(Path.Combine(legacyDir, "update.json.sig")), "legacy download is reused and gains signed metadata");
var backup = Path.Combine(legacyDir, "settings.backup.json"); File.WriteAllText(backup, "keep");
var staleDir = Path.Combine(cacheRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staleDir);
var staleFile = Path.Combine(staleDir, "Setup.exe"); File.WriteAllBytes(staleFile, [1,2,3]); File.SetLastWriteTimeUtc(staleFile, DateTime.UtcNow.AddDays(-8));
var partialFile = Path.Combine(legacyDir, "Setup.exe.part"); File.WriteAllBytes(partialFile, [1]);
await legacy.RestoreAsync(); Check(!File.Exists(staleFile) && !File.Exists(partialFile), "startup cleans stale legacy package and interrupted download");
SetFeed(same); await legacy.CheckAsync();
Check(!File.Exists(legacyFile) && File.ReadAllText(backup) == "keep", "obsolete package cleaned while settings backup survives");
var instanceName = @"Local\WarDogsTests-" + Guid.NewGuid().ToString("N");
using (var activation = new ManualResetEventSlim())
{
    using (var owner = new SingleInstance(() => activation.Set(), instanceName))
    {
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false, CreateNoWindow=true };
        start.ArgumentList.Add("--instance-probe"); start.ArgumentList.Add(instanceName);
        using var child = System.Diagnostics.Process.Start(start)!;
        Check(child.WaitForExit(10000) && child.ExitCode == 2, "second process cannot acquire application instance");
        Check(activation.Wait(3000), "second process signals original window to activate");
    }
    using var reopened = new SingleInstance(() => {}, instanceName);
    Check(reopened.IsOwner, "application instance can restart after owner exits");
}
if ((args.Length == 2 && args[0] == "--release") || (args.Length == 3 && args[0] == "--available"))
{
    var live = new UpdateService(new Controller(), new HttpClient { Timeout = Timeout.InfiniteTimeSpan }, File.ReadAllText(args[1]), Path.Combine(Controller.Root, "Live"));
    await live.CheckAsync();
    if (args[0] == "--available")
    {
        Check(live.HasUpdate && live.Available?.Version == args[2], "public signed upgrade offer: " + live.Status);
        Console.WriteLine($"PASS: {UpdateService.CurrentVersion} detects signed public update {live.Available!.Version}");
    }
    else
    {
        Check(!live.HasUpdate && live.Status == "当前已是最新版本", "public signed release feed: " + live.Status);
        Console.WriteLine("PASS: public release feed and signature verified, current version is latest");
    }
}
Console.WriteLine($"PASS: {assertions} update assertions (signature, versions, downloads, retry, cancellation)");
}
catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }

sealed class FeedHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> Respond = _ => new(HttpStatusCode.NotFound);
    public bool Delay;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Delay) await Task.Delay(Timeout.Infinite, cancellationToken);
        return Respond(request);
    }
}
namespace WarDogs
{
    public sealed class Controller
    {
        public static string Root = Path.Combine(Path.GetTempPath(), "WarDogsUpdateTests-" + Guid.NewGuid().ToString("N"));
        public bool Demo => false;
        public void Quit() => throw new InvalidOperationException("Tests must not run an installer");
    }
}
