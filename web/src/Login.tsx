import { useState } from 'react'
import type { User } from './types'
import { login } from './api'

export default function Login({ onLogin }: { onLogin: (u: User) => void }) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    setSuccess(null)
    setBusy(true)
    try {
      // 后台管理端只允许管理员登录
      const data = await login(username.trim(), password, 'admin')
      setSuccess('登录成功')
      setTimeout(() => onLogin(data.user), 700)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login-page">
      <form className="login-card" onSubmit={submit}>
        <h1>园区巡检 · 后台管理</h1>
        <p className="subtitle">仅管理员可登录后台；巡检人员请使用手机 App</p>

        {error && <div className="banner error">⚠️ {error}</div>}
        {success && <div className="banner success">✅ {success}</div>}

        <label className="field">
          <span>用户名</span>
          <input
            className="search"
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            placeholder="请输入用户名"
            autoFocus
          />
        </label>

        <label className="field">
          <span>密码</span>
          <input
            className="search"
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            placeholder="请输入密码"
          />
        </label>

        <button
          className="btn login-btn"
          type="submit"
          disabled={busy || !username.trim() || !password}
        >
          {busy ? '登录中…' : '登录'}
        </button>

        <p className="login-hint">
          管理员：<b>admin / admin123</b>
          <br />
          巡检员：<b>inspector / 123456</b>（仅可登录 App，不能登录后台）
        </p>
      </form>
    </div>
  )
}
