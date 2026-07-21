---
state: Current
document_role: Design Spec
last_updated: "2026-07-21"
---

# 认证服务器事件与统一错误设计规格

> 后续决策（2026-07-21）：当前[产品需求](../../PRD.md)已批准框架搭建阶段使用已知默认配置凭据和明文 HTTP，并明确未来不采用 Cookie 认证。本文保留 2026-07-20 实施时的原始设计上下文；其中“无默认凭据”和最终 SQLite/Cookie 方向不再代表当前产品合同或 Target 设计。

## 上游与范围

本规格落实[产品需求](../../PRD.md)中的 `CAP-01`、`CAP-02`、`CAP-05`、`NFR-02` 和 `NFR-04`，并延续[产品设计](../../design.md)的登录、会话过期、日志补取和稳定错误码规则，以及[后端目标架构蓝图](../../architecture/backend-target-blueprint.md)中已经批准的认证 SSE、每客户端有界 mailbox、角色权限、连接配额和稳定错误契约。

本切片把当前默认关闭的未认证开发日志流替换为首个认证生产事件流，同时引入统一 Problem Details、过渡期配置身份、Basic 认证和短期 Bearer Token。这里的“生产事件流”表示路由、认证、错误、背压、恢复和生命周期已经按生产边界设计，不表示 `CAP-05` 的 SQLite 用户、首个 `Owner` 初始化、持久会话、完整角色管理或公网 TLS 部署已经完成。

## 目标

- 为 Web API 2 提供统一、可追踪且不泄露内部异常的 Problem Details 错误契约。
- 使用 `Microsoft.Owin.Security.OAuth 4.2.3` 验证 Bearer Token，并实现同一管线中的 Basic 认证。
- 从 `config.json` 读取无默认值的过渡期管理员用户名和密码，把成功身份映射为临时 `Owner`。
- 提供认证的 `GET /api/v1/events/stream`，一个连接承载多个稳定命名事件并先发送 Welcome 快照。
- 复用并泛化当前日志窗口、replay、gap、heartbeat、订阅上限和慢客户端隔离，不引入通用领域 Event Bus。
- 删除默认关闭的未认证开发 SSE 路由和配置开关，避免同时维护两个安全语义不同的实时入口。

## 非目标

- 不宣称完成首个 `Owner` 初始化、SQLite 用户、密码摘要、持久会话、CSRF、用户管理或完整权限矩阵。
- 不实现 refresh token、外部 OAuth Client、第三方身份提供者、JWT、Cookie 会话或跨服务器单点登录。
- 不把所有 `ModEvents` 反射扫描并广播到网络，不复制旧项目的通用 `EventForwarder`。
- 不在 URL、QueryString 或 SSE event data 中传递访问 Token。
- 不持久化普通游戏事件或控制台日志，不提供跨 7DTD 进程 replay。
- 不在本切片修改 Admin 登录页或接入前端 SSE；Bearer Header 的浏览器消费者进入后续前端纵向切片。

## 参考实现取舍

外部 `7dtd-serveradmin` 的 `ApiControllerBase`、`ProblemDetailsFactory`、Basic/OAuth middleware 和 `Startup` 只作为行为证据，不复制源码。保留以下思路：

- `application/problem+json`；
- `type`、`title`、`status`、`detail`、`instance` 和关联标识；
- Basic 与 Bearer middleware 位于受保护端点之前；
- OAuth password grant 只验证同一自托管面板的管理员凭据；
- SSE 在认证完成后建立。

明确拒绝参考实现中的以下行为：

- `AllowInsecureHttp = true` 作为默认值；
- 从 `access_token` QueryString 读取 Bearer Token；
- `Access-Control-Allow-Origin: *`；
- 使用设备信息、`MD5`、固定 IV 且忽略 purpose 的自定义 `IDataProtector`；
- 生成 refresh token 并在尚无身份持久化边界时扩大状态；
- 使用普通字符串比较密码或固定写入 `role=user`；
- 把 QueryString 写入 Problem Details `instance`；
- 信任任意长度的客户端 `X-Request-ID`，或只把 `traceId` 写入 body 而遗漏响应 Header；
- 在 SSE body 已开始后尝试改写为 Problem Details。

## 公开 HTTP 契约

| 方法与路径 | 认证 | 成功语义 | 失败语义 |
|---|---|---|---|
| `GET /health` | 匿名 | 保持现有精确健康契约 | 保持现有行为 |
| `GET /api/v1/health` | 匿名 | 保持现有精确健康契约 | 保持现有行为 |
| `POST /api/v1/auth/token` | 匿名提交凭据 | OAuth password grant 返回短期 Bearer Token | OAuth 协议错误保持 `error`/`error_description`；限流或宿主错误使用 Problem Details |
| `GET /api/v1/events/stream` | Basic 或 Bearer；临时 `Owner` | `text/event-stream` Welcome、replay 和 live events | 建流前使用 Problem Details；建流后只发送定义的 SSE 控制事件或关闭连接 |

