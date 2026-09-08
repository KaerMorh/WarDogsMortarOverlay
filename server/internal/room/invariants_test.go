package room

import (
	"encoding/json"
	"sync"
	"testing"
	"time"
)

func TestRoomIsolationAndNoopUpdates(t *testing.T) {
	s := New(Config{})
	a, _ := join(t, s, NewID(), "A")
	b := &capture{}
	_, err := s.Join(Message{V: Protocol, Type: "join", UID: NewID(), Room: "bbbb", Callsign: "B", Role: "gunner", Weapon: "mortar", Map: "ozeti"}, b)
	if err != nil {
		t.Fatal(err)
	}
	before := len(b.events)
	apply(t, s, a, Message{Type: "publish", TaskID: NewID(), Point: &Point{Map: "bakurani", X: 80, Y: 70}})
	if len(b.events) != before {
		t.Fatal("cross-room broadcast")
	}
	r, _ := s.current(a)
	revision := r.Revision
	apply(t, s, a, Message{Type: "profile", Callsign: "A", Role: "gunner", Weapon: "mortar", Map: "bakurani"})
	apply(t, s, a, Message{Type: "target"})
	apply(t, s, a, Message{Type: "origin"})
	if r.Revision != revision {
		t.Fatal("unchanged data broadcast")
	}
}
func TestCapacityAndTTLBoundary(t *testing.T) {
	s := New(Config{MaxRooms: 1, MaxMembers: 1})
	now := time.Now()
	s.now = func() time.Time { return now }
	a, _ := join(t, s, NewID(), "A")
	request := Message{V: Protocol, Type: "join", UID: NewID(), Room: "aabb", Callsign: "B", Role: "gunner", Weapon: "mortar", Map: "bakurani"}
	if _, err := s.Join(request, &capture{}); err == nil || err.Error() != "room_full" {
		t.Fatal(err)
	}
	s.Leave(a)
	now = now.Add(3*time.Minute - time.Nanosecond)
	s.Sweep()
	if n, _ := s.Counts(); n != 1 {
		t.Fatal("expired before deadline")
	}
	now = now.Add(time.Nanosecond)
	s.Sweep()
	if n, _ := s.Counts(); n != 0 {
		t.Fatal("did not expire at deadline")
	}
	if _, err := s.Join(request, &capture{}); err != nil {
		t.Fatal(err)
	}
}

type discardSink struct{}

func (discardSink) Send([]byte) {}
func (discardSink) Stop()       {}
func TestConcurrentRoomsAndCleanup(t *testing.T) {
	s := New(Config{})
	var workers sync.WaitGroup
	for i := 0; i < 16; i++ {
		workers.Add(1)
		go func() {
			defer workers.Done()
			l, err := s.Join(Message{V: Protocol, Type: "join", UID: NewID(), Room: NewID()[:8], Callsign: "Parallel", Role: "gunner", Weapon: "mortar", Map: "bakurani"}, discardSink{})
			if err != nil {
				t.Error(err)
				return
			}
			for n := 0; n < 20; n++ {
				if err = s.Apply(l, Message{V: Protocol, Type: "publish", TaskID: NewID(), Point: &Point{Map: "bakurani", X: 80, Y: 70}}); err != nil {
					t.Error(err)
				}
			}
			s.Leave(l)
		}()
	}
	workers.Wait()
	rooms, members := s.Counts()
	if rooms != 16 || members != 16 {
		t.Fatal(rooms, members)
	}
}
func TestEncodedBroadcastIsImmutable(t *testing.T) {
	s := New(Config{})
	a, c := join(t, s, NewID(), "A")
	first := c.events[0]
	apply(t, s, a, Message{Type: "profile", Callsign: "Changed", Role: "scout", Weapon: "mortar", Map: "ozeti"})
	if first.Members[0].Callsign != "A" {
		t.Fatal("prior snapshot mutated")
	}
	if _, err := json.Marshal(first); err != nil {
		t.Fatal(err)
	}
}
