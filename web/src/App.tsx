import { useCallback, useEffect, useState } from 'react'
import Login from './Login'
import IssuesPanel from './IssuesPanel'
import UsersPanel from './UsersPanel'
import { clearToken, fetchMe, getToken, logout } from './api'
import type { User } from './types'
import './App.css'

type Tab = 'issues' | 'users'

export default function App() {
  const [user, setUser] = useState<User | null>(null)
  const [booting, setBooting] = useState(true)
  const [tab, setTab] = useState<Tab>('issues')

  useEffect(() => {
    if (!getToken()) {
      setBooting(false)
      return
    }
    fetchMe()
      .then(setUser)
      .catch(() => clearToken())
      .finally(() => setBooting(false))
  }, [])

  const handleLogin = useCallback((u: User) => {
    setUser(u)
    setTab('issues')
  }, [])

  const handleLogout = useCallback(async () => {
    await logout()
    setUser(null)
  }, [])

  if (booting) {
    return (
      <div className="page empty">
        <div className="spinner" />
        <p>加载中…</p>
      </div>
    )
  }

  if (!user) {
    return <Login onLogin={handleLogin} />
  }

  const isAdmin = user.role === 'admin'

  return (
    <div className="page">
      <header className="topbar">
        <div>
          <h1>园区巡检问题管理</h1>
          <p className="subtitle">现场 AR 上报 · 统一跟踪处理</p>
        </div>
        <div className="user-area">
          <span className={`role-badge role-${user.role}`}>{isAdmin ? '管理员' : '巡检员'}</span>
          <span className="user-name">{user.displayName}</span>
          <button className="btn btn-ghost" onClick={() => void handleLogout()}>
            退出登录
          </button>
        </div>
      </header>

      <nav className="tabs">
        <button className={`tab ${tab === 'issues' ? 'active' : ''}`} onClick={() => setTab('issues')}>
          问题管理
        </button>
        {isAdmin && (
          <button className={`tab ${tab === 'users' ? 'active' : ''}`} onClick={() => setTab('users')}>
            人员管理
          </button>
        )}
      </nav>

      {tab === 'issues' ? <IssuesPanel user={user} /> : <UsersPanel currentUser={user} />}
    </div>
  )
}
