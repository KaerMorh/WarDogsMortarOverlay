package room

import (
	"errors"
	"sort"
	"strconv"
	"strings"
	"sync"
	"time"
)

type Config struct {
	MaxRooms, MaxMembers int
	OfflineTTL           time.Duration
}
type State struct {
	ID, Code, Map      string
	Revision, Sequence int64
	Members            map[string]*Member
	Identities         map[string]string
}
type Store struct {
	mu     sync.Mutex
	rooms  map[string]*State
	config Config
	now    func() time.Time
}

func New(config Config) *Store {
	if config.MaxRooms <= 0 {
		config.MaxRooms = 64
	}
	if config.MaxMembers <= 0 {
		config.MaxMembers = 32
	}
	if config.OfflineTTL <= 0 {
		config.OfflineTTL = 3 * time.Minute
	}
	return &Store{rooms: make(map[string]*State), config: config, now: time.Now}
}
func (s *Store) Join(msg Message, sink Sink) (Lease, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	msg.Room = strings.ToLower(strings.TrimSpace(msg.Room))
	if msg.V != Protocol || msg.Type != "join" || !roomPattern.MatchString(msg.Room) || !uuidPattern.MatchString(msg.UID) || !ValidName(msg.Callsign) || !validRole(msg.Role) || !validMap(msg.Map) {
		return Lease{}, ErrInvalid
	}
	s.sweepLocked()
	r := s.rooms[msg.Room]
	if r == nil {
		if len(s.rooms) >= s.config.MaxRooms {
			return Lease{}, errors.New("server_full")
		}
		r = &State{ID: NewID(), Code: msg.Room, Map: msg.Map, Members: make(map[string]*Member), Identities: make(map[string]string)}
		s.rooms[msg.Room] = r
	}
	old := r.Members[msg.UID]
	if old == nil && len(r.Members) >= s.config.MaxMembers {
		return Lease{}, errors.New("room_full")
	}
	if old != nil && old.sink != nil {
		send(old.sink, Event{V: Protocol, Type: "error", Code: "session_replaced"})
		old.sink.Stop()
	}
	r.Sequence++
	m := &Member{UID: msg.UID, SessionID: NewID(), Callsign: msg.Callsign, DisplayName: msg.Callsign, Role: msg.Role, Map: msg.Map, Online: true, Joined: r.Sequence, Tasks: []*Task{}, sink: sink}
	r.Members[m.UID] = m
	renamed := s.names(r)
	r.Revision++
	send(sink, s.snapshot(r, m, msg.RequestID))
	s.emit(r, Event{Type: "member", Member: m})
	for _, member := range renamed {
		if member != m {
			s.emit(r, Event{Type: "member", Member: member})
		}
	}
	s.emitIdentities(r, append(renamed, m))
	return Lease{Room: r.Code, UID: m.UID, SessionID: m.SessionID}, nil
}
func (s *Store) snapshot(r *State, m *Member, requestID string) Event {
	return Event{V: Protocol, Type: "snapshot", RoomID: r.ID, Room: r.Code, Map: r.Map, Revision: r.Revision, SessionID: m.SessionID, RequestID: requestID, Members: ordered(r), Identities: identities(r)}
}
func identities(r *State) []Identity {
	out := make([]Identity, 0, len(r.Identities))
	for uid, name := range r.Identities {
		out = append(out, Identity{UID: uid, Name: name})
	}
	sort.Slice(out, func(i, j int) bool { return out[i].UID < out[j].UID })
	return out
}
func ordered(r *State) []*Member {
	out := make([]*Member, 0, len(r.Members))
	for _, m := range r.Members {
		out = append(out, m)
	}
	sort.Slice(out, func(i, j int) bool { return out[i].Joined < out[j].Joined })
	return out
}
func (s *Store) names(r *State) []*Member {
	changed := []*Member{}
	groups := map[string][]*Member{}
	for _, m := range ordered(r) {
		groups[m.Callsign] = append(groups[m.Callsign], m)
	}
	for name, g := range groups {
		for i, m := range g {
			display := name
			if len(g) > 1 {
				display = name + "#" + strconv.Itoa(i+1)
			}
			if m.DisplayName != display {
				m.DisplayName = display
				changed = append(changed, m)
			}
			r.Identities[m.UID] = display
		}
	}
	sort.Slice(changed, func(i, j int) bool { return changed[i].Joined < changed[j].Joined })
	return changed
}
func (s *Store) emitIdentities(r *State, members []*Member) {
	seen := map[string]bool{}
	for _, m := range members {
		if seen[m.UID] {
			continue
		}
		seen[m.UID] = true
		identity := Identity{UID: m.UID, Name: m.DisplayName}
		s.emit(r, Event{Type: "identity", Identity: &identity})
	}
}
func (s *Store) emit(r *State, e Event) {
	r.Revision++
	e.V = Protocol
	e.RoomID = r.ID
	e.Revision = r.Revision
	data := encode(e)
	for _, m := range r.Members {
		if m.Online && m.sink != nil {
			m.sink.Send(data)
		}
	}
}
func (s *Store) current(l Lease) (*State, *Member) {
	r := s.rooms[l.Room]
	if r == nil {
		return nil, nil
	}
	m := r.Members[l.UID]
	if m == nil || m.SessionID != l.SessionID || !m.Online {
		return nil, nil
	}
	return r, m
}
func (s *Store) Apply(l Lease, msg Message) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	r, m := s.current(l)
	if m == nil {
		return ErrReplaced
	}
	if msg.V != Protocol {
		return ErrInvalid
	}
	switch msg.Type {
	case "profile":
		if !ValidName(msg.Callsign) || !validRole(msg.Role) || !validMap(msg.Map) {
			return ErrInvalid
		}
		if m.Callsign == msg.Callsign && m.Role == msg.Role && m.Map == msg.Map {
			break
		}
		m.Callsign = msg.Callsign
		m.Role = msg.Role
		m.Map = msg.Map
		renamed := s.names(r)
		s.emit(r, Event{Type: "member", Member: m})
		for _, member := range renamed {
			if member != m {
				s.emit(r, Event{Type: "member", Member: member})
			}
		}
		s.emitIdentities(r, renamed)
	case "sync":
		if !m.lastSync.IsZero() && s.now().Sub(m.lastSync) < 2*time.Second {
			return errors.New("sync_rate_limited")
		}
		m.lastSync = s.now()
		send(m.sink, s.snapshot(r, m, msg.RequestID))
	case "origin":
		if msg.Point != nil && !msg.Point.Valid() {
			return ErrInvalid
		}
		if Equal(m.Origin, msg.Point) {
			break
		}
		m.Origin = msg.Point
		s.emit(r, Event{Type: "member", Member: m})
	case "target":
		if msg.Point != nil && !msg.Point.Valid() || msg.Solved && (msg.Point == nil || m.Role != "gunner") {
			return ErrInvalid
		}
		if Equal(m.Target, msg.Point) && m.Solved == msg.Solved {
			break
		}
		m.Target = msg.Point
		m.Solved = msg.Solved
		if msg.Solved {
			for _, owner := range ordered(r) {
				changed := false
				for _, task := range owner.Tasks {
					if Same(task.Point, msg.Point) && remember(task, m) {
						changed = true
					}
				}
				if changed && owner != m {
					s.emit(r, Event{Type: "member", Member: owner})
				}
			}
		}
		s.emit(r, Event{Type: "member", Member: m})
	case "publish":
		if !uuidPattern.MatchString(msg.TaskID) || !msg.Point.Valid() {
			return ErrInvalid
		}
		for _, id := range m.seen {
			if id == msg.TaskID {
				send(m.sink, Event{V: Protocol, Type: "ack", RoomID: r.ID, RequestID: msg.RequestID})
				return nil
			}
		}
		r.Sequence++
		task := &Task{ID: msg.TaskID, Sequence: r.Sequence, CreatedAt: s.now().UTC(), Point: msg.Point, SolvedBy: []string{}}
		for _, gunner := range ordered(r) {
			if gunner.Role == "gunner" && gunner.Online && gunner.Solved && Same(gunner.Target, task.Point) {
				remember(task, gunner)
			}
		}
		m.Tasks = append([]*Task{task}, m.Tasks...)
		if len(m.Tasks) > 3 {
			m.Tasks = m.Tasks[:3]
		}
		m.seen = append(m.seen, msg.TaskID)
		if len(m.seen) > 256 {
			m.seen = m.seen[len(m.seen)-256:]
		}
		s.emit(r, Event{Type: "member", Member: m})
	default:
		return ErrInvalid
	}
	send(m.sink, Event{V: Protocol, Type: "ack", RoomID: r.ID, RequestID: msg.RequestID})
	return nil
}
func remember(task *Task, m *Member) bool {
	for _, uid := range task.SolvedBy {
		if uid == m.UID {
			return false
		}
	}
	if len(task.SolvedBy) >= MaxSolverRefs {
		return false
	}
	task.SolvedBy = append(task.SolvedBy, m.UID)
	return true
}
func (s *Store) Leave(l Lease) {
	s.mu.Lock()
	defer s.mu.Unlock()
	r, m := s.current(l)
	if m == nil {
		return
	}
	now := s.now().UTC()
	m.Online = false
	m.OfflineAt = &now
	m.sink = nil
	s.emit(r, Event{Type: "member", Member: m})
}
func (s *Store) Sweep() { s.mu.Lock(); defer s.mu.Unlock(); s.sweepLocked() }
func (s *Store) sweepLocked() {
	now := s.now()
	for code, r := range s.rooms {
		changed := false
		for uid, m := range r.Members {
			if !m.Online && m.OfflineAt != nil && now.Sub(*m.OfflineAt) >= s.config.OfflineTTL {
				delete(r.Members, uid)
				s.emit(r, Event{Type: "removed", UID: uid})
				changed = true
			}
		}
		if len(r.Members) == 0 {
			delete(s.rooms, code)
		} else if changed {
			s.pruneIdentities(r)
			for _, m := range s.names(r) {
				s.emit(r, Event{Type: "member", Member: m})
			}
		}
	}
}
func (s *Store) pruneIdentities(r *State) {
	keep := map[string]bool{}
	for uid, m := range r.Members {
		keep[uid] = true
		for _, task := range m.Tasks {
			for _, solver := range task.SolvedBy {
				keep[solver] = true
			}
		}
	}
	for uid := range r.Identities {
		if !keep[uid] {
			delete(r.Identities, uid)
		}
	}
}
func (s *Store) Counts() (int, int) {
	s.mu.Lock()
	defer s.mu.Unlock()
	n := 0
	for _, r := range s.rooms {
		n += len(r.Members)
	}
	return len(s.rooms), n
}
