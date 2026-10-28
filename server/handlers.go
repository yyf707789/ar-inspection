package main

import (
	"encoding/json"
	"errors"
	"net/http"
	"strings"
	"time"
)

var validPriority = map[string]bool{"low": true, "medium": true, "high": true}
var validStatus = map[string]bool{"open": true, "in_progress": true, "resolved": true}

type api struct {
	store    *Store
	users    *UserStore
	sessions *sessionStore
}

// routes 组装路由。使用标准库 net/http，无第三方框架依赖。
func (a *api) routes() http.Handler {
	mux := http.NewServeMux()

	mux.HandleFunc("/health", a.health)

	// 登录 / 会话
	mux.HandleFunc("/api/login", a.login)
	mux.HandleFunc("/api/logout", a.logout)
	mux.HandleFunc("/api/me", a.me)

	// 人员管理（仅管理员）
	mux.HandleFunc("/api/users", func(w http.ResponseWriter, r *http.Request) {
		u, ok := a.authenticateUser(w, r)
		if !ok || !a.requireAdmin(w, u) {
			return
		}
		switch r.Method {
		case http.MethodGet:
			a.listUsers(w, r)
		case http.MethodPost:
			a.createUser(w, r)
		default:
			methodNotAllowed(w)
		}
	})
	mux.HandleFunc("/api/users/", func(w http.ResponseWriter, r *http.Request) {
		u, ok := a.authenticateUser(w, r)
		if !ok || !a.requireAdmin(w, u) {
			return
		}
		if r.Method == http.MethodDelete {
			a.deleteUser(w, r)
		} else {
			methodNotAllowed(w)
		}
	})

	// 问题：GET / POST 都需要登录（AR App 登录后上报）
	mux.HandleFunc("/api/issues", func(w http.ResponseWriter, r *http.Request) {
		switch r.Method {
		case http.MethodGet:
			if _, ok := a.authenticateUser(w, r); !ok {
				return
			}
			a.listIssues(w, r)
		case http.MethodPost:
			user, ok := a.authenticateUser(w, r)
			if !ok {
				return
			}
			a.createIssue(w, r, user)
		default:
			methodNotAllowed(w)
		}
	})

	// /api/issues/{id}：需要登录；删除仅管理员
	mux.HandleFunc("/api/issues/", func(w http.ResponseWriter, r *http.Request) {
		user, ok := a.authenticateUser(w, r)
		if !ok {
			return
		}
		switch r.Method {
		case http.MethodPatch, http.MethodPut:
			a.updateIssue(w, r)
		case http.MethodDelete:
			if !a.requireAdmin(w, user) {
				return
			}
			a.deleteIssue(w, r)
		default:
			methodNotAllowed(w)
		}
	})

	return mux
}

func (a *api) health(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, http.StatusOK, map[string]any{
		"status": "ok",
		"time":   time.Now().UTC().Format(time.RFC3339),
	})
}

type createIssueRequest struct {
	Title       string   `json:"title"`
	Description string   `json:"description"`
	Priority    string   `json:"priority"`
	Position    Position `json:"position"`
}

func (a *api) createIssue(w http.ResponseWriter, r *http.Request, reporter *User) {
	var req createIssueRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "请求体不是合法的 JSON")
		return
	}
	if strings.TrimSpace(req.Title) == "" {
		writeError(w, http.StatusBadRequest, "title 不能为空")
		return
	}
	if !validPriority[req.Priority] {
		writeError(w, http.StatusBadRequest, "priority 必须是 low、medium 或 high")
		return
	}

	id, err := newID()
	if err != nil {
		writeError(w, http.StatusInternalServerError, "生成 ID 失败")
		return
	}
	reporterName := strings.TrimSpace(reporter.DisplayName)
	if reporterName == "" {
		reporterName = reporter.Username
	}
	now := time.Now().UTC()
	issue := Issue{
		ID:           id,
		Title:        req.Title,
		Description:  req.Description,
		Priority:     req.Priority,
		Status:       "open", // 新问题默认状态为 open
		Position:     req.Position,
		ReporterID:   reporter.ID,
		ReporterName: reporterName,
		CreatedAt:    now,
		UpdatedAt:    now,
	}

	created, err := a.store.Create(issue)
	if err != nil {
		writeError(w, http.StatusInternalServerError, "保存失败")
		return
	}
	writeJSON(w, http.StatusCreated, created)
}

func (a *api) listIssues(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, http.StatusOK, map[string]any{
		"issues": a.store.List(),
	})
}

type updateIssueRequest struct {
	Status string `json:"status"`
}

func (a *api) updateIssue(w http.ResponseWriter, r *http.Request) {
	id := issueIDFromPath(r)
	if id == "" {
		writeError(w, http.StatusNotFound, "issue 不存在")
		return
	}

	var req updateIssueRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "请求体不是合法的 JSON")
		return
	}
	if !validStatus[req.Status] {
		writeError(w, http.StatusBadRequest, "status 必须是 open、in_progress 或 resolved")
		return
	}

	updated, err := a.store.UpdateStatus(id, req.Status)
	if err != nil {
		if errors.Is(err, errNotFound) {
			writeError(w, http.StatusNotFound, "issue 不存在")
			return
		}
		writeError(w, http.StatusInternalServerError, "更新失败")
		return
	}
	writeJSON(w, http.StatusOK, updated)
}

func (a *api) deleteIssue(w http.ResponseWriter, r *http.Request) {
	id := issueIDFromPath(r)
	if id == "" {
		writeError(w, http.StatusNotFound, "issue 不存在")
		return
	}
	if err := a.store.Delete(id); err != nil {
		if errors.Is(err, errNotFound) {
			writeError(w, http.StatusNotFound, "issue 不存在")
			return
		}
		writeError(w, http.StatusInternalServerError, "删除失败")
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

// issueIDFromPath 从 /api/issues/{id} 提取 id，非法路径返回空串。
func issueIDFromPath(r *http.Request) string {
	id := strings.TrimPrefix(r.URL.Path, "/api/issues/")
	if id == "" || strings.Contains(id, "/") {
		return ""
	}
	return id
}

func writeJSON(w http.ResponseWriter, code int, v any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(code)
	_ = json.NewEncoder(w).Encode(v)
}

func writeError(w http.ResponseWriter, code int, msg string) {
	writeJSON(w, code, map[string]string{"error": msg})
}

func methodNotAllowed(w http.ResponseWriter) {
	writeError(w, http.StatusMethodNotAllowed, "不支持的方法")
}
