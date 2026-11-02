package vault

import (
	"crypto/rand"
	"crypto/sha256"
	"crypto/subtle"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"sync"
	"syscall"
	"time"
)

type Store struct {
	mu       sync.RWMutex
	dir      string
	state    State
	lock     *os.File
	lastUsed map[string]time.Time
}

func OpenStore(dir string) (*Store, error) {
	dir, e := filepath.Abs(dir)
	if e != nil {
		return nil, e
	}
	if e = os.MkdirAll(dir, 0700); e != nil {
		return nil, e
	}
	if e = os.Chmod(dir, 0700); e != nil {
		return nil, e
	}
	f, e := os.OpenFile(filepath.Join(dir, ".lock"), os.O_CREATE|os.O_RDWR, 0600)
	if e != nil {
		return nil, e
	}
	if e = syscall.Flock(int(f.Fd()), syscall.LOCK_EX|syscall.LOCK_NB); e != nil {
		f.Close()
		return nil, errors.New("data directory is already in use")
	}
	s := &Store{dir: dir, state: emptyState(), lock: f, lastUsed: map[string]time.Time{}}
	b, e := os.ReadFile(filepath.Join(dir, "state.json"))
	if e == nil {
		e = json.Unmarshal(b, &s.state)
		if e == nil && (s.state.Version != 1 || s.state.Users == nil || s.state.Libraries == nil || s.state.Worlds == nil || s.state.Members == nil || s.state.Credentials == nil || s.state.Operations == nil) {
			e = errors.New("unsupported or incomplete state file")
		}
	} else if os.IsNotExist(e) {
		e = s.persist(s.state)
	}
	if e != nil {
		s.Close()
		return nil, e
	}
	for _, name := range []string{"objects", "incoming"} {
		if e = os.MkdirAll(filepath.Join(dir, name), 0700); e != nil {
			s.Close()
			return nil, e
		}
	}
	entries, e := os.ReadDir(filepath.Join(dir, "incoming"))
	if e != nil {
		s.Close()
		return nil, e
	}
	for _, f := range entries {
		if !f.IsDir() {
			_ = os.Remove(filepath.Join(dir, "incoming", f.Name()))
		}
	}
	for _, b := range s.state.Backups {
		if _, e = os.Stat(s.ObjectPath(b.ID)); e != nil {
			s.Close()
			return nil, fmt.Errorf("missing backup object %s", b.ID)
		}
	}
	return s, nil
}
func (s *Store) Close() {
	if s.lock != nil {
		_ = syscall.Flock(int(s.lock.Fd()), syscall.LOCK_UN)
		_ = s.lock.Close()
		s.lock = nil
	}
}
func (s *Store) Dir() string                 { return s.dir }
func (s *Store) ObjectPath(id string) string { return filepath.Join(s.dir, "objects", id+".zip") }
func (s *Store) persist(next State) error {
	f, e := os.CreateTemp(s.dir, ".state-*")
	if e != nil {
		return e
	}
	name := f.Name()
	defer os.Remove(name)
	defer f.Close()
	if e = f.Chmod(0600); e != nil {
		return e
	}
	enc := json.NewEncoder(f)
	enc.SetIndent("", "  ")
	if e = enc.Encode(next); e != nil {
		return e
	}
	if e = f.Sync(); e != nil {
		return e
	}
	if e = f.Close(); e != nil {
		return e
	}
	if e = os.Rename(name, filepath.Join(s.dir, "state.json")); e != nil {
		return e
	}
	d, e := os.Open(s.dir)
	if e != nil {
		return e
	}
	defer d.Close()
	return d.Sync()
}
func (s *Store) mutate(fn func(*State) error) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	raw, e := json.Marshal(s.state)
	if e != nil {
		return e
	}
	var next State
	if e = json.Unmarshal(raw, &next); e != nil {
		return e
	}
	if e = fn(&next); e != nil {
		return e
	}
	if e = s.persist(next); e != nil {
		return e
	}
	s.state = next
	return nil
}

