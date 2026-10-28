package main

import (
	"bytes"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"testing"
)

func newTestAPI(t *testing.T) *api {
	t.Helper()
	store, err := NewStore(filepath.Join(t.TempDir(), "data.json"))
	if err != nil {
		t.Fatalf("NewStore: %v", err)
	}
	return &api{store: store}
}

func doJSON(t *testing.T, h http.Handler, method, path string, body any) *httptest.ResponseRecorder {
	t.Helper()
	var buf bytes.Buffer
	if body != nil {
		if err := json.NewEncoder(&buf).Encode(body); err != nil {
			t.Fatalf("encode: %v", err)
		}
	}
	req := httptest.NewRequest(method, path, &buf)
	req.Header.Set("Content-Type", "application/json")
	rec := httptest.NewRecorder()
	h.ServeHTTP(rec, req)
	return rec
}

func TestHealth(t *testing.T) {
	a := newTestAPI(t)
	rec := doJSON(t, a.routes(), http.MethodGet, "/health", nil)
	if rec.Code != http.StatusOK {
		t.Fatalf("health status = %d, want 200", rec.Code)
	}
}

func TestCreateAndListIssue(t *testing.T) {
	a := newTestAPI(t)
	h := a.routes()

	rec := doJSON(t, h, http.MethodPost, "/api/issues", map[string]any{
		"title":       "入口墙面破损",
		"description": "左侧墙体存在裂缝",
		"priority":    "high",
		"position":    map[string]any{"x": 0.42, "y": 0.03, "z": 1.26},
	})
	if rec.Code != http.StatusCreated {
		t.Fatalf("create status = %d, want 201; body=%s", rec.Code, rec.Body.String())
	}
	var created Issue
	if err := json.Unmarshal(rec.Body.Bytes(), &created); err != nil {
		t.Fatalf("unmarshal created: %v", err)
	}
	if created.ID == "" || created.Status != "open" {
		t.Fatalf("unexpected created issue: %+v", created)
	}

	rec = doJSON(t, h, http.MethodGet, "/api/issues", nil)
	if rec.Code != http.StatusOK {
		t.Fatalf("list status = %d", rec.Code)
	}
	var list struct {
		Issues []Issue `json:"issues"`
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &list); err != nil {
		t.Fatalf("unmarshal list: %v", err)
	}
	if len(list.Issues) != 1 {
		t.Fatalf("list len = %d, want 1", len(list.Issues))
	}
}

func TestCreateValidation(t *testing.T) {
	a := newTestAPI(t)
	h := a.routes()

	cases := []map[string]any{
		// 缺少 title
		{"priority": "high", "position": map[string]any{"x": 0, "y": 0, "z": 0}},
		// 非法 priority
		{"title": "ok", "priority": "urgent", "position": map[string]any{"x": 0, "y": 0, "z": 0}},
	}
	for _, c := range cases {
		rec := doJSON(t, h, http.MethodPost, "/api/issues", c)
		if rec.Code != http.StatusBadRequest {
			t.Fatalf("want 400, got %d for %v", rec.Code, c)
		}
	}
}

func TestUpdateStatus(t *testing.T) {
	a := newTestAPI(t)
	h := a.routes()

	rec := doJSON(t, h, http.MethodPost, "/api/issues", map[string]any{
		"title": "t", "priority": "low", "position": map[string]any{"x": 0, "y": 0, "z": 0},
	})
	var created Issue
	if err := json.Unmarshal(rec.Body.Bytes(), &created); err != nil {
		t.Fatalf("unmarshal created: %v", err)
	}

	rec = doJSON(t, h, http.MethodPatch, "/api/issues/"+created.ID, map[string]string{"status": "resolved"})
	if rec.Code != http.StatusOK {
		t.Fatalf("update status = %d, want 200", rec.Code)
	}
	var updated Issue
	if err := json.Unmarshal(rec.Body.Bytes(), &updated); err != nil {
		t.Fatalf("unmarshal updated: %v", err)
	}
	if updated.Status != "resolved" {
		t.Fatalf("status = %s, want resolved", updated.Status)
	}

	// 非法状态
	rec = doJSON(t, h, http.MethodPatch, "/api/issues/"+created.ID, map[string]string{"status": "bogus"})
	if rec.Code != http.StatusBadRequest {
		t.Fatalf("want 400 for bogus status, got %d", rec.Code)
	}

	// 不存在的 ID
	rec = doJSON(t, h, http.MethodPatch, "/api/issues/does-not-exist", map[string]string{"status": "open"})
	if rec.Code != http.StatusNotFound {
		t.Fatalf("want 404, got %d", rec.Code)
	}
}

func TestPersistenceAcrossRestart(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "data.json")

	s1, err := NewStore(path)
	if err != nil {
		t.Fatalf("NewStore: %v", err)
	}
	h1 := (&api{store: s1}).routes()
	doJSON(t, h1, http.MethodPost, "/api/issues", map[string]any{
		"title": "persist", "priority": "medium", "position": map[string]any{"x": 1, "y": 2, "z": 3},
	})

	// 模拟服务重启：重新加载同一文件
	s2, err := NewStore(path)
	if err != nil {
		t.Fatalf("reload: %v", err)
	}
	if got := len(s2.List()); got != 1 {
		t.Fatalf("after restart list len = %d, want 1", got)
	}
}

func TestDeleteIssue(t *testing.T) {
	a := newTestAPI(t)
	h := a.routes()

	rec := doJSON(t, h, http.MethodPost, "/api/issues", map[string]any{
		"title": "del", "priority": "low", "position": map[string]any{"x": 0, "y": 0, "z": 0},
	})
	var created Issue
	if err := json.Unmarshal(rec.Body.Bytes(), &created); err != nil {
		t.Fatalf("unmarshal: %v", err)
	}

	rec = doJSON(t, h, http.MethodDelete, "/api/issues/"+created.ID, nil)
	if rec.Code != http.StatusNoContent {
		t.Fatalf("delete status = %d, want 204", rec.Code)
	}

	rec = doJSON(t, h, http.MethodGet, "/api/issues", nil)
	var list struct {
		Issues []Issue `json:"issues"`
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &list); err != nil {
		t.Fatalf("unmarshal list: %v", err)
	}
	if len(list.Issues) != 0 {
		t.Fatalf("after delete list len = %d, want 0", len(list.Issues))
	}

	// 删除不存在的 ID
	rec = doJSON(t, h, http.MethodDelete, "/api/issues/nope", nil)
	if rec.Code != http.StatusNotFound {
		t.Fatalf("want 404, got %d", rec.Code)
	}
}
