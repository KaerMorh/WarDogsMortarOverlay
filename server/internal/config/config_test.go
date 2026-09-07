package config

import "testing"

func TestDefaults(t *testing.T) {
	c, err := Load(func(string) string { return "" })
	if err != nil || c.Address != "127.0.0.1:8080" || c.MaxMembers != 32 || c.MaxConnections != 512 {
		t.Fatal(c, err)
	}
}
func TestInvalidSettings(t *testing.T) {
	for _, settings := range []map[string]string{
		{"WARDOGS_ADDR": "localhost"}, {"WARDOGS_ADDR": "localhost:abc"}, {"WARDOGS_ADDR": "localhost:0"},
		{"WARDOGS_MAX_CONNECTIONS": "-1"}, {"WARDOGS_MAX_ROOMS": "bad"}, {"WARDOGS_MAX_MEMBERS": "33"},
	} {
		if _, err := Load(func(k string) string { return settings[k] }); err == nil {
			t.Fatal("accepted invalid configuration", settings)
		}
	}
}
func TestOverrides(t *testing.T) {
	settings := map[string]string{"WARDOGS_ADDR": "0.0.0.0:8080", "WARDOGS_MAX_ROOMS": "10", "WARDOGS_MAX_MEMBERS": "8", "WARDOGS_MAX_CONNECTIONS": "100"}
	c, err := Load(func(k string) string { return settings[k] })
	if err != nil || c.MaxRooms != 10 || c.MaxMembers != 8 || c.MaxConnections != 100 {
		t.Fatal(c, err)
	}
}
