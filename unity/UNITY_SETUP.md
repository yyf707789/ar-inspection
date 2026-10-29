# Unity AR 移动端搭建步骤

> 本目录已包含**完整的 Unity 工程源**（`Assets/`、`Packages/`、`ProjectSettings/`），可直接用 Unity Hub「打开」本 `unity/` 目录作为工程继续打包。下面是「从零新建」时的手动搭建说明。

本工程实现「登录 → 检测水平平面 → 点击放置标记 → 填写并上报问题」，相关脚本在 `Assets/` 根目录：`UnityCompat.cs`、`ARPlacementController.cs`、`IssueReporter.cs`（登录 + 上报，网络请求用原始 Socket 实现）；另含 `Assets/Editor/ARSceneSetup.cs`（一键搭场景）和 `Assets/Plugins/Android/AndroidManifest.xml`（INTERNET 权限与明文放行）。

## 0. 版本要求

| 项 | 版本 |
|---|---|
| Unity | 2022.3 LTS（Unity 6 / 6000.x 亦可，脚本已做兼容） |
| AR Foundation | 5.1.x |
| ARCore XR Plugin | 5.1.x（Android 真机） |
| ARKit XR Plugin | 5.1.x（iOS 真机） |
| AR Foundation Samples | 可选，用于获取「AR Default Plane」平面可视化预制体 |

## 1. 新建项目

1. Unity Hub → 新建 3D 项目，选择 Unity 2022.3 LTS。
2. 把本目录 `Assets/` 根目录下的三个脚本复制进项目的 `Assets/`：
   - `UnityCompat.cs`
   - `ARPlacementController.cs`
   - `IssueReporter.cs`
   （可选：把 `Assets/Editor/` 与 `Assets/Plugins/Android/` 一并复制；前者提供「Tools → Auto-Build AR Scene」一键搭场景，后者是明文放行与 INTERNET 权限配置。）

## 2. 安装依赖包

打开 Window → Package Manager：

1. 点左上角「+」→「Install package by name」安装：
   - `com.unity.xr.arfoundation`（选 5.1.x）
   - Android：`com.unity.xr.arcore`（5.1.x）
   - iOS：`com.unity.xr.arkit`（5.1.x）
2. （可选但推荐）再安装 `com.unity.xr.arfoundation-samples`，里面有现成的「AR Default Plane」预制体。

## 3. 项目设置

### 3.1 启用 XR 插件

Edit → Project Settings → XR Plug-in Management：

1. 点「Install XR Plugin Management」。
2. 切到 Android 平台页签，勾选 **ARCore**。
3. 切到 iOS 平台页签，勾选 **ARKit**。

### 3.2 输入处理（重要）

Edit → Project Settings → Player → Other Settings → **Active Input Handling** 改为 **Both**（或 Input Manager (Old)）。脚本里用的是传统 `Input.GetTouch`。

### 3.3 平台权限

- **iOS**：Player Settings → iOS → Other Settings → **Camera Usage Description** 填 `用于 AR 平面检测`。
- **Android**：Player Settings → Android → Other Settings → Minimum API Level 设为 **26** 或更高（ARCore 要求）。

## 4. 搭建场景

1. 删除默认的 Main Camera。
2. 菜单 GameObject → XR → **AR Session**（会自动带上 AR Session + AR Input Manager 组件）。
3. 菜单 GameObject → XR → **XR Origin**（自动创建带 AR Camera Manager / AR Camera Background 的相机）。
4. 在 XR Origin 物体上：
   - Add Component → **AR Plane Manager**；若安装了 Samples，把 `AR Default Plane` 预制体拖到 Plane Prefab 字段（用于可视化检测到的平面；不填也能点击平面，只是看不到平面网格）。
   - Add Component → **AR Raycast Manager**。
5. 新建空物体，命名为 `PlacementController`，挂上两个脚本：
   - `ARPlacementController`
   - `IssueReporter`
   - 组件里的 `Raycast Manager` / `Plane Manager` / `Marker Prefab` 均可留空，脚本运行时会自动查找、自动生成标记。

> 如果场景偏暗，可加一盏 Directional Light（AR 模板通常自带，但空场景需要手动加）。

## 5. 配置后端地址

选中 `PlacementController`，在 `IssueReporter` 组件的 **Server Url** 里填：

```
http://<电脑局域网IP>:8080
```

要求手机与电脑在同一 Wi-Fi。运行时也可在画面底部「服务器地址」输入框里临时修改，无需重新打包。

## 6. 打包到真机

1. File → Build Settings → 切到 Android 或 iOS 平台 → Switch Platform。
2. 连上真机，勾选开发模式，点 Build And Run。
3. 真机上移动手机对准地面，出现平面网格后点击即可放置橙色标记；填标题、选优先级、点「提交问题」。

## 常见问题

- **看不到平面**：确认 AR Plane Manager 存在；光照充足、纹理明显的平面更容易识别；慢速移动手机。
- **点击没反应**：确认 AR Raycast Manager 存在；确认「Active Input Handling」是 Both。
- **提交失败**：确认手机能访问电脑 IP（可先用手机浏览器打开 `http://<IP>:8080/health` 测试）；确认防火墙放行 8080 端口。
- **UI 文字为粉色/乱码**：内置字体获取失败，确认脚本里 `UnityCompat.GetDefaultFont()` 使用的字体名与你的 Unity 版本匹配（脚本已做 Arial / LegacyRuntime 双兜底）。
