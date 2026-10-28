package main

import (
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"errors"
	"os"
	"sync"
	"time"
)

// Position 是问题标记在 AR 场景中的三维坐标（米，y 向上）。
type Position struct {
	X float64 `json:"x"`
	Y float64 `json:"y"`
	Z float64 `json:"z"`
}

// Issue 是三端（Unity / Go / React）共享的数据结构，字段名保持一致。
type Issue struct {
	ID           string    `json:"id"`
	Title        string    `json:"title"`
	Description  string    `json:"description"`
	Priority     string    `json:"priority"`
	Status       string    `json:"status"`
	Position     Position  `json:"position"`
	ReporterID   string    `json:"reporterId"`   // 上报人 ID
	ReporterName string    `json:"reporterName"` // 上报人显示名
	CreatedAt    time.Time `json:"createdAt"`
	UpdatedAt    time.Time `json:"updatedAt"`
}

var errNotFound = errors.New("issue not found")

// Store 使用本地 JSON 文件持久化数据，进程重启后仍然保留。
type Store struct {
	mu     sync.RWMutex
	path   string
	issues map[string]Issue
	order  []string // 保持插入顺序，列表按创建先后返回
}

// NewStore 从磁盘加载数据；文件不存在时初始化为空。
func NewStore(path string) (*Store, error) {
	s := &Store{
		path:   path,
		issues: make(map[string]Issue),
		order:  []string{},
	}
	raw, err := os.ReadFile(path)
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			return s, nil
		}
		return nil, err
	}
	if len(raw) == 0 {
		return s, nil
	}
	var list []Issue
	if err := json.Unmarshal(raw, &list); err != nil {
		return nil, err
	}
	for _, it := range list {
		s.issues[it.ID] = it
		s.order = append(s.order, it.ID)
	}
	return s, nil
}

// List 返回按创建先后排序的问题列表副本。
func (s *Store) List() []Issue {
	s.mu.RLock()
	defer s.mu.RUnlock()
	out := make([]Issue, 0, len(s.order))
	for _, id := range s.order {
		out = append(out, s.issues[id])
	}
	return out
}

// Create 保存新问题并写回磁盘。
func (s *Store) Create(it Issue) (Issue, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.issues[it.ID] = it
	s.order = append(s.order, it.ID)
	if err := s.persist(); err != nil {
		return Issue{}, err
	}
	return it, nil
}

// UpdateStatus 修改问题状态并写回磁盘。
func (s *Store) UpdateStatus(id, status string) (Issue, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	it, ok := s.issues[id]
	if !ok {
		return Issue{}, errNotFound
	}
	it.Status = status
	it.UpdatedAt = time.Now().UTC()
	s.issues[id] = it
	if err := s.persist(); err != nil {
		return Issue{}, err
	}
	return it, nil
}

// Delete 删除指定问题并写回磁盘。
func (s *Store) Delete(id string) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	if _, ok := s.issues[id]; !ok {
		return errNotFound
	}
	delete(s.issues, id)
	for i, oid := range s.order {
		if oid == id {
			s.order = append(s.order[:i], s.order[i+1:]...)
			break
		}
	}
	return s.persist()
}

// persist 把内存数据原子地写回磁盘（调用方必须已持有锁）。
func (s *Store) persist() error {
	list := make([]Issue, 0, len(s.order))
	for _, id := range s.order {
		list = append(list, s.issues[id])
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

// newID 生成 32 位十六进制唯一 ID。
func newID() (string, error) {
	b := make([]byte, 16)
	if _, err := rand.Read(b); err != nil {
		return "", err
	}
	return hex.EncodeToString(b), nil
}
