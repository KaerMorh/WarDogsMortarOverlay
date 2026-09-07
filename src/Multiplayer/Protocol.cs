using System.Text.Json;
using System.Text.Json.Serialization;

namespace WarDogs.Multiplayer;

public static class Protocol
{
    public const int Version = 1;
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
}

public record NetworkPoint(string Map, double X, double Y)
{
    [JsonIgnore] public Coord Coordinate => new(X, Y);
    [JsonIgnore] public bool Valid => Map is "bakurani" or "ozeti" && double.IsFinite(X) && double.IsFinite(Y) && X >= -.03 && X <= 163.81 && Y >= -.01 && Y <= 163.83;
    public bool Same(NetworkPoint? other) => other != null && Map == other.Map && Quantize(X) == Quantize(other.X) && Quantize(Y) == Quantize(other.Y);
    static double Quantize(double value) => Math.Round(value * 1e6, MidpointRounding.AwayFromZero);
}
public record Solver(string Uid, string Name);
public sealed class RoomTask
{
    public string Id { get; set; } = "";
    public long Sequence { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public NetworkPoint Point { get; set; } = new("bakurani", 0, 0);
    public List<Solver> SolvedBy { get; set; } = new();
}
public sealed class RoomMember
{
    public string Uid { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Callsign { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "gunner";
    public string Map { get; set; } = "bakurani";
    public bool Online { get; set; }
    public DateTimeOffset? OfflineAt { get; set; }
    public long Joined { get; set; }
    public NetworkPoint? Origin { get; set; }
    public NetworkPoint? Target { get; set; }
    public bool Solved { get; set; }
    public List<RoomTask> Tasks { get; set; } = new();
}
public sealed class RoomMessage
{
    public int V { get; set; } = Protocol.Version;
    public string Type { get; set; } = "";
    public string? RequestId { get; set; }
    public string? Room { get; set; }
    public string? Uid { get; set; }
    public string? Callsign { get; set; }
    public string? Role { get; set; }
    public string? Map { get; set; }
    public string? TaskId { get; set; }
    public NetworkPoint? Point { get; set; }
    public bool Solved { get; set; }
}
public sealed class RoomEvent
{
    public int V { get; set; }
    public string Type { get; set; } = "";
    public string RoomId { get; set; } = "";
    public string Room { get; set; } = "";
    public string Map { get; set; } = "";
    public long Revision { get; set; }
    public string SessionId { get; set; } = "";
    public string? RequestId { get; set; }
    public List<RoomMember> Members { get; set; } = new();
    public RoomMember? Member { get; set; }
    public string? Uid { get; set; }
    public string? Code { get; set; }
}
public sealed class MultiplayerPreferences
{
    public string ServerUrl { get; set; } = "ws://127.0.0.1:8080/ws";
    public string Room { get; set; } = "";
    public string Callsign { get; set; } = "Player";
    public string Role { get; set; } = "gunner";
    public bool AutoJoin { get; set; }
    public bool ShareTarget { get; set; } = true;
}
