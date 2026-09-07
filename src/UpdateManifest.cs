using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WarDogs;

public sealed record UpdateManifest(string Version, string Notes, string Url, long Size, string Sha256)
{
    public const string Repository = "https://github.com/KaerMorh/WarDogsMortarOverlay";
    public const string Feed = Repository + "/releases/latest/download/update.json";
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static Version ParseVersion(string value)
    {
        if (!Regex.IsMatch(value ?? "", @"^\d+\.\d+\.\d+$") || !System.Version.TryParse(value, out var version))
            throw new InvalidDataException("版本号格式无效");
        return version;
    }

    public static UpdateManifest Verify(byte[] bytes, byte[] signature, string publicKey)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKey);
        if (!rsa.VerifyData(bytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new InvalidDataException("更新清单签名无效");
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(bytes, Json) ?? throw new InvalidDataException("更新清单为空");
        ParseVersion(manifest.Version);
        // Pin the repository, tag and asset name, not arbitrary commands or download hosts.
        var expected = $"{Repository}/releases/download/v{manifest.Version}/WarDogsOverlay-{manifest.Version}-win-x64-Setup.exe";
        if (manifest.Url != expected || manifest.Size <= 0 || manifest.Size > 2L * 1024 * 1024 * 1024 ||
            !Regex.IsMatch(manifest.Sha256 ?? "", "^[a-fA-F0-9]{64}$") || manifest.Notes == null || manifest.Notes.Length > 20000)
            throw new InvalidDataException("更新清单内容无效");
        return manifest;
    }

    public async Task VerifyFileAsync(string path, CancellationToken cancellation = default)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (file.Length != Size) throw new InvalidDataException("安装包大小不匹配");
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellation));
        if (!hash.Equals(Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("安装包校验失败");
    }
}
