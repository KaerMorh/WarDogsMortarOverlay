package transport

import (
	"context"
	"encoding/json"
	"net"
	"net/http"
	"sync"
	"sync/atomic"
	"time"

	"github.com/coder/websocket"
	"wardogs/server/internal/room"
)

type Options struct {
	MaxConnections                                                 int
	QueueDepth                                                     int
	PeerQueueBytes                                                 int64
	TotalQueueBytes                                                int64
	JoinTimeout, WriteTimeout, HeartbeatInterval, HeartbeatTimeout time.Duration
	RatePerSecond                                                  float64
	Burst                                                          int
	JoinRatePerSecond                                              float64
	JoinBurst                                                      int
}

func (o Options) defaults() Options {
	if o.MaxConnections <= 0 {
		o.MaxConnections = 512
	}
	if o.QueueDepth <= 0 {
		o.QueueDepth = 128
	}
	if o.PeerQueueBytes <= 0 {
		o.PeerQueueBytes = 1024 * 1024
	}
	if o.TotalQueueBytes <= 0 {
		o.TotalQueueBytes = 32 * 1024 * 1024
	}
	if o.JoinTimeout <= 0 {
		o.JoinTimeout = 10 * time.Second
	}
	if o.WriteTimeout <= 0 {
		o.WriteTimeout = 5 * time.Second
	}
	if o.HeartbeatInterval <= 0 {
		o.HeartbeatInterval = 15 * time.Second
	}
	if o.HeartbeatTimeout <= 0 {
		o.HeartbeatTimeout = 10 * time.Second
	}
	if o.RatePerSecond <= 0 {
		o.RatePerSecond = 10
	}
	if o.Burst <= 0 {
		o.Burst = 20
	}
	if o.JoinRatePerSecond <= 0 {
		o.JoinRatePerSecond = .5
	}
	if o.JoinBurst <= 0 {
		o.JoinBurst = 20
	}
	return o
}

type Server struct {
	Store    *room.Store
	slots    chan struct{}
	options  Options
	mu       sync.Mutex
	peers    map[*peer]struct{}
	joiners  map[string]*joinBucket
	stopping bool
	workers  sync.WaitGroup
	queued   atomic.Int64
	accepted atomic.Uint64
	rejected atomic.Uint64
	received atomic.Uint64
	sent     atomic.Uint64
	slow     atomic.Uint64
}
type joinBucket struct {
	tokens float64
	last   time.Time
}

func New(store *room.Store, maxConnections int) *Server {
	return NewWithOptions(store, Options{MaxConnections: maxConnections})
}
func NewWithOptions(store *room.Store, options Options) *Server {
	options = options.defaults()
	return &Server{Store: store, options: options, slots: make(chan struct{}, options.MaxConnections), peers: make(map[*peer]struct{}), joiners: make(map[string]*joinBucket)}
}
func (s *Server) Handler() http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /healthz", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"status":"ok","protocol":2}`))
	})
	mux.HandleFunc("GET /readyz", func(w http.ResponseWriter, r *http.Request) {
		s.mu.Lock()
		stopping := s.stopping
		s.mu.Unlock()
		if stopping {
			http.Error(w, "stopping", http.StatusServiceUnavailable)
			return
		}
		w.WriteHeader(http.StatusOK)
		_, _ = w.Write([]byte("ready\n"))
	})
	mux.HandleFunc("GET /ws", s.connect)
	return mux
}

type Stats struct {
	Connections, Rooms, Members                         int
	QueuedBytes                                         int64
	Accepted, Rejected, Received, Sent, SlowDisconnects uint64
}

func (s *Server) Stats() Stats {
	s.mu.Lock()
	connections := len(s.peers)
	s.mu.Unlock()
	rooms, members := s.Store.Counts()
	return Stats{connections, rooms, members, s.queued.Load(), s.accepted.Load(), s.rejected.Load(), s.received.Load(), s.sent.Load(), s.slow.Load()}
}

