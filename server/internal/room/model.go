package room

import (
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"errors"
	"math"
	"regexp"
	"strings"
	"time"
	"unicode"
	"unicode/utf8"
)

const Protocol = 1

type Point struct {
	Map string  `json:"map"`
	X   float64 `json:"x"`
	Y   float64 `json:"y"`
}

func (p *Point) Valid() bool {
	return p != nil && validMap(p.Map) && finite(p.X) && finite(p.Y) && p.X >= -.03 && p.X <= 163.81 && p.Y >= -.01 && p.Y <= 163.83
}
func finite(n float64) bool { return !math.IsNaN(n) && !math.IsInf(n, 0) }
func Same(a, b *Point) bool {
	return a != nil && b != nil && a.Map == b.Map && math.Round(a.X*1e6) == math.Round(b.X*1e6) && math.Round(a.Y*1e6) == math.Round(b.Y*1e6)
}
func Equal(a, b *Point) bool  { return a == nil && b == nil || a != nil && b != nil && *a == *b }
func validMap(s string) bool  { return s == "bakurani" || s == "ozeti" }
func validRole(s string) bool { return s == "gunner" || s == "scout" }

var uuidPattern = regexp.MustCompile(`^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$`)
var roomPattern = regexp.MustCompile(`^[a-z0-9_-]{4,32}$`)

func NewID() string {
	var b [16]byte
	if _, err := rand.Read(b[:]); err != nil {
		panic(err)
	}
	b[6] = (b[6] & 15) | 64
	b[8] = (b[8] & 63) | 128
	s := hex.EncodeToString(b[:])
	return s[:8] + "-" + s[8:12] + "-" + s[12:16] + "-" + s[16:20] + "-" + s[20:]
}
func ValidName(s string) bool {
	if strings.TrimSpace(s) != s || utf8.RuneCountInString(s) < 1 || utf8.RuneCountInString(s) > 32 {
		return false
	}
	for _, r := range s {
		if unicode.IsControl(r) || unicode.Is(unicode.Cf, r) {
			return false
		}
	}
	return true
}

type Actor struct {
	UID  string `json:"uid"`
	Name string `json:"name"`
}
type Task struct {
	ID        string    `json:"id"`
	Sequence  int64     `json:"sequence"`
	CreatedAt time.Time `json:"createdAt"`
	Point     *Point    `json:"point"`
	SolvedBy  []Actor   `json:"solvedBy"`
}
type Member struct {
	UID         string     `json:"uid"`
	SessionID   string     `json:"sessionId"`
	Callsign    string     `json:"callsign"`
	DisplayName string     `json:"displayName"`
	Role        string     `json:"role"`
	Map         string     `json:"map"`
	Online      bool       `json:"online"`
	OfflineAt   *time.Time `json:"offlineAt,omitempty"`
	Joined      int64      `json:"joined"`
	Origin      *Point     `json:"origin"`
	Target      *Point     `json:"target"`
	Solved      bool       `json:"solved"`
	Tasks       []*Task    `json:"tasks"`
	seen        []string
	sink        Sink
}
type Message struct {
	V         int    `json:"v"`
	Type      string `json:"type"`
	RequestID string `json:"requestId,omitempty"`
	Room      string `json:"room,omitempty"`
	UID       string `json:"uid,omitempty"`
	Callsign  string `json:"callsign,omitempty"`
	Role      string `json:"role,omitempty"`
	Map       string `json:"map,omitempty"`
	TaskID    string `json:"taskId,omitempty"`
	Point     *Point `json:"point"`
	Solved    bool   `json:"solved,omitempty"`
}
type Event struct {
	V         int       `json:"v"`
	Type      string    `json:"type"`
	RoomID    string    `json:"roomId,omitempty"`
	Room      string    `json:"room,omitempty"`
	Map       string    `json:"map,omitempty"`
	Revision  int64     `json:"revision,omitempty"`
	SessionID string    `json:"sessionId,omitempty"`
	RequestID string    `json:"requestId,omitempty"`
	Members   []*Member `json:"members,omitempty"`
	Member    *Member   `json:"member,omitempty"`
	UID       string    `json:"uid,omitempty"`
	Code      string    `json:"code,omitempty"`
}

// Send must be non-blocking. The encoded bytes are immutable and may be shared
// by all recipients. Never call Store from a Sink callback (room lock is held).
type Sink interface {
	Send([]byte)
	Stop()
}

func encode(e Event) []byte {
	data, err := json.Marshal(e)
	if err != nil {
		panic("invalid internal room event: " + err.Error())
	}
	return data
}
func send(s Sink, e Event) { s.Send(encode(e)) }

type Lease struct{ Room, UID, SessionID string }

var ErrInvalid = errors.New("invalid_message")
var ErrReplaced = errors.New("session_replaced")
