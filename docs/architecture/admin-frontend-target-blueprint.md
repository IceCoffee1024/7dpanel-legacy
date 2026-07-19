---
state: Draft
last_updated: "2026-07-19"
document_role: Target
---

# 7DPanel Admin 前端目标架构蓝图

> 本文描述 `frontend/apps/admin/` 的批准 Target 设计，不是当前实现证据。当前系统事实以
> [系统架构](../architecture.md)为准，产品范围以 [PRD](../PRD.md) 为准，页面、流程和视觉规则以
> [产品设计](../design.md)为准，验证要求以[测试策略](../test.md)为准。

## 用途与提升条件

本蓝图为 Admin 管理面板定义应用边界、运行链路、状态所有权、API 与安全约束、目标目录、
依赖候选和发布责任。已导入的模板基线和当前代码不能替代目标契约，也不能作为未验证运行链路的实现证据。
它覆盖 `CAP-01` 至 `CAP-05`、`NFR-01` 和 `NFR-02` 的前端实现边界，
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
       -> SSE  /api/v1/stream
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
| 当前操作者、角色和权限 | 后端会话 | 启动时恢复、控制导航和交互、处理过期 | 仅依据本地角色决定授权 |
| 表单草稿、对话框和展开状态 | 所属 Feature | 在安全范围内保留和清理 | 将密码、初始化凭证或 CSRF Token 持久化 |
| 长任务显示状态 | 后端作业与审计 | 跨页面、刷新和重连后恢复 | 只依赖内存 Toast 或单次 HTTP 响应 |
| 日志流游标和连接状态 | 日志 Feature | 维护最后游标、暂停、补取和缺口 | 把断线期间数据假装为连续实时流 |

服务端状态缓存与客户端交互状态必须分离。不得为了方便把全部 API 数据复制进一个全局 Store。全局状态只用于确有
跨路由生命周期的客户端协调；服务器数据仍由查询层根据键、时间戳和失效规则管理。

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

- 所有 HTTP 调用经过一个薄的同源 API Client，统一处理 base path、`credentials`、取消、超时、关联标识和错误映射。
- Feature 定义自己的请求、响应和页面模型；Controllers 的内部异常、数据库字段和文件路径不得泄漏到浏览器。
- 后端提供稳定契约后，可以评估从 OpenAPI 生成传输类型；生成代码必须隔离，不能成为 Feature 组织方式。
- 错误结果至少保留稳定错误码、用户可见消息、Correlation ID、适用时的 Audit ID 和可否安全重试。
- 查询使用 `AbortController` 或框架等效能力取消已经失去消费者的请求。

### 会话与 CSRF

- 会话使用服务端保存的随机不透明标识和 `HttpOnly` Cookie；前端不得读取或复制会话 Token。
- 状态变更请求携带后端签发的 CSRF Token。Token 只保留在当前应用生命周期所需范围，不写入
  `localStorage`、URL、日志或错误报告。
- 初始化链接中的一次性凭证读取后立即通过 History API 从地址栏和历史记录中移除；页面不得加载第三方资源，
  避免凭证通过 Referer 或遥测泄漏。
- 登录、初始化和会话过期页面不得根据错误信息泄露账号是否存在。
- 路由守卫和隐藏按钮只改善体验；服务端授权拒绝始终映射为明确的 `Forbidden` 页面或局部状态。

### SSE 与补取

- 首版优先使用浏览器原生 `EventSource` 和同源 Cookie，不为了封装 SSE 默认增加 npm 依赖。
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
  -> no Owner: setup route
  -> existing session: authorized app shell
  -> no session: login route
  -> fetch current server and task summaries
