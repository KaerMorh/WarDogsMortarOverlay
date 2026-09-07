using System.IO;
using System.Text.Json;

namespace WarDogs.Multiplayer;

public static class Identity
{
    sealed record StoredIdentity(string Uid);
    public static string LoadOrCreate(string userDirectory)
    {
        Directory.CreateDirectory(userDirectory);
        var path = Path.Combine(userDirectory, "identity.json");
        if (File.Exists(path))
        {
            var stored = JsonSerializer.Deserialize<StoredIdentity>(File.ReadAllText(path), Protocol.Json);
            if (stored != null && Guid.TryParseExact(stored.Uid, "D", out var id) && id != Guid.Empty) return id.ToString("D");
            throw new InvalidDataException("身份文件无效，请备份并移走 UserData/identity.json 后重试。");
        }
        var uid = Guid.NewGuid().ToString("D");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new StoredIdentity(uid), Protocol.Json));
            File.Move(temporary, path, false);
        }
        catch (IOException) when (File.Exists(path)) { return LoadOrCreate(userDirectory); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return uid;
    }
}