删除 `GET /api/v1/dev/console-logs/stream` 和 `enableUnauthenticatedDevelopmentConsoleLogStream`。当前实现尚未形成已发布兼容合同，因此不保留匿名兼容别名。

## 统一 Problem Details

### 响应模型

所有非 OAuth 协议型 API 错误使用 `application/problem+json`：

```json
{
  "type": "about:blank",
  "title": "Unauthorized",
  "status": 401,
  "detail": "Authentication is required to access this resource.",
  "instance": "/api/v1/events/stream",
  "code": "authentication_required",
  "traceId": "7f3d9c0d3cb74ec2a48a1a93be335a61"
}
```

- `instance` 只包含请求 Path，不包含 QueryString、凭据或内部文件路径。
- `code` 是前端本地化和稳定分类依据；`detail` 只提供安全的通用诊断，不包含堆栈、内部异常、用户名是否存在或配置值。
- `traceId` 使用每请求关联标识；服务端同时返回 `X-Request-ID` Header。只接受长度不超过 64 且由 ASCII 字母、数字、`.`、`_`、`-` 组成的传入值，否则生成 32 位小写十六进制值。
- 400 validation 可以增加 `errors`，但错误内容必须来自稳定校验规则，不序列化异常消息。
- 401 同时返回适用的 `WWW-Authenticate: Basic ...` 与 `WWW-Authenticate: Bearer` challenge；403 不把资源存在性或所需更高角色泄漏给无权用户。
- 未知 `/api/*`、无效 `Last-Event-ID`、订阅限额、认证不可用和未处理 API 异常都进入同一工厂。健康成功响应不被包装。

### 组件边界

- `RequestCorrelationMiddleware` 在最外层确定关联标识并设置响应 Header。
- `ApiProblemDetailsFactory` 只创建 DTO 和 `HttpResponseMessage`，复用 Web API 的统一 JSON formatter。
- `ApiProblemDetailsHandler` 规范化 Web API 产生的空 400/401/403/404/405/415/429/500/503 响应，但不覆盖已有合法 Problem Details 或 OAuth token response。
- 预期 Controller 错误直接调用工厂；当前只有一个需要该能力的 Controller 时不创建通用 `ApiControllerBase`。
- API 未处理异常由 Web API exception handler 映射为 500，并使用现有 Mod 日志记录 traceId 和异常。SSE 开始写出后的断线、取消和 I/O 异常只清理资源，不再改写 HTTP body。

## 过渡期认证

### 配置

`config.example.json` 增加无秘密默认值的嵌套配置：

```json
{
  "authentication": {
    "enabled": false,
    "username": "",
    "password": "",
    "accessTokenLifetimeMinutes": 30,
    "allowInsecureHttp": false
  }
}
```

- 不生成默认用户名或默认密码；发布物和测试夹具不得包含可登录凭据。
- `enabled=true` 时 username 和 password 必须非空；用户名去除首尾空白后按 ordinal 比较，密码不 trim、不规范化。
- `accessTokenLifetimeMinutes` 限制为 5 到 1440；无效认证配置保持失败关闭，健康端点仍可用，token 与生产 SSE 不返回受保护数据，并记录不含配置值的运维错误。
- `allowInsecureHttp=false` 是安全默认值。只有本机开发或 TLS 在受控本机反向代理终止时才允许显式开启；开启时启动日志必须输出不含凭据的风险警告。
- `config.json` 继续由服主拥有且不进入发布模板覆盖、版本库或前端。日志、异常和 `ToString()` 不得输出 username、password 或 Token。

### Basic

- 自有 `BasicAuthenticationMiddleware` 使用 Katana `AuthenticationMiddleware<TOptions>`，只解析 `Authorization: Basic`。
- Base64 解码后以第一个冒号分隔用户名与密码，允许密码包含冒号；无效编码、空用户名和验证失败统一表现为未认证。
- 凭据比较使用固定耗时字节比较，不用普通字符串 `==`。
- 成功身份包含 `ClaimTypes.Name` 和 `ClaimTypes.Role = Owner`，并标记为过渡配置身份。
- 非 HTTPS 请求在未显式允许时不得验证凭据，也不得把失败原因细分给客户端。

### Bearer

