import { useCallback, useEffect, useMemo, useState } from 'react'
import { deleteIssue, fetchIssues, updateIssueStatus } from './api'
import type { Issue, Priority, Status, User } from './types'

const STATUS_LABEL: Record<Status, string> = {
  open: '待处理',
  in_progress: '处理中',
  resolved: '已解决',
}

const PRIORITY_LABEL: Record<Priority, string> = {
  low: '低',
  medium: '中',
  high: '高',
}

const ALL_STATUSES: Status[] = ['open', 'in_progress', 'resolved']

type StatusFilter = 'all' | Status

export default function IssuesPanel({ user }: { user: User }) {
  const [issues, setIssues] = useState<Issue[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all')
  const [keyword, setKeyword] = useState('')
  const [savingId, setSavingId] = useState<string | null>(null)
  const [deletingId, setDeletingId] = useState<string | null>(null)

  const isAdmin = user.role === 'admin'

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setIssues(await fetchIssues())
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  const handleStatusChange = useCallback(async (id: string, status: Status) => {
    setSavingId(id)
    setError(null)
    try {
      const updated = await updateIssueStatus(id, status)
      setIssues((prev) => prev.map((it) => (it.id === id ? updated : it)))
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setSavingId(null)
    }
  }, [])

  const handleDelete = useCallback(async (id: string) => {
    if (!window.confirm('确定删除这条问题吗？')) return
    setDeletingId(id)
    setError(null)
    try {
      await deleteIssue(id)
      setIssues((prev) => prev.filter((it) => it.id !== id))
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setDeletingId(null)
    }
  }, [])

  const stats = useMemo(() => {
    const count = (s: Status) => issues.filter((it) => it.status === s).length
    return {
      total: issues.length,
      open: count('open'),
      inProgress: count('in_progress'),
      resolved: count('resolved'),
    }
  }, [issues])

  const filtered = useMemo(() => {
    const kw = keyword.trim().toLowerCase()
    return issues.filter((it) => {
      const matchStatus = statusFilter === 'all' || it.status === statusFilter
      const matchKw =
        !kw ||
        it.title.toLowerCase().includes(kw) ||
        it.description.toLowerCase().includes(kw)
      return matchStatus && matchKw
    })
  }, [issues, statusFilter, keyword])

  return (
    <>
      {error && (
        <div className="banner error">
          <span>⚠️ {error}</span>
          <button className="btn btn-ghost" onClick={() => void load()}>
            重试
          </button>
        </div>
      )}

      <section className="stats">
        <StatCard label="全部问题" value={stats.total} tone="blue" />
        <StatCard label="待处理" value={stats.open} tone="red" />
        <StatCard label="处理中" value={stats.inProgress} tone="amber" />
        <StatCard label="已解决" value={stats.resolved} tone="green" />
      </section>

      <div className="toolbar">
        <input
          className="search"
          type="text"
          placeholder="搜索标题或描述…"
          value={keyword}
          onChange={(e) => setKeyword(e.target.value)}
        />
        <select
          className="filter-select"
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value as StatusFilter)}
        >
          <option value="all">全部状态</option>
          {ALL_STATUSES.map((s) => (
            <option key={s} value={s}>
              {STATUS_LABEL[s]}
            </option>
          ))}
        </select>
        <button className="btn" onClick={() => void load()} disabled={loading}>
          {loading ? '加载中…' : '刷新'}
        </button>
      </div>

      <div className="card">
        {loading ? (
          <div className="empty">
            <div className="spinner" />
            <p>加载中…</p>
          </div>
        ) : filtered.length === 0 ? (
          <div className="empty">
            <div className="empty-icon">📋</div>
            <p>暂无问题记录</p>
            <p className="empty-hint">在手机端 AR 场景中放置标记并提交后，问题会显示在这里</p>
          </div>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>标题</th>
                <th>提交人</th>
                <th>优先级</th>
                <th>状态</th>
                <th>位置 (x, y, z)</th>
                <th>更新时间</th>
                {isAdmin && <th className="th-right">操作</th>}
              </tr>
            </thead>
            <tbody>
              {filtered.map((it) => (
                <tr key={it.id}>
                  <td>
                    <div className="title">{it.title}</div>
                    {it.description && <div className="desc">{it.description}</div>}
                  </td>
                  <td className="reporter">{it.reporterName || '—'}</td>
                  <td>
                    <span className={`badge badge-${it.priority}`}>{PRIORITY_LABEL[it.priority]}</span>
                  </td>
                  <td>
                    <select
                      className={`status-select status-${it.status}`}
                      value={it.status}
                      disabled={savingId === it.id}
                      onChange={(e) => void handleStatusChange(it.id, e.target.value as Status)}
                    >
                      {ALL_STATUSES.map((s) => (
                        <option key={s} value={s}>
                          {STATUS_LABEL[s]}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td className="pos">
                    ({it.position.x.toFixed(2)}, {it.position.y.toFixed(2)}, {it.position.z.toFixed(2)})
                  </td>
                  <td className="time">{new Date(it.updatedAt).toLocaleString()}</td>
                  {isAdmin && (
                    <td className="th-right">
                      <button
                        className="btn btn-danger"
                        disabled={deletingId === it.id}
                        onClick={() => void handleDelete(it.id)}
                      >
                        {deletingId === it.id ? '删除中…' : '删除'}
                      </button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </>
  )
}

function StatCard({ label, value, tone }: { label: string; value: number; tone: string }) {
  return (
    <div className={`stat stat-${tone}`}>
      <div className="stat-value">{value}</div>
      <div className="stat-label">{label}</div>
    </div>
  )
}
