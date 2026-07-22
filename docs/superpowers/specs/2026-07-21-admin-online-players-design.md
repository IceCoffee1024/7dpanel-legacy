---
state: Current
document_role: Change Record
last_updated: "2026-07-21"
---

# Admin 登录与在线玩家页面设计规格

> 本规格描述一个已批准但尚未实现的前端纵向切片，不是当前实现证据。产品能力以 [PRD](../../PRD.md) 为准，交互规则以[界面设计](../../design.md)为准，当前实现以[系统架构](../../architecture.md)为准，目标前端边界以 [Admin 前端目标蓝图](../../architecture/admin-frontend-target-blueprint.md)为准。

## 目标

让当前唯一操作者 `Owner` 可以在 Admin 中通过独立登录页建立仅存于浏览器内存的 Bearer 会话，并在受保护的 `/players` 页面查看真实在线玩家快照。该切片完成 `CAP-02` 的首个浏览器只读玩家链路和 `CAP-05` 的首个 Admin 登录消费链路，同时落实 `NFR-02` 的离线、过期和失败状态诚实性，以及 `NFR-04` 的 Header-only Token 边界。

## 范围

本切片包含：

- 独立 `/login` 路由、Owner password grant 登录和安全站内返回目标。
- `/login` 之外全部当前 Admin 路由的统一认证守卫。
- Pinia Setup Store 管理跨路由的内存会话、登录动作、到期和登出。
- 薄的同源 API Client，统一 Bearer Header、取消、超时和 Problem Details 映射。
- `/players` 路由、应用壳导航和在线玩家只读页面。
- 页面级在线玩家查询状态，首次加载、每 10 秒自动刷新和手动刷新。
- 桌面表格、窄屏行详情，以及 Loading、Empty、Fresh、Stale、Offline、Forbidden 和 Session expired 状态。
- Vitest、Vue Test Utils 与 DOM 测试环境的首个 Admin 自动化门禁。
- 真实 OWIN 中的 Owner 登录、单玩家页面、响应式和关服后状态浏览器 smoke。

本切片不包含：

- `Admin`、`Viewer` 用户创建、角色管理或当前用户资料接口。
- Token 持久化、refresh token、Cookie、CSRF Token、跨标签页会话同步或自动恢复。
- 玩家搜索、排序控件、分页、详情页、位置、IP、封禁信息、战斗统计或离线历史。
- 踢出、禁言、封禁、传送、批量操作、危险确认或审计关联。
- SSE、控制台页面、国际化框架、全局查询缓存库或 Pinia Colada。
- 后端接口、认证策略、权限角色或玩家响应字段变更。

## 已有后端契约

### 登录

```http
POST /api/v1/auth/token
Content-Type: application/x-www-form-urlencoded

grant_type=password&username=...&password=...
```

成功响应必须至少包含非空 `access_token`、大小写不敏感的 `token_type=bearer` 和正数 `expires_in`。客户端以收到响应时的可注入当前时间计算到期边界；无效 JSON、缺少字段或不支持的 token type 均作为无效响应处理，不建立会话。

OAuth 协议失败保持现有 `error`/`error_description` JSON。客户端只根据 HTTP 状态和稳定 OAuth `error` 选择用户文案，不直接展示 `error_description`，也不区分用户名不存在、密码错误或用户不可用。

### 在线玩家

```http
GET /api/v1/players/online
Authorization: Bearer <opaque-token>
```

```json
{
  "capturedAtUtc": "2026-07-21T00:00:00.0000000+00:00",
  "players": [
    {
      "entityId": 1,
      "name": "Player",
      "platformIdentity": {
        "combinedId": "platform:id",
        "platform": "Platform"
      },
      "crossplatformIdentity": null,
      "ping": 42,
      "level": 10,
      "health": 100
    }
  ]
}
```

客户端必须在 Feature 边界验证根对象、UTC 捕获时间、数组、非空名称、平台身份和整数数值。`crossplatformIdentity` 允许为 `null`。无效响应不能覆盖最后成功快照。

稳定响应语义为：

