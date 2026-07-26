---
state: Current
document_role: Change Record
last_updated: "2026-07-26"
---

# Admin 生成式 API Client 与 Pinia Colada 设计规格

> 本规格描述 Admin 传输与服务端状态管理的内部改造。当前系统边界以[系统架构](../../architecture.md)为准，批准的 Admin 目标边界以[Admin 前端目标蓝图](../../architecture/admin-frontend-target-blueprint.md)为准；本规格不改变产品能力或页面交互。

## 目标与范围

本次改造引入 `@hey-api/openapi-ts` 和 `@pinia/colada`，从后端运行时 OpenAPI 契约生成隔离的 TypeScript 传输代码，并让 Pinia Colada 管理综合概览查询和重启服务器 Mutation。Admin 同时使用生成的 `serverEventsGet()` 建立单一认证 Fetch SSE，把命名事件作为综合概览立即刷新的提示；REST 综合快照仍是页面状态的唯一权威来源。现有 Feature composable 的公开接口、严格响应解析、401 会话失效、页面可见性刷新和高风险操作确认保持不变。`@hey-api/openapi-ts 0.94.0` 是支持当前 Node.js `20.19+` 基线的最新已核对版本；其内置 Fetch Client 生成器，不安装已废弃的独立 `@hey-api/client-fetch` 包。

本次包含：

- 为全部 OpenAPI operation 提供稳定且唯一的 `operationId`，并以 Katana 文档测试锁定。
- 从受控 Katana 测试主机导出并提交 `frontend/apps/admin/openapi/7dpanel.v1.json`。
- 使用本地 OpenAPI 快照生成 Fetch Client、SDK、Pinia Colada query/mutation options 和 query keys。
- 在 Admin 启动时先注册 Pinia，再注册 Pinia Colada，并配置生成客户端。
- 用生成 SDK 替换综合概览和重启服务器的手写 HTTP 传输调用；Feature 层继续执行运行时解析和领域错误映射。
- 使用生成的 `serverEventsGet()` 建立 Header Bearer SSE，处理 `welcome`、`game-ready`、`server-stopping`、`gap`、heartbeat、自动重连和 `Last-Event-ID` replay 游标。
- 在 `game-ready`、`server-stopping` 或 `gap` 到达时立即强制刷新综合概览；没有玩家进入/离开事件前，在线玩家列表继续独立按 10 秒轮询。
- 提供 `api:schema`、`api:gen` 和 `api:check` 的可重复命令，并记录最接近所有者的操作说明。

本次不包含：

- 修改任何页面布局、文案、路由或产品能力。
- 删除 `requestJson` 或迁移登录、玩家、历史玩家、API Key 及关闭服务器。
- 使用 Hey API 云服务、远程构建期契约或生产服务器作为生成输入。
- 用 TypeScript 生成类型替代运行时响应校验。
- 对高风险 Mutation 自动重试、乐观更新或自动重放。
- 引入 refresh token、持久查询缓存、原生 `EventSource`、`@hey-api/vite-plugin` 或 Pinia Colada 自动轮询插件。

## 契约快照与生成边界

后端 `/swagger/v1/swagger.json` 仍是运行时权威文档。测试通过真实进程内 Katana Host 获取该文档，规范化后与提交的 `frontend/apps/admin/openapi/7dpanel.v1.json` 比较。显式更新环境变量只用于维护者主动刷新快照；普通测试只读且在漂移时失败。

`openapi-ts.config.ts` 只读取仓库内快照，输出到 `src/shared/api/generated/`。生成目录禁止手改，必须整体清理后生成并提交。生成配置启用内置 `@hey-api/client-fetch` 插件、`@hey-api/sdk` 以及 `@pinia/colada` 的 query/mutation options。生成器和所有生成代码直接导入的包由 Admin 自己声明并锁定；`@hey-api/client-fetch` 只作为内置插件名称出现，不是独立依赖。

OpenAPI operation 名称属于前端生成 API 的稳定输入。Web Adapter 的集中 operation processor 按 HTTP method 和规范化 route 分配明确名称；手工 OAuth operation 保留 `issueAccessToken`。Katana 测试断言 operationId 非空、唯一并覆盖当前路径，避免 Controller/action 重命名产生无提示的前端破坏。

## 运行时边界

`src/shared/api/generated/` 只包含传输类型、SDK 和 Colada 生成定义。手写 `src/shared/api/generatedClient.ts` 配置同源 base URL、`credentials: 'omit'`、Bearer Header、调用者取消、普通请求 10 秒超时和 Problem Details 错误映射；明确请求 `Accept: text/event-stream` 的长连接不使用普通请求超时，但仍服从调用者 `AbortSignal`。生成代码不得导入 Auth Store 或 Feature 模块。