```

应用启动只阻塞建立身份边界所需的最小请求。其他页面数据按路由加载；一个非关键查询失败不得让整个管理面板白屏。
初始化成功后凭证立即失效并进入 Owner 会话。会话过期时保留可安全恢复的 URL 与草稿，但清除密码、初始化凭证、
CSRF Token 和危险确认状态。

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
|   |   |   |   `-- styles/                     # 应用级 tokens 和基础样式
|   |   |   |-- pages/                           # 路由级 Feature 组合
|   |   |   |   |-- SetupPage.*
|   |   |   |   |-- LoginPage.*
|   |   |   |   |-- DashboardPage.*
|   |   |   |   |-- PlayersPage.*
|   |   |   |   |-- ConsoleLogsPage.*
|   |   |   |   |-- AnnouncementsPage.*
|   |   |   |   |-- BackupsPage.*
|   |   |   |   |-- AuditPage.*
|   |   |   |   `-- SettingsPage.*
|   |   |   |-- features/
|   |   |   |   |-- auth/                       # 初始化、登录、会话和权限
|   |   |   |   |-- server-status/              # 新鲜度和连接状态
|   |   |   |   |-- players/                    # 玩家查询与类型化动作
|   |   |   |   |-- console-logs/               # SSE、补取和筛选
|   |   |   |   |-- announcements/              # 即时公告与自动化
|   |   |   |   |-- backups/                    # 备份、作业和恢复
|   |   |   |   |-- audit/                      # 审计查询与关联
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
| 包管理与 workspace | pnpm；各应用独立管理 | 已批准 | 所有前端应用统一使用 pnpm，但分别拥有依赖图、锁文件、Node.js 兼容范围和发布周期 | 各应用的 pnpm 精确版本、Node.js 范围和 CI 安装；出现真实共享后再评估根 workspace |
| Admin SPA | Vue 3 Composition API + `<script setup lang="ts">` | Admin 目标采用 | 适合长会话、高交互 SPA；当前应用壳已验证 TypeScript SFC 构建 | 生产包体积、浏览器基线和真实业务切片 |
| 构建 | Vite、`@vitejs/plugin-vue` | Admin 目标采用 | 生成静态输出、支持 base path 和带哈希资源，不要求生产 Node.js | OWIN 部署路径、SPA fallback、manifest、缓存策略和所选 Node.js 基线 |
| 路由 | Vue Router 文件路由 | Admin 目标采用 | 使用官方 Vite 插件生成页面路由，并让后续筛选和分页进入 URL | 权限元数据、URL 状态、恢复导航和 404 行为 |
| 文件布局路由 | `vite-plugin-vue-layouts` | 当前不采用 | 当前只有一个稳定 App Shell，直接包裹 `RouterView` 更简单，也避免引入与当前 Vite、Vue Router peer 范围不兼容的插件 | 出现多个稳定布局且手工布局映射产生实际维护成本 |
| UI 组件 | `@nuxt/ui` standalone Vue 模式、Tailwind CSS | Admin 目标采用 | 当前官方文档支持通过 Vite 插件用于独立 Vue，应用壳已验证 Dashboard 组件 | 高密度运维表格、主题约束、Tailwind 成本、包体积和键盘行为 |
| 数据表格 | 优先评估 Nuxt UI Table；高级需求再评估 `@tanstack/vue-table` | 条件候选 | Vue adapter 负责响应式集成；`@tanstack/table-core` 主要用于 vanilla 或自定义 adapter | 服务端分页、排序、筛选、列状态、虚拟化和 Nuxt UI 已覆盖能力 |
| 服务端状态 | 薄 API Client；查询缓存库待真实复杂度决定 | 预留 | 避免在需求简单时引入第二套状态模型 | 缓存键、失效、轮询、SSE 协作和 DevTools 价值 |
| 客户端全局状态 | 不默认引入；出现跨 Feature 客户端状态后评估 Pinia 等方案 | 预留 | 服务端数据不应复制到全局 Store | 所有权、持久化、安全清理和是否有两个真实消费者 |
| Vue composables | `@vueuse/core` | Admin 目标采用 | 当前用于颜色模式等浏览器状态；新增能力仍需逐项证明直接价值 | 每个新增 composable 的真实复用、包体积和清理行为 |
| SSE | 浏览器原生 `EventSource` | 已批准 | 同源 Cookie 场景无需额外封装依赖 | 游标、补取、退避、会话过期和代理缓冲 |
| 表单与边界校验 | `zod` | 优先候选 | 初始化、恢复和自动化表单需要类型化 schema 与一致错误映射 | Nuxt UI Form 集成、异步服务端错误、包体积和传输 DTO 映射 |
| 日历日期 | `@internationalized/date` | 条件候选 | 适合日期、日历和时区语义；仅在计划任务控件直接使用时安装 | 与 Nuxt UI 日期组件的直接使用边界、序列化和服务器时区契约 |
| 日期格式与计算 | `date-fns` | 条件候选 | 仅用于 `Intl` 和已有日期能力不足的纯函数格式化或计算 | 是否与 `@internationalized/date` 重复、locale 体积和时区语义 |
| Head 管理 | `@unhead/vue` | Admin 目标采用 | 当前用于响应颜色模式更新 `theme-color`，保留模板中已经直接使用的轻量集成 | 后续页面标题、meta 所有权和是否仍有直接 API 需求 |
| 图表 | `@unovis/vue` + `@unovis/ts` | 预留 | 官方 Vue 用法要求 Vue wrapper 与 core 配套；当前设计没有必须图表化的指标 | 先确认业务指标、无图表替代、可访问性、包体积和窄屏表现 |
| 字符串 case 工具 | `scule` | 默认不直接安装 | 简单标识转换不构成独立运行依赖的充分理由 | 代码直接使用且 BCL/局部函数无法清晰表达的稳定重复需求 |
| 日志虚拟化 | 默认不引入；达到实测 DOM 与滚动瓶颈后选型 | 默认不采用 | 首先用有界窗口和分页控制复杂度 | 行高、动态内容、键盘访问、复制、搜索和定位 |
| 静态检查 | ESLint、`eslint-plugin-vue`、`typescript-eslint`、`vue-tsc` | 优先候选 | 分别覆盖代码规则、Vue SFC 和独立类型检查 | 版本兼容、flat config、测试文件范围和编辑器/CI 一致性 |
| 测试 | Vitest、Vue Test Utils、Playwright 等随框架初始化确定 | 条件候选 | 当前候选清单尚未包含测试依赖，但蓝图要求单元、组件和浏览器 E2E | net48 OWIN 测试环境、浏览器矩阵、可访问性、运行时间和 CI 成本 |

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
- 初始化时补充测试脚本，并在验证 Node.js、pnpm、Vite、TypeScript 和 ESLint 的兼容组合后固定版本；
- 只有浏览器运行代码直接导入的包进入 `dependencies`；构建、类型检查、lint 和测试工具进入
  `devDependencies`。Tailwind CSS 的最终归类由 Nuxt UI standalone Vue 安装要求和实际构建流程验证；
- 不把传递依赖为了“版本看得见”提升为直接依赖。只有应用直接使用其 API 或必须控制兼容边界时才显式声明。

Nuxt UI、查询缓存、全局 Store、文件布局路由、图表或虚拟列表都不能因为“常用”而自动引入。实施者若发现
更合适的当前库，应说明它如何更好满足本表门槛；涉及应用边界、运行时或发布方式变化时先更新本蓝图或对应变更设计。

## 构建、发布与缓存

- 生产构建输出到 Admin 自有的 `dist/`，再由后端发布组装显式复制到 Mod 静态资源目录；前端构建不得直接写入
  服主的 `Mods/7DPanel` 运行目录。
- 构建 base path 由最终 OWIN 挂载位置决定，禁止写死开发主机、端口、绝对磁盘路径或公网域名。
- JS、CSS 和媒体使用内容哈希并可长期缓存；HTML shell 使用可重新验证或短缓存策略，避免引用已经删除的资源。
- API 和 SSE 响应不进入 Service Worker 离线缓存。首版不默认启用 PWA 或 Service Worker，防止管理状态过期。
- 发布检查必须确认入口 HTML、全部引用资源和四个后端产品 DLL 齐全，且不包含源码、测试、开发服务器配置、
  Marketing 产物、`config.json`、`data/` 或外部 CDN 依赖。
- 前端与后端的兼容性至少由产品版本、API 契约测试和同一发布物 smoke 证明；不得只凭两边分别构建成功。

## 质量属性与验证门槛

具体测试层级和发布门禁由[测试策略](../test.md)拥有。本蓝图要求至少提供以下证据：

- `CAP-01`：启动、状态新鲜度、离线、过期和 `Draining` 的页面与同源集成测试；
- `CAP-02`：玩家查询、危险动作确认、重复提交、`Unknown` 结果、日志 SSE 断线和补取；
- `CAP-03`：备份状态、恢复确认、关服、浏览器重开、重启后最终结果和回滚失败；
- `CAP-04`：公告预览、固定触发器编辑、启停、最近执行结果和去重显示；
- `CAP-05`：初始化、登录、会话过期、角色导航、服务端 `Forbidden`、CSRF 和审计关联；
- `NFR-01`：断开公网后核心管理能力可用，生产资源不存在第三方运行依赖；
- `NFR-02`：所有写操作都能区分排队、执行、成功、失败和未知，不以 HTTP 200 替代游戏结果；
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

- Admin 最低 Node.js 开发版本，以及已固定的 `pnpm@11.13.1` 与 Vite、TypeScript、ESLint 的兼容组合；
- OWIN 中 Admin 的最终挂载路径、SPA fallback、压缩、缓存头和 CSP；
- REST 错误契约、分页与游标格式、SSE 事件 envelope 和补取窗口；
- CSRF Token 获取方式、登录限速反馈和初始化凭证 URL 清除时机；
- 日志典型速率、浏览器保留窗口、渲染预算和是否需要虚拟列表；
- 生产包体积预算、最低浏览器范围和自动化可访问性门槛；
- Nuxt UI standalone Vue 在目标密度、响应式表格和键盘操作上的原型证据；
- Windows/Linux Mod 发布物中的静态资源路径和真实进程 smoke。

这些缺口在首个 Admin 纵向切片的变更设计和实施计划中逐项关闭。未经代码、自动化测试和真实 OWIN 发布验证，
不得把候选框架、目录或运行链路提升为当前架构事实。
