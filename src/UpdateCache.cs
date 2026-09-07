using System.Diagnostics;
using System.IO;

namespace WarDogs;

internal sealed class UpdateCache(string root)
{
    static readonly string[] PayloadFiles = ["Setup.exe", "Setup.exe.part", "update.json", "update.json.sig"];
    public string CreateDirectory()
    {
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("更新缓存目录不能是链接");
        var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); return directory;
    }
    public string[] Directories()
    {
        try
        {
            if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return [];
            return Directory.GetDirectories(root).Where(d => Guid.TryParseExact(Path.GetFileName(d), "N", out _) &&
                (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }
    static bool Regular(string file) => File.Exists(file) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0;
    public void Save(string directory, byte[] manifest, byte[] signature)
    {
        File.WriteAllBytes(Path.Combine(directory, "update.json"), manifest);
        File.WriteAllBytes(Path.Combine(directory, "update.json.sig"), signature);
    }
    public async Task<(UpdateManifest Manifest, string Path)?> RestoreAsync(string key, Version current)
    {
        (UpdateManifest Manifest, string Path)? best = null;
        foreach (var directory in Directories())
        {
            RemoveFile(directory, "Setup.exe.part");
            var file = Path.Combine(directory, "Setup.exe");
            var json = Path.Combine(directory, "update.json"); var sig = json + ".sig";
            try
            {
                if (!Regular(json) || !Regular(sig))
                {
                    // v0.6.0/1 caches have no manifest. Reuse only after an online signed check.
                    if (Regular(file))
                    {
                        var versionText = FileVersionInfo.GetVersionInfo(file).ProductVersion?.Split('+')[0];
                        if ((Version.TryParse(versionText, out var version) && version <= current) || File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7)) RemovePayload(directory);
                    }
                    continue;
                }
                if (new FileInfo(json).Length > 65536 || new FileInfo(sig).Length > 4096) throw new InvalidDataException();
                var manifest = UpdateManifest.Verify(await File.ReadAllBytesAsync(json), await File.ReadAllBytesAsync(sig), key);
                if (UpdateManifest.ParseVersion(manifest.Version) <= current) { RemovePayload(directory); continue; }
                if (!Regular(file)) { RemovePayload(directory); continue; }
                await manifest.VerifyFileAsync(file);
                if (best == null || UpdateManifest.ParseVersion(manifest.Version) > UpdateManifest.ParseVersion(best.Value.Manifest.Version)) best = (manifest, file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
            { RemovePayload(directory); }
        }
        // Keep only the newest authenticated cached package; legacy files wait for an online check.
        foreach (var directory in Directories())
            if (File.Exists(Path.Combine(directory, "update.json")) && directory != Path.GetDirectoryName(best?.Path)) RemovePayload(directory);
        return best;
    }
    public async Task<string?> FindAsync(UpdateManifest manifest, CancellationToken token)
    {
        foreach (var directory in Directories())
        {
            var file = Path.Combine(directory, "Setup.exe");
            try
            {
                if (!Regular(file) || new FileInfo(file).Length != manifest.Size) continue;
                await manifest.VerifyFileAsync(file, token); return file;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { }
        }
        return null;
    }
    public void CleanExcept(string? keep)
    {
        foreach (var directory in Directories())
            if (!string.Equals(directory, Path.GetDirectoryName(keep), StringComparison.OrdinalIgnoreCase)) RemovePayload(directory);
    }
    void RemovePayload(string directory)
    {
        foreach (var name in PayloadFiles) RemoveFile(directory, name);
        // Never recursively delete: leave diagnostic logs, settings backups and unknown files.
        try { if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    static void RemoveFile(string directory, string name)
    {
        try { var file = Path.Combine(directory, name); if (Regular(file)) File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
