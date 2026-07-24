---
state: Draft
last_updated: "2026-07-24"
document_role: Target
---

# 7DPanel Admin 前端目标架构蓝图

> 本文描述 `frontend/apps/admin/` 的批准 Target 设计，不是当前实现证据。当前系统事实以
> [系统架构](../architecture.md)为准，产品范围以 [PRD](../PRD.md) 为准，页面、流程和视觉规则以
> [产品设计](../design.md)为准，验证要求以[测试策略](../test.md)为准。

## 用途与提升条件

本蓝图为 Admin 管理面板定义应用边界、运行链路、状态所有权、API 与安全约束、目标目录、
依赖候选和发布责任。已导入的模板基线和当前代码不能替代目标契约，也不能作为未验证运行链路的实现证据。
它覆盖 `CAP-01` 至 `CAP-05`、`NFR-01` 至 `NFR-04` 的前端实现边界，
但不重新定义产品行为或界面设计。

只有相应目录、构建、测试和真实 OWIN 部署证据存在后，才把稳定结论提升到[系统架构](../architecture.md)。
框架候选、目标目录和未验证链路不得用于声称当前前端已经实现。当所有持久结论均已提升、本文不再包含独立的
未来设计时，应删除或缩减本蓝图。

## 应用与部署边界

| 边界 | 目标职责 | 部署单元 | 本蓝图是否详细定义 |
|---|---|---|---|
| `frontend/apps/admin/` | 服主和管理员使用的自托管管理面板 | 编译为静态资源并随 Mod 发布 | 是 |
| `frontend/apps/marketing/` | 官网、定价、下载和公开内容 | 独立站点，不进入 Mod 发布物 | 否 |
| `frontend/apps/player/` | 未来玩家身份、积分与商城 | 产品立项后独立应用和认证边界 | 否，当前不创建 |
| `frontend/packages/` | 两个以上前端应用的真实共享资产 | 按消费者和构建需求决定 | 否，出现复用证据后创建 |

Admin、Marketing 和未来 Player Portal 可以采用不同框架、发布方式和设计系统。不得为了未来可能共享而提前创建
`ui`、`api-client` 或通用业务包。未来玩家身份不得复用 Admin 的 `Viewer` 角色、路由或会话边界。

### Admin 目标运行拓扑

```text
Browser
  -> same-origin Admin SPA
       -> REST /api/v1
       -> SSE  /api/v1/events/stream
  -> OWIN static-file hosting and SPA fallback
  -> Web API / Application
```

- Admin 是客户端 SPA；生产服务器不需要 Node.js、SSR 进程或前端开发服务器。
- 浏览器只通过同源 HTTP/HTTPS 接触 7DPanel，不直接访问 Telnet、SQLite、存档、`config.json` 或 7DTD 对象。
- OWIN 负责静态文件、SPA fallback、REST 和 SSE。fallback 不得吞掉 `/api/*`、静态资源缺失或真实 HTTP 错误。
- Mod 发布物包含 Admin 生产资源；Marketing、测试产物、源码、开发配置和本机路径不得进入 Mod 目录。
- Admin 不依赖产品方云服务、外部 CDN、远程字体或第三方脚本才能完成核心能力，落实 `NFR-01`。

## 前端职责与依赖方向

目标代码采用应用组合、路由页面、业务 Feature 和共享技术原语四类边界：

```text
app composition
      |
      v
route pages
      |
      v
feature public APIs
      |
      v
shared technical primitives

feature A  -X-> feature B internals
shared     -X-> feature business code
```

- `app/` 只负责启动、路由、全局 provider、错误边界和管理面板外壳。
- `pages/` 负责路由级组合，不拥有可复用业务规则或直接拼接底层 HTTP。
- `features/` 按身份、概览、玩家、日志、公告、备份、审计和设置组织；每个 Feature 拥有自己的 API 映射、
  状态转换、组件和测试。
- Feature 之间只通过公开类型、路由参数或稳定的共享能力协作，不导入彼此内部目录。
- `shared/` 只容纳无业务所有者的 API 传输、UI 原语、格式化、时间和测试设施。一个类型只有在至少两个真实
  Feature 使用、没有更明确所有者且不改变产品语义时才能进入。
- 不建立全局 `models/`、`services/`、`utils/` 或 `components/` 杂物目录。
- 后端响应 DTO 不直接成为可编辑表单模型；Feature 在边界处映射请求、响应和页面状态。

## 状态所有权

| 状态类别 | 权威所有者 | 前端责任 | 不允许的做法 |
|---|---|---|---|
| 服务器、玩家、备份、任务和审计 | 后端 | 查询、缓存、显示采样时间、按契约失效 | 把缓存值改写为新的权威事实 |
| 搜索、筛选、排序、分页和时间范围 | URL | 可分享、可返回、刷新后恢复 | 只藏在组件内导致导航丢失 |
| 当前操作者、角色和权限 | 后端会话 | 从服务端确认的身份元数据恢复浏览器会话、控制导航和交互、处理过期 | 仅依据本地角色决定授权 |
| 表单草稿、对话框和展开状态 | 所属 Feature | 在安全范围内保留和清理 | 将密码、初始化凭证或 Bearer Token 持久化 |
| 当前界面语言 | `app/i18n` | 解析浏览器语言、不支持时回退 `en`、在 `zh-CN`/`en` 间切换并持久化非敏感偏好 | 由 Feature 各自保存语言或把语言偏好写入服务端业务状态 |
| 长任务显示状态 | 后端作业与审计 | 跨页面、刷新和重连后恢复 | 只依赖内存 Toast 或单次 HTTP 响应 |
| 日志流游标和连接状态 | 日志 Feature | 维护最后游标、暂停、补取和缺口 | 把断线期间数据假装为连续实时流 |

