import type { Issue, Role, Status, User } from './types'

// 开发环境默认走 Vite 代理（同源 /api -> http://localhost:8080）。
// 生产部署时可通过 VITE_API_BASE_URL 指定后端地址。
const BASE = import.meta.env.VITE_API_BASE_URL ?? ''
const TOKEN_KEY = 'ar-inspection-token'

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY)
}

function setToken(token: string): void {
  localStorage.setItem(TOKEN_KEY, token)
}

export function clearToken(): void {
  localStorage.removeItem(TOKEN_KEY)
}

async function doFetch(url: string, init?: RequestInit): Promise<Response> {
  const headers = new Headers(init?.headers)
  const token = getToken()
  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }
  if (init?.body && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }
  try {
    return await fetch(url, { ...init, headers })
  } catch {
    throw new Error('后端服务不可用，请确认 Go 服务已启动')
  }
}

async function readError(res: Response): Promise<string> {
  try {
    const data = (await res.json()) as { error?: string }
    if (data && typeof data.error === 'string') {
      return data.error
    }
  } catch {
    /* ignore JSON parse error, fall through */
  }
  return `${res.status} ${res.statusText}`
}

// ---------------- 认证 ----------------

export async function login(
  username: string,
  password: string,
  client: 'app' | 'admin' = 'admin',
): Promise<{ token: string; user: User }> {
  const res = await doFetch(`${BASE}/api/login`, {
    method: 'POST',
    body: JSON.stringify({ username, password, client }),
  })
  if (!res.ok) {
    throw new Error(await readError(res))
  }
  const data = (await res.json()) as { token: string; user: User }
  setToken(data.token)
  return data
}

export async function logout(): Promise<void> {
  try {
    await doFetch(`${BASE}/api/logout`, { method: 'POST' })
  } catch {
    /* ignore network error on logout */
  }
  clearToken()
}

export async function fetchMe(): Promise<User> {
  const res = await doFetch(`${BASE}/api/me`)
  if (!res.ok) {
    throw new Error(await readError(res))
  }
  return (await res.json()) as User
}

// ---------------- 问题 ----------------

export async function fetchIssues(): Promise<Issue[]> {
  const res = await doFetch(`${BASE}/api/issues`)
  if (!res.ok) {
    throw new Error(await readError(res))
  }
  const data = (await res.json()) as { issues: Issue[] }
  return data.issues
}

export async function updateIssueStatus(id: string, status: Status): Promise<Issue> {
  const res = await doFetch(`${BASE}/api/issues/${id}`, {
    method: 'PATCH',
    body: JSON.stringify({ status }),
  })
  if (!res.ok) {
    throw new Error(await readError(res))
  }
  return (await res.json()) as Issue
}

export async function deleteIssue(id: string): Promise<void> {
  const res = await doFetch(`${BASE}/api/issues/${id}`, {
    method: 'DELETE',
  })
  if (!res.ok) {
    throw new Error(await readError(res))
  }
}

// ---------------- 人员管理（仅管理员） ----------------

export async function fetchUsers(): Promise<User[]> {
  const res = await doFetch(`${BASE}/api/users`)
  if (!res.ok) {
    throw new Error(await readError(res))
  }
  const data = (await res.json()) as { users: User[] }
  return data.users
}

export async function createUser(payload: {
  username: string
  password: string
  displayName: string
  role: Role
}): Promise<User> {
  const res = await doFetch(`${BASE}/api/users`, {
    method: 'POST',
    body: JSON.stringify(payload),
  })
  if (!res.ok) {
    throw new Error(await readError(res))
  }
  return (await res.json()) as User
}

export async function deleteUser(id: string): Promise<void> {
  const res = await doFetch(`${BASE}/api/users/${id}`, {
    method: 'DELETE',
  })
  if (!res.ok) {
    throw new Error(await readError(res))
  }
}