type Identity struct {
	User         User      `json:"user"`
	CredentialID string    `json:"credential_id"`
	Device       string    `json:"device"`
	ExpiresAt    time.Time `json:"expires_at"`
}

var ErrUnauthorized = errors.New("unauthorized")

func authenticate(st State, token string) (Identity, error) {
	if len(token) != 79 || !strings.HasPrefix(token, "es_") {
		return Identity{}, ErrUnauthorized
	}
	parts := strings.Split(token[3:], ".")
	if len(parts) != 2 || len(parts[0]) != 32 || len(parts[1]) != 43 {
		return Identity{}, ErrUnauthorized
	}
	idb, e := hex.DecodeString(parts[0])
	if e != nil || len(idb) != 16 {
		return Identity{}, ErrUnauthorized
	}
	secret, e := base64.RawURLEncoding.DecodeString(parts[1])
	if e != nil || len(secret) != 32 {
		return Identity{}, ErrUnauthorized
	}
	c, found := st.Credentials[parts[0]]
	expected := make([]byte, 32)
	if found {
		b, err := hex.DecodeString(c.SecretHash)
		if err != nil || len(b) != 32 {
			return Identity{}, ErrUnauthorized
		}
		copy(expected, b)
	}
	actual := sha256.Sum256(secret)
	matches := subtle.ConstantTimeCompare(actual[:], expected) == 1
	u, userOK := st.Users[c.UserID]
	if !found || !matches || c.Revoked || !time.Now().Before(c.ExpiresAt) || !userOK || u.Disabled {
		return Identity{}, ErrUnauthorized
	}
	return Identity{u, c.ID, c.Device, c.ExpiresAt}, nil
}
func (s *Store) Authenticate(token string) (Identity, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	who, err := authenticate(s.state, token)
	if err == nil {
		s.lastUsed[who.CredentialID] = time.Now().UTC()
	}
	return who, err
}
func (s *Store) WorldFor(user, id string, write bool) (World, bool) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	w, ok := s.state.Worlds[id]
	return w, ok && canRead(s.state, user, w.LibraryID) && (!write || canWrite(s.state, user, w.LibraryID))
}
func (s *Store) BackupFor(user, id string) (Backup, bool) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	for _, b := range s.state.Backups {
		if b.ID == id && canRead(s.state, user, b.LibraryID) {
			return b, true
		}
	}
	return Backup{}, false
}

type VisibleWorld struct {
	World
	CanUpload bool `json:"can_upload"`
}

func (s *Store) WorldsFor(user string) []VisibleWorld {
	s.mu.RLock()
	defer s.mu.RUnlock()
	a := []VisibleWorld{}
	for _, w := range s.state.Worlds {
		if canRead(s.state, user, w.LibraryID) {
			a = append(a, VisibleWorld{w, canWrite(s.state, user, w.LibraryID)})
		}
	}
	sort.Slice(a, func(i, j int) bool { return a[i].Name < a[j].Name })
	return a
}

type ListResult struct {
	Items    []Backup `json:"items"`
	Total    int      `json:"total"`
	Page     int      `json:"page"`
	PageSize int      `json:"page_size"`
	Snapshot int64    `json:"snapshot"`
}