// Close explicitly drains upgraded WebSockets, which http.Server.Shutdown does
// not own. No peers can register after stopping is set under the same mutex.
func (s *Server) Close(ctx context.Context) error {
	s.mu.Lock()
	s.stopping = true
	peers := make([]*peer, 0, len(s.peers))
	for p := range s.peers {
		peers = append(peers, p)
	}
	s.mu.Unlock()
	for _, p := range peers {
		p.closeWithStatus(websocket.StatusGoingAway, "server_stopping")
	}
	done := make(chan struct{})
	go func() { s.workers.Wait(); close(done) }()
	select {
	case <-done:
		return nil
	case <-ctx.Done():
		for _, p := range peers {
			p.shutdown()
		}
		return ctx.Err()
	}
}

type peer struct {
	server     *Server
	conn       *websocket.Conn
	ctx        context.Context
	cancel     context.CancelFunc
	mu         sync.Mutex
	stopped    bool
	queue      chan []byte
	queued     int64
	closeOnce  sync.Once
	writerDone chan struct{}
}

func (p *peer) sendEvent(e room.Event) {
	data, err := json.Marshal(e)
	if err != nil {
		p.shutdown()
		return
	}
	p.Send(data)
}
func (p *peer) Send(data []byte) {
	p.mu.Lock()
	if p.stopped {
		p.mu.Unlock()
		return
	}
	size := int64(len(data))
	if p.queued+size > p.server.options.PeerQueueBytes {
		p.mu.Unlock()
		p.server.slow.Add(1)
		p.shutdown()
		return
	}
	if p.server.queued.Add(size) > p.server.options.TotalQueueBytes {
		p.server.queued.Add(-size)
		p.mu.Unlock()
		p.server.slow.Add(1)
		p.shutdown()
		return
	}
	select {
	case p.queue <- data:
		p.queued += size
		p.mu.Unlock()
	default:
		p.server.queued.Add(-size)
		p.mu.Unlock()
		p.server.slow.Add(1)
		p.shutdown()
	}
}
func (p *peer) release(size int) {
	p.mu.Lock()
	p.queued -= int64(size)
	p.mu.Unlock()
	p.server.queued.Add(-int64(size))
}
func (p *peer) shutdown() {
	p.mu.Lock()
	if p.stopped {
		p.mu.Unlock()
		return
	}
	p.stopped = true
	p.mu.Unlock()
	p.cancel()
	if p.conn != nil {
		_ = p.conn.CloseNow()
	}
}
func (p *peer) closeWithStatus(code websocket.StatusCode, reason string) {
	p.closeOnce.Do(func() { go func() { _ = p.conn.Close(code, reason); p.shutdown() }() })
}
func (p *peer) Stop() { p.closeWithStatus(websocket.StatusCode(4001), "session_replaced") }
func (p *peer) drain() {
	for {
		select {
		case data := <-p.queue:
			p.release(len(data))
		default:
			return
		}
	}
}
func (p *peer) write() {
	defer close(p.writerDone)
	defer p.drain()
	defer p.shutdown()
	for {
		select {
		case <-p.ctx.Done():
			return
		case data := <-p.queue:
			ctx, cancel := context.WithTimeout(p.ctx, p.server.options.WriteTimeout)
			err := p.conn.Write(ctx, websocket.MessageText, data)
			cancel()
			p.release(len(data))
			if err != nil {
				return
			}
			p.server.sent.Add(1)
		}
	}
}
func (p *peer) heartbeat() {
	tick := time.NewTicker(p.server.options.HeartbeatInterval)
	defer tick.Stop()
	for {
		select {
		case <-p.ctx.Done():
			return
		case <-tick.C:
			ctx, cancel := context.WithTimeout(p.ctx, p.server.options.HeartbeatTimeout)
			err := p.conn.Ping(ctx)
			cancel()
			if err != nil {
				p.shutdown()
				return
			}
		}
	}
}
func (s *Server) connect(w http.ResponseWriter, r *http.Request) {
	if !s.allowJoin(r.RemoteAddr, time.Now()) {
		s.rejected.Add(1)
		http.Error(w, "too many join attempts", http.StatusTooManyRequests)
		return
	}
	select {
	case s.slots <- struct{}{}:
		defer func() { <-s.slots }()
	default:
		s.rejected.Add(1)
		http.Error(w, "busy", http.StatusServiceUnavailable)
		return
	}
	s.mu.Lock()
	stopping := s.stopping
	s.mu.Unlock()
	if stopping {
		http.Error(w, "stopping", http.StatusServiceUnavailable)
		return
	}
	conn, err := websocket.Accept(w, r, nil)
	if err != nil {
		s.rejected.Add(1)
		return
	}
	conn.SetReadLimit(8 * 1024)
	ctx, cancel := context.WithCancel(r.Context())
	p := &peer{server: s, conn: conn, ctx: ctx, cancel: cancel, queue: make(chan []byte, s.options.QueueDepth), writerDone: make(chan struct{})}
	s.mu.Lock()
	if s.stopping {
		s.mu.Unlock()
		p.shutdown()
		return
	}
	s.peers[p] = struct{}{}
	s.workers.Add(1)
	s.mu.Unlock()
	s.accepted.Add(1)
	go p.write()
	defer func() {
		p.shutdown()
		<-p.writerDone
		s.mu.Lock()
		delete(s.peers, p)
		s.mu.Unlock()
		s.workers.Done()
	}()
	joinCtx, joinCancel := context.WithTimeout(ctx, s.options.JoinTimeout)
	kind, data, err := conn.Read(joinCtx)
	joinCancel()
	if err != nil {
		return
	}
	var msg room.Message
	if kind != websocket.MessageText || json.Unmarshal(data, &msg) != nil {
		p.closeWithStatus(websocket.StatusPolicyViolation, "invalid_join")
		return
	}
	lease, err := s.Store.Join(msg, p)
	if err != nil {
		// No room messages have been queued for a rejected join.
		response, _ := json.Marshal(room.Event{V: room.Protocol, Type: "error", Code: err.Error()})
		deadline, c := context.WithTimeout(ctx, s.options.WriteTimeout)
		_ = conn.Write(deadline, websocket.MessageText, response)
		c()
		s.rejected.Add(1)
		return
	}
	defer s.Store.Leave(lease)
	go p.heartbeat()
	tokens := float64(s.options.Burst)
	last := time.Now()
	for {
		kind, data, err = conn.Read(ctx)
		if err != nil {
			return
		}
		s.received.Add(1)
		now := time.Now()
		tokens += now.Sub(last).Seconds() * s.options.RatePerSecond
		last = now
		if tokens > float64(s.options.Burst) {
			tokens = float64(s.options.Burst)
		}
		if tokens < 1 {
			s.rejected.Add(1)
			p.closeWithStatus(websocket.StatusPolicyViolation, "rate_limited")
			return
		}
		tokens--
		msg = room.Message{}
		if kind != websocket.MessageText || json.Unmarshal(data, &msg) != nil {
			s.rejected.Add(1)
			p.closeWithStatus(websocket.StatusPolicyViolation, "invalid_message")
			return
		}
		if err = s.Store.Apply(lease, msg); err != nil {
			p.sendEvent(room.Event{V: room.Protocol, Type: "error", RequestID: msg.RequestID, Code: err.Error()})
			if err == room.ErrReplaced {
				return
			}
		}
	}
}

func (s *Server) allowJoin(remote string, now time.Time) bool {
	host, _, err := net.SplitHostPort(remote)
	if err != nil {
		host = remote
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	const maxJoinIPs = 4096
	const idleTTL = 10 * time.Minute
	if len(s.joiners) >= maxJoinIPs {
		for ip, bucket := range s.joiners {
			if now.Sub(bucket.last) >= idleTTL {
				delete(s.joiners, ip)
			}
		}
	}
	bucket := s.joiners[host]
	if bucket == nil {
		if len(s.joiners) >= maxJoinIPs {
			return false
		}
		bucket = &joinBucket{tokens: float64(s.options.JoinBurst), last: now}
		s.joiners[host] = bucket
	}
	bucket.tokens += now.Sub(bucket.last).Seconds() * s.options.JoinRatePerSecond
	if bucket.tokens > float64(s.options.JoinBurst) {
		bucket.tokens = float64(s.options.JoinBurst)
	}
	bucket.last = now
	if bucket.tokens < 1 {
		return false
	}
	bucket.tokens--
	return true
}
