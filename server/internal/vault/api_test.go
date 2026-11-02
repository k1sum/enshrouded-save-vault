package vault

import (
	"archive/zip"
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"io"
	"mime/multipart"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

type fixture struct {
	s           *Store
	api         *API
	a, b, other string
}

func setup(t *testing.T) fixture {
	t.Helper()
	s, e := OpenStore(t.TempDir())
	if e != nil {
		t.Fatal(e)
	}
	t.Cleanup(s.Close)
	admin := func(a AdminRequest) any {
		t.Helper()
		v, e := s.Admin(a)
		if e != nil {
			t.Fatal(e)
		}
		return v
	}
	for _, u := range []string{"a", "b", "other"} {
		admin(AdminRequest{Action: "user.add", ID: u, Name: strings.ToUpper(u)})
	}
	for _, lib := range []string{"friends", "private"} {
		admin(AdminRequest{Action: "library.add", ID: lib, Name: lib})
	}
	admin(AdminRequest{Action: "world.add", ID: "shared", Name: "Shared", LibraryID: "friends", SaveKey: "3ad85aea"})
	admin(AdminRequest{Action: "world.add", ID: "secret", Name: "Secret", LibraryID: "private", SaveKey: "1234abcd"})
	for _, u := range []string{"a", "b"} {
		admin(AdminRequest{Action: "member.grant", UserID: u, LibraryID: "friends", Write: true})
	}
	admin(AdminRequest{Action: "member.grant", UserID: "other", LibraryID: "private", Write: true})
	token := func(u string) string {
		return admin(AdminRequest{Action: "token.issue", UserID: u, Device: u + "-PC", Days: 180}).(map[string]any)["token"].(string)
	}
	l := DefaultLimits()
	l.AllowHTTP = true
	l.MinFree = 0
	return fixture{s, NewAPI(s, l), token("a"), token("b"), token("other")}
}
func call(api *API, method, path, token string, body io.Reader, contentType, op string) *httptest.ResponseRecorder {
	r := httptest.NewRequest(method, path, body)
	if token != "" {
		r.Header.Set("Authorization", "Bearer "+token)
	}
	if contentType != "" {
		r.Header.Set("Content-Type", contentType)
	}
	if op != "" {
		r.Header.Set("Idempotency-Key", op)
	}
	w := httptest.NewRecorder()
	api.ServeHTTP(w, r)
	return w
}
func archive(t *testing.T, world, key, name, note string) (UploadMetadata, []byte) {
	t.Helper()
	data := []byte("world payload " + name)
	fhash := sha256.Sum256(data)
	var buf bytes.Buffer
	zw := zip.NewWriter(&buf)
	entry, e := zw.Create(key)
	if e != nil {
		t.Fatal(e)
	}
	_, _ = entry.Write(data)
	if e = zw.Close(); e != nil {
		t.Fatal(e)
	}
	sum := sha256.Sum256(buf.Bytes())
	return UploadMetadata{WorldID: world, Name: name, Note: note, ArchiveSHA256: hex.EncodeToString(sum[:]), Files: []FileEntry{{key, int64(len(data)), hex.EncodeToString(fhash[:])}}}, buf.Bytes()
}
func upload(t *testing.T, f fixture, token, op string, m UploadMetadata, z []byte) *httptest.ResponseRecorder {
	t.Helper()
	var buf bytes.Buffer
	mw := multipart.NewWriter(&buf)
	p, _ := mw.CreateFormField("metadata")
	_ = json.NewEncoder(p).Encode(m)
	p, _ = mw.CreateFormFile("archive", "backup.zip")
	_, _ = p.Write(z)
	_ = mw.Close()
	return call(f.api, "POST", "/v1/backups", token, &buf, mw.FormDataContentType(), op)
}
func TestIdentityIsolationRevocationAndNoSecretLeak(t *testing.T) {
	f := setup(t)
	for _, token := range []string{"", "wrong", f.a[:len(f.a)-1] + "!"} {
		w := call(f.api, "GET", "/v1/me", token, nil, "", "")
		if w.Code != 401 {
			t.Fatalf("expected 401: %d", w.Code)
		}
	}
	w := call(f.api, "GET", "/v1/me", f.a, nil, "", "")
	if w.Code != 200 || strings.Contains(w.Body.String(), "secret_hash") || strings.Contains(w.Body.String(), "private") {
		t.Fatal(w.Body.String())
	}
	id := strings.Split(f.a[3:], ".")[0]
	if _, e := f.s.Admin(AdminRequest{Action: "token.revoke", ID: id}); e != nil {
		t.Fatal(e)
	}
	if call(f.api, "GET", "/v1/me", f.a, nil, "", "").Code != 401 {
		t.Fatal("revoked credential accepted")
	}
	if call(f.api, "GET", "/v1/me", f.b, nil, "", "").Code != 200 {
		t.Fatal("revocation affected another user")
	}
	b, _ := os.ReadFile(filepath.Join(f.s.Dir(), "state.json"))
	if bytes.Contains(b, []byte(f.b)) {
		t.Fatal("plaintext token persisted")
	}
}
func TestUploadDownloadAuthorizationAndIdempotency(t *testing.T) {
	f := setup(t)
	m, z := archive(t, "shared", "3ad85aea", "基地完成", "装备在二楼")
	w := upload(t, f, f.a, "op-one", m, z)
	if w.Code != 201 {
		t.Fatal(w.Code, w.Body.String())
	}
	var b Backup
	_ = json.Unmarshal(w.Body.Bytes(), &b)
	if b.UserID != "a" || b.Uploader != "A" {
		t.Fatal("untrusted uploader")
	}
	if w = upload(t, f, f.a, "op-one", m, z); w.Code != 200 {
		t.Fatal("retry not idempotent", w.Code, w.Body.String())
	}
	m.Name = "different"
	if upload(t, f, f.a, "op-one", m, z).Code != 409 {
		t.Fatal("operation key allowed conflicting metadata")
	}
	if call(f.api, "GET", "/v1/backups/"+b.ID, f.other, nil, "", "").Code != 404 {
		t.Fatal("cross-library download")
	}
	w = call(f.api, "GET", "/v1/backups/"+b.ID, f.b, nil, "", "")
	if w.Code != 200 || !bytes.Equal(w.Body.Bytes(), z) {
		t.Fatal("download failed")
	}
	if upload(t, f, f.other, "evil", m, z).Code != 404 {
		t.Fatal("cross-library write")
	}
	w = call(f.api, "GET", "/v1/backups?q=装备", f.other, nil, "", "")
	var list ListResult
	_ = json.Unmarshal(w.Body.Bytes(), &list)
	if list.Total != 0 {
		t.Fatal("search leaks another library")
	}
	w = call(f.api, "GET", "/v1/backups?q=装备", f.b, nil, "", "")
	_ = json.Unmarshal(w.Body.Bytes(), &list)
	if list.Total != 1 {
		t.Fatal("search failed")
	}
}
func TestStablePaginationAndMemberRevoke(t *testing.T) {
	f := setup(t)
	for _, n := range []string{"first", "second", "third"} {
		m, z := archive(t, "shared", "3ad85aea", n, "")
		if w := upload(t, f, f.a, n, m, z); w.Code != 201 {
			t.Fatal(w.Body.String())
		}
	}
	w := call(f.api, "GET", "/v1/backups?page_size=1", f.b, nil, "", "")
	var first ListResult
	_ = json.Unmarshal(w.Body.Bytes(), &first)
	m, z := archive(t, "shared", "3ad85aea", "fourth", "")
	_ = upload(t, f, f.a, "fourth", m, z)
	w = call(f.api, "GET", "/v1/backups?page_size=1&page=2&snapshot=3", f.b, nil, "", "")
	var second ListResult
	_ = json.Unmarshal(w.Body.Bytes(), &second)
	if first.Snapshot != 3 || second.Total != 3 || second.Items[0].Name != "second" {
		t.Fatal("pagination shifted")
	}
	_, _ = f.s.Admin(AdminRequest{Action: "member.revoke", UserID: "b", LibraryID: "friends"})
	if call(f.api, "GET", "/v1/backups/"+first.Items[0].ID, f.b, nil, "", "").Code != 404 {
		t.Fatal("membership revocation ineffective")
	}
}
func TestRejectUnsafeFilesCorruptionAndBadTransport(t *testing.T) {
	f := setup(t)
	for _, path := range []string{"../3ad85aea", "3ad85aea:evil", "3ad85aea/evil", "3ad85aea..x", "3ad85aea.", "characters", "1234abcd"} {
		m, z := archive(t, "shared", path, "bad", "")
		if w := upload(t, f, f.a, "badpath", m, z); w.Code != 400 {
			t.Fatal(path, w.Code, w.Body.String())
		}
	}
	m, z := archive(t, "shared", "3ad85aea", "bad hash", "")
	z[len(z)/2] ^= 0xff
	if upload(t, f, f.a, "corrupt", m, z).Code != 400 {
		t.Fatal("corrupt upload accepted")
	}
	if len(f.s.state.Backups) != 0 {
		t.Fatal("failed uploads committed")
	}
	l := DefaultLimits()
	a := NewAPI(f.s, l)
	if call(a, "GET", "/v1/me", f.a, nil, "", "").Code != 400 {
		t.Fatal("plaintext request accepted")
	}
}
func TestExpiredAndDisabledAndForgedUploader(t *testing.T) {
	f := setup(t)
	_, _ = f.s.Admin(AdminRequest{Action: "user.disable", ID: "a", Disabled: true})
	if call(f.api, "GET", "/v1/me", f.a, nil, "", "").Code != 401 {
		t.Fatal("disabled user accepted")
	}
	m, z := archive(t, "shared", "3ad85aea", "world", "")
	raw, _ := json.Marshal(m)
	raw = bytes.Replace(raw, []byte(`"world_id":`), []byte(`"user_id":"other","world_id":`), 1)
	var body bytes.Buffer
	mw := multipart.NewWriter(&body)
	p, _ := mw.CreateFormField("metadata")
	p.Write(raw)
	p, _ = mw.CreateFormFile("archive", "x")
	p.Write(z)
	mw.Close()
	if call(f.api, "POST", "/v1/backups", f.b, &body, mw.FormDataContentType(), "forged").Code != 400 {
		t.Fatal("forged uploader metadata accepted")
	}
}
func TestStateSurvivesRestartAndExclusiveLock(t *testing.T) {
	f := setup(t)
	if second, e := OpenStore(f.s.Dir()); e == nil {
		second.Close()
		t.Fatal("second writer opened")
	}
	m, z := archive(t, "shared", "3ad85aea", "saved", "")
	if upload(t, f, f.a, "persist", m, z).Code != 201 {
		t.Fatal("upload")
	}
	dir := f.s.Dir()
	f.s.Close()
	s, e := OpenStore(dir)
	if e != nil {
		t.Fatal(e)
	}
	defer s.Close()
	if _, e = s.Authenticate(f.a); e != nil {
		t.Fatal(e)
	}
	if s.List("a", "", "", "newest", 1, 5, 0).Total != 1 {
		t.Fatal("backup lost on restart")
	}
}
func TestArchiveRejectsSymlinkAndQuota(t *testing.T) {
	f := setup(t)
	m, _ := archive(t, "shared", "3ad85aea", "link", "")
	var buf bytes.Buffer
	zw := zip.NewWriter(&buf)
	hdr := &zip.FileHeader{Name: "3ad85aea"}
	hdr.SetMode(os.ModeSymlink | 0777)
	w, _ := zw.CreateHeader(hdr)
	w.Write([]byte("world payload link"))
	zw.Close()
	sum := sha256.Sum256(buf.Bytes())
	m.ArchiveSHA256 = hex.EncodeToString(sum[:])
	if upload(t, f, f.a, "link", m, buf.Bytes()).Code != 400 {
		t.Fatal("symlink accepted")
	}
	m, z := archive(t, "shared", "3ad85aea", "quota", "")
	f.api.Limits.MaxStorage = 1
	if upload(t, f, f.a, "quota", m, z).Code != 507 {
		t.Fatal("quota not enforced")
	}
}

func TestExpiredCredentialAndPerUserQuota(t *testing.T) {
	f := setup(t)
	id := strings.Split(f.a[3:], ".")[0]
	if e := f.s.mutate(func(st *State) error {
		c := st.Credentials[id]
		c.ExpiresAt = time.Now().Add(-time.Second)
		st.Credentials[id] = c
		return nil
	}); e != nil {
		t.Fatal(e)
	}
	if call(f.api, "GET", "/v1/me", f.a, nil, "", "").Code != 401 {
		t.Fatal("expired credential accepted")
	}
	f.api.Limits.MaxUserStorage = 1
	m, z := archive(t, "shared", "3ad85aea", "quota", "")
	if upload(t, f, f.b, "user-quota", m, z).Code != 507 {
		t.Fatal("per-user quota not enforced")
	}
}