- `200`：返回在线玩家快照；空服务器是空数组。
- `401`：会话无效或过期，清除内存会话并转到登录页。
- `403`：当前身份无权访问，保留会话并显示 Forbidden。
- `503 game_not_ready`：游戏尚未就绪。
- `503 online_player_query_busy`：已有查询占用 single-flight。
- `503 game_thread_timeout`：查询未在截止时间内进入游戏主线程。
- `503 online_player_snapshot_unavailable`：游戏快照基础设施不可用。

## 组件与状态所有权

```text
main.ts
  -> createPinia
  -> createRouter(pinia)
  -> route guard -> useAuthStore(pinia)
  -> App.vue
       -> public LoginPage
       -> authenticated AppShell
            -> OverviewPage
            -> PlayersPage
                 -> OnlinePlayersView
                      -> useOnlinePlayers
                           -> players API
```

### Pinia 认证 Store

`features/auth` 拥有唯一的 Setup Store。Store 只保存：

- opaque access token；
- 计算后的到期时刻；
- `idle | submitting | authenticated` 会话状态；
- 不泄露账号存在性的登录错误分类。

用户名和密码只作为 `login(username, password)` action 的临时参数。密码不得进入 Store state、组件外共享 ref、URL、浏览器持久存储、日志、异常文本或测试快照。登录 action 完成或失败后，登录表单负责清空密码；Store 不保留凭据副本。

Store 提供 `isAuthenticated`、`authorizationHeader`、`login`、`logout` 和 `expireSession`。`isAuthenticated` 必须同时检查 Token 非空和当前时间早于到期边界。到期后第一次守卫或受保护请求会同步清除会话，不发送已知过期 Token。

Pinia 只管理客户端自有会话，不保存玩家快照、健康状态或查询缓存。不安装持久化插件。开发 HMR 不能把 Token 写到磁盘；测试为每个用例创建独立 Pinia 实例。

### 共享 API Client

`shared/api` 提供薄的同源 Fetch 边界：

- base path 固定为相对同源 `/api/v1`；
- 受保护请求从认证 Store 的当前值生成 `Authorization: Bearer` Header；
- 不接受调用方通过 URL 或 QueryString 传 Token；
- 支持 `AbortSignal` 和有限请求超时；
- 解析 JSON 与 `application/problem+json`，保留稳定错误码、HTTP 状态和可选 `traceId`；
- 不把任意服务端异常消息直接暴露给页面。

登录端点可复用同一传输原语，但不添加 Authorization Header。玩家 Feature 拥有自己的 DTO 校验和页面模型映射；共享层不认识玩家业务字段。

### 在线玩家查询

`features/players` 的 `useOnlinePlayers` 拥有页面级查询状态：

- `snapshot`：最后成功且已验证的页面模型；
- `state`：`loading | fresh | stale | offline | forbidden`；
- 当前错误分类和最近一次尝试时间；
- 当前请求的 `AbortController`；
- 10 秒刷新计时器和请求序号。

查询状态不进入 Pinia。首次挂载立即请求；成功后以服务端 `capturedAtUtc` 替换快照。自动刷新和手动刷新共用同一个客户端 single-flight：请求进行中时不再发第二个请求，手动刷新只复用当前请求状态。离开页面或组件卸载时取消请求并清理计时器。

页面不可见时暂停新轮询；恢复可见后立即刷新一次，再恢复 10 秒周期。取消结果不得改写状态。只有当前请求的最新响应可以提交状态，避免旧响应覆盖新快照。

## 路由与会话流

### 路由结构

- `/login`：公开页面，不渲染 Dashboard App Shell。
- `/`：受保护的现有 Overview。
- `/players`：受保护的在线玩家页。
- 未知路由仍由后续独立 404 设计处理，本切片不扩大路由范围。

路由元数据标记公开或 `requiresAuth`。全局守卫在函数内使用显式传入的同一 Pinia 实例读取认证 Store，避免应用安装顺序和测试隔离问题。

### 未认证访问

```text
visit protected route
  -> no valid in-memory token
  -> /login?redirect=<encoded internal fullPath>
  -> password grant succeeds
  -> replace validated internal target
```

`redirect` 只接受以单个 `/` 开始、不能以 `//` 开始且能由当前 Router 解析的站内路径。其他值回退 `/players`，防止开放重定向。登录成功使用 replace，避免返回键重新进入已完成的登录提交。

