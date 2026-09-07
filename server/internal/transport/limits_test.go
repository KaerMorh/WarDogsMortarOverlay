package transport

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/coder/websocket"
	"wardogs/server/internal/room"
)

func testServer(t *testing.T, options Options) (*Server, string) {
	t.Helper()
	api := NewWithOptions(room.New(room.Config{}), options)
	httpServer := httptest.NewServer(api.Handler())
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), time.Second)
		defer cancel()
		_ = api.Close(ctx)
		httpServer.Close()
	})
	return api, "ws" + strings.TrimPrefix(httpServer.URL, "http") + "/ws"
}
func dial(t *testing.T, url string) *websocket.Conn {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	conn, _, err := websocket.Dial(ctx, url, nil)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = conn.CloseNow() })
	return conn
}
func write(t *testing.T, conn *websocket.Conn, msg room.Message) {
	t.Helper()
	b, _ := json.Marshal(msg)
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	if err := conn.Write(ctx, websocket.MessageText, b); err != nil {
		t.Fatal(err)
	}
}
func readType(t *testing.T, conn *websocket.Conn, kind string) room.Event {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	for {
		_, data, err := conn.Read(ctx)
		if err != nil {
			t.Fatal(err)
		}
		var e room.Event
		if err = json.Unmarshal(data, &e); err != nil {
			t.Fatal(err)
		}
		if e.Type == kind {
			return e
		}
	}
}
func joinClient(t *testing.T, conn *websocket.Conn) {
	write(t, conn, room.Message{V: 1, Type: "join", UID: room.NewID(), Room: "test", Callsign: "A", Role: "gunner", Map: "bakurani"})
	readType(t, conn, "snapshot")
}
func eventually(t *testing.T, predicate func() bool) {
	t.Helper()
	deadline := time.Now().Add(time.Second)
	for !predicate() {
		if time.Now().After(deadline) {
			t.Fatal("condition not reached")
		}
		time.Sleep(time.Millisecond)
	}
}

func TestJoinTimeoutReleasesConnectionSlot(t *testing.T) {
	api, url := testServer(t, Options{MaxConnections: 1, JoinTimeout: 30 * time.Millisecond})
	conn := dial(t, url)
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	if _, _, err := conn.Read(ctx); err == nil {
		t.Fatal("idle join not closed")
	}
	eventually(t, func() bool { return api.Stats().Connections == 0 })
	other := dial(t, url)
	joinClient(t, other)
}

func TestInvalidJoinDoesNotCreateRoom(t *testing.T) {
	api, url := testServer(t, Options{})
	conn := dial(t, url)
	write(t, conn, room.Message{V: 2, Type: "join", UID: room.NewID(), Room: "test", Callsign: "A", Role: "gunner", Map: "bakurani"})
	e := readType(t, conn, "error")
	if e.Code != "invalid_message" {
		t.Fatal(e)
	}
	if api.Stats().Rooms != 0 {
		t.Fatal("invalid join created a room")
	}
}

func TestInvalidPointRejectedWithoutMutatingMember(t *testing.T) {
	_, url := testServer(t, Options{})
	conn := dial(t, url)
	joinClient(t, conn)
	write(t, conn, room.Message{V: 1, Type: "target", RequestID: "invalid", Point: &room.Point{Map: "other", X: 80, Y: 70}})
	e := readType(t, conn, "error")
	if e.Code != "invalid_message" || e.RequestID != "invalid" {
		t.Fatal(e)
	}
	write(t, conn, room.Message{V: 1, Type: "origin", RequestID: "valid", Point: &room.Point{Map: "bakurani", X: 80, Y: 70}})
	e = readType(t, conn, "ack")
	if e.RequestID != "valid" {
		t.Fatal("connection unusable after recoverable rejection", e)
	}
}

func TestForeignBrowserOriginRejected(t *testing.T) {
	api, url := testServer(t, Options{})
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	headers := http.Header{}
	headers.Set("Origin", "https://unrelated.invalid")
	conn, response, err := websocket.Dial(ctx, url, &websocket.DialOptions{HTTPHeader: headers})
	if conn != nil {
		_ = conn.CloseNow()
	}
	if err == nil || response == nil || response.StatusCode != http.StatusForbidden {
		t.Fatal("origin accepted", err)
	}
	if api.Stats().Rooms != 0 {
		t.Fatal("room allocated on rejected upgrade")
	}
}

