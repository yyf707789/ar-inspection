import { useCallback, useEffect, useState } from 'react'
import { createUser, deleteUser, fetchUsers } from './api'
import type { Role, User } from './types'

const ROLE_LABEL: Record<Role, string> = {
  admin: '管理员',
  inspector: '巡检员',
}

function msg(e: unknown): string {
  return e instanceof Error ? e.message : String(e)
}

export default function UsersPanel({ currentUser }: { currentUser: User }) {
  const [users, setUsers] = useState<User[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [form, setForm] = useState<{
    username: string
    password: string
    displayName: string
    role: Role
  }>({ username: '', password: '', displayName: '', role: 'inspector' })

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setUsers(await fetchUsers())
    } catch (e) {
      setError(msg(e))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  const add = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    setBusy(true)
    try {
      await createUser(form)
      setForm({ username: '', password: '', displayName: '', role: 'inspector' })
      await load()
    } catch (err) {
      setError(msg(err))
    } finally {
      setBusy(false)
    }
  }

  const remove = async (u: User) => {
    if (u.id === currentUser.id) return
    if (!window.confirm(`确定删除用户「${u.displayName}」吗？`)) return
    setError(null)
    try {
      await deleteUser(u.id)
      await load()
    } catch (err) {
      setError(msg(err))
    }
  }

  return (
    <>
      {error && <div className="banner error">⚠️ {error}</div>}

      <div className="card">
        <form className="user-form" onSubmit={add}>
          <input
            className="search"
            placeholder="用户名"
            value={form.username}
            onChange={(e) => setForm({ ...form, username: e.target.value })}
          />
          <input
            className="search"
            placeholder="显示名（可空）"
            value={form.displayName}
            onChange={(e) => setForm({ ...form, displayName: e.target.value })}
          />
          <input
            className="search"
            type="password"
            placeholder="密码（≥4 位）"
            value={form.password}
            onChange={(e) => setForm({ ...form, password: e.target.value })}
          />
          <select
            className="filter-select"
            value={form.role}
            onChange={(e) => setForm({ ...form, role: e.target.value as Role })}
          >
            <option value="inspector">巡检员</option>
            <option value="admin">管理员</option>
          </select>
          <button
            className="btn"
            type="submit"
            disabled={busy || !form.username.trim() || form.password.length < 4}
          >
            {busy ? '添加中…' : '添加用户'}
          </button>
        </form>
      </div>

      <div className="card">
        {loading ? (
          <div className="empty">
            <div className="spinner" />
            <p>加载中…</p>
          </div>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>显示名</th>
                <th>用户名</th>
                <th>角色</th>
                <th>创建时间</th>
                <th className="th-right">操作</th>
              </tr>
            </thead>
            <tbody>
              {users.map((u) => (
                <tr key={u.id}>
                  <td className="title">{u.displayName}</td>
                  <td className="pos">{u.username}</td>
                  <td>
                    <span className={`role-badge role-${u.role}`}>{ROLE_LABEL[u.role]}</span>
                  </td>
                  <td className="time">{new Date(u.createdAt).toLocaleString()}</td>
                  <td className="th-right">
                    <button
                      className="btn btn-danger"
                      disabled={u.id === currentUser.id}
                      onClick={() => void remove(u)}
                      title={u.id === currentUser.id ? '不能删除当前登录用户' : '删除用户'}
                    >
                      删除
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </>
  )
}
