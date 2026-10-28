package main

import (
	"flag"
	"log"
	"net/http"
	"time"
)

func main() {
	addr := flag.String("addr", ":8080", "HTTP 监听地址")
	data := flag.String("data", "data.json", "数据文件路径")
	userData := flag.String("users", "users.json", "用户数据文件路径")
	flag.Parse()

	store, err := NewStore(*data)
	if err != nil {
		log.Fatalf("加载数据失败: %v", err)
	}
	users, err := NewUserStore(*userData)
	if err != nil {
		log.Fatalf("加载用户失败: %v", err)
	}
	a := &api{store: store, users: users, sessions: newSessionStore()}

	handler := withCORS(withLogging(a.routes()))

	log.Printf("巡检后端已启动，监听 %s，数据文件 %s，用户文件 %s", *addr, *data, *userData)
	if err := http.ListenAndServe(*addr, handler); err != nil {
		log.Fatalf("服务启动失败: %v", err)
	}
}

// withCORS 允许浏览器跨域访问（开发环境下 React 与 Go 不同端口）。
func withCORS(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if origin := r.Header.Get("Origin"); origin != "" {
			w.Header().Set("Access-Control-Allow-Origin", origin)
			w.Header().Add("Vary", "Origin")
			w.Header().Set("Access-Control-Allow-Methods", "GET, POST, PATCH, PUT, DELETE, OPTIONS")
			w.Header().Set("Access-Control-Allow-Headers", "Content-Type, Authorization")
		}
		if r.Method == http.MethodOptions {
			w.WriteHeader(http.StatusNoContent)
			return
		}
		next.ServeHTTP(w, r)
	})
}

// withLogging 打印请求日志，方便联调定位。
func withLogging(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		start := time.Now()
		next.ServeHTTP(w, r)
		log.Printf("%s %s (%s)", r.Method, r.URL.Path, time.Since(start))
	})
}
