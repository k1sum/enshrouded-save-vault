package vault

import (
	"archive/zip"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"log"
	"net"
	"net/http"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"sync"
	"time"
)

type Limits struct {
	MaxArchive     int64
	MaxExpanded    int64
	MaxStorage     int64
	MaxUserStorage int64
	MinFree        int64
	AllowHTTP      bool
}

func DefaultLimits() Limits {
	return Limits{MaxArchive: 512 << 20, MaxExpanded: 2 << 30, MaxStorage: 20 << 30, MaxUserStorage: 10 << 30, MinFree: 256 << 20}
}

type bucket struct {
	tokens float64
	at     time.Time
}
type API struct {
	Store   *Store
	Limits  Limits
	uploads chan struct{}
	rateMu  sync.Mutex
	rates   map[string]bucket
}

func NewAPI(s *Store, l Limits) *API {
	return &API{s, l, make(chan struct{}, 2), sync.Mutex{}, map[string]bucket{}}
}
func (a *API) rate(key string) bool {
	a.rateMu.Lock()
	defer a.rateMu.Unlock()
	now := time.Now()
	b, ok := a.rates[key]
	if !ok {
		if len(a.rates) >= 4096 {
			for k, v := range a.rates {
				if now.Sub(v.at) > time.Minute {
					delete(a.rates, k)
				}
			}
			if len(a.rates) >= 4096 {
				return false
			}
		}
		b = bucket{60, now}
	}
	b.tokens += now.Sub(b.at).Seconds() * 2
	if b.tokens > 60 {
		b.tokens = 60
	}
	b.at = now
	allowed := b.tokens >= 1
	if allowed {
		b.tokens--
	}
	a.rates[key] = b
	return allowed
}
func respond(w http.ResponseWriter, status int, v any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(v)
}
func fail(w http.ResponseWriter, status int, message string) {
	respond(w, status, map[string]string{"error": message})
}
func (a *API) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Cache-Control", "no-store")
	w.Header().Set("X-Content-Type-Options", "nosniff")
	if r.URL.Path == "/healthz" && r.Method == http.MethodGet {
		respond(w, 200, map[string]bool{"ok": true})
		return
	}
	ip, _, _ := net.SplitHostPort(r.RemoteAddr)
	if !a.rate("ip:" + ip) {
		w.Header().Set("Retry-After", "30")
		fail(w, 429, "too many requests")
		return
	}
	if !a.Limits.AllowHTTP && r.TLS == nil && !(net.ParseIP(ip).IsLoopback() && r.Header.Get("X-Forwarded-Proto") == "https") {
		fail(w, 400, "HTTPS is required")
		return
	}
	auth := r.Header.Values("Authorization")
	if len(auth) != 1 || !strings.HasPrefix(auth[0], "Bearer ") {
		fail(w, 401, "invalid credentials")
		return
	}
	token := strings.TrimPrefix(auth[0], "Bearer ")
	who, e := a.Store.Authenticate(token)
	if e != nil {
		fail(w, 401, "invalid credentials")
		return
	}
	if !a.rate("credential:" + who.CredentialID) {
		fail(w, 429, "too many requests")
		return
	}
	switch {
	case r.URL.Path == "/v1/me" && r.Method == http.MethodGet:
		respond(w, 200, map[string]any{"identity": who, "worlds": a.Store.WorldsFor(who.User.ID)})
	case r.URL.Path == "/v1/backups" && r.Method == http.MethodGet:
		a.list(w, r, who)
	case r.URL.Path == "/v1/backups" && r.Method == http.MethodPost:
		a.upload(w, r, who, token)
	case strings.HasPrefix(r.URL.Path, "/v1/backups/") && (r.Method == http.MethodGet || r.Method == http.MethodHead):
		a.download(w, r, who)
	default:
		fail(w, 404, "not found")
	}
}
func (a *API) list(w http.ResponseWriter, r *http.Request, who Identity) {
	q := r.URL.Query()
	page, e := strconv.Atoi(defaultString(q.Get("page"), "1"))
	if e != nil || page < 1 || page > 1000000 {
		fail(w, 400, "invalid page")
		return
	}
	size, e := strconv.Atoi(defaultString(q.Get("page_size"), "5"))
	if e != nil || size < 1 || size > 50 {
		fail(w, 400, "invalid page size")
		return
	}
	snapshot, e := strconv.ParseInt(defaultString(q.Get("snapshot"), "0"), 10, 64)
	if e != nil || snapshot < 0 {
		fail(w, 400, "invalid snapshot")
		return
	}
	order := defaultString(q.Get("sort"), "newest")
	if order != "newest" && order != "oldest" && order != "name" {
		fail(w, 400, "invalid sort")
		return
	}
	if len(q.Get("q")) > 512 {
		fail(w, 400, "search too long")
		return
	}
	respond(w, 200, a.Store.List(who.User.ID, q.Get("world_id"), q.Get("q"), order, page, size, snapshot))
}
func defaultString(s, d string) string {
	if s == "" {
		return d
	}
	return s
}
func (a *API) download(w http.ResponseWriter, r *http.Request, who Identity) {
	id := strings.TrimPrefix(r.URL.Path, "/v1/backups/")
	if len(id) != 32 {
		fail(w, 404, "not found")
		return
	}
	b, ok := a.Store.BackupFor(who.User.ID, id)
	if !ok {
		fail(w, 404, "not found")
		return
	}
	f, e := os.Open(a.Store.ObjectPath(b.ID))
	if e != nil {
		fail(w, 500, "backup object unavailable")
		return
	}
	defer f.Close()
	info, e := f.Stat()
	if e != nil || info.Size() != b.ArchiveSize {
		fail(w, 500, "backup integrity error")
		return
	}
	h := sha256.New()
	if _, e = io.Copy(h, f); e != nil || hex.EncodeToString(h.Sum(nil)) != b.ArchiveSHA256 {
		fail(w, 500, "backup integrity error")
		return
	}
	if _, e = f.Seek(0, 0); e != nil {
		fail(w, 500, "backup unavailable")
		return
	}
	w.Header().Set("Content-Type", "application/zip")
	w.Header().Set("Content-Disposition", `attachment; filename="`+b.ID+`.zip"`)
	w.Header().Set("X-Archive-SHA256", b.ArchiveSHA256)
	w.Header().Set("ETag", `"`+b.ArchiveSHA256+`"`)
	log.Printf("audit action=download user=%s credential=%s backup=%s", who.User.ID, who.CredentialID, b.ID)
	http.ServeContent(w, r, b.ID+".zip", b.CreatedAt, f)
}
func (a *API) upload(w http.ResponseWriter, r *http.Request, who Identity, token string) {
	select {
	case a.uploads <- struct{}{}:
		defer func() { <-a.uploads }()
	default:
		fail(w, 429, "uploads busy; retry later")
		return
	}
	op := r.Header.Get("Idempotency-Key")
	if !validID(op) {
		fail(w, 400, "Idempotency-Key required (1..64 safe characters)")
		return
	}
	if a.Limits.MinFree > 0 {
		free, e := freeBytes(a.Store.Dir())
		if e != nil || free < a.Limits.MinFree+a.Limits.MaxArchive {
			fail(w, 507, "insufficient free storage")
			return
		}
	}
	r.Body = http.MaxBytesReader(w, r.Body, a.Limits.MaxArchive+(128<<10))
	mr, e := r.MultipartReader()
	if e != nil {
		fail(w, 400, "multipart upload required")
		return
	}
	part, e := mr.NextPart()
	if e != nil || part.FormName() != "metadata" {
		fail(w, 400, "metadata must be the first part")
		return
	}
	raw, e := io.ReadAll(io.LimitReader(part, (64<<10)+1))
	part.Close()
	if e != nil || len(raw) > 64<<10 {
		fail(w, 413, "metadata too large")
		return
	}
	var m UploadMetadata
	dec := json.NewDecoder(strings.NewReader(string(raw)))
	dec.DisallowUnknownFields()
	if dec.Decode(&m) != nil {
		fail(w, 400, "invalid metadata")
		return
	}
	world, ok := a.Store.WorldFor(who.User.ID, m.WorldID, true)
	if !ok {
		fail(w, 404, "world not found")
		return
	}
	if e = ValidateMetadata(m, world, a.Limits.MaxExpanded); e != nil {
		fail(w, 400, e.Error())
		return
	}
	if m.ParentID != "" {
		parent, ok := a.Store.BackupFor(who.User.ID, m.ParentID)
		if !ok || parent.WorldID != world.ID {
			fail(w, 400, "invalid parent backup")
			return
		}
	}
	part, e = mr.NextPart()
	if e != nil || part.FormName() != "archive" {
		fail(w, 400, "archive must be the second part")
		return
	}
	temp, e := os.CreateTemp(filepath.Join(a.Store.Dir(), "incoming"), "upload-*")
	if e != nil {
		fail(w, 500, "cannot create upload")
		return
	}
	name := temp.Name()
	defer os.Remove(name)
	defer temp.Close()
	h := sha256.New()
	n, e := io.Copy(io.MultiWriter(temp, h), io.LimitReader(part, a.Limits.MaxArchive+1))
	part.Close()
	if e != nil || n > a.Limits.MaxArchive {
		fail(w, 413, "archive too large or incomplete")
		return
	}
	if _, e = mr.NextPart(); !errors.Is(e, io.EOF) {
		fail(w, 400, "unexpected multipart data")
		return
	}
	if hex.EncodeToString(h.Sum(nil)) != m.ArchiveSHA256 {
		fail(w, 400, "archive checksum mismatch")
		return
	}
	if e = temp.Sync(); e != nil {
		fail(w, 500, "cannot persist upload")
		return
	}
	if e = temp.Close(); e != nil {
		fail(w, 500, "cannot persist upload")
		return
	}
	if e = ValidateArchive(name, m, a.Limits.MaxExpanded); e != nil {
		fail(w, 400, e.Error())
		return
	}
	canonical, _ := json.Marshal(m)
	digest := sha256.Sum256(canonical)
	backup, duplicate, e := a.Store.Commit(token, op, hex.EncodeToString(digest[:]), name, m, n, a.Limits.MaxStorage, a.Limits.MaxUserStorage)
	if e != nil {
		switch e.Error() {
		case "unauthorized":
			fail(w, 401, "invalid credentials")
		case "forbidden":
			fail(w, 404, "world not found")
		case "idempotency conflict":
			fail(w, 409, "operation ID was already used for different content")
		case "storage quota exceeded":
			fail(w, 507, "storage quota exceeded")
		default:
			log.Printf("upload commit failed user=%s: %v", who.User.ID, e)
			fail(w, 500, "could not commit backup")
		}
		return
	}
	status := 201
	if duplicate {
		status = 200
	}
	log.Printf("audit action=upload user=%s credential=%s backup=%s duplicate=%t", who.User.ID, who.CredentialID, backup.ID, duplicate)
	respond(w, status, backup)
}
func ValidateArchive(path string, m UploadMetadata, maxExpanded int64) error {
	z, e := zip.OpenReader(path)
	if e != nil {
		return errors.New("invalid ZIP archive")
	}
	defer z.Close()
	if len(z.File) != len(m.Files) {
		return errors.New("archive file count mismatch")
	}
	expected := map[string]FileEntry{}
	for _, f := range m.Files {
		expected[f.Path] = f
	}
	var total int64
	for _, f := range z.File {
		entry, ok := expected[f.Name]
		if !ok || f.Mode()&os.ModeType != 0 || f.UncompressedSize64 != uint64(entry.Size) {
			return errors.New("archive contains unexpected file")
		}
		delete(expected, f.Name)
		total += entry.Size
		if total > maxExpanded {
			return errors.New("archive expands too large")
		}
		if f.UncompressedSize64 > 1<<20 && (f.CompressedSize64 == 0 || f.UncompressedSize64/f.CompressedSize64 > 1000) {
			return errors.New("archive compression ratio too high")
		}
		rd, e := f.Open()
		if e != nil {
			return errors.New("cannot read archive")
		}
		h := sha256.New()
		n, e := io.Copy(h, io.LimitReader(rd, entry.Size+1))
		rd.Close()
		if e != nil || n != entry.Size || hex.EncodeToString(h.Sum(nil)) != entry.SHA256 {
			return fmt.Errorf("file checksum mismatch: %s", entry.Path)
		}
	}
	return nil
}