已认证用户访问 `/login` 时直接替换为安全 redirect 或 `/players`。登出清除会话并替换到 `/login`。401 清除会话并保留当前内部目标；403 不清除 Token，也不自动重试。

## 页面与交互设计

### 登录页

登录页沿用当前 Nuxt UI 和 Template 的中性工作界面，不使用 Dashboard 侧栏。首屏包含 7DPanel 品牌、用户名、密码和“登录”主按钮：

- 表单使用明确 label、自动完成语义和可见焦点；
- 提交中按钮尺寸固定，以进度图标替换前导图标；
- 用户名可以保留在当前表单中，失败后密码清空；
- 错误统一为“无法登录，请检查凭据或服务状态”，限流时另提示稍后重试；
- 不显示默认凭据、Token 生命周期、后端地址或技术异常；
- 不加载第三方字体、脚本或图片。

### App Shell

认证后的 App Shell 保持 `058da3c` Template 派生结构：固定/可折叠侧栏、Dashboard Search 和 `RouterView`。导航顺序为“概览”“玩家”；玩家项使用 Lucide users 图标。搜索组同步包含两个真实路由，不增加未实现导航。

侧栏 footer 保留外观菜单，并增加紧凑账号菜单或登出命令。当前后端没有“当前用户”接口，因此只显示稳定角色 `Owner`，不把登录时用户名当作服务端权威资料。

### 在线玩家页

页面使用 `UDashboardPanel`、`UDashboardNavbar` 和轻量 `UDashboardToolbar`，不把整个列表包进装饰性卡片：

- Navbar 标题为“在线玩家”，leading 保留侧栏折叠按钮；
- right 放置固定尺寸的刷新图标按钮，旋转只表达当前请求；
- Toolbar 展示在线人数、快照捕获时间和带图标/文本的新鲜度；
- 桌面主体使用 Nuxt UI Table，列为玩家、平台、跨平台身份、等级、生命值和延迟；
- 玩家列主文本是名称，次级等宽文本是 entity ID；
- 身份保留原值并提供显式复制按钮，不依赖截断文本完成识别；
- 不显示操作列、选择框、批量操作、位置或其他未批准字段。

桌面表格列宽稳定，数值列使用等宽数字并右对齐。窄屏不横向压缩整张表，而切换为有分隔线的行详情：名称和连接摘要为首行，身份、等级、生命与延迟使用明确 label/value 网格。320 CSS 像素下仍可读取最长字段并触发复制操作。

## 状态与错误映射

- `Loading`：没有旧快照时显示与最终行高一致的骨架。
- `Empty`：显示“当前没有在线玩家”和最近捕获时间，不显示错误或虚假操作。
- `Fresh`：展示快照和绿色状态文本；颜色不是唯一提示。
- `Stale`：保留最后快照、捕获时间和滚动位置，以琥珀状态明确标记数据已过期。
- `Offline`：没有可显示快照时展示连接失败状态和手动刷新；有旧快照时表现为 Stale。
- `Forbidden`：不显示玩家数据，说明当前身份无权访问并提供返回概览入口。
- `Session expired`：清除 Token，跳转登录并保留安全返回目标，不自动重放请求。

`online_player_query_busy` 保留旧快照并等待下一周期；若没有旧快照，显示短暂加载后允许手动重试。`game_not_ready` 明确说明游戏仍在加载。`game_thread_timeout` 和 `online_player_snapshot_unavailable` 进入 Stale/Offline，并提供手动刷新。页面不显示原始 Problem Details `detail`。

## 组件边界

```text
pages/login.vue
  -> features/auth/ui/LoginForm.vue

pages/players.vue
  -> features/players/ui/OnlinePlayersView.vue
       -> OnlinePlayersToolbar.vue
       -> OnlinePlayersTable.vue
       -> OnlinePlayersList.vue
       -> OnlinePlayersState.vue
```

- 路由页面只组合 Feature，不直接 Fetch。
- `LoginForm` 只管理表单输入并调用 Store action。
- `OnlinePlayersView` 连接查询 composable 与展示组件。
- Toolbar 只接收人数、时间、新鲜度和刷新事件。
- Table 与 List 接收同一只读页面模型；通过 CSS breakpoint 决定展示，不各自请求数据。
- State 负责 loading、empty、offline 和 forbidden，不拥有查询副作用。

