package room

import (
	"encoding/json"
	"strconv"
	"testing"
	"time"
)

type capture struct {
	events  []Event
	stopped bool
}

func (c *capture) Send(b []byte) {
	var copy Event
	_ = json.Unmarshal(b, &copy)
	c.events = append(c.events, copy)
}
func (c *capture) Stop() { c.stopped = true }
func join(t *testing.T, s *Store, uid, name string) (Lease, *capture) {
	t.Helper()
	c := &capture{}
	l, err := s.Join(Message{V: Protocol, Type: "join", UID: uid, Room: "aabb", Callsign: name, Role: "gunner", Weapon: "mortar", Map: "bakurani"}, c)
	if err != nil {
		t.Fatal(err)
	}
	return l, c
}
func apply(t *testing.T, s *Store, l Lease, m Message) {
	t.Helper()
	m.V = Protocol
	if err := s.Apply(l, m); err != nil {
		t.Fatal(err)
	}
}
func TestLifecycleAndReplacement(t *testing.T) {
	s := New(Config{})
	now := time.Date(2026, 9, 8, 0, 0, 0, 0, time.UTC)
	s.now = func() time.Time { return now }
	uid := NewID()
	a, old := join(t, s, uid, "Alpha")
	_, b := join(t, s, NewID(), "Alpha")
	if got := b.events[0].Members; got[0].DisplayName != "Alpha#1" || got[1].DisplayName != "Alpha#2" {
		t.Fatal("duplicate names", got)
	}
	apply(t, s, a, Message{Type: "publish", TaskID: NewID(), Point: &Point{Map: "bakurani", X: 80, Y: 70}})
	s.Leave(a)
	now = now.Add(179 * time.Second)
	s.Sweep()
	_, n := s.Counts()
	if n != 2 {
		t.Fatal("early expiry")
	}
	fresh, _ := join(t, s, uid, "Alpha")
	if fresh.SessionID == a.SessionID {
		t.Fatal("session reused")
	}
	s.Leave(a)
	if r, m := s.current(fresh); r == nil || !m.Online || len(m.Tasks) != 0 {
		t.Fatal("replacement restored tasks or stale leave")
	}
	// Replace a live session as well; only that socket is stopped.
	latest, _ := join(t, s, uid, "Alpha")
	if latest.SessionID == fresh.SessionID {
		t.Fatal("reused session")
	}
	_ = old
	if err := s.Apply(a, Message{V: Protocol, Type: "target"}); err != ErrReplaced {
		t.Fatal("stale write accepted", err)
	}
	s.Leave(latest)
	now = now.Add(180 * time.Second)
	s.Sweep()
	_, n = s.Counts()
	if n != 1 {
		t.Fatal("offline expiry", n)
	}
}

