package main

import (
	"context"
	"log"
	"net"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"
	"wardogs/server/internal/config"
	"wardogs/server/internal/room"
	"wardogs/server/internal/transport"
)

func main() {
	cfg, err := config.Load(os.Getenv)
	if err != nil {
		log.Fatal(err)
	}
	if len(os.Args) > 1 && os.Args[1] == "healthcheck" {
		host, port, _ := net.SplitHostPort(cfg.Address)
		if host == "" || host == "0.0.0.0" {
			host = "127.0.0.1"
		}
		if host == "::" {
			host = "::1"
		}
		client := http.Client{Timeout: 2 * time.Second}
		response, err := client.Get("http://" + net.JoinHostPort(host, port) + "/readyz")
		if err != nil {
			log.Fatal(err)
		}
		defer response.Body.Close()
		if response.StatusCode != http.StatusOK {
			os.Exit(1)
		}
		return
	}
	store := room.New(room.Config{MaxRooms: cfg.MaxRooms, MaxMembers: cfg.MaxMembers})
	api := transport.New(store, cfg.MaxConnections)
	server := &http.Server{Addr: cfg.Address, Handler: api.Handler(), ReadHeaderTimeout: 5 * time.Second, IdleTimeout: 60 * time.Second, MaxHeaderBytes: 16 * 1024}
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	go func() {
		tick := time.NewTicker(time.Second)
		statsTick := time.NewTicker(time.Minute)
		defer tick.Stop()
		defer statsTick.Stop()
		for {
			select {
			case <-ctx.Done():
				return
			case <-tick.C:
				store.Sweep()
			case <-statsTick.C:
				stats := api.Stats()
				log.Printf("connections=%d rooms=%d members=%d queued_bytes=%d received=%d sent=%d rejected=%d slow_disconnects=%d", stats.Connections, stats.Rooms, stats.Members, stats.QueuedBytes, stats.Received, stats.Sent, stats.Rejected, stats.SlowDisconnects)
			}
		}
	}()
	shutdownDone := make(chan struct{})
	go func() {
		defer close(shutdownDone)
		<-ctx.Done()
		deadline, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()
		_ = api.Close(deadline)
		_ = server.Shutdown(deadline)
	}()
	log.Printf("WarDogs protocol=1 listen=%s max_rooms=%d max_members=%d max_connections=%d", cfg.Address, cfg.MaxRooms, cfg.MaxMembers, cfg.MaxConnections)
	if err := server.ListenAndServe(); err != nil && err != http.ErrServerClosed {
		log.Fatal(err)
	}
	<-shutdownDone
}
