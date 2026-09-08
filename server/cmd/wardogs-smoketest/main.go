package main

import (
	"context"
	"encoding/json"
	"fmt"
	"os"
	"time"

	"github.com/coder/websocket"
	"wardogs/server/internal/room"
	"wardogs/server/internal/testbots"
)

type client struct {
	conn    *websocket.Conn
	events  chan room.Event
	members map[string]*room.Member
	session string
}

func connect(ctx context.Context, url, roomCode, uid, name, role string) (*client, error) {
	conn, _, err := websocket.Dial(ctx, url, nil)
	if err != nil {
		return nil, err
	}
	c := &client{conn: conn, events: make(chan room.Event, 128), members: make(map[string]*room.Member)}
	if err = c.send(ctx, room.Message{Type: "join", UID: uid, Room: roomCode, Callsign: name, Role: role, Map: "bakurani"}); err != nil {
		return nil, err
	}
	go func() {
		for {
			_, data, err := conn.Read(ctx)
			if err != nil {
				close(c.events)
				return
			}
			var event room.Event
			if json.Unmarshal(data, &event) == nil {
				c.events <- event
			}
		}
	}()
	if _, err = c.wait(ctx, func(e room.Event) bool { return e.Type == "snapshot" }); err != nil {
		return nil, err
	}
	return c, nil
}
func (c *client) close() { _ = c.conn.CloseNow() }
func (c *client) send(ctx context.Context, message room.Message) error {
	message.V = room.Protocol
	if message.Type != "join" {
		message.RequestID = room.NewID()
	}
	data, _ := json.Marshal(message)
	deadline, cancel := context.WithTimeout(ctx, 5*time.Second)
	defer cancel()
	return c.conn.Write(deadline, websocket.MessageText, data)
}
func (c *client) apply(event room.Event) {
	if event.Type == "snapshot" {
		c.members = make(map[string]*room.Member)
		for _, member := range event.Members {
			c.members[member.UID] = member
		}
		c.session = event.SessionID
	} else if event.Type == "member" && event.Member != nil {
		c.members[event.Member.UID] = event.Member
	} else if event.Type == "removed" {
		delete(c.members, event.UID)
	}
}
func (c *client) wait(ctx context.Context, predicate func(room.Event) bool) (room.Event, error) {
	deadline, cancel := context.WithTimeout(ctx, 12*time.Second)
	defer cancel()
	for {
		select {
		case <-deadline.Done():
			return room.Event{}, deadline.Err()
		case event, ok := <-c.events:
			if !ok {
				return room.Event{}, fmt.Errorf("connection closed")
			}
			if event.V != room.Protocol {
				return room.Event{}, fmt.Errorf("protocol %d", event.V)
			}
			c.apply(event)
			if predicate(event) {
				return event, nil
			}
		}
	}
}
func check(err error, label string) {
	if err != nil {
		panic(label + ": " + err.Error())
	}
	fmt.Println("PASS", label)
}

func main() {
	if len(os.Args) != 2 {
		panic("usage: wardogs-smoketest wss://host/ws")
	}
	ctx, cancel := context.WithTimeout(context.Background(), time.Minute)
	defer cancel()
	url := os.Args[1]
	protocolSmoke(ctx, url)
	botSmoke(ctx, url)
	fmt.Println("Remote protocol and test-room smoke tests passed.")
}

