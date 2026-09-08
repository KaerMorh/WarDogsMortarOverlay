namespace WarDogs.Multiplayer;

// Mutated on one owner thread (the WPF Dispatcher in the desktop application).
public sealed class RoomState
{
    public string RoomId { get; private set; } = "";
    public string RoomCode { get; private set; } = "";
    public string Map { get; private set; } = "";
    public string SessionId { get; private set; } = "";
    public long Revision { get; private set; }
    public Dictionary<string, RoomMember> Members { get; } = new();
    public Dictionary<string, string> Identities { get; } = new();
    readonly HashSet<string> seen = new();
    readonly Queue<string> seenOrder = new();
    public event Action<RoomMember, RoomTask>? TaskReceived;
    public bool Apply(RoomEvent e)
    {
        if (e.V != Protocol.Version) return false;
        if (e.Type == "snapshot")
        {
            RoomId = e.RoomId; RoomCode = e.Room; Map = e.Map; SessionId = e.SessionId; Revision = e.Revision;
            Members.Clear();
            Identities.Clear();
            foreach (var entry in e.Identities) Identities[entry.Uid] = entry.Name;
            foreach (var member in e.Members) Members[member.Uid] = member;
            foreach (var member in e.Members) Identities.TryAdd(member.Uid, member.DisplayName);
            foreach (var item in e.Members.SelectMany(m => m.Tasks.Select(t => (m, t))).OrderBy(x => x.t.Sequence)) Receive(item.m, item.t);
            return true;
        }
        if (e.RoomId != RoomId || e.Revision <= Revision) return false;
        if (e.Type == "member" && e.Member is { } updated)
        {
            Revision = e.Revision; Members[updated.Uid] = updated;
            Identities[updated.Uid] = updated.DisplayName;
            foreach (var task in updated.Tasks.OrderBy(t => t.Sequence)) Receive(updated, task);
            return true;
        }
        if (e.Type == "removed" && e.Uid != null) { Revision = e.Revision; Members.Remove(e.Uid); return true; }
        if (e.Type == "identity" && e.Identity is { } identity)
        {
            Revision = e.Revision; Identities[identity.Uid] = identity.Name; return true;
        }
        return false;
    }
    void Receive(RoomMember member, RoomTask task)
    {
        var key = RoomId + "/" + task.Id;
        if (!seen.Add(key)) return;
        seenOrder.Enqueue(key);
        while (seenOrder.Count > 1024) seen.Remove(seenOrder.Dequeue());
        TaskReceived?.Invoke(member, task);
    }
    public string ResolveName(string uid) => Identities.GetValueOrDefault(uid, uid.Length > 8 ? uid[..8] : uid);
    public void Clear() { RoomId = RoomCode = Map = SessionId = ""; Revision = 0; Members.Clear(); Identities.Clear(); }
}

public sealed class PublishCapture
{
    public bool Waiting { get; private set; }
    uint armedSequence;
    public void Toggle(uint clipboardSequence) { Waiting = !Waiting; armedSequence = clipboardSequence; }
    public void Cancel() => Waiting = false;
    public Coord? Consume(uint clipboardSequence, Coord? coordinate)
    {
        if (!Waiting || clipboardSequence == armedSequence || coordinate == null) return null;
        Waiting = false;
        return coordinate;
    }
}

public sealed class SharedTaskHistory
{
    public string RoomId { get; init; } = "";
    public string TaskId { get; init; } = "";
    public string PublisherUid { get; init; } = "";
    public string PublisherName { get; private set; } = "";
    public long Sequence { get; init; }
    public NetworkPoint Point { get; init; } = new("bakurani", 0, 0);
    public List<Solver> SolvedBy { get; } = new();
    public string Status => SolvedBy.Count == 0 ? "尚未解算" : "已解算：" + string.Join("、", SolvedBy.Select(x => x.Name));
    public void UpdateNames(RoomState state)
    {
        PublisherName = state.ResolveName(PublisherUid);
        for (var i = 0; i < SolvedBy.Count; i++) SolvedBy[i] = new(SolvedBy[i].Uid, state.ResolveName(SolvedBy[i].Uid));
    }
    public void Remember(IEnumerable<string> solverUids, RoomState state)
    {
        foreach (var uid in solverUids) if (!SolvedBy.Any(s => s.Uid == uid)) SolvedBy.Add(new(uid, state.ResolveName(uid)));
        UpdateNames(state);
    }
}