服务端状态缓存与客户端交互状态必须分离。不得为了方便把全部 API 数据复制进一个全局 Store。全局状态只用于确有
跨路由生命周期的客户端协调；服务器数据仍由查询层根据键、时间戳和失效规则管理。

### 在线玩家详情状态

Players Feature 在现有查询快照之外只增加一个页面局部详情选择，不把玩家或抽屉状态放入 Pinia：

```text
OnlinePlayersView
  -> useOnlinePlayers: last validated server snapshot
  -> selectedPlayerKey: entityId + platformIdentity.combinedId
  -> selectedPlayer: update from latest snapshot until unavailable locks
  -> OnlinePlayersTable / OnlinePlayersList: emit viewDetails(player)
  -> OnlinePlayerDetailsSlideover: read-only details and kickPlayer(player)
  -> KickPlayerDialog: independent fixed action target
```

- `OnlinePlayersView` 继续作为组合面，拥有抽屉开关、稳定选择键和最后详情 observation；Table、List 与 Slideover 只接收只读 props 并上抛类型化事件。
- 尚未锁存 unavailable 时，成功刷新用 entity ID 与原生 `combinedId` 同时匹配选中玩家；匹配时替换详情 observation，未匹配时保留最后值并锁存 unavailable。该状态直到关闭抽屉才清除，后续同身份重现也不自动恢复，不从相同或复用 entity ID 的新会话偷换详情目标。
- 关闭抽屉时清除详情选择和最后 observation。详情选择不复用现有踢出 `selectedPlayer`；只有用户从当前详情发起踢出时，才把当时完整玩家值复制为独立危险操作目标。
- `OnlinePlayerDetailsSlideover` 使用 Nuxt UI `USlideover` 表达仪表盘次要详情，窄屏占满可用宽度；踢出仍由现有阻断式确认对话框负责。详情踢出能力由现有授权、`state === 'fresh'` 和未锁存 unavailable 共同决定；Stale、Offline、Forbidden、Session expired 或游戏未就绪时不从旧详情发起新动作。抽屉分区使用普通布局和分隔，不创建嵌套卡片。
- Players Feature 自己拥有设备、时长、整数坐标/距离和空值格式化。坐标与距离四舍五入后按当前语言显示整数，分钟值转换为天/小时/分钟；所有传输空值统一显示“未知”。纯格式化函数保持无状态，只有出现第二个真实 Feature 消费者后才提升到 `shared`。
- API 边界严格验证 25 字段、位置对象、设备枚举、有限数值和可空字符串。无效新响应不能覆盖最后成功快照或当前详情；前端不根据本地角色删除响应字段，也不承担敏感字段授权。

### 统一结果状态

所有读取和写入界面必须能表达：

- `Loading`：首次加载，使用稳定占位尺寸；
- `Fresh`：具有有效采样或完成时间的权威结果；
- `Stale`：保留最后值并标注过期时间；
- `Offline`：游戏能力不可用，但本地历史和审计仍可读取；
- `Queued` / `Running`：动作已接受但未完成；
- `Succeeded` / `Failed`：存在权威最终结果；
- `Unknown`：动作可能已开始但结果未确认，不得直接重放；
- `Draining`：服务端正在关闭，拒绝新写入并保留收尾状态；
- `Forbidden`：当前操作者无权访问，不泄露目标细节；
- `SessionExpired`：停止新提交并重新认证，不自动重放写操作。

这些状态的文案和视觉表现由[产品设计](../design.md)负责；本蓝图负责确保状态模型不会把 HTTP 成功、缓存或离线
误报为游戏动作成功，落实 `NFR-02`。

## API、认证与实时边界

### API Client

- 所有 HTTP 调用经过一个薄的同源 API Client，统一处理 base path、`Authorization` Header、取消、超时、关联标识和错误映射。
- Feature 定义自己的请求、响应和页面模型；Controllers 的内部异常、数据库字段和文件路径不得泄漏到浏览器。
- 后端提供稳定、可重复获取的 OpenAPI 契约后，优先评估使用 `@hey-api/openapi-ts` 从本地契约生成传输类型和客户端；生成代码必须隔离，不能成为 Feature 组织方式，也不能要求连接 Hey API 云服务。
- 错误结果至少保留稳定错误码、Correlation ID、适用时的 Audit ID 和可否安全重试；前端根据稳定错误码生成当前语言的用户消息，不翻译或直接展示任意服务端异常文本。
- 查询使用 `AbortController` 或框架等效能力取消已经失去消费者的请求。

### Header 认证与 Token 生命周期

- 产品不采用 Cookie 认证。目标 Admin 只通过 `Authorization` Header 发送 Bearer Access Token，不接受或发送 Basic 身份，不把 Token 放入 URL，也不设计 CSRF Token；如果未来引入浏览器自动附带的认证机制，必须重新评估 CSRF 边界。
- 网站 Access Token 默认有效期 8 小时。Auth Feature 只保存严格版本化的 `{ version, token, expiresAt, username, role }` 记录：未选择“保持登录”时只在 `sessionStorage` 恢复当前标签页，选择后才写入 `localStorage` 以恢复同一浏览器会话。记录不得进入 URL、Cookie、日志或错误报告；密码、完整 API Key 和 Authorization Header 前缀不进入记录。页面到期、登出、401、损坏记录或同源删除事件后重新认证；产品不提供 refresh token 或 silent refresh。
- 用户创建的 API Key 只在创建成功对话框中短暂存在，关闭后必须清除，不能进入 Auth Store 或替代网站 Access Token。API Key 列表只保存元数据。
- 配置引导凭据只通过登录表单进入请求，不写入 URL、浏览器持久存储、日志或遥测；页面不得加载第三方资源，
  避免认证数据通过 Referer 或外部脚本泄漏。
