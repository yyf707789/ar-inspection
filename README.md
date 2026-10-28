# 园区巡检 AR 原型（Unity + Go + React）

打通「Unity AR 放置标记并上报 → Go 后端保存 → React 页面查看 → React 修改处理状态」的最小闭环原型，并在此基础上补全了 **登录权限** 与 **人员管理**。

## 目录结构

```
.
├── server/        Go 后端（仅标准库，JSON 文件持久化）
├── web/           React 管理端（Vite + TypeScript）
├── unity/         Unity AR 移动端（C# + AR Foundation）
│   └── UNITY_SETUP.md    Unity 详细搭建步骤
├── AI_LOG.md      AI 使用记录
└── README.md
```

## 版本

| 部分 | 技术 | 版本 |
|---|---|---|
| 移动端 | Unity | 2022.3 LTS |
| | AR Foundation | 5.1.x |
| | ARCore XR Plugin | 5.1.x（Android 真机） |
| | ARKit XR Plugin | 5.1.x（iOS 真机） |
| 管理端 | React | 18.x |
| | TypeScript | 5.x |
| | Vite | 5.x |
| 后端 | Go | 1.22+（仅标准库，无第三方依赖） |

## 三部分如何启动

### 1. Go 后端

```bash
cd server
go run .                          # 监听 :8080，问题存 data.json，用户存 users.json
go run . -addr :8080 -data data.json -users users.json
go test ./...                     # 运行单元测试
```

- 问题数据保存在 `data.json`，用户数据保存在 `users.json`，服务重启后仍然存在。
- 首次启动会自动写入默认账号（见下方「登录与权限」）。
- 已内置 CORS，浏览器可直接跨域访问。

### 2. React 管理端

```bash
cd web
npm install
npm run dev                       # 打开 http://localhost:5173，/api 自动代理到 localhost:8080
```

- 生产构建：`npm run build`，产物在 `web/dist/`。
- 若前端与后端不同源，构建前设置环境变量：`VITE_API_BASE_URL=http://your-host:8080 npm run build`。

### 3. Unity 移动端

详见 [`unity/UNITY_SETUP.md`](unity/UNITY_SETUP.md)。核心步骤：新建 3D 项目 → 装 AR Foundation / ARCore / ARKit 包 → 场景中加入 AR Session 与 XR Origin → 挂上 AR Plane Manager、AR Raycast Manager → 把 `Assets/Scripts/` 三个脚本挂到空物体上 → 打包真机。

> 本仓库提供 Unity 源码与配置说明；真机打包与演示视频需在 Unity 环境内完成。

## Unity 真机如何配置 Go 后端地址

1. 手机与运行 Go 后端的电脑连到同一 Wi-Fi。
2. 在 Unity 中选中挂有 `IssueReporter` 的物体，把 **Server Url** 填成 `http://<电脑局域网IP>:8080`（用 `ipconfig` 查看电脑 IP）。
3. 自测联通：手机浏览器打开 `http://<电脑IP>:8080/health`，返回 `{"status":"ok",...}` 即通。
4. App 打开后需先 **登录**，登录成功后才能放置标记并上报问题。

## 登录与权限

系统包含两种角色：**管理员（admin）** 与 **巡检员（inspector）**。

| 账号 | 密码 | 角色 | 登录后台管理 | 登录 App |
|---|---|---|---|---|
| `admin` | `admin123` | 管理员 | ✅ | ✅ |
| `inspector` | `123456` | 巡检员 | ❌（提示「您的权限不够」） | ✅ |

- **后台管理端**（React）仅管理员可登录；巡检员登录会返回「您的权限不够」。
- **App 端**（Unity）两种角色均可登录。
- 登录接口通过 `client` 字段区分登录端：`client=admin`（后台）或 `client=app`（App）。
- 管理员可在「人员管理」页签中新增/删除用户；密码以加盐 SHA-256 哈希存储，不保存明文。
- 问题上报会记录 **提交人**（`reporterId` / `reporterName`）。

## 接口设计