共享复制按钮只有出现第二个真实消费者时才提升到 `shared/ui`；本切片默认保留在玩家 Feature 内。

## 自动化与验证

本切片新增 `vitest`、`@vue/test-utils` 和 `happy-dom`，使用一次性 `pnpm test` 作为 CI 门禁。测试复用 Vite 的 Vue SFC 与路径解析配置，不依赖快照测试证明业务行为。

### 单元测试

- OAuth token 响应验证：合法 Bearer、缺字段、错误类型、无效到期时间和无效 JSON。
- 玩家 DTO 验证：空数组、可空跨平台身份、无效 UTC、缺字段和错误数值类型。
- 认证 Store：登录成功、统一失败、到期、登出、401 清理、密码不进入 state、每测试独立实例。
- 安全 redirect：合法内部路径、`//host`、绝对 URL、不可解析路由和空值。
- API Client：Bearer Header、无 URL Token、取消、超时、Problem Details 与非 JSON 错误。
- 玩家查询：首次加载、10 秒轮询、页面隐藏暂停、恢复刷新、single-flight、旧响应抑制、卸载取消和错误状态映射。

### 组件与路由测试

- 未认证访问 `/`、`/players` 重定向登录，登录成功返回原目标。
- 已认证访问登录页跳转 `/players`，登出返回登录。
- LoginForm 提交状态、统一错误和密码清理。
- 玩家 Loading、Empty、Fresh、Stale、Offline、Forbidden 与 Session expired 可见行为。
- 桌面表格字段、窄屏列表语义、刷新按钮和复制入口。
- 测试使用真实 Pinia action 验证会话行为；只有纯展示组件才按需 stub Store。

### 聚合与浏览器验证

- `pnpm lint`、`pnpm typecheck`、`pnpm test` 和 `pnpm build` 全部通过。
- 发布构建不包含密码、有效 Token、开发代理目标或第三方运行时资源。
- 在真实 OWIN 静态托管中验证深链接 `/login`、`/players` 刷新不会落入 API 或 404。
- 使用测试 Owner 登录，确认浏览器请求只在 Authorization Header 携带 Bearer，URL、Cookie、Storage 和控制台均无 Token。
- 一个真实玩家在线时验证页面人数、批准字段、采样时间和手动/自动刷新。
- 在桌面和 `390x844` 视口验证无重叠、无关键横向滚动、键盘焦点和窄屏行详情。
- 正常关服后保留旧快照并标记过期；重新启动后手动或自动刷新恢复 Fresh。

## 文档影响与完成条件

实现完成并验证后：

- 更新 `docs/design.md` 的 Current 页面、导航和状态事实。
- 更新 `docs/architecture.md` 的 Admin 路由、Pinia 会话、API Client、玩家 Feature 和依赖矩阵。
- 从 Admin Target 蓝图中把已经落地的边界提升到 Current，并保留尚未实现的目标差异。
- 更新 `docs/test.md` 的前端自动化数量、门禁和真实浏览器证据。
- 更新 `frontend/apps/admin/README.md` 的精确 `pnpm test` 命令。
- 仅在代码、自动化和真实浏览器证据支持后，才宣称 Admin 登录与在线玩家页面已实现。

## 已批准决策

- 使用独立 `/login`，不在玩家页内嵌登录，也不依赖浏览器 Basic 弹窗。
- `/login` 之外的当前 Admin 路由统一要求内存会话；健康 API 本身继续匿名。
- 采用 Pinia Setup Store 管理跨路由认证会话；不使用持久化插件。
- 玩家快照保持 Feature 局部查询状态，不进入 Pinia。
- 自动刷新周期为 10 秒，并提供手动刷新；刷新期间保留旧快照及原采样时间。
- 页面沿用 `058da3c` Template 派生的 Dashboard 视觉语言，由实现按本规格自由完成响应式布局。
- 建立 Vitest、Vue Test Utils 和真实浏览器验证，不用类型检查或静态构建替代行为测试。