func (s *Store) List(user, world, query, order string, page, size int, snapshot int64) ListResult {
	s.mu.RLock()
	defer s.mu.RUnlock()
	if snapshot <= 0 || snapshot > s.state.Sequence {
		snapshot = s.state.Sequence
	}
	a := []Backup{}
	query = strings.ToLower(strings.TrimSpace(query))
	for _, b := range s.state.Backups {
		if b.Sequence > snapshot || !canRead(s.state, user, b.LibraryID) || (world != "" && b.WorldID != world) {
			continue
		}
		if query != "" && !strings.Contains(strings.ToLower(b.Name+" "+b.Note+" "+b.Uploader), query) {
			continue
		}
		a = append(a, b)
	}
	sort.Slice(a, func(i, j int) bool {
		if order == "oldest" {
			return a[i].Sequence < a[j].Sequence
		}
		if order == "name" {
			x, y := strings.ToLower(a[i].Name), strings.ToLower(a[j].Name)
			if x != y {
				return x < y
			}
		}
		return a[i].Sequence > a[j].Sequence
	})
	total := len(a)
	start := (page - 1) * size
	if start > total {
		start = total
	}
	end := start + size
	if end > total {
		end = total
	}
	return ListResult{a[start:end], total, page, size, snapshot}
}
func (s *Store) Commit(token, op, requestHash, temp string, m UploadMetadata, archiveSize, maxTotal, maxUser int64) (Backup, bool, error) {
	var out Backup
	var duplicate bool
	e := s.mutate(func(st *State) error {
		who, e := authenticate(*st, token)
		if e != nil {
			return e
		}
		w, ok := st.Worlds[m.WorldID]
		if !ok || !canWrite(*st, who.User.ID, w.LibraryID) {
			return errors.New("forbidden")
		}
		if m.ParentID != "" {
			found := false
			for _, b := range st.Backups {
				if b.ID == m.ParentID && b.WorldID == w.ID {
					found = true
				}
			}
			if !found {
				return errors.New("invalid parent backup")
			}
		}
		operationKey := who.CredentialID + ":" + op
		if old, ok := st.Operations[operationKey]; ok {
			if old.RequestHash != requestHash {
				return errors.New("idempotency conflict")
			}
			for _, b := range st.Backups {
				if b.ID == old.BackupID {
					out = b
					duplicate = true
					return nil
				}
			}
			return errors.New("operation record is incomplete")
		}
		entries, e := os.ReadDir(filepath.Join(s.dir, "objects"))
		if e != nil {
			return e
		}
		var used int64
		for _, f := range entries {
			info, err := f.Info()
			if err != nil {
				return err
			}
			used += info.Size()
		}
		if used+archiveSize > maxTotal {
			return errors.New("storage quota exceeded")
		}
		var userBytes int64
		for _, b := range st.Backups {
			if b.UserID == who.User.ID {
				userBytes += b.ArchiveSize
			}
		}
		if userBytes+archiveSize > maxUser {
			return errors.New("storage quota exceeded")
		}
		id, e := randomID(16)
		if e != nil {
			return e
		}
		dest := s.ObjectPath(id)
		if _, e = os.Stat(dest); !os.IsNotExist(e) {
			return errors.New("object ID collision")
		}
		if e = os.Rename(temp, dest); e != nil {
			return e
		}
		d, e := os.Open(filepath.Dir(dest))
		if e != nil {
			return e
		}
		e = d.Sync()
		d.Close()
		if e != nil {
			return e
		}
		var total int64
		for _, f := range m.Files {
			total += f.Size
		}
		st.Sequence++
		out = Backup{id, st.Sequence, w.LibraryID, w.ID, w.Name, w.SaveKey, m.Name, m.Note, m.ParentID, who.User.ID, who.User.Name, time.Now().UTC(), m.ArchiveSHA256, archiveSize, total, m.Files}
		st.Backups = append(st.Backups, out)
		st.Operations[operationKey] = Operation{id, requestHash}
		return nil
	})
	return out, duplicate, e
}

type AdminRequest struct {
	Action    string `json:"action"`
	ID        string `json:"id"`
	Name      string `json:"name"`
	UserID    string `json:"user_id"`
	LibraryID string `json:"library_id"`
	SaveKey   string `json:"save_key"`
	Device    string `json:"device"`
	Days      int    `json:"days"`
	Write     bool   `json:"write"`
	Disabled  bool   `json:"disabled"`
}

