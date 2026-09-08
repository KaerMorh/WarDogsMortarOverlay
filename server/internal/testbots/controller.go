package testbots

import (
	"sort"
	"wardogs/server/internal/room"
)

const (
	ScoutUID  = "7b0d9f6c-6cc4-4bc3-9f4c-6cfffa9b83f1"
	GunnerUID = "69d97a48-58dc-4ef0-af38-d6de2481e7a6"
)

type Action struct {
	Kind  string
	Point *room.Point
}

type memberState struct {
	session   string
	target    *room.Point
	responded bool
}

type Controller struct {
	members   map[string]memberState
	seen      map[string]bool
	seenOrder []string
}

func NewController() *Controller {
	return &Controller{members: make(map[string]memberState), seen: make(map[string]bool)}
}

func (c *Controller) Apply(event room.Event) []Action {
	switch event.Type {
	case "snapshot":
		c.members = make(map[string]memberState)
		c.seen = make(map[string]bool)
		c.seenOrder = nil
		for _, member := range event.Members {
			c.baseline(member)
		}
		return nil
	case "removed":
		if old, ok := c.members[event.UID]; ok {
			delete(c.members, event.UID)
			for key := range c.seen {
				if len(key) > len(old.session) && key[:len(old.session)] == old.session {
					delete(c.seen, key)
				}
			}
		}
		return nil
	case "member":
		if event.Member != nil {
			return c.member(event.Member)
		}
	}
	return nil
}

func (c *Controller) baseline(member *room.Member) {
	if member == nil || isBot(member.UID) {
		return
	}
	c.members[member.UID] = memberState{session: member.SessionID, target: clone(member.Target)}
	for _, task := range member.Tasks {
		c.remember(member.SessionID + "/" + task.ID)
	}
}

func (c *Controller) member(member *room.Member) []Action {
	if isBot(member.UID) {
		return nil
	}
	previous, known := c.members[member.UID]
	if !known || previous.session != member.SessionID {
		c.baseline(member)
		return nil
	}
	actions := make([]Action, 0, 2)
	targetChanged := !pointsEqual(previous.target, member.Target)
	responded := previous.responded
	if targetChanged {
		responded = false
	}
	if member.Role == "gunner" && member.Target != nil && member.Origin != nil && member.Target.Map == member.Origin.Map && !responded {
		if point := offset(member.Target, 1); point != nil {
			actions = append(actions, Action{Kind: "publish", Point: point})
		} else {
			actions = append(actions, Action{Kind: "skip_publish_bounds"})
		}
		responded = true
	}
	c.members[member.UID] = memberState{session: member.SessionID, target: clone(member.Target), responded: responded}

	tasks := append([]*room.Task(nil), member.Tasks...)
	sort.Slice(tasks, func(i, j int) bool { return tasks[i].Sequence < tasks[j].Sequence })
	for _, task := range tasks {
		key := member.SessionID + "/" + task.ID
		if c.seen[key] {
			continue
		}
		c.remember(key)
		if member.Role == "scout" && task.Point != nil {
			actions = append(actions, Action{Kind: "beacon", Point: clone(task.Point)})
		}
	}
	return actions
}

func (c *Controller) remember(key string) {
	if c.seen[key] {
		return
	}
	c.seen[key] = true
	c.seenOrder = append(c.seenOrder, key)
	for len(c.seenOrder) > 4096 {
		delete(c.seen, c.seenOrder[0])
		c.seenOrder = c.seenOrder[1:]
	}
}

func isBot(uid string) bool { return uid == ScoutUID || uid == GunnerUID }
func clone(point *room.Point) *room.Point {
	if point == nil {
		return nil
	}
	copy := *point
	return &copy
}
func pointsEqual(a, b *room.Point) bool { return a == nil && b == nil || room.Same(a, b) }
func offset(point *room.Point, amount float64) *room.Point {
	result := &room.Point{Map: point.Map, X: point.X + amount, Y: point.Y + amount}
	if !result.Valid() {
		return nil
	}
	return result
}
