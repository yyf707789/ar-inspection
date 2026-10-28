package main

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"net/http"
	"os"
	"strings"
	"sync"
	"time"
)

// 角色定义：admin 可管理用户与删除问题；inspector 只能查看与修改状态。
const (
	RoleAdmin     = "admin"
	RoleInspector = "inspector"
)

var (
	errUserNotFound    = errors.New("user not found")
	errUserExists      = errors.New("用户名已存在")
	errBadCredentials  = errors.New("用户名或密码错误")
	errUnauthorized    = errors.New("未登录或登录已过期")
	errForbidden       = errors.New("无权限")
)

// User 是后台管理用户（含密码哈希，不对外返回）。
type User struct {
	ID           string    `json:"id"`
	Username     string    `json:"username"`
	DisplayName  string    `json:"displayName"`
	Role         string    `json:"role"`
	PasswordHash string    `json:"passwordHash"`
	CreatedAt    time.Time `json:"createdAt"`
}

// userView 是返回给前端的用户信息（不含密码哈希）。
type userView struct {
	ID          string    `json:"id"`
	Username    string    `json:"username"`
	DisplayName string    `json:"displayName"`
	Role        string    `json:"role"`
	CreatedAt   time.Time `json:"createdAt"`
}

func (u *User) view() userView {
	return userView{ID: u.ID, Username: u.Username, DisplayName: u.DisplayName, Role: u.Role, CreatedAt: u.CreatedAt}
}

// hashPassword 对密码做加盐 SHA-256（演示用，非生产级加密）。
func hashPassword(p string) string {
	sum := sha256.Sum256([]byte("ar-inspection|" + p))
	return hex.EncodeToString(sum[:])
}

// UserStore 使用 users.json 持久化用户。
type UserStore struct {
	mu    sync.RWMutex
	path  string
	users map[string]*User // by id
}

// NewUserStore 加载用户；文件不存在时写入默认账号。
func NewUserStore(path string) (*UserStore, error) {
	s := &UserStore{path: path, users: make(map[string]*User)}
	raw, err := os.ReadFile(path)
	if err == nil && len(raw) > 0 {
		var list []*User
		if e := json.Unmarshal(raw, &list); e != nil {
			return nil, e
		}
		for _, u := range list {
			s.users[u.ID] = u
		}
	}
	if len(s.users) == 0 {
		now := time.Now().UTC()
		s.users["u-admin"] = &User{ID: "u-admin", Username: "admin", DisplayName: "系统管理员", Role: RoleAdmin, PasswordHash: hashPassword("admin123"), CreatedAt: now}
		s.users["u-inspector"] = &User{ID: "u-inspector", Username: "inspector", DisplayName: "现场巡检员", Role: RoleInspector, PasswordHash: hashPassword("123456"), CreatedAt: now}
		if err := s.persist(); err != nil {
			return nil, err
		}
	}
	return s, nil
}

func (s *UserStore) persist() error {
	list := make([]*User, 0, len(s.users))
	for _, u := range s.users {
		list = append(list, u)
	}
	data, err := json.MarshalIndent(list, "", "  ")
	if err != nil {
		return err
	}
	tmp := s.path + ".tmp"
	if err := os.WriteFile(tmp, data, 0o644); err != nil {
		return err
	}
	return os.Rename(tmp, s.path)
}

func (s *UserStore) List() []userView {
	s.mu.RLock()
	defer s.mu.RUnlock()
	out := make([]userView, 0, len(s.users))
	for _, u := range s.users {
		out = append(out, u.view())
	}
	return out
}

func (s *UserStore) ByUsername(username string) (*User, error) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	for _, u := range s.users {
		if u.Username == username {
			return u, nil
		}
	}
	return nil, errUserNotFound
}

func (s *UserStore) ByID(id string) (*User, error) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	u, ok := s.users[id]
	if !ok {
		return nil, errUserNotFound
	}
	return u, nil
}

func (s *UserStore) Create(u *User) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	for _, existing := range s.users {
		if existing.Username == u.Username {
			return errUserExists
		}
	}
	s.users[u.ID] = u
	return s.persist()
}

func (s *UserStore) Delete(id string) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	if _, ok := s.users[id]; !ok {
		return errUserNotFound
	}
	// 至少保留一个管理员
	admins := 0
	for _, u := range s.users {
		if u.Role == RoleAdmin {
			admins++
		}
	}
	if s.users[id].Role == RoleAdmin && admins <= 1 {
		return errors.New("必须至少保留一个管理员")
	}
	delete(s.users, id)
	return s.persist()
}

func (s *UserStore) Count() int {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return len(s.users)
}

// ---------------- 会话（内存 token -> userID） ----------------

type sessionStore struct {
	mu sync.RWMutex
	m  map[string]string
}

func newSessionStore() *sessionStore {
	return &sessionStore{m: make(map[string]string)}
}

func (s *sessionStore) create(userID string) (string, error) {
	tok, err := newID()
	if err != nil {
		return "", err
	}
	s.mu.Lock()
	s.m[tok] = userID
	s.mu.Unlock()
	return tok, nil
}

func (s *sessionStore) lookup(tok string) (string, bool) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	id, ok := s.m[tok]
	return id, ok
}

func (s *sessionStore) remove(tok string) {
	s.mu.Lock()
	delete(s.m, tok)
	s.mu.Unlock()
}