- `Microsoft.Owin.Security.OAuth 4.2.3` 的 authorization server 与 bearer middleware 共享一个进程内 `AccessTokenProvider`。
- `POST /api/v1/auth/token` 只支持 `grant_type=password`；不支持 refresh token、QueryString token 或跨源通配访问。
- Access Token 使用密码学随机的至少 256 bit 不透明值；服务端只在有界进程内表中保存票据和到期时间。最多保留 128 个未到期 Token，创建和读取时清理过期项。
- Token 在到期、服务停止或 7DTD 进程重启后失效；本切片不承诺跨重启会话恢复。
- 无效用户名和错误密码使用相同错误。`AuthenticationRateLimitMiddleware` 对 token endpoint 和携带 Basic Header 的生产 SSE 建连请求按 `RemoteIpAddress` 执行每分钟 20 次的固定窗口限制，最多保留 1024 个地址 bucket；达到上限返回带 `Retry-After` 的 429 Problem Details。过期 bucket 惰性清理，达到 bucket 上限时先清理过期项，再拒绝创建新 bucket；反向代理可以额外加严，但不能替代本进程限制。

OAuth token endpoint 的 `invalid_grant` 等协议错误保留 OAuth 标准 body，这是统一 Problem Details 的唯一协议例外；受 Bearer 保护的普通 API 和 SSE 仍使用 Problem Details 401。

## OWIN 管线顺序

```text
RequestCorrelationMiddleware
  -> API exception / Problem Details boundary
  -> AuthenticationRateLimitMiddleware
  -> ScopedServiceProviderMiddleware
  -> OAuthAuthorizationServerMiddleware (/api/v1/auth/token)
  -> BasicAuthenticationMiddleware (Active)
  -> OAuthBearerAuthenticationMiddleware (Active)
  -> Web API
       -> OwinScopeBridgingHandler
       -> ApiProblemDetailsHandler
       -> authorized controllers
  -> SPA fallback and StaticFiles
```

静态 Admin shell 和健康端点保持匿名。生产事件 Controller 使用 Web API `[Authorize(Roles = "Owner,Admin,Viewer")]`；当前配置身份只有 `Owner`。认证发生在创建 request-scoped SSE session、订阅 mailbox 或读取 Welcome 快照之前。

## 多命名服务器事件流

### 事件类型

本切片只增加存在真实生产者和消费者的事件，不建立反射扫描注册表：

| SSE event | 来源 | replay | 语义 |
|---|---|---|---|
| `welcome` | request-scoped snapshot provider | 否 | 每次连接一次，包含 product、version、host state、game readiness 和 `connectedAtUtc` |
| `console-log` | `Log.LogCallbacksExtended` consumer | 是 | 保留当前日志字段和全局 sequence |
| `game-ready` | `IModRuntime.MarkGameReady` 组合边界 | 是 | 当前进程首次进入 Ready 时一次 |
| `server-stopping` | 幂等 runtime stop 边界 | 是，best-effort 写出 | 接受新事件停止前发布；随后完成订阅并关闭连接 |
| `gap` | SSE session | 否 | 请求游标超出保留窗口或慢客户端 mailbox 溢出 |

订阅达到总上限时，Controller 必须在 `PushStreamContent` 和响应 body 开始前返回 503 Problem Details `stream_capacity_exhausted`，不能先返回 200 再发送 `unavailable`。已建立连接的过载只影响对应慢客户端。

### 流程与顺序

```text
authorized request
  -> reserve bounded subscription
  -> capture Welcome snapshot
  -> return text/event-stream
  -> write welcome
  -> replay events after Last-Event-ID
  -> de-duplicate replay/live race by sequence
  -> live named events
  -> heartbeat comment every 15 seconds
  -> cancellation / overflow / host complete
  -> dispose subscription and request scope exactly once
```

- 所有可 replay 的事件共享当前 7DTD 进程内单调 `long` sequence；Welcome 和 gap 不推进游标。
- `Last-Event-ID` 只接受非负十进制整数。无效值在建流前返回 400 Problem Details `invalid_event_cursor`。
- 服务重启后窗口和 sequence 重置；旧游标超前时发送 gap 并从当前窗口开始。跨进程游标歧义作为已知限制保留，不伪造跨进程连续性。
- 先订阅再 replay，并按 sequence 去重，避免 replay 与 live 之间遗漏。
- 每客户端 mailbox 保持 256，默认总订阅上限保持 8；生产路径使用非等待 `TryWrite`，不得用逐事件 `Task.Run` 或 `async void` 等待队列空间。
- SSE 使用无 BOM UTF-8、`Cache-Control: no-cache, no-store` 和 `X-Accel-Buffering: no`。事件 data 是单行 camelCase JSON。
- 浏览器客户端不得通过 URL Token 兼容原生 `EventSource`。后续前端切片必须使用支持 `Authorization` Header、取消、HTTP 状态分类和有上限重连的 Fetch 型 SSE 客户端；最终 Cookie 会话落地后再复核是否回到原生 `EventSource`。

### 事件 Hub 边界