客户端配置通过显式会话提供者读取当前 Authorization Header，并通过显式回调通知 401；Token 不进入 URL、query key、持久查询数据、日志或错误文本。登录和登出仍由 Auth Feature 拥有。`serverState.ts` 随 Authorization Header 生命周期启停一个 SSE 连接；登出、401 或会话替换时先停止连接、清除 replay 游标并清除受保护查询缓存，防止不同会话共享事件位置或缓存数据。

Pinia Colada 只拥有服务端权威查询、去重、缓存、失效和 Mutation 生命周期。普通 Pinia 继续拥有认证与客户端偏好。全局 `queryOptions` 固定为 `staleTime: 0` 和 `refetchOnWindowFocus: false`：数据立即过期且不会因窗口聚焦额外刷新，但缓存本身不被删除。综合概览返回值仍经过 `parseOverview`，因此 `Fresh`、`Partial`、`Stale` 和 `Offline` 由领域响应和刷新结果决定，而不是由通用查询状态推断。

综合概览 query key 固定且不包含 Token。页面可见时每 3 秒刷新，隐藏时暂停，恢复可见后立即刷新；SSE 生命周期事件和 gap 通过现有 `refetch()` 强制请求，不依赖缓存新鲜度。刷新失败保留最后成功快照和原始采样时间，但页面必须标为 `Stale`；首次失败且没有快照时标为 `Offline`。重启 Mutation 禁止自动重试和乐观更新，确认状态继续由 `useRestartServer` 管理；成功只表示脚本进程已启动，并精确失效概览查询。

`src/app/serverEvents.ts` 只拥有 SSE 连接、重连、游标和协议事件发布。连接显式发送 `Accept: text/event-stream` 与当前 `Last-Event-ID`，消费生成 SDK 返回的异步流以真正启动请求；收到带 `id` 的事件后更新内存游标，重连时请求 replay。`welcome` 只确认连接目标和当前主机/游戏状态，heartbeat 只确认连接存活，二者不直接改写综合概览；`game-ready`、`server-stopping` 和 `gap` 通知活跃的综合概览执行立即刷新。SSE 数据不得直接覆盖 REST 快照。

## 错误与安全

- 生成客户端的 HTTP、网络、超时、取消和无效响应统一映射为现有 `HttpError` 语义。
- `application/problem+json` 中的稳定 `code` 与 `traceId` 必须保留；任意服务端异常文本不直接展示。
- 401 只触发一次会话失效通知，不自动重放请求或 Mutation。
- SSE 不使用 URL Token 或 Cookie；连接只由当前网站 Access Token 的 `Authorization` Header 建立，且同一时刻最多存在一个活动连接。
- SSE 解析或网络错误不得清空最后成功 REST 快照；generated SSE 的有界退避和应用级正常结束重连都必须可由登出或会话替换立即取消。
- 重启 Mutation 的 403、确认缺失、并发操作、审计失败和脚本启动失败继续映射为现有领域错误码。
- 生成 TypeScript 类型不是运行时验证；`parseOverview` 和 `parseRestartAccepted` 等严格解析保留在 Feature API 边界。
- API Key 明文、Access Token、密码和 Authorization Header 不进入 Pinia Colada 缓存。

## 验证

- 后端 Katana 测试验证 operationId 唯一稳定、快照一致且更新模式可生成合法 JSON。
- Admin 生成检查验证配置可从快照重复生成且工作区无漂移。
- 单元测试先验证生成客户端的 Header、普通请求超时、SSE 超时豁免、Problem Details、401 和调用者取消映射。
- `serverEvents` 测试验证单连接、Authorization Header、事件映射、heartbeat、游标 replay、gap、重连和停止行为。
- `serverState` 测试验证认证建立连接，登出或会话替换停止连接、清游标和清受保护缓存。
- `useOverview` 测试验证全局零新鲜度不会抑制请求、3 秒可见刷新、隐藏暂停/恢复立即刷新、SSE 触发强制刷新，以及失败时保留带原始时间的旧快照并进入 `Stale`/`Offline`。
- `useRestartServer` 测试验证确认、单飞、无重试、错误映射和概览失效。
- 完成前执行后端聚焦测试，以及 Admin lint、typecheck、unit test 和 production build；不执行真实 7DTD、发布或浏览器 smoke。