// ---------------- 认证辅助 ----------------

// bearerToken 从 Authorization: Bearer xxx 中提取 token。
func bearerToken(r *http.Request) string {
	h := r.Header.Get("Authorization")
	if len(h) > 7 && strings.EqualFold(h[:7], "Bearer ") {
		return strings.TrimSpace(h[7:])
	}
	return ""
}

// authenticateUser 校验 token 并返回当前用户；失败时已写好 401 响应。
func (a *api) authenticateUser(w http.ResponseWriter, r *http.Request) (*User, bool) {
	tok := bearerToken(r)
	if tok == "" {
		writeError(w, http.StatusUnauthorized, errUnauthorized.Error())
		return nil, false
	}
	userID, ok := a.sessions.lookup(tok)
	if !ok {
		writeError(w, http.StatusUnauthorized, errUnauthorized.Error())
		return nil, false
	}
	u, err := a.users.ByID(userID)
	if err != nil {
		writeError(w, http.StatusUnauthorized, errUnauthorized.Error())
		return nil, false
	}
	return u, true
}

// requireAdmin 在已认证基础上要求 admin 角色。
func (a *api) requireAdmin(w http.ResponseWriter, u *User) bool {
	if u.Role != RoleAdmin {
		writeError(w, http.StatusForbidden, errForbidden.Error())
		return false
	}
	return true
}

// ---------------- 登录 / 用户管理 接口 ----------------

type loginRequest struct {
	Username string `json:"username"`
	Password string `json:"password"`
	// Client 区分登录端：app（巡检 App）或 admin（后台管理）。后台仅管理员可登录。
	Client string `json:"client"`
}

func (a *api) login(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodPost {
		methodNotAllowed(w)
		return
	}
	var req loginRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "请求体不是合法的 JSON")
		return
	}
	u, err := a.users.ByUsername(strings.TrimSpace(req.Username))
	if err != nil || hashPassword(req.Password) != u.PasswordHash {
		writeError(w, http.StatusUnauthorized, errBadCredentials.Error())
		return
	}
	// 按登录端校验身份：后台管理仅管理员可登录，巡检 App 两种角色均可登录。
	scope := strings.ToLower(strings.TrimSpace(req.Client))
	if scope == "" {
		scope = "admin"
	}
	if scope == "admin" && u.Role != RoleAdmin {
		writeError(w, http.StatusForbidden, "您的权限不够")
		return
	}
	tok, err := a.sessions.create(u.ID)
	if err != nil {
		writeError(w, http.StatusInternalServerError, "生成登录凭证失败")
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{"token": tok, "user": u.view()})
}

func (a *api) logout(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodPost {
		methodNotAllowed(w)
		return
	}
	if tok := bearerToken(r); tok != "" {
		a.sessions.remove(tok)
	}
	w.WriteHeader(http.StatusNoContent)
}

func (a *api) me(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodGet {
		methodNotAllowed(w)
		return
	}
	u, ok := a.authenticateUser(w, r)
	if !ok {
		return
	}
	writeJSON(w, http.StatusOK, u.view())
}

func (a *api) listUsers(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, http.StatusOK, map[string]any{"users": a.users.List()})
}

type createUserRequest struct {
	Username    string `json:"username"`
	Password    string `json:"password"`
	DisplayName string `json:"displayName"`
	Role        string `json:"role"`
}

func (a *api) createUser(w http.ResponseWriter, r *http.Request) {
	var req createUserRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "请求体不是合法的 JSON")
		return
	}
	req.Username = strings.TrimSpace(req.Username)
	if req.Username == "" {
		writeError(w, http.StatusBadRequest, "用户名不能为空")
		return
	}
	if len(req.Password) < 4 {
		writeError(w, http.StatusBadRequest, "密码至少 4 位")
		return
	}
	if req.Role != RoleAdmin && req.Role != RoleInspector {
		writeError(w, http.StatusBadRequest, "role 必须是 admin 或 inspector")
		return
	}
	if strings.TrimSpace(req.DisplayName) == "" {
		req.DisplayName = req.Username
	}
	id, err := newID()
	if err != nil {
		writeError(w, http.StatusInternalServerError, "生成 ID 失败")
		return
	}
	u := &User{
		ID:           "u-" + id[:12],
		Username:     req.Username,
		DisplayName:  strings.TrimSpace(req.DisplayName),
		Role:         req.Role,
		PasswordHash: hashPassword(req.Password),
		CreatedAt:    time.Now().UTC(),
	}
	if err := a.users.Create(u); err != nil {
		if errors.Is(err, errUserExists) {
			writeError(w, http.StatusConflict, err.Error())
			return
		}
		writeError(w, http.StatusInternalServerError, "保存失败")
		return
	}
	writeJSON(w, http.StatusCreated, u.view())
}

func (a *api) deleteUser(w http.ResponseWriter, r *http.Request) {
	id := strings.TrimPrefix(r.URL.Path, "/api/users/")
	if id == "" || strings.Contains(id, "/") {
		writeError(w, http.StatusNotFound, "用户不存在")
		return
	}
	if err := a.users.Delete(id); err != nil {
		if errors.Is(err, errUserNotFound) {
			writeError(w, http.StatusNotFound, "用户不存在")
			return
		}
		writeError(w, http.StatusBadRequest, err.Error())
		return
	}
	w.WriteHeader(http.StatusNoContent)
}