func protocolSmoke(ctx context.Context, url string) {
	roomCode := "smoke-" + room.NewID()[:8]
	uidA, uidB := room.NewID(), room.NewID()
	a, err := connect(ctx, url, roomCode, uidA, "Same", "gunner")
	check(err, "first public WSS join")
	defer a.close()
	b, err := connect(ctx, url, roomCode, uidB, "Same", "gunner")
	check(err, "second public WSS join")
	defer b.close()
	_, err = b.wait(ctx, func(room.Event) bool { return b.members[uidB] != nil && b.members[uidB].DisplayName == "Same#2" })
	check(err, "duplicate name suffix")
	point := &room.Point{Map: "bakurani", X: 80, Y: 70}
	taskID := room.NewID()
	check(a.send(ctx, room.Message{Type: "publish", TaskID: taskID, Point: point}), "publish write")
	_, err = b.wait(ctx, func(room.Event) bool { return hasTask(b.members[uidA], taskID, "") })
	check(err, "public task broadcast")
	check(b.send(ctx, room.Message{Type: "target", Point: point, Solved: true}), "solved write")
	_, err = a.wait(ctx, func(room.Event) bool { return hasTask(a.members[uidA], taskID, uidB) })
	check(err, "UID solver attribution")
	check(a.send(ctx, room.Message{Type: "profile", Callsign: "Renamed", Role: "gunner", Map: "bakurani"}), "profile write")
	_, err = b.wait(ctx, func(e room.Event) bool {
		return e.Type == "identity" && e.Identity != nil && e.Identity.UID == uidA && e.Identity.Name == "Renamed"
	})
	check(err, "latest identity event")
	session := b.session
	check(b.send(ctx, room.Message{Type: "sync"}), "sync write")
	_, err = b.wait(ctx, func(e room.Event) bool { return e.Type == "snapshot" })
	check(err, "sync snapshot")
	if b.session != session {
		panic("sync changed session")
	}
	fmt.Println("PASS sync preserves session")
}

func botSmoke(ctx context.Context, url string) {
	gunnerUID, scoutUID := room.NewID(), room.NewID()
	gunner, err := connect(ctx, url, "testzz4z", gunnerUID, "Smoke Gunner", "gunner")
	check(err, "test room gunner join")
	defer gunner.close()
	scout, err := connect(ctx, url, "testzz4z", scoutUID, "Smoke Scout", "scout")
	check(err, "test room scout join")
	defer scout.close()
	_, err = gunner.wait(ctx, func(room.Event) bool {
		return gunner.members[testbots.ScoutUID] != nil && gunner.members[testbots.GunnerUID] != nil
	})
	check(err, "both test bots online")
	origin := &room.Point{Map: "bakurani", X: 70, Y: 60}
	target := &room.Point{Map: "bakurani", X: 71, Y: 61}
	check(gunner.send(ctx, room.Message{Type: "origin", Point: origin}), "human origin write")
	check(gunner.send(ctx, room.Message{Type: "target", Point: target}), "human target write")
	response := &room.Point{Map: "bakurani", X: 72, Y: 62}
	_, err = gunner.wait(ctx, func(room.Event) bool { return hasPointTask(gunner.members[testbots.ScoutUID], response) })
	check(err, "scout bot plus-one task")
	beacon := &room.Point{Map: "bakurani", X: 80, Y: 70}
	taskID := room.NewID()
	check(scout.send(ctx, room.Message{Type: "publish", TaskID: taskID, Point: beacon}), "human beacon write")
	_, err = gunner.wait(ctx, func(room.Event) bool {
		m := gunner.members[testbots.GunnerUID]
		return m != nil && room.Same(m.Origin, &room.Point{Map: "bakurani", X: 82, Y: 72})
	})
	check(err, "gunner bot plus-two origin")
	_, err = gunner.wait(ctx, func(room.Event) bool { return hasTask(gunner.members[scoutUID], taskID, testbots.GunnerUID) })
	check(err, "gunner bot simulated solved")
}

func hasTask(member *room.Member, taskID, solver string) bool {
	if member == nil {
		return false
	}
	for _, task := range member.Tasks {
		if task.ID != taskID {
			continue
		}
		if solver == "" {
			return true
		}
		for _, uid := range task.SolvedBy {
			if uid == solver {
				return true
			}
		}
	}
	return false
}
func hasPointTask(member *room.Member, point *room.Point) bool {
	if member == nil {
		return false
	}
	for _, task := range member.Tasks {
		if room.Same(task.Point, point) {
			return true
		}
	}
	return false
}