func TestConnectionCapacity(t *testing.T) {
	_, url := testServer(t, Options{MaxConnections: 1})
	first := dial(t, url)
	joinClient(t, first)
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	conn, response, err := websocket.Dial(ctx, url, nil)
	if conn != nil {
		_ = conn.CloseNow()
	}
	if err == nil || response == nil || response.StatusCode != http.StatusServiceUnavailable {
		t.Fatal("connection cap ignored", err)
	}
}

func TestRateLimitClosesFloodingConnection(t *testing.T) {
	api, url := testServer(t, Options{RatePerSecond: .01, Burst: 1})
	conn := dial(t, url)
	joinClient(t, conn)
	message := room.Message{V: 1, Type: "origin", Point: &room.Point{Map: "bakurani", X: 80, Y: 70}}
	write(t, conn, message)
	readType(t, conn, "ack")
	write(t, conn, message)
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	for {
		if _, _, err := conn.Read(ctx); err != nil {
			break
		}
	}
	eventually(t, func() bool { return api.Stats().Connections == 0 })
	if api.Stats().Rejected == 0 {
		t.Fatal("rate rejection not counted")
	}
}

func TestHeartbeatDropsNonReadingClient(t *testing.T) {
	api, url := testServer(t, Options{HeartbeatInterval: 20 * time.Millisecond, HeartbeatTimeout: 30 * time.Millisecond})
	conn := dial(t, url)
	write(t, conn, room.Message{V: 1, Type: "join", UID: room.NewID(), Room: "test", Callsign: "A", Role: "gunner", Map: "bakurani"})
	eventually(t, func() bool { return api.Stats().Rooms == 1 })
	eventually(t, func() bool { return api.Stats().Connections == 0 })
}

func TestOversizedFrame(t *testing.T) {
	api, url := testServer(t, Options{})
	conn := dial(t, url)
	joinClient(t, conn)
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	_ = conn.Write(ctx, websocket.MessageText, []byte(strings.Repeat("x", 9000)))
	for {
		if _, _, err := conn.Read(ctx); err != nil {
			break
		}
	}
	eventually(t, func() bool { return api.Stats().Connections == 0 })
}

func TestGracefulCloseDrainsConnections(t *testing.T) {
	api, url := testServer(t, Options{})
	conn := dial(t, url)
	joinClient(t, conn)
	readDone := make(chan struct{})
	go func() {
		defer close(readDone)
		for {
			if _, _, err := conn.Read(context.Background()); err != nil {
				return
			}
		}
	}()
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	if err := api.Close(ctx); err != nil {
		t.Fatal(err)
	}
	select {
	case <-readDone:
	case <-ctx.Done():
		t.Fatal("client not closed")
	}
	stats := api.Stats()
	if stats.Connections != 0 || stats.QueuedBytes != 0 {
		t.Fatal("shutdown leaked resources", stats)
	}
	response, err := http.Get("http" + strings.TrimSuffix(strings.TrimPrefix(url, "ws"), "/ws") + "/readyz")
	if err != nil {
		t.Fatal(err)
	}
	defer response.Body.Close()
	if response.StatusCode != http.StatusServiceUnavailable {
		t.Fatal("readiness remains healthy after close")
	}
}

func TestSendBudgets(t *testing.T) {
	for _, global := range []bool{false, true} {
		api := NewWithOptions(room.New(room.Config{}), Options{QueueDepth: 1, PeerQueueBytes: 20, TotalQueueBytes: 20})
		ctx, cancel := context.WithCancel(context.Background())
		p := &peer{server: api, ctx: ctx, cancel: cancel, queue: make(chan []byte, 1)}
		p.Send(make([]byte, 10))
		if api.queued.Load() != 10 {
			t.Fatal("queued bytes not reserved")
		}
		var other *peer
		if global {
			otherCtx, otherCancel := context.WithCancel(context.Background())
			other = &peer{server: api, ctx: otherCtx, cancel: otherCancel, queue: make(chan []byte, 1)}
			other.Send(make([]byte, 15))
			other.shutdown()
			other.drain()
		} else {
			p.Send(make([]byte, 10))
		}
		if api.queued.Load() != 10 || api.slow.Load() != 1 {
			t.Fatal("queue overflow leaked reservation", api.queued.Load())
		}
		p.shutdown()
		p.drain()
		if api.queued.Load() != 0 {
			t.Fatal("queue bytes not released")
		}
	}
}