// The server no longer attributes solves. It must relay the claim verbatim and
// keep publication records untouched; clients derive attribution themselves.
func TestTasksAndSolveRelay(t *testing.T) {
	s := New(Config{})
	a, _ := join(t, s, NewID(), "Scout")
	b, _ := join(t, s, NewID(), "Gun")
	p := &Point{Map: "bakurani", X: 80, Y: 70}
	id := NewID()
	apply(t, s, a, Message{Type: "publish", TaskID: id, Point: p})
	apply(t, s, a, Message{Type: "publish", TaskID: id, Point: p})
	_, m := s.current(a)
	if len(m.Tasks) != 1 {
		t.Fatal("not idempotent")
	}
	_, gun := s.current(b)
	apply(t, s, b, Message{Type: "target", Point: p, Solved: true})
	if !gun.Solved || !Same(gun.Target, p) {
		t.Fatal("solve claim not relayed")
	}
	apply(t, s, b, Message{Type: "target", Point: p, Declined: true})
	if gun.Solved || !gun.Declined {
		t.Fatal("declined claim not relayed")
	}
	apply(t, s, b, Message{Type: "target", Point: nil})
	if gun.Solved || gun.Declined || gun.Target != nil {
		t.Fatal("clearing the target left a stale claim")
	}
	if err := s.Apply(b, Message{V: Protocol, Type: "target", Point: p, Solved: true, Declined: true}); err != ErrInvalid {
		t.Fatal("contradictory claim accepted", err)
	}
	apply(t, s, a, Message{Type: "profile", Callsign: "Scout", Role: "scout", Weapon: "mortar", Map: "bakurani"})
	if err := s.Apply(a, Message{V: Protocol, Type: "target", Point: p, Solved: true}); err != ErrInvalid {
		t.Fatal("scout solve accepted", err)
	}
	for i := 0; i < 4; i++ {
		apply(t, s, a, Message{Type: "publish", TaskID: NewID(), Point: p})
	}
	if len(m.Tasks) != 3 || m.Tasks[0].Sequence <= m.Tasks[1].Sequence {
		t.Fatal("recent three order")
	}
	if m.Target != nil || m.Origin != nil {
		t.Fatal("publishing changed local/public state")
	}
}
func TestEmptyRoomAndValidation(t *testing.T) {
	s := New(Config{MaxRooms: 1, MaxMembers: 1})
	now := time.Now()
	s.now = func() time.Time { return now }
	a, c := join(t, s, NewID(), "A")
	first := c.events[0].RoomID
	s.Leave(a)
	now = now.Add(3 * time.Minute)
	s.Sweep()
	n, _ := s.Counts()
	if n != 0 {
		t.Fatal("room leaked")
	}
	_, c = join(t, s, NewID(), "B")
	if c.events[0].RoomID == first || len(c.events[0].Members) != 1 {
		t.Fatal("room not fresh")
	}
	if _, err := s.Join(Message{V: Protocol - 1, Type: "join"}, &capture{}); err == nil {
		t.Fatal("version accepted")
	}
}

func TestIdentityUpdatesAndSync(t *testing.T) {
	s := New(Config{MaxMembers: 34})
	now := time.Date(2026, 9, 8, 0, 0, 0, 0, time.UTC)
	s.now = func() time.Time { return now }
	owner, ownerSink := join(t, s, NewID(), "Scout")
	point := &Point{Map: "bakurani", X: 80, Y: 70}
	apply(t, s, owner, Message{Type: "publish", TaskID: NewID(), Point: point})
	// Many gunners claiming the same point cost the task record nothing now.
	for i := 0; i < 33; i++ {
		solver, _ := join(t, s, NewID(), "Gun"+strconv.Itoa(i))
		apply(t, s, solver, Message{Type: "target", Point: point, Solved: true})
	}
	_, member := s.current(owner)
	if len(member.Tasks) != 1 {
		t.Fatal("publication record altered by solves", len(member.Tasks))
	}

	apply(t, s, owner, Message{Type: "profile", Callsign: "Scout New", Role: "gunner", Weapon: "mortar", Map: "bakurani"})
	if s.rooms[owner.Room].Identities[owner.UID] != "Scout New" {
		t.Fatal("latest identity not stored")
	}
	before := len(ownerSink.events)
	apply(t, s, owner, Message{Type: "sync", RequestID: "sync-1"})
	if len(ownerSink.events) < before+2 || ownerSink.events[before].Type != "snapshot" || ownerSink.events[before].SessionID != owner.SessionID {
		t.Fatal("sync did not return same-session snapshot")
	}
	if err := s.Apply(owner, Message{V: Protocol, Type: "sync", RequestID: "sync-2"}); err == nil || err.Error() != "sync_rate_limited" {
		t.Fatal("sync rate limit missing", err)
	}
	now = now.Add(2 * time.Second)
	if err := s.Apply(owner, Message{V: Protocol, Type: "sync", RequestID: "sync-3"}); err != nil {
		t.Fatal(err)
	}
}
