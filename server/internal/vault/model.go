package vault

import (
	"crypto/rand"
	"encoding/hex"
	"errors"
	"fmt"
	"regexp"
	"strings"
	"time"
)

type User struct {
	ID       string `json:"id"`
	Name     string `json:"name"`
	Disabled bool   `json:"disabled"`
}
type Library struct {
	ID   string `json:"id"`
	Name string `json:"name"`
}
type World struct {
	ID        string `json:"id"`
	LibraryID string `json:"library_id"`
	Name      string `json:"name"`
	SaveKey   string `json:"save_key"`
}
type Membership struct {
	UserID    string `json:"user_id"`
	LibraryID string `json:"library_id"`
	Write     bool   `json:"write"`
}
type Credential struct {
	ID         string    `json:"id"`
	UserID     string    `json:"user_id"`
	Device     string    `json:"device"`
	SecretHash string    `json:"secret_hash"`
	CreatedAt  time.Time `json:"created_at"`
	ExpiresAt  time.Time `json:"expires_at"`
	Revoked    bool      `json:"revoked"`
}
type FileEntry struct {
	Path   string `json:"path"`
	Size   int64  `json:"size"`
	SHA256 string `json:"sha256"`
}
type UploadMetadata struct {
	WorldID       string      `json:"world_id"`
	Name          string      `json:"name"`
	Note          string      `json:"note"`
	ParentID      string      `json:"parent_id,omitempty"`
	ArchiveSHA256 string      `json:"archive_sha256"`
	Files         []FileEntry `json:"files"`
}
type Backup struct {
	ID            string      `json:"id"`
	Sequence      int64       `json:"sequence"`
	LibraryID     string      `json:"library_id"`
	WorldID       string      `json:"world_id"`
	WorldName     string      `json:"world_name"`
	SaveKey       string      `json:"save_key"`
	Name          string      `json:"name"`
	Note          string      `json:"note"`
	ParentID      string      `json:"parent_id,omitempty"`
	UserID        string      `json:"user_id"`
	Uploader      string      `json:"uploader"`
	CreatedAt     time.Time   `json:"created_at"`
	ArchiveSHA256 string      `json:"archive_sha256"`
	ArchiveSize   int64       `json:"archive_size"`
	TotalSize     int64       `json:"total_size"`
	Files         []FileEntry `json:"files"`
}
type Operation struct {
	BackupID    string `json:"backup_id"`
	RequestHash string `json:"request_hash"`
}
type State struct {
	Version     int                   `json:"version"`
	Users       map[string]User       `json:"users"`
	Libraries   map[string]Library    `json:"libraries"`
	Worlds      map[string]World      `json:"worlds"`
	Members     map[string]Membership `json:"members"`
	Credentials map[string]Credential `json:"credentials"`
	Backups     []Backup              `json:"backups"`
	Operations  map[string]Operation  `json:"operations"`
	Sequence    int64                 `json:"sequence"`
}

func emptyState() State {
	return State{Version: 1, Users: map[string]User{}, Libraries: map[string]Library{}, Worlds: map[string]World{}, Members: map[string]Membership{}, Credentials: map[string]Credential{}, Backups: []Backup{}, Operations: map[string]Operation{}}
}
func randomID(n int) (string, error) {
	b := make([]byte, n)
	if _, e := rand.Read(b); e != nil {
		return "", e
	}
	return hex.EncodeToString(b), nil
}

var idRE = regexp.MustCompile(`^[a-zA-Z0-9_-]{1,64}$`)
var keyRE = regexp.MustCompile(`^[0-9a-f]{8}$`)
var hashRE = regexp.MustCompile(`^[0-9a-f]{64}$`)
var suffixRE = regexp.MustCompile(`^[a-zA-Z0-9_.-]+$`)

func validID(s string) bool                  { return idRE.MatchString(s) }
func memberKey(user, library string) string  { return user + ":" + library }
func canRead(s State, user, lib string) bool { _, ok := s.Members[memberKey(user, lib)]; return ok }
func canWrite(s State, user, lib string) bool {
	m, ok := s.Members[memberKey(user, lib)]
	return ok && m.Write
}

// Flat files belonging to exactly one chosen world; never accept arbitrary paths.
func ValidSaveName(name, key string) bool {
	if !keyRE.MatchString(key) || len(name) > 128 || strings.TrimSpace(name) != name || strings.HasSuffix(name, ".") {
		return false
	}
	if name == key {
		return true
	}
	if !strings.HasPrefix(name, key) || len(name) <= len(key)+1 {
		return false
	}
	suffix := name[len(key):]
	return strings.ContainsRune("._-", rune(suffix[0])) && suffixRE.MatchString(suffix) && !strings.Contains(suffix, "..")
}
func ValidateMetadata(m UploadMetadata, w World, maxExpanded int64) error {
	if m.WorldID != w.ID || strings.TrimSpace(m.Name) == "" || len([]rune(m.Name)) > 60 || len([]rune(m.Note)) > 500 || !hashRE.MatchString(m.ArchiveSHA256) {
		return errors.New("invalid backup metadata")
	}
	if len(m.Files) == 0 || len(m.Files) > 128 {
		return errors.New("invalid file count")
	}
	seen := map[string]bool{}
	var total int64
	for _, f := range m.Files {
		k := strings.ToLower(f.Path)
		if !ValidSaveName(f.Path, w.SaveKey) || seen[k] || f.Size < 0 || f.Size > maxExpanded || !hashRE.MatchString(f.SHA256) {
			return fmt.Errorf("invalid world file: %s", f.Path)
		}
		seen[k] = true
		total += f.Size
		if total > maxExpanded {
			return errors.New("expanded archive too large")
		}
	}
	return nil
}
