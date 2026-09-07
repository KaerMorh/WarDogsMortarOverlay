package transport

import (
	"context"
	"encoding/json"
	"github.com/coder/websocket"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
	"wardogs/server/internal/room"
)

func TestWebSocketRoundTrip(t *testing.T) {
	server := httptest.NewServer(New(room.New(room.Config{}), 10).Handler())
	defer server.Close()
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	conn, _, err := websocket.Dial(ctx, "ws"+strings.TrimPrefix(server.URL, "http")+"/ws", nil)
	if err != nil {
		t.Fatal(err)
	}
	defer conn.CloseNow()
	send := func(msg room.Message) {
		b, _ := json.Marshal(msg)
		if err := conn.Write(ctx, websocket.MessageText, b); err != nil {
			t.Fatal(err)
		}
	}
	send(room.Message{V: 1, Type: "join", Room: "test", UID: room.NewID(), Callsign: "A", Role: "gunner", Map: "bakurani"})
	_, data, err := conn.Read(ctx)
	if err != nil {
		t.Fatal(err)
	}
	var event room.Event
	if json.Unmarshal(data, &event) != nil || event.Type != "snapshot" {
		t.Fatal(string(data))
	}
	id := room.NewID()
	send(room.Message{V: 1, Type: "publish", TaskID: id, RequestID: "publish", Point: &room.Point{Map: "bakurani", X: 80, Y: 70}})
	found := false
	for {
		_, data, err = conn.Read(ctx)
		if err != nil {
			t.Fatal(err)
		}
		_ = json.Unmarshal(data, &event)
		if event.Type == "member" && len(event.Member.Tasks) > 0 {
			found = event.Member.Tasks[0].ID == id
		}
		if event.Type == "ack" && event.RequestID == "publish" {
			break
		}
	}
	if !found {
		t.Fatal("task not broadcast before ack")
	}
}