- 登录、认证配置异常和会话过期页面不得根据错误信息泄露账号是否存在。
- 路由守卫和隐藏按钮只改善体验；服务端授权拒绝始终映射为明确的 `Forbidden` 页面或局部状态。

### SSE 与补取

- 当前后端只接受 `Authorization` Header 中的 Access Token 或 API Key Bearer，拒绝 Basic、URL Token 和 Cookie 凭据。Admin SSE 必须使用能设置 Header、读取 401/403/429/503、主动取消并限制重连的 Fetch 型客户端，不能用原生 `EventSource` 或 QueryString Token 绕过安全边界。
- Admin 自身使用 Auth Store 当前 Access Token 建立 SSE；API Key 是外部集成凭据，不进入浏览器会话。原生 `EventSource` 不能设置 `Authorization` Header，不进入目标方案。
- 每次连接先消费不推进游标的 `welcome`，再处理 replay/live；当前命名事件为 `console-log`、`game-ready` 和 `server-stopping`，`gap` 表示窗口或慢客户端缺口。
- 每条事件包含可排序游标、事件类型和服务器时间；前端按游标去重，不使用到达时间伪造顺序。
- 断线后保留当前日志和最后接收时间，使用最后游标重连或调用 REST 补取。
- 服务端无法补齐保留窗口外记录时，页面插入明确缺口，不把两段日志拼成连续事实。
- SSE 事件只更新对应查询或触发精确失效；不得用广播事件直接任意修改所有 Feature 状态。
- 重连采用有上限的退避并响应 `Offline`、`Draining` 和会话过期，不产生无界请求循环。

## 关键运行链路

### 启动、初始化与登录

```text
load static shell
  -> fetch bootstrap/session state
  -> bootstrap Owner unavailable: configuration-required route
  -> existing session: authorized app shell
  -> no session: login route
  -> fetch current server and task summaries
```

应用启动只阻塞建立身份边界所需的最小请求。其他页面数据按路由加载；一个非关键查询失败不得让整个管理面板白屏。
当前过渡阶段不提供浏览器内首个 Owner 初始化。服务端成功同步配置引导 `Owner` 后，用户通过登录页建立会话；同步
失败时配置提示页不得提供匿名降级或远程用户创建。会话过期时保留可安全恢复的 URL 与草稿，但清除密码、
Bearer Token 和危险确认状态。

### 查询与页面导航

```text
route params and URL filters
  -> feature query
  -> API response with sampledAt / cursor
  -> map transport DTO to view state
  -> cache by stable key
  -> SSE event or mutation result invalidates exact key
```

返回上级页面时恢复筛选、排序、分页和滚动上下文。刷新期间保留旧值及原时间戳；只有新响应到达后才更新新鲜度。

### 管理动作

```text
select target and typed action
  -> permission-aware confirmation
  -> submit once with correlation context
  -> Queued / Running / Succeeded / Failed / Unknown
  -> refresh target state and linked audit
```

确认界面固定显示目标、动作和后果。提交后不靠禁用按钮永久锁死页面；状态由后端结果、作业或审计恢复。
`Unknown` 状态先查询审计或目标状态，禁止自动重放踢出、封禁、恢复等副作用。

### 日志、任务与跨重启恢复

- 日志 Feature 将实时窗口与历史搜索分开，暂停跟随不会停止连接或丢弃游标。
- 全局任务入口只显示后端可恢复的作业摘要，详情由所属 Feature 拥有。
- 发起恢复后，页面依次展示校验、`PendingRestart`、正在关服和等待重启；连接中断不是成功。
- 浏览器关闭或刷新后，通过持久作业标识恢复进度。服务端重新可用后读取最终的
  `Succeeded`、`Failed` 或 `RollbackFailed`，并链接备份与审计详情。

## 目标目录

目录只在对应职责进入首个纵向切片时创建，不生成空 Feature 或预留页面。