func (s *Store) Admin(a AdminRequest) (any, error) {
	if a.Action == "status" {
		s.mu.RLock()
		defer s.mu.RUnlock()
		creds := []map[string]any{}
		for _, c := range s.state.Credentials {
			creds = append(creds, map[string]any{"id": c.ID, "user_id": c.UserID, "device": c.Device, "expires_at": c.ExpiresAt, "revoked": c.Revoked, "last_used_since_start": s.lastUsed[c.ID]})
		}
		return map[string]any{"users": s.state.Users, "libraries": s.state.Libraries, "worlds": s.state.Worlds, "members": s.state.Members, "credentials": creds, "backup_count": len(s.state.Backups)}, nil
	}
	var out any = map[string]bool{"ok": true}
	e := s.mutate(func(st *State) error {
		switch a.Action {
		case "user.add":
			if !validID(a.ID) || strings.TrimSpace(a.Name) == "" || len([]rune(a.Name)) > 60 {
				return errors.New("invalid user")
			}
			if _, ok := st.Users[a.ID]; ok {
				return errors.New("user already exists")
			}
			st.Users[a.ID] = User{a.ID, a.Name, false}
		case "user.disable":
			u, ok := st.Users[a.ID]
			if !ok {
				return errors.New("user not found")
			}
			u.Disabled = a.Disabled
			st.Users[a.ID] = u
		case "library.add":
			if !validID(a.ID) || strings.TrimSpace(a.Name) == "" {
				return errors.New("invalid library")
			}
			if _, ok := st.Libraries[a.ID]; ok {
				return errors.New("library already exists")
			}
			st.Libraries[a.ID] = Library{a.ID, a.Name}
		case "world.add":
			if !validID(a.ID) || !keyRE.MatchString(a.SaveKey) || strings.TrimSpace(a.Name) == "" {
				return errors.New("world requires id, name, and 8 lowercase hex save-key")
			}
			if _, ok := st.Libraries[a.LibraryID]; !ok {
				return errors.New("library not found")
			}
			if _, ok := st.Worlds[a.ID]; ok {
				return errors.New("world already exists")
			}
			st.Worlds[a.ID] = World{a.ID, a.LibraryID, a.Name, a.SaveKey}
		case "member.grant":
			if _, ok := st.Users[a.UserID]; !ok {
				return errors.New("user not found")
			}
			if _, ok := st.Libraries[a.LibraryID]; !ok {
				return errors.New("library not found")
			}
			st.Members[memberKey(a.UserID, a.LibraryID)] = Membership{a.UserID, a.LibraryID, a.Write}
		case "member.revoke":
			delete(st.Members, memberKey(a.UserID, a.LibraryID))
		case "token.issue":
			u, ok := st.Users[a.UserID]
			if !ok || u.Disabled {
				return errors.New("active user required")
			}
			if a.Days < 1 || a.Days > 365 || len(a.Device) > 128 {
				return errors.New("days must be 1..365; device label <=128 bytes")
			}
			id, e := randomID(16)
			if e != nil {
				return e
			}
			if _, ok := st.Credentials[id]; ok {
				return errors.New("credential ID collision")
			}
			secret := make([]byte, 32)
			if _, e = rand.Read(secret); e != nil {
				return e
			}
			h := sha256.Sum256(secret)
			now := time.Now().UTC()
			st.Credentials[id] = Credential{id, a.UserID, a.Device, hex.EncodeToString(h[:]), now, now.Add(time.Duration(a.Days) * 24 * time.Hour), false}
			out = map[string]any{"credential_id": id, "user_id": a.UserID, "expires_at": st.Credentials[id].ExpiresAt, "token": "es_" + id + "." + base64.RawURLEncoding.EncodeToString(secret)}
		case "token.revoke":
			c, ok := st.Credentials[a.ID]
			if !ok {
				return errors.New("credential not found")
			}
			c.Revoked = true
			st.Credentials[a.ID] = c
		case "token.revoke-all":
			for id, c := range st.Credentials {
				c.Revoked = true
				st.Credentials[id] = c
			}
		default:
			return errors.New("unknown admin action")
		}
		return nil
	})
	return out, e
}
