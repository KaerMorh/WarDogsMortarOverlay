package main

import (
	"context"
	"encoding/json"
	"errors"
	"log"
	"os"
	"os/signal"
	"sync/atomic"
	"syscall"
	"time"

	"github.com/coder/websocket"
	"wardogs/server/internal/room"
	"wardogs/server/internal/testbots"
)

const queueCapacity = 64

type request struct {
	message room.Message
	done    chan error
	ctx     context.Context
}
type botClient struct {
	url, roomCode, uid, name, role string
	events                         chan<- room.Event
	requests                       chan request
	connected                      atomic.Bool
}

func newBot(url, roomCode, uid, name, role string, events chan<- room.Event) *botClient {
	return &botClient{url: url, roomCode: roomCode, uid: uid, name: name, role: role, events: events, requests: make(chan request, 32)}
}
func (b *botClient) send(ctx context.Context, message room.Message) error {
	if !b.connected.Load() {
		return errors.New("bot not joined")
	}
	requestCtx, cancel := context.WithTimeout(ctx, 8*time.Second)
	defer cancel()
	message.V = room.Protocol
	message.RequestID = room.NewID()
	r := request{message: message, done: make(chan error, 1), ctx: requestCtx}
	select {
	case b.requests <- r:
	case <-requestCtx.Done():
		return requestCtx.Err()
	}
	select {
	case err := <-r.done:
		return err
	case <-requestCtx.Done():
		return requestCtx.Err()
	}
}
func (b *botClient) run(ctx context.Context) {
	backoff := 2 * time.Second
	for ctx.Err() == nil {
		conn, _, err := websocket.Dial(ctx, b.url, nil)
		if err == nil {
			join := room.Message{V: room.Protocol, Type: "join", UID: b.uid, Room: b.roomCode, Callsign: b.name, Role: b.role, Weapon: "mortar", Map: "bakurani"}
			data, _ := json.Marshal(join)
			writeCtx, cancel := context.WithTimeout(ctx, 5*time.Second)
			err = conn.Write(writeCtx, websocket.MessageText, data)
			cancel()
		}
		if err == nil {
			backoff = 2 * time.Second
			err = b.connectedLoop(ctx, conn)
			_ = conn.CloseNow()
		}
		b.connected.Store(false)
		if ctx.Err() != nil {
			return
		}
		log.Printf("bot=%s disconnected error=%v retry_in=%s", b.role, err, backoff)
		select {
		case <-time.After(backoff):
		case <-ctx.Done():
			return
		}
		if backoff < 30*time.Second {
			backoff *= 2
			if backoff > 30*time.Second {
				backoff = 30 * time.Second
			}
		}
	}
}
func (b *botClient) connectedLoop(ctx context.Context, conn *websocket.Conn) error {
	incoming := make(chan room.Event, 8)
	readError := make(chan error, 1)
	go func() {
		for {
			_, data, err := conn.Read(ctx)
			if err != nil {
				readError <- err
				return
			}
			var event room.Event
			if json.Unmarshal(data, &event) != nil || event.V != room.Protocol {
				readError <- errors.New("invalid server event")
				return
			}
			select {
			case incoming <- event:
			case <-ctx.Done():
				return
			}
		}
	}()
	for {
		select {
		case <-ctx.Done():
			return ctx.Err()
		case err := <-readError:
			return err
		case event := <-incoming:
			if event.Type == "snapshot" {
				b.connected.Store(true)
			}
			if b.events != nil {
				select {
				case b.events <- event:
				case <-ctx.Done():
					return ctx.Err()
				}
			}
		case request := <-b.requests:
			if request.ctx.Err() != nil {
				request.done <- request.ctx.Err()
				continue
			}
			if !b.connected.Load() {
				request.done <- errors.New("bot not joined")
				continue
			}
			data, _ := json.Marshal(request.message)
			writeCtx, cancel := context.WithTimeout(ctx, 5*time.Second)
			err := conn.Write(writeCtx, websocket.MessageText, data)
			cancel()
			request.done <- err
			if err != nil {
				return err
			}
		}
	}
}

func main() {
	url := os.Getenv("WARDOGS_BOT_URL")
	if url == "" {
		url = "ws://127.0.0.1:8080/ws"
	}
	roomCode := os.Getenv("WARDOGS_BOT_ROOM")
	if roomCode == "" {
		roomCode = "testzz4z"
	}
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	events := make(chan room.Event, 128)
	scout := newBot(url, roomCode, testbots.ScoutUID, "测试侦察兵 BOT", "scout", events)
	gunner := newBot(url, roomCode, testbots.GunnerUID, "测试炮兵 BOT", "gunner", nil)
	go scout.run(ctx)
	go gunner.run(ctx)
	actions := make(chan testbots.Action, queueCapacity)
	var dropped uint64
	go func() {
		controller := testbots.NewController()
		for {
			select {
			case <-ctx.Done():
				return
			case event := <-events:
				for _, action := range controller.Apply(event) {
					select {
					case actions <- action:
					default:
						dropped++
						if dropped == 1 || dropped%32 == 0 {
							log.Printf("action_queue_full dropped=%d", dropped)
						}
					}
				}
			}
		}
	}()
	for {
		select {
		case <-ctx.Done():
			return
		case action := <-actions:
			switch action.Kind {
			case "skip_publish_bounds":
				log.Printf("scout response skipped reason=offset_out_of_bounds")
			case "publish":
				err := scout.send(ctx, room.Message{Type: "publish", TaskID: room.NewID(), Point: action.Point})
				if err != nil {
					log.Printf("scout response skipped error=%v", err)
				}
			case "beacon":
				origin := &room.Point{Map: action.Point.Map, X: action.Point.X + 2, Y: action.Point.Y + 2}
				if !origin.Valid() {
					log.Printf("gunner response skipped reason=offset_out_of_bounds")
					continue
				}
				if err := gunner.send(ctx, room.Message{Type: "origin", Point: origin}); err != nil {
					log.Printf("gunner origin skipped error=%v", err)
					continue
				}
				if err := gunner.send(ctx, room.Message{Type: "target", Point: action.Point, Solved: true}); err != nil {
					log.Printf("gunner target skipped error=%v", err)
					continue
				}
				select {
				case <-time.After(time.Second):
				case <-ctx.Done():
					return
				}
				if err := gunner.send(ctx, room.Message{Type: "target", Point: nil}); err != nil {
					log.Printf("gunner clear skipped error=%v", err)
				}
			}
		}
	}
}