### 问题

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/health` | 服务运行状态检查（无需登录） |
| POST | `/api/issues` | 上报问题（**需登录**）；`title` 必填，`priority` ∈ low/medium/high，`status` 默认 open |
| GET | `/api/issues` | 问题列表（**需登录**） |
| PATCH | `/api/issues/{id}` | 修改状态（**需登录**），`status` ∈ open/in_progress/resolved |
| DELETE | `/api/issues/{id}` | 删除问题（**仅管理员**） |

### 认证与人员管理

| 方法 | 路径 | 说明 |
|---|---|---|
| POST | `/api/login` | 登录，请求体 `{username, password, client}`；返回 `{token, user}` |
| POST | `/api/logout` | 退出登录 |
| GET | `/api/me` | 获取当前登录用户（需登录） |
| GET | `/api/users` | 用户列表（**仅管理员**） |
| POST | `/api/users` | 新增用户（**仅管理员**），`{username, password, displayName, role}` |
| DELETE | `/api/users/{id}` | 删除用户（**仅管理员**） |

- 除 `/health` 与 `/api/login` 外，其余接口均需在请求头携带 `Authorization: Bearer <token>`。
- 参数错误（标题为空、枚举值非法、用户名重复、密码过短等）返回 `400/409` 与 `{"error":"明确原因"}`；未登录返回 `401`；无权限返回 `403`；ID 不存在返回 `404`。

### 数据与请求示例

```json
{
  "id": "由后端生成",
  "title": "入口墙面破损",
  "description": "左侧墙体存在裂缝",
  "priority": "high",
  "status": "open",
  "position": { "x": 0.42, "y": 0.03, "z": 1.26 },
  "reporterId": "u-inspector",
  "reporterName": "现场巡检员",
  "createdAt": "由后端生成",
  "updatedAt": "由后端生成"
}
```

```bash
# 登录（App 端，任意角色）
curl -X POST http://localhost:8080/api/login \
  -H "Content-Type: application/json" \
  -d '{"username":"inspector","password":"123456","client":"app"}'

# 上报（带上 token）
curl -X POST http://localhost:8080/api/issues \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <token>" \
  -d '{"title":"入口墙面破损","description":"左侧墙体存在裂缝","priority":"high","position":{"x":0.42,"y":0.03,"z":1.26}}'

# 列表 / 改状态 / 删除
curl http://localhost:8080/api/issues -H "Authorization: Bearer <token>"
curl -X PATCH http://localhost:8080/api/issues/<id> \
  -H "Content-Type: application/json" -H "Authorization: Bearer <token>" \
  -d '{"status":"resolved"}'
curl -X DELETE http://localhost:8080/api/issues/<id> -H "Authorization: Bearer <token>"
```

## 已完成

- [x] Unity 真机 AR：检测水平平面、点击放置标记、填写并上报（标题/描述/优先级/位置）、成功失败提示、UI 防误触。
- [x] Go 后端：健康检查、问题创建/列表/改状态/删除、唯一 ID、默认 open、参数校验、JSON 持久化、CORS、单元测试。
- [x] React：获取列表、展示标题/优先级/状态/提交人、修改状态、删除、后端不可用时错误提示（不白屏）。
- [x] 登录与权限：两种角色（admin/inspector）、后台仅管理员可登录、App 两种角色可登录、token 鉴权。
- [x] 人员管理：管理员新增/删除用户、角色管理、至少保留一个管理员。
- [x] 提交人记录：上报时自动记录 `reporterId` / `reporterName`。
- [x] 三端字段名保持一致。

## 未完成 / 已知问题

- 密码传输仍为 HTTP 明文（演示环境可接受；生产应改 HTTPS/TLS）。
- 应用重启后不把历史标记恢复到真实物理位置（题目明确不要求）。
- 未实现实时推送（WebSocket）、详情页、分页、跨设备定位等加分项。
- Unity 真机需在真机环境打包验证并录制演示视频（本仓库提供源码与配置说明）。

## 演示视频要求

需录制两段 3–5 分钟视频（手机、电脑各一段），至少展示：真机登录 → 识别平面并放置标记 → 手机提交问题 → React 刷新后看到该问题（含提交人）→ React 修改状态 → 管理员后台的人员管理 → Go 重启后数据仍然存在。
