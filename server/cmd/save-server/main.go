package main

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"log"
	"net"
	"net/http"
	"os"
	"os/signal"
	"path/filepath"
	"strings"
	"syscall"
	"time"

	"enshrouded/vault/internal/vault"
)

func main() {
	if e := run(); e != nil {
		fmt.Fprintln(os.Stderr, "error:", e)
		os.Exit(1)
	}
}
func run() error {
	f := flag.NewFlagSet("save-server", flag.ContinueOnError)
	data := f.String("data", "./data", "private data directory")
	if e := f.Parse(os.Args[1:]); e != nil {
		return e
	}
	args := f.Args()
	if len(args) == 0 {
		return errors.New("usage: save-server --data DIR serve | status | user add | library add | world add | member grant | token issue")
	}
	dir, e := filepath.Abs(*data)
	if e != nil {
		return e
	}
	if args[0] == "serve" {
		return serve(dir, args[1:])
	}
	if args[0] == "status" {
		return adminCall(dir, vault.AdminRequest{Action: "status"})
	}
	if len(args) < 2 {
		return errors.New("subcommand required")
	}
	a := vault.AdminRequest{Action: args[0] + "." + args[1]}
	opts := flag.NewFlagSet(a.Action, flag.ContinueOnError)
	opts.StringVar(&a.ID, "id", "", "object ID")
	opts.StringVar(&a.Name, "name", "", "display name")
	opts.StringVar(&a.UserID, "user", "", "user ID")
	opts.StringVar(&a.LibraryID, "library", "", "library ID")
	opts.StringVar(&a.SaveKey, "save-key", "", "world.ksd for game 1.0 worlds, or the Early Access 8-character lowercase hex prefix")
	opts.StringVar(&a.Device, "device", "", "device label")
	opts.IntVar(&a.Days, "days", 180, "credential lifetime")
	opts.BoolVar(&a.Write, "write", true, "allow uploads")
	opts.BoolVar(&a.Disabled, "disabled", true, "disabled state")
	if e = opts.Parse(args[2:]); e != nil {
		return e
	}
	return adminCall(dir, a)
}
func adminCall(dir string, a vault.AdminRequest) error {
	transport := &http.Transport{DialContext: func(ctx context.Context, _, _ string) (net.Conn, error) {
		return (&net.Dialer{}).DialContext(ctx, "unix", filepath.Join(dir, "admin.sock"))
	}}
	defer transport.CloseIdleConnections()
	client := &http.Client{Transport: transport, Timeout: 15 * time.Second}
	body, _ := json.Marshal(a)
	resp, e := client.Post("http://unix/admin", "application/json", bytes.NewReader(body))
	if e != nil {
		return fmt.Errorf("cannot contact local service; start 'serve' first: %w", e)
	}
	defer resp.Body.Close()
	b, e := io.ReadAll(io.LimitReader(resp.Body, 2<<20))
	if e != nil {
		return e
	}
	if resp.StatusCode != 200 {
		return fmt.Errorf("admin rejected: %s", strings.TrimSpace(string(b)))
	}
	var formatted bytes.Buffer
	if json.Indent(&formatted, b, "", "  ") == nil {
		_, e = fmt.Fprintln(os.Stdout, formatted.String())
	} else {
		_, e = os.Stdout.Write(b)
	}
	return e
}
func serve(dir string, args []string) error {
	l := vault.DefaultLimits()
	f := flag.NewFlagSet("serve", flag.ContinueOnError)
	listen := f.String("listen", "127.0.0.1:8787", "loopback address behind HTTPS proxy")
	f.Int64Var(&l.MaxArchive, "max-archive-bytes", l.MaxArchive, "upload limit")
	f.Int64Var(&l.MaxExpanded, "max-expanded-bytes", l.MaxExpanded, "expanded snapshot limit")
	f.Int64Var(&l.MaxStorage, "max-storage-bytes", l.MaxStorage, "total object quota")
	f.Int64Var(&l.MaxUserStorage, "max-user-storage-bytes", l.MaxUserStorage, "per-user object quota")
	f.Int64Var(&l.MinFree, "min-free-bytes", l.MinFree, "free disk reserve")
	f.BoolVar(&l.AllowHTTP, "allow-http-dev", false, "development only: permit local HTTP")
	if e := f.Parse(args); e != nil {
		return e
	}
	host, _, e := net.SplitHostPort(*listen)
	if e != nil || !(host == "localhost" || net.ParseIP(host).IsLoopback()) {
		return errors.New("listen must be a loopback address")
	}
	if l.MaxArchive < 1 || l.MaxExpanded < 1 || l.MaxStorage < l.MaxArchive || l.MaxUserStorage < l.MaxArchive || l.MinFree < 0 {
		return errors.New("invalid storage limits")
	}
	s, e := vault.OpenStore(dir)
	if e != nil {
		return e
	}
	defer s.Close()
	sock := filepath.Join(dir, "admin.sock")
	if fi, e := os.Lstat(sock); e == nil {
		if fi.Mode()&os.ModeSocket == 0 {
			return errors.New("admin.sock is not a socket")
		}
		if e = os.Remove(sock); e != nil {
			return e
		}
	}
	unix, e := net.Listen("unix", sock)
	if e != nil {
		return e
	}
	defer unix.Close()
	defer os.Remove(sock)
	if e = os.Chmod(sock, 0600); e != nil {
		return e
	}
	admin := &http.Server{ReadHeaderTimeout: 5 * time.Second, ReadTimeout: 10 * time.Second, WriteTimeout: 10 * time.Second, Handler: http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method != "POST" || r.URL.Path != "/admin" {
			http.NotFound(w, r)
			return
		}
		var a vault.AdminRequest
		dec := json.NewDecoder(http.MaxBytesReader(w, r.Body, 8192))
		dec.DisallowUnknownFields()
		if dec.Decode(&a) != nil {
			http.Error(w, "invalid request", 400)
			return
		}
		result, e := s.Admin(a)
		w.Header().Set("Content-Type", "application/json")
		if e != nil {
			w.WriteHeader(400)
			_ = json.NewEncoder(w).Encode(map[string]string{"error": e.Error()})
			return
		}
		_ = json.NewEncoder(w).Encode(result)
	})}
	public := &http.Server{Addr: *listen, Handler: vault.NewAPI(s, l), ReadHeaderTimeout: 10 * time.Second, ReadTimeout: 10 * time.Minute, WriteTimeout: 10 * time.Minute, IdleTimeout: 60 * time.Second, MaxHeaderBytes: 16 << 10}
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	errs := make(chan error, 2)
	go func() { errs <- admin.Serve(unix) }()
	go func() { errs <- public.ListenAndServe() }()
	log.Printf("save-server ready listen=%s data=%s", *listen, dir)
	select {
	case <-ctx.Done():
	case e = <-errs:
		if !errors.Is(e, http.ErrServerClosed) {
			stop()
		}
	}
	shutdown, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()
	_ = public.Shutdown(shutdown)
	_ = admin.Shutdown(shutdown)
	if e != nil && !errors.Is(e, http.ErrServerClosed) {
		return e
	}
	return nil
}