```text
frontend/
|-- apps/
|   |-- admin/
|   |   |-- package.json                        # Admin 依赖、脚本和精确 packageManager
|   |   |-- pnpm-workspace.yaml                 # Admin 自有 pnpm 配置
|   |   |-- pnpm-lock.yaml                      # Admin 自有依赖锁
|   |   |-- framework.config.*
|   |   |-- src/
|   |   |   |-- main.ts                         # 浏览器入口
|   |   |   |-- app/
|   |   |   |   |-- AppShell.*                  # 认证后的稳定外壳
|   |   |   |   |-- bootstrap.*                 # 最小启动和会话恢复
|   |   |   |   |-- router.*                    # 路由、权限元数据和 fallback
|   |   |   |   |-- providers.*                 # 已选框架插件和错误边界
|   |   |   |   |-- i18n/                        # 语言解析、消息目录、回退和格式化边界
|   |   |   |   `-- styles/                     # 应用级 tokens 和基础样式
|   |   |   |-- pages/                           # 路由级 Feature 组合
|   |   |   |   |-- ConfigurationRequiredPage.*
|   |   |   |   |-- LoginPage.*
|   |   |   |   |-- DashboardPage.*
|   |   |   |   |-- PlayersPage.*
|   |   |   |   |-- ConsoleLogsPage.*
|   |   |   |   |-- AnnouncementsPage.*
|   |   |   |   |-- BackupsPage.*
|   |   |   |   |-- AuditPage.*
|   |   |   |   |-- ApiKeysPage.*
|   |   |   |   `-- SettingsPage.*
|   |   |   |-- features/
|   |   |   |   |-- auth/                       # 初始化、登录、会话和权限
|   |   |   |   |-- server-status/              # 新鲜度和连接状态
|   |   |   |   |-- players/                    # 玩家查询与类型化动作
|   |   |   |   |-- console-logs/               # SSE、补取和筛选
|   |   |   |   |-- announcements/              # 即时公告与自动化
|   |   |   |   |-- backups/                    # 备份、作业和恢复
|   |   |   |   |-- audit/                      # 审计查询与关联
|   |   |   |   |-- api-keys/                   # 一次性创建、元数据列表和撤销
|   |   |   |   `-- settings/                   # 用户、角色和安全提示
|   |   |   `-- shared/
|   |   |       |-- api/                         # 同源传输和统一错误
|   |   |       |-- ui/                          # 无业务语义的 UI 原语
|   |   |       |-- time/                        # 时间格式和新鲜度计算
|   |   |       `-- testing/                     # 跨 Feature 测试设施
|   |   |-- tests/
|   |   |   |-- unit/
|   |   |   |-- component/
|   |   |   `-- e2e/
|   |   `-- public/                              # 仅真正无需打包处理的静态文件
|   |
|   `-- marketing/                               # 独立应用，架构另行决定
|
`-- packages/                                    # 两个真实应用需要共享后再创建
```

Feature 内部可按需要创建 `api/`、`model/`、`ui/` 和同目录测试，但不要求每个 Feature 机械复制同一模板。
页面只能通过 Feature 公共入口和 `shared` 组合，不直接依赖另一个 Feature 的内部状态。

## 依赖策略与候选

本表记录候选与采用门槛，不是安装命令。精确版本以 `package.json` 和锁文件为权威来源；初始化或引入前必须
重新检查官方文档、维护状态、许可证、安全公告、浏览器支持、包体积和传递依赖。

| 能力 | 当前方向或候选 | 状态 | 采用理由或限制 | 决定前必须验证 |
|---|---|---|---|---|
| 语言 | TypeScript strict mode | 已批准 | API、权限、状态机和表格数据需要稳定类型边界 | 与所选框架、测试和生成代码的配置一致性 |
| 编译期类型工具 | 优先使用 TypeScript 内置工具类型；不足时评估 `type-fest` | 条件候选 | 仅在具体边界需要高级类型且局部定义容易出错或重复时，按实际使用类型直接导入；不为类型技巧本身增加依赖 | TypeScript 5.9+、ESM 和 strict 兼容性，具体导入类型、可读性、类型检查耗时及升级影响 |
| 包管理与 workspace | pnpm；各应用独立管理 | 已批准 | 所有前端应用统一使用 pnpm，但分别拥有依赖图、锁文件、Node.js 兼容范围和发布周期 | 各应用的 pnpm 精确版本、`package.json` 的 Node.js 范围和 CI 安装；出现真实共享后再评估根 workspace |
| Admin SPA | Vue 3 Composition API + `<script setup lang="ts">` | Admin 目标采用 | 适合长会话、高交互 SPA；当前应用壳已验证 TypeScript SFC 构建 | 生产包体积、浏览器基线和真实业务切片 |
| 构建 | Vite 8、Rolldown/Oxc、`@vitejs/plugin-vue` | Admin 目标采用 | 生成静态输出、支持 base path 和带哈希资源，不要求生产 Node.js；Vite 8 使用 Rolldown/Oxc 统一生产构建与转换，开发和 CI 推荐使用 Node.js `24+` | OWIN 部署路径、SPA fallback、manifest、缓存策略、Rolldown 插件兼容性、构建产物体积和 Node.js 兼容范围 |
| Node.js 配置类型 | `@types/node@24`（`devDependency`） | 已采用；当前边界已验证 | `vite.config.ts` 直接导入 `node:process` 并使用 `process.cwd()`；仅加入 Node 侧 `tsconfig.node.json`，其范围只包含 Vite 配置，不向浏览器应用类型环境泄漏 | `package.json` 保留 `^20.19.0 || ^22.13.0 || >=24.0.0` 的工具链兼容范围；CI 固定 Node.js `24+`，并继续检查 TypeScript、`tsconfig.node.json`、直接使用证据和跨平台配置 |
| 路由 | Vue Router 文件路由 | Admin 目标采用 | 使用官方 Vite 插件生成页面路由，并让后续筛选和分页进入 URL | 权限元数据、URL 状态、恢复导航和 404 行为 |
| 文件布局路由 | `vite-plugin-vue-layouts` | 当前不采用 | 当前只有一个稳定 App Shell，直接包裹 `RouterView` 更简单，也避免引入与当前 Vite、Vue Router peer 范围不兼容的插件 | 出现多个稳定布局且手工布局映射产生实际维护成本 |
| 路由不确定进度 | 默认不引入；出现可感知的路由懒加载等待后评估 `@bprogress/core` 或 `@bprogress/vue`；`nprogress` 当前不采用 | 条件候选 | 顶部进度条只表达导航或代码分块仍在加载，不能代表页面数据、游戏动作、备份或恢复的真实进度；BProgress 是现代 TypeScript 实现，优先于长期停留在旧版本的 NProgress | 实测导航延迟、并发和取消导航、失败清理、防闪烁、主题/CSS、层级、键盘与屏幕阅读器提示、减少动态效果、包体积和 Vue Router 集成 |
| UI 组件 | `@nuxt/ui` standalone Vue 模式、Tailwind CSS | Admin 目标采用 | 当前官方文档支持通过 Vite 插件用于独立 Vue，应用壳已验证 Dashboard 组件 | 高密度运维表格、主题约束、Tailwind 成本、包体积和键盘行为 |
| 图标与离线资源 | Nuxt UI `UIcon`、本地 `@iconify-json/lucide`（`devDependency`）和 `@nuxt/ui/vite` 的 `icon.clientBundle` | Admin 目标采用；当前基线已验证 | 核心界面图标必须随静态产物离线可用；当前静态 `i-lucide-*` 用法已通过生产构建和 Chrome DevTools MCP 验证，最终 DOM 使用内联 Lucide SVG，页面运行时只请求同源静态资源且不请求 Iconify API。当前使用 `scan: true` 从已安装集合按源码用法打包，动态名称无法可靠扫描时改用显式 `icons` 清单。Nuxt UI Vite 集成已提供组件与自动导入能力，不直接安装 `unplugin-auto-import` 或 `unplugin-vue-components`。`unplugin-icons` 当前不采用；只有出现必须通过 `~icons/*` 将自定义 SVG 或图标作为 Vue 组件直接导入，且 `UIcon` 无法清晰满足的真实需求时才重新评估 | 动态图标名称的显式清单、新增集合的直接依赖、浏览器包体积和可重复的离线回归门禁 |
| 数据表格 | 基础表格使用 Nuxt UI Table；应用直接导入分页、排序等 TanStack API 时添加 `@tanstack/vue-table` | 条件候选 | `@nuxt/ui` 已提供基础表格能力；传递依赖不构成应用可直接使用的 API 契约，不默认声明 `@tanstack/table-core` | 服务端分页、排序、筛选、列状态、虚拟化和 Nuxt UI 已覆盖能力 |
| API 契约代码生成 | `@hey-api/openapi-ts`（`devDependency`）；生成代码直接导入的运行时客户端包按实际用途声明 | 优先候选 | 仅在后端提供稳定、可重复获取的本地 OpenAPI 契约后采用；生成类型、SDK 和客户端必须输出到隔离目录，不成为 Feature 组织方式，不依赖 Hey API 云服务，并由可重复脚本和 CI 检查契约或生成结果漂移 | Web API 2 契约生成与 OpenAPI 版本、稳定错误码和分页模型、Fetch 的 Header Bearer/取消/超时、插件与锁定工具链兼容性、确定性输出、生成代码审查边界及运行时导入 |
| 服务端状态 | 薄 API Client；出现真实查询缓存复杂度后评估 `@pinia/colada` | 条件候选 | Pinia Colada 只管理服务器权威数据的查询、去重、缓存、失效和 Mutation；采用后必须先注册 `pinia`，普通 Pinia Store 仍只管理客户端自有状态，不复制查询缓存。SSE 只更新对应查询或触发精确失效 | 至少两个真实查询消费者、缓存键和新鲜度、取消与去重、重试上限、会话过期、`Offline`/`Unknown`、乐观更新与回滚、SSE 补取和失效、DevTools、包体积，以及缺少 `networkMode` 和 `structuralSharing` 对目标流程的影响 |
| 客户端全局状态 | `pinia` Setup Store | 已采用；认证、玩家和应用壳自动化通过 | 跨路由会话由 Auth Store、登录页、显式 Pinia Router guard、App Shell 和受保护 API 共同消费；Store 统一拥有严格浏览器会话恢复、到期、登出、401 和跨标签页删除，不安装通用持久化插件，也不复制玩家或其他服务器权威数据 | 真实 OWIN 下两种 Storage 合同、CSP 和敏感信息边界仍需浏览器 smoke；HMR 和包体积继续随应用演进验证 |
| 进程内瞬时事件 | 优先使用 props/emits、显式 Feature API、路由状态和查询失效；确有解耦需求时评估 `mitt` | 条件候选 | 只传递无持久状态、无需权威恢复的应用级通知；必须定义集中式 TypeScript 事件映射，不承载服务器事实、业务命令、权限或长任务结果 | 明确生产者与至少两个独立消费者、订阅释放、重复注册、事件顺序、异常隔离、测试可追踪性及 HMR 行为 |
| Vue composables | `@vueuse/core` | Admin 目标采用 | 当前用于颜色模式等浏览器状态；新增能力仍需逐项证明直接价值 | 每个新增 composable 的真实复用、包体积和清理行为 |
| SSE | Header Bearer 全阶段使用 Fetch 型客户端；实现时比较无依赖 Fetch parser、`event-source-plus` 与 `@microsoft/fetch-event-source` | Fetch 型边界已批准；具体库为条件候选 | 必须设置 `Authorization` Header、检查 401/403/429/503、主动取消并限制重连，禁止 QueryString Token；产品不采用 Cookie 认证，原生 `EventSource` 不进入目标方案；`event-source-plus` 提供显式 Controller 和内置重试策略，Microsoft 方案生态更成熟 | Content-Type、Welcome/命名事件、Last-Event-ID/游标、Header Bearer 生命周期、取消、页面隐藏、重试上限、错误分类、代理缓冲、额外依赖、包体积、维护状态和浏览器基线 |
| 表单与边界校验 | `valibot` | 已采用；当前表单边界已验证 | 登录、踢出原因和 API Key 名称使用模块化 schema 驱动提交边界，兼容 Nuxt UI Standard Schema；当前分别保留服务端 200/80 Unicode 字符合同 | 后续异步服务端错误、更多传输 DTO 映射和团队使用成本 |
| 产品文案国际化 | `vue-i18n` | 已采用；当前界面已验证 | 集中管理 `zh-CN`、`en` 产品与业务文案、以 `en` 为默认回退和响应式语言切换；当前全部已实现 Admin 页面完成迁移 | 后续 Feature 的键类型安全、CSP、生产包体积和真实浏览器双语门禁 |
| Vue I18n 构建期集成 | `@intlify/unplugin-vue-i18n`（`devDependency`） | 已采用；生产构建通过 | 精确预编译 `src/app/i18n/locales/**` 的成对 JSON 目录，不启用 runtime compiler；消息自动化禁止 HTML 并检查缺失键和插值漂移 | 后续语言拆包需求、插件升级兼容性和生产包体积 |
| Nuxt UI 组件语言 | `UApp :locale` | Admin 目标采用 | 让 Nuxt UI 内置文案和区域格式跟随应用当前语言；不能替代产品文案国际化 | `zh-CN`/`en` 映射、切换响应和组件覆盖范围 |
| Valibot 内置错误翻译 | `@valibot/i18n` | 已采用；语言同步已验证 | 只导入官方 `zh-CN` 子模块，英语使用 Valibot 内置消息；global `lang` 与应用语言同步，自定义业务反馈仍由产品目录拥有 | 后续实际呈现更多内置 issue 时的浏览器验证、自定义业务错误归属和包体积 |
| 日历日期 | `@internationalized/date` | 条件候选 | Nuxt UI 内部使用该包；只有 7DPanel 源码直接导入 `CalendarDate`、`CalendarDateTime`、`Time` 或 `ZonedDateTime` 时才声明为直接依赖 | 序列化、服务器时区契约、日期与时刻语义以及 Nuxt UI 表单集成 |
| 日期格式与计算 | `date-fns` | 条件候选 | 仅用于 `Intl` 和已有日期能力不足的纯函数格式化或计算 | 是否与 `@internationalized/date` 重复、locale 体积和时区语义 |
| Head 管理 | `@unhead/vue` | Admin 目标采用 | 当前用于响应颜色模式更新 `theme-color`，保留模板中已经直接使用的轻量集成 | 后续页面标题、meta 所有权和是否仍有直接 API 需求 |
| 图表 | `@unovis/vue` + `@unovis/ts` | 预留 | 官方 Vue 用法要求 Vue wrapper 与 core 配套；当前设计没有必须图表化的指标 | 先确认业务指标、无图表替代、可访问性、包体积和窄屏表现 |
| 游戏地图与空间交互 | `ol`（OpenLayers） | 条件候选 | 仅在产品批准玩家位置、世界地图或区域编辑等地图流程后采用；适合自定义投影、静态/瓦片底图、矢量覆盖和绘制交互，不因展示单个坐标而引入 | 7DTD X/Z 坐标与图像像素映射、原点和轴方向、世界范围与缩放层级、离线地图资产、性能、可访问性、CSS、包体积和许可证 |
| OpenLayers 扩展 | `ol-ext`；缺少内置声明时评估 `@types/ol-ext` 到 `npm:@siedlerchr/types-ol-ext` 的别名 | 二级条件候选 | 只有 `ol` 已采用且核心 API 无法清晰满足某个已批准控件、交互、覆盖层或渲染需求时，才按具体模块引入；社区声明仅进入 `devDependencies`，精确版本由实际清单和锁文件拥有 | 与锁定 `ol`/`ol-ext` 版本及实际导入模块的类型兼容性、声明包的 `jspdf` peer、额外 CSS、维护状态、tree-shaking、交互可访问性和无扩展替代方案 |
| 通用工具函数 | 优先使用原生 JavaScript/TypeScript；出现跨 Feature 的复杂纯函数需求后评估 `es-toolkit` | 条件候选 | 简单数组、对象和字符串转换不构成引入工具库的理由；深比较、深拷贝、防抖或复杂集合操作应避免重复手写 | 至少两个真实消费者、原生实现的正确性与可读性、tree-shaking、浏览器基线和具体函数语义 |
| 日志虚拟化 | 默认不引入；达到实测 DOM 与滚动瓶颈后选型 | 默认不采用 | 首先用有界窗口和分页控制复杂度 | 行高、动态内容、键盘访问、复制、搜索和定位 |
| 静态检查 | ESLint、`@antfu/eslint-config`、`vue-tsc` | Admin 目标采用 | Antfu flat config 统一 JavaScript、TypeScript 和 Vue SFC 规则，`vue-tsc` 独立负责类型检查；项目覆盖规则按所属集成显式配置 | 全工程 lint 基线、type-aware lint 耗时、忽略范围、编辑器/CI 一致性，以及配置不再直接导入后移除 `eslint-plugin-vue`/`typescript-eslint` 的结果 |
| ESLint formatter | Antfu formatters 与直接 `devDependency` `eslint-plugin-format` | Admin 目标采用 | 统一格式化 CSS、HTML、Markdown 和 Vue `<style>`；精确版本和脚本由应用清单拥有，不把格式化成功等同于类型或业务验证 | 首次自动修复差异、Prettier/dprint 行为、生成文件排除、编辑器保存行为和 CI 非交互执行 |
| 单元与组件测试运行器 | `vitest` | 已采用；当前自动化证据见[测试策略](../test.md) | 复用 Vite 的 ESM、TypeScript、Vue SFC 和路径解析链路，负责认证 Store、API 映射、查询状态和组件行为测试；一次性 run 模式通过共享 Vite 配置排除 `tests/e2e/` | 覆盖率 provider、watch 与 CI 固定任务尚未建立；当前 happy-dom teardown 噪声的根因仍待定位 |
| Vue 组件测试 | `@vue/test-utils` + `happy-dom`；仅在实际 Web API 兼容缺口出现时回退评估 `jsdom` | 已采用；登录、路由与玩家组件自动化通过 | 通过 `mount` 验证登录、路由和玩家页面可见行为；共享挂载器负责 Nuxt UI、Router 和真实/测试 Pinia，不默认使用浅挂载或只依赖快照 | 真实焦点、CSS、视口和浏览器布局仍由 Playwright 负责，不能从 happy-dom 结果推导 |
| 浏览器端到端测试 | Playwright | Chromium 门禁基础已建立；当前真实 suite 未验证 | 配置使用真实 OWIN base URL、失败 trace/截图和 Desktop Chrome；Owner suite 覆盖匿名重定向、登录、8 小时 Token、默认标签页会话、显式浏览器会话、身份显示、登出/到期/损坏记录清理、API Key 创建/一次性显示/复制/使用/撤销、深链接刷新、Authorization Header、请求 URL、Storage/Cookie/控制台和 `390x844` 水平溢出，不 mock 后端 | 本轮因 `SEVENDPANEL_ADMIN_URL`、`PANEL_USERNAME`、`PANEL_PASSWORD` 缺失而 8 项未执行；服务启停、真实玩家、真实布局、CSP 控制台泄漏和关服后 Stale 仍需受控环境证据 |

### 工程清单约束

用户提供的 `package.json` 片段和已导入模板作为 Admin 初始化输入，但蓝图不复制其版本范围和完整 JSON：

- 所有前端应用必须使用 pnpm，但 Admin、Marketing 和未来 Player 分别拥有自己的 `package.json`、
  `pnpm-workspace.yaml`、`pnpm-lock.yaml`、精确 `packageManager` 和 Node.js 兼容范围；
- 当前不创建 `frontend/package.json`、`frontend/pnpm-workspace.yaml` 或根锁文件。只有至少两个应用出现
  真实共享包、必须协调安装或需要联动发布时，才评估根 workspace；届时不得默认强迫不同框架使用同一发布周期；
- 每个应用只能在自己的目录内使用 `pnpm add`、`pnpm remove`、`pnpm run` 和 `pnpm exec`
  修改或执行依赖，不得使用 npm、Yarn 或 Bun 改写其依赖图；
- 每个应用只提交自己的 pnpm 锁文件，不得在同一应用中生成或提交 `package-lock.json`、`yarn.lock`、
  `bun.lock` 或 `bun.lockb`；
- CI 在目标应用目录使用 `pnpm install --frozen-lockfile` 或经该应用当前 pnpm 版本验证的
  `pnpm ci`，锁文件漂移必须使构建失败；
- Admin 应用的 `private: true` 和 `type: "module"` 作为工程基线；
- 工程提供 `dev`、`build`、`preview`、`lint` 和 `typecheck` 能力，精确脚本由实际 `package.json` 拥有；
- `preview` 只用于本地检查，不是生产服务方式；
- Vite 的 `build` 不替代 `vue-tsc` 类型检查，提交和发布门禁必须同时执行 lint、typecheck、测试和生产构建；
- Admin 开发和 CI 以 Node.js `24+` 为基线；`package.json` 声明 `^20.19.0 || ^22.13.0 || >=24.0.0` 以保留 Vite 8 与 ESLint 的精确兼容范围，并使用锁定的 pnpm、Vite 8/Rolldown、TypeScript 和 ESLint 组合；
- 只有浏览器运行代码直接导入的包进入 `dependencies`；构建、类型检查、lint 和测试工具进入
  `devDependencies`。Tailwind CSS 的最终归类由 Nuxt UI standalone Vue 安装要求和实际构建流程验证；
- OpenAPI 生成器及仅在生成时运行的插件进入 `devDependencies`；生成代码在浏览器运行时直接导入的客户端包进入 `dependencies`，不得因生成器自身是开发依赖而遗漏运行时直接依赖；
- Admin 应用仅通过 `import type` 使用的纯类型包（如 `type-fest`）进入 `devDependencies`；若未来可发布共享包的公开声明暴露其类型，再重新评估 dependency 或 peer dependency 边界；
- 不把传递依赖为了“版本看得见”提升为直接依赖。只有应用直接使用其 API 或必须控制兼容边界时才显式声明。

Nuxt UI、查询缓存、全局 Store、文件布局路由、图表或虚拟列表都不能因为“常用”而自动引入。实施者若发现
更合适的当前库，应说明它如何更好满足本表门槛；涉及应用边界、运行时或发布方式变化时先更新本蓝图或对应变更设计。

## 构建、发布与缓存

- 生产构建输出到 Admin 自有的 `dist/`，再由后端发布组装显式复制到 Mod 静态资源目录；前端构建不得直接写入
  服主的 `Mods/7DPanel` 运行目录。
- 构建 base path 由最终 OWIN 挂载位置决定，禁止写死开发主机、端口、绝对磁盘路径或公网域名。
- JS、CSS 和媒体使用内容哈希并可长期缓存；HTML shell 使用可重新验证或短缓存策略，避免引用已经删除的资源。
- API 和 SSE 响应不进入 Service Worker 离线缓存。首版不默认启用 PWA 或 Service Worker，防止管理状态过期。
- 发布检查必须确认入口 HTML、全部引用资源，以及 Bootstrap、Application、Hosting、Web、SevenDays 和
  Persistence 六个产品 DLL 齐全，且不包含源码、测试、开发服务器配置、Marketing 产物、`config.json`、
  `data/` 或外部 CDN 依赖。
- 前端与后端的兼容性至少由产品版本、API 契约测试和同一发布物 smoke 证明；不得只凭两边分别构建成功。

## 质量属性与验证门槛

具体测试层级和发布门禁由[测试策略](../test.md)拥有。本蓝图要求至少提供以下证据：

- `CAP-01`：启动、状态新鲜度、离线、过期和 `Draining` 的页面与同源集成测试；
- `CAP-02`：玩家查询、危险动作确认、重复提交、`Unknown` 结果、日志 SSE 断线和补取；
- `CAP-03`：备份状态、恢复确认、关服、浏览器重开、重启后最终结果和回滚失败；
- `CAP-04`：公告预览、固定触发器编辑、启停、最近执行结果和去重显示；
- `CAP-05`：引导 `Owner` 登录、认证配置异常、8 小时 Access Token 到期、API Key 一次性显示/撤销/当前角色继承、角色导航、服务端 `Forbidden` 和审计关联；
- `NFR-01`：断开公网后核心管理能力可用，生产资源不存在第三方运行依赖；
- `NFR-02`：所有写操作都能区分排队、执行、成功、失败和未知，不以 HTTP 200 替代游戏结果；
- `NFR-03`：`zh-CN` 与 `en` 的全部 P0 页面和表单通过 E2E，覆盖浏览器语言匹配、默认回退 `en`、登录前后切换、偏好持久化、缺失键、Valibot 内置错误、Nuxt UI 文案、日期数字格式和稳定服务端错误码映射；
- `NFR-04`：默认凭据可在批准的明文 HTTP 边界完成网站登录，Basic 被拒绝，Access Token 只进入 `Authorization` Header 和批准的版本化浏览器会话记录，API Key 完整值只显示一次，错误、到期及 QueryString 凭据被拒绝，且客户端不使用 Cookie 认证；
- 320 CSS 像素、常用桌面和宽屏下无不可达操作、文本遮挡或布局跳动；
- 键盘、焦点、语义标签、状态非纯颜色表达、减少动态效果和 WCAG 2.2 AA 对比度；
- 真实 OWIN 静态托管下的深链接刷新、缓存、API 路由隔离、SSE、登录和正常关服；
- 生产构建的类型检查、单元测试、组件测试、浏览器 E2E、资源清单和包体积记录。

## 架构复审触发条件

只有出现以下证据时才重新评估本蓝图：

- Admin 无法作为静态 SPA 在 OWIN 中满足核心流程；
- 某个 Feature 形成独立部署、安全或发布周期；
- Marketing 或 Player Portal 出现两个以上真实共享需求；
- 同源 REST/SSE 无法满足经过量化的实时性或规模要求；
- 当前状态方案导致重复权威来源、失效错误或无法恢复的跨页面任务；
- 生产包体积、日志规模、渲染性能或浏览器兼容性超过已验证边界；
- API 契约变化无法通过兼容演进和类型边界吸收。

单独增加页面、组件、composable 或 npm 包不构成应用边界变化。

## 尚需验证的证据缺口

- Admin 全工程 lint 基线已在当前工具链下通过；自动修复审查和移除无直接导入的兼容依赖仍应在对应依赖变更中单独评估；
- Admin 的 Node.js `24+` CI 固定任务尚未纳入仓库自有 CI；本地工具链的精确兼容范围已由 `package.json` 和锁文件声明；
- OWIN 中 Admin 的最终挂载路径、SPA fallback、压缩和缓存头；当前 Admin 文档 CSP 已有 Katana 自动化，仍需真实 OWIN 浏览器控制台验证；
- REST 分页与查询游标仍待定义；Problem Details、认证 SSE 路由、Welcome/命名事件、`Last-Event-ID`、gap 和补取窗口已有后端契约，但前端类型映射、Header Bearer 生命周期、有界重连和浏览器验证尚未实现；
- 登录限速和认证配置异常反馈；8 小时 Access Token 的到期重登、默认标签页/显式浏览器会话、账号身份显示、API Key 一次性显示/清除与撤销已有组件和受控时间自动化，仍缺真实 OWIN 浏览器证据；
- 日志典型速率、浏览器保留窗口、渲染预算和是否需要虚拟列表；
- 生产包体积预算、最低浏览器范围和自动化可访问性门槛；
- Nuxt UI standalone Vue 在目标密度、响应式表格和键盘操作上的原型证据；
- `vue-i18n`、Nuxt UI locale 与 `@valibot/i18n` 的语言标识映射、按需加载、缺失键门禁和同步切换原型；
- Windows/Linux Mod 发布物中的静态资源路径和真实进程 smoke。
- 当前 8 项 Playwright Owner/API Key suite 已建立缺前置条件时的明确 skip 门禁，但尚未在真实 OWIN、真实凭据和受控在线玩家环境执行；`390x844` 真实渲染、Header-only Access Token/API Key 浏览器证据、两种浏览器会话、CSP 控制台和关服后 Stale 仍未取得。

这些缺口在首个 Admin 纵向切片的变更设计和实施计划中逐项关闭。未经代码、自动化测试和真实 OWIN 发布验证，
不得把候选框架、目录或运行链路提升为当前架构事实。