当前 `ConsoleLogLiveWindow` 和 `ConsoleLogHub` 收敛为专用的服务器实时事件窗口与 Hub，承载上述四种生产事件，不扩展为领域 Event Bus、命令总线或任意对象广播器。事件 envelope 只允许明确列出的稳定 event name 和不可变、JSON 安全 payload。

日志回调仍只构造不可变数据并执行一次非等待投递；sequence 分配和网络广播不回到游戏日志回调线程。`game-ready` 和 `server-stopping` 从现有组合运行时发布，不让 Web Adapter 直接订阅静态 `ModEvents`。停止时先拒绝新生产、把已接受日志和 stopping marker 排入同一有界顺序边界、限时排空，再 complete Hub 和停止 OWIN。

## 依赖与发布

- Web Adapter 增加直接依赖 `Microsoft.Owin.Security.OAuth 4.2.3`，与现有 Katana `4.2.3` 对齐。
- Basic middleware 复用现有 `Microsoft.Owin.Security`，不增加第三方 Basic 包。
- 不增加 `Microsoft.Owin.Security.Jwt`、ASP.NET Core Data Protection、NLog、refresh-token store 或前端包。
- 发布脚本必须要求 OAuth DLL 存在，并继续拒绝复制游戏提供的 Newtonsoft.Json、Unsafe、LogLibrary 和 Unity 程序集。
- Windows `v3.0.1-b4` 真实进程必须验证 OAuth DLL 和依赖加载；Linux 兼容仍是独立证据缺口。

## 验证标准

### Problem Details

- 400、401、403、404、405、415、429、500 和 503 的代表路径具有精确 `application/problem+json`、稳定 `code`、Path-only `instance`、同一个 body/Header traceId，且没有堆栈、配置值或凭据。
- OAuth `invalid_grant` 保持 OAuth body，不被错误包装。
- SSE body 开始后的取消和写失败不尝试追加 Problem Details。

### 认证与授权

- 缺省、缺失和无效配置均没有可登录默认值，且生产 SSE 不匿名降级。
- Basic 正确处理非法 Base64、空用户名、密码内冒号、错误凭据、HTTPS 限制、challenge 和临时 Owner claims。
- Token endpoint 覆盖正确凭据、错误凭据、无效 grant、到期、Token 容量清理、重启失效，以及限流窗口、`Retry-After`、地址 bucket 容量和过期清理。
- Bearer 覆盖缺失、无效、到期 Token，且不接受 QueryString token。
- 健康端点和静态 Admin 保持匿名可用；生产事件流在建立订阅前完成认证和授权。

### SSE

- 同一连接依次观察 Welcome、日志和至少一个真实生命周期事件，event name、camelCase payload 和 sequence 精确。
- replay、gap、replay/live 去重、心跳、订阅总上限、慢客户端隔离、取消、关服完成和 request scope 一次释放均有自动化证据。
- 订阅容量不足在 body 开始前返回 503 Problem Details。
- Release Rebuild 零警告，后端全量测试通过。
- 发布后执行 Windows `v3.0.1-b4` 启动、token、Basic、Bearer、SSE、`GameStartDone`、日志、关服和端口释放 smoke；测试使用临时凭据并在结束后逐字节恢复服主配置。

## 文档影响

- 本规格批准后才创建对应 implementation plan。
- 实现并验证后更新[当前系统架构](../../architecture.md)中的 OWIN 管线、当前接口、配置、事件流、依赖矩阵、安全性和残余风险。
- 更新[后端目标架构蓝图](../../architecture/backend-target-blueprint.md)，记录过渡身份与最终 SQLite/Cookie 身份之间的替换边界，不把 config 身份提升为最终 `CAP-05`。
- Bearer Header 改变未来 Admin SSE 客户端条件，更新[Admin 前端目标蓝图](../../architecture/admin-frontend-target-blueprint.md)的 SSE 候选状态，但本切片不修改当前 Admin UI。
- 更新[测试策略](../../test.md)的认证、Problem Details、生产 SSE、发布清单和真实进程门禁；更新 `backend/README.md` 和配置模板的运维说明。
- 不更新 `CHANGELOG.md`，直到该能力作为用户或运维可见版本发布。

## 批准检查点

批准本规格即同时确认以下决策：

- 配置身份只是过渡期临时 `Owner`，不替代最终首个 Owner/SQLite/Cookie 方案；
- 删除未认证开发 SSE，而不是同时保留两套路由；
- token endpoint 只支持 password grant 和进程内短期 Bearer，不支持 refresh token；
- OAuth 协议错误是 Problem Details 的明确例外；
- 第一批生产事件固定为 `welcome`、`console-log`、`game-ready` 和 `server-stopping`；
- 明文认证默认拒绝，只允许显式开发/受控反向代理例外；
- 后续 Admin 连接 Bearer SSE 时使用 Fetch 型客户端，不把 Token 放进 URL。
