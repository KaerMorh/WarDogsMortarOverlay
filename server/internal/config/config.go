// Package config validates deployment settings before opening any sockets.
package config

import (
	"fmt"
	"net"
	"strconv"
)

type Config struct {
	Address                              string
	MaxRooms, MaxMembers, MaxConnections int
}

func Load(getenv func(string) string) (Config, error) {
	c := Config{Address: "127.0.0.1:8080", MaxRooms: 64, MaxMembers: 32, MaxConnections: 512}
	if addr := getenv("WARDOGS_ADDR"); addr != "" {
		c.Address = addr
	}
	if _, port, err := net.SplitHostPort(c.Address); err != nil {
		return c, fmt.Errorf("WARDOGS_ADDR must be host:port: %w", err)
	} else {
		p, e := strconv.Atoi(port)
		if e != nil || p < 1 || p > 65535 {
			return c, fmt.Errorf("WARDOGS_ADDR port must be 1..65535")
		}
	}
	for _, setting := range []struct {
		name   string
		target *int
		max    int
	}{
		{"WARDOGS_MAX_ROOMS", &c.MaxRooms, 1024},
		{"WARDOGS_MAX_MEMBERS", &c.MaxMembers, 32},
		{"WARDOGS_MAX_CONNECTIONS", &c.MaxConnections, 4096},
	} {
		if value := getenv(setting.name); value != "" {
			number, err := strconv.Atoi(value)
			if err != nil || number < 1 || number > setting.max {
				return c, fmt.Errorf("%s must be 1..%d", setting.name, setting.max)
			}
			*setting.target = number
		}
	}
	return c, nil
}
