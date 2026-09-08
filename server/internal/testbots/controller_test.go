package testbots

import (
	"testing"
	"wardogs/server/internal/room"
)

func member(uid, session, role string, origin, target *room.Point, tasks ...*room.Task) *room.Member {
	return &room.Member{UID: uid, SessionID: session, Role: role, Origin: origin, Target: target, Tasks: tasks}
}
func event(m *room.Member) room.Event { return room.Event{V: room.Protocol, Type: "member", Member: m} }

func TestGunnerTargetAndLateOriginTriggerOncePerTargetChange(t *testing.T) {
	c := NewController()
	p := &room.Point{Map: "bakurani", X: 80, Y: 70}
	c.Apply(room.Event{Type: "snapshot", Members: []*room.Member{member("human", "s", "gunner", nil, nil)}})
	if got := c.Apply(event(member("human", "s", "gunner", nil, p))); len(got) != 0 {
		t.Fatal(got)
	}
	got := c.Apply(event(member("human", "s", "gunner", p, p)))
	if len(got) != 1 || got[0].Kind != "publish" || got[0].Point.X != 81 || got[0].Point.Y != 71 {
		t.Fatal(got)
	}
	if got = c.Apply(event(member("human", "s", "gunner", p, p))); len(got) != 0 {
		t.Fatal("duplicate response", got)
	}
	other := &room.Point{Map: "bakurani", X: 82, Y: 72}
	if got = c.Apply(event(member("human", "s", "gunner", p, other))); len(got) != 1 {
		t.Fatal("new target not handled", got)
	}
	if got = c.Apply(event(member("human", "s", "gunner", p, p))); len(got) != 1 {
		t.Fatal("A-B-A not treated as changes", got)
	}
}

func TestScoutTasksBaselineDedupBotsAndBounds(t *testing.T) {
	c := NewController()
	p := &room.Point{Map: "ozeti", X: 80, Y: 70}
	old := &room.Task{ID: "old", Sequence: 1, Point: p}
	c.Apply(room.Event{Type: "snapshot", Members: []*room.Member{member("scout", "s", "scout", nil, nil, old)}})
	newTask := &room.Task{ID: "new", Sequence: 2, Point: p}
	got := c.Apply(event(member("scout", "s", "scout", nil, nil, newTask, old)))
	if len(got) != 1 || got[0].Kind != "beacon" {
		t.Fatal(got)
	}
	if got = c.Apply(event(member("scout", "s", "scout", nil, nil, newTask, old))); len(got) != 0 {
		t.Fatal("task replayed", got)
	}
	if got = c.Apply(event(member(ScoutUID, "bot", "scout", nil, nil, newTask))); len(got) != 0 {
		t.Fatal("bot loop", got)
	}
	edge := &room.Point{Map: "bakurani", X: 163.5, Y: 163.5}
	c.Apply(room.Event{Type: "snapshot", Members: []*room.Member{member("gun", "g", "gunner", edge, nil)}})
	if got = c.Apply(event(member("gun", "g", "gunner", edge, edge))); len(got) != 1 || got[0].Kind != "skip_publish_bounds" {
		t.Fatal("out-of-range offset not recorded", got)
	}
}

func TestTaskDedupMemoryIsBounded(t *testing.T) {
	c := NewController()
	c.baseline(member("scout", "session", "scout", nil, nil))
	for i := 0; i < 4200; i++ {
		c.remember(room.NewID())
	}
	if len(c.seen) != 4096 || len(c.seenOrder) != 4096 {
		t.Fatal("dedup memory is unbounded", len(c.seen), len(c.seenOrder))
	}
}
