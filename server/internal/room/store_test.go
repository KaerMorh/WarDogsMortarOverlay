package room

import (
	"encoding/json"
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
	l, err := s.Join(Message{V: 1, Type: "join", UID: uid, Room: "aabb", Callsign: name, Role: "gunner", Map: "bakurani"}, c)
	if err != nil {
		t.Fatal(err)
	}
	return l, c
}
func apply(t *testing.T, s *Store, l Lease, m Message) {
	t.Helper()
	m.V = 1
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
	if err := s.Apply(a, Message{V: 1, Type: "target"}); err != ErrReplaced {
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
func TestTasksAndSolved(t *testing.T) {
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
	apply(t, s, b, Message{Type: "target", Point: &Point{Map: "ozeti", X: 80, Y: 70}, Solved: true})
	if len(m.Tasks[0].SolvedBy) != 0 {
		t.Fatal("cross-map solve")
	}
	apply(t, s, b, Message{Type: "target", Point: p, Solved: false})
	if len(m.Tasks[0].SolvedBy) != 0 {
		t.Fatal("invalid solve marked")
	}
	apply(t, s, b, Message{Type: "target", Point: p, Solved: true})
	if len(m.Tasks[0].SolvedBy) != 1 || m.Tasks[0].SolvedBy[0].UID != b.UID {
		t.Fatal("solver missing")
	}
	apply(t, s, b, Message{Type: "target", Point: nil})
	if len(m.Tasks[0].SolvedBy) != 1 {
		t.Fatal("past solver lost")
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
	if _, err := s.Join(Message{V: 2, Type: "join"}, &capture{}); err == nil {
		t.Fatal("version accepted")
	}
}
