---
state: Current
document_role: Design Spec
last_updated: "2026-07-23"
---

# 无 Basic 与用户 API Key 认证设计规格

> 本文描述已经批准、尚待实现的认证目标变更。产品行为由[产品需求](../../PRD.md)中的
> `CAP-05`、`NFR-04` 和 `NFR-05` 定义，页面与交互由[产品设计](../../design.md)定义；
> 当前实现事实仍以[系统架构](../../architecture.md)和[测试策略](../../test.md)为准。

## 上游与范围

当前实现同时接受 Basic Authentication、OAuth password grant 签发的持久不透明 Bearer
Token，以及认证 SSE。真实 Unity Mono 测量证明，每个 Basic 请求都会执行 600,000 次
PBKDF2-HMAC-SHA256，导致动态控制台请求稳定增加约 4 秒；同一接口改用现有 Bearer Token
后只需约 50 毫秒。

本变更完全移除 Basic Authentication，保留网站用户名/密码登录和 password grant，把网站
Access Token 默认生命周期改为 8 小时，并增加用户自助管理的长期 API Key。密码 PBKDF2
只在登录和引导凭据同步时执行；网站登录后的请求、SSE、脚本和第三方集成都使用高熵
Bearer 凭据，不在高频路径执行密码摘要。

项目尚未进入内测且测试数据库可以删除，因此本切片不兼容旧认证数据库，不增加旧摘要
升级或 schema 数据迁移。实现发布前必须删除测试服务器的 SQLite 主文件、WAL 和 SHM，
由重写后的首个 migration 创建新状态。

## 目标

- 从全部 API、SSE、OpenAPI 和客户端合同中移除 Basic Authentication。
- 保留网站 password grant，并使用持久用户当前状态签发默认有效期 8 小时的 Access Token。
- 将密码 PBKDF2-HMAC-SHA256 迭代次数改为 1,000，只用于低频交互式登录和引导同步。
- 允许已登录用户创建、列出和撤销自己的 API Key，完整 Key 只返回一次。
- API Key 绑定创建者，并在每次验证时继承创建者当前角色和启用状态。
- 让 Access Token 与 API Key 共用 `Authorization: Bearer` 传输和受保护资源授权入口，同时保留可区分的凭据类型。
- 保持认证失败关闭、Header-only、固定时间摘要比较、SSE 周期复验和敏感值不泄露边界。

## 非目标

- 不增加 refresh token、Cookie 会话、CSRF Token、JWT、QueryString Token 或浏览器持久会话。
- 不使用 API Key 代替网站登录，也不允许 API Key 创建另一把 API Key。
- 不增加 API Key scopes、Key 自有角色、Key 重新启用、secret 恢复或 secret 轮换；丢失时撤销并重建。
- 不实现完整用户创建、删除、禁用、角色管理或密码管理界面。
- 不兼容或自动升级已有 600,000 次密码摘要和旧 migration journal。
- 不为本地文件读取者增加 SQLite 静态加密；该主体仍按 `NFR-05` 视为受信任运维者。
- 不把临时 PBKDF2 Unity Mono 基准命令保留为正式运维能力。

## 凭据模型

### 用户密码

- `config.json` 在过渡期继续提供固定 `Subject=owner` 的引导用户名和密码。
- 持久用户记录保存当前管理角色；引导 `owner` 固定同步为 `Owner`。本切片不提供角色管理 API，但身份读取和测试必须能够表达 `Owner`、`Admin` 与 `Viewer`，不得在 Web claims 工厂硬编码角色。
- 数据库保存 16 字节随机盐、32 字节 PBKDF2-HMAC-SHA256 摘要和固定迭代值 `1000`，不保存明文密码。
- password grant 是唯一接受用户名和密码的网络入口，并继续按远端地址限制每分钟 20 次、最多 1024 个地址 bucket。
- 用户名不存在、密码错误、用户禁用和 Store 故障保持同一客户端失败语义。
- 迭代值降低的风险由 `NFR-05` 的受信任本地文件边界和低频登录限流共同约束；它不用于高频 API Key 验证。

### 网站 Access Token

- password grant 成功后签发现有不透明 `7dp_` Access Token，默认有效期从 30 分钟改为 8 小时。
- 配置仍允许 5 至 1440 分钟，默认值改为 480 分钟；当前最大值已经覆盖 8 小时，不扩大配置上限。
- 数据库只保存随机 secret 的 SHA-256 摘要、签发时间、到期时间和所属 Subject，最多保留 128 个未到期 Token。
- 浏览器只在应用内存中保存 Token 和到期时间；页面刷新或到期后清除会话并重新登录。
- 用户凭据变化、用户禁用、Token 撤销、到期或容量淘汰后，Token 必须失败。

### API Key

API Key 使用明确独立的格式：

```text
7dp_k_<key-id>_<random-secret>
```

- `key-id` 是不可预测的公开随机标识，用于索引一条 Key 记录；它不是秘密。
- `random-secret` 由至少 32 字节密码学安全随机数编码而成。
- 数据库只保存 secret 的 SHA-256 摘要并使用固定时间比较。高熵随机 secret 不使用 PBKDF2，也不需要额外逐记录盐。
- Key 记录保存创建者 Subject、名称、创建时间、可选到期时间、可选最后使用时间和可选撤销时间；不复制创建者角色。
- 每个用户最多保留 32 把尚未撤销的 Key，包括有效和已过期记录；已撤销记录继续保留为元数据，但不计入创建容量。达到上限时拒绝创建，用户可以先撤销一把 Key 后再创建，不需要删除历史记录。
- 名称 trim 后必须为 1 至 80 个 Unicode 字符；同一用户允许重名，界面同时显示安全标识以供区分。
- 到期时间可省略；提供时必须晚于创建时刻。永久 Key 仍可由用户撤销，且创建者禁用后立即失效。
- `last_used_utc` 只用于用户可见元数据，每把 Key 每小时最多落库一次；更新失败不得把已经成功的只读鉴权改写为失败，也不得记录完整 Key。

## 身份、授权与凭据类型

- Access Token 和 API Key 都通过 `Authorization: Bearer <credential>` 进入同一认证阶段。
- 认证入口先按严格前缀区分凭据类型。以 `7dp_k_` 开头但格式错误的值只能按 API Key 失败，不能回退为 Access Token；其他 `7dp_` 值只按 Access Token 验证。
- 成功身份包含稳定 Subject、当前用户名、当前角色和内部凭据类型 `access_token` 或 `api_key`。凭据类型供授权策略使用，不作为客户端可伪造字段。
- API Key 验证先定位 Key、检查撤销和到期、固定时间比较摘要，再读取创建者当前启用状态和角色。Key 不保存创建时角色，因此降权、升权和禁用都在下一请求生效。
- 现有受保护 API 和 SSE 对两类 Bearer 凭据使用相同角色授权。
- API Key 管理写操作额外要求凭据类型为 `access_token`；即使 API Key 继承 `Owner`，也不能创建另一把 Key。
- 列表和撤销始终按认证 Subject 限定，只允许操作自己的 Key。本切片不提供 Owner 代管其他用户 Key 的入口。
- 无效、格式错误、不存在、已撤销、已过期或创建者禁用统一返回 401；角色不足或凭据类型不允许统一返回 403，不泄露内部状态。

## API 与一次性明文

新增受保护资源：

```text
GET    /api/v1/api-keys
POST   /api/v1/api-keys
DELETE /api/v1/api-keys/{keyId}
```

### 创建

创建请求包含名称和可选到期时间。只有网站 Access Token 可以调用。服务端完成验证、随机生成和持久化后，响应一次性返回：

- `id`
- `name`
- `apiKey`
- `createdAtUtc`
- `expiresAtUtc`

只有本次成功响应包含完整 `apiKey`。持久化失败时不得返回完整 Key；客户端超时或响应丢失时不能恢复同一 secret，只能刷新列表并根据元数据判断是否创建，再撤销或新建。

### 列表

列表只返回当前 Subject 的 Key 元数据：

- `id` 和安全显示前缀
- `name`
- `createdAtUtc`
- `lastUsedAtUtc`
- `expiresAtUtc`
- `status`，取 `active`、`expired` 或 `revoked`

列表不返回完整 Key、secret、secret 摘要、创建者角色副本或数据库内部字段。按创建时间降序、ID 稳定打破并列顺序。

### 撤销

撤销以当前 Subject 和 `keyId` 同时定位，幂等地设置撤销时间。自己的已撤销 Key 重复撤销仍返回成功终态；不存在或属于其他用户的 ID 使用同一不可枚举错误。撤销后新请求立即失败，已经建立的 SSE 最迟在下一凭据复验边界关闭。

## Web 与 OpenAPI 边界

- 删除 Basic middleware、handler、options 和 Basic challenge。携带 Basic Header 的受保护请求与缺少凭据相同，不建立身份。
- OWIN 顺序保留请求关联、Problem Details、password grant 限流、请求 scope、OAuth password grant、Bearer 认证、OpenAPI/Swagger、Web API、SPA fallback 和 StaticFiles。
- 认证限流只需覆盖 password grant；API Key 和 Access Token 验证不执行密码摘要，也不增加 Basic 专用限流分支。
- OpenAPI 删除 Basic security scheme。password grant operation 保留；受保护操作声明 Header Bearer，API Key 管理写操作在描述和错误响应中明确只接受网站 Access Token。
- SSE 继续使用可设置 Authorization Header 的 Fetch 型客户端，接受 Access Token 或 API Key，拒绝 QueryString、Cookie 和 Basic。
- SSE session 保存原始 Bearer 凭据和凭据类型，按现有周期复验对应 Store；撤销、到期、用户禁用或角色不再满足后关闭连接。

## Admin 前端

- 登录页和 Auth Store 继续调用 password grant；默认 8 小时来自服务端响应，不在前端硬编码期限。
- Auth Store 继续只在内存保存 Access Token。401 清除会话并返回登录页，不实现自动刷新。
- 新增独立 API Keys 页面和 Feature，只允许从网站会话进入；导航、页面状态和一次性显示流程遵循[产品设计](../../design.md#api-key-管理)。
- 创建成功对话框拥有完整 Key 的唯一前端引用。关闭后主动清除；Pinia、URL、浏览器存储、通知、错误遥测和日志不得接收该值。
- 复制按钮只使用当前对话框内值并反馈复制成功/失败。页面刷新或重新进入列表无法恢复完整 Key。
- 列表表达 loading、empty、fresh、failed 和 forbidden；撤销表达确认、submitting、succeeded、failed，失败时不乐观伪造已撤销。

## Persistence 与无迁移重置

- 直接重写 `001_Authentication.sql` 的密码迭代约束为精确允许当前设计值，并新增 API Key schema；不创建兼容旧数据的 migration。
- Persistence Adapter 继续独占 SQLite、Dapper、DbUp、连接、事务和摘要实现；Hosting 定义凭据类型中立的 Store 端口和值对象，Web 不直接引用 SQLite。
- API Key 创建必须在随机生成后以单个持久事务写入记录；数据库提交成功前不得向调用方暴露完整 Key。
- 测试服务器部署前必须正常停止 7DTD，然后删除 `data/7dpanel.db`、`data/7dpanel.db-wal` 和 `data/7dpanel.db-shm`。不得在进程运行时删除数据库文件。
- 启动不自动识别、升级或重哈希旧 600,000 次记录。遗留 journal/schema 导致查询、约束或 migration 不匹配时保持失败关闭，并提示运维执行批准的测试数据库重置。
- 删除数据库会撤销所有网站 Access Token、API Key 和当前审计数据。该破坏性操作只适用于用户已确认的开发测试环境，不形成未来发布升级策略。

## 临时基准代码

- 实现稳定后删除 `Pbkdf2BenchmarkConsoleCommand` 和对应测试，不发布该命令。
- 删除只影响临时诊断入口，不删除认证摘要本身的自动化测试。
- Git 不提交真实基准密码、服务端配置、数据库、完整 Key 或发布物。

## 失败与安全语义

- 所有认证 Store、数据库初始化和引导同步失败保持失败关闭，不让受保护 API 匿名运行。
- 完整密码、Access Token、API Key、secret、摘要和 Authorization Header 不进入 API 错误、产品日志、SSE、审计参数、前端资产或版本库。
- API Key 创建响应是唯一允许向已认证创建者返回完整 Key 的产品边界；响应使用 `Cache-Control: no-store`。
- API Key 最后使用时间写入采用节流和 fail-open，只影响元数据新鲜度；摘要验证、用户状态或授权读取失败仍必须失败关闭。
- Access Token/API Key 的错误不区分不存在、格式错误、摘要错误、撤销、过期或用户禁用；API Key 列表不会成为跨用户枚举入口。
- 当前默认 HTTP 与已知引导凭据风险继续由 `NFR-04` 明确接受；本切片不宣称明文网络传输已经安全。

## 验证标准

- Basic Header 在 REST、SSE 和 OpenAPI 中均不再建立身份或成为受支持 scheme；无 Basic challenge。
- password grant 正确验证持久用户，错误登录受每地址限流；新数据库密码记录使用 PBKDF2-HMAC-SHA256 1,000 次。
- 默认配置和缺失配置使用 480 分钟 Access Token；显式 5 至 1440 分钟配置保持有效，过期、撤销、用户禁用和凭据轮换语义不回归。
- API Key 创建只接受 Access Token，完整 Key 只在一次成功响应出现；数据库、列表、日志、错误和前端持久状态扫描均找不到完整 Key。
- API Key 有效、格式错误、不存在、过期、撤销、容量满、名称/到期时间无效和并发撤销均有确定结果。
- API Key 随创建者当前启用状态和角色变化；API Key 不能创建 Key，用户不能列出或撤销其他用户的 Key。
- `last_used_utc` 节流不产生逐请求 SQLite 写入，写入失败不影响已通过的请求，身份/授权读取失败仍失败关闭。
- Access Token 和 API Key 都能调用角色允许的 REST 与 SSE；撤销或失效后新请求立即 401，现有 SSE 在复验边界关闭。
- Admin 组件测试覆盖列表、一次性显示、复制、关闭清除、刷新不可恢复、撤销确认和 401/403；浏览器 E2E 覆盖登录、创建、复制、使用、撤销和会话过期重登。
- 后端 Release Rebuild、全量自动化、Admin lint/typecheck/unit/build 和适用 E2E 全部通过。
- Windows `v3.0.1-b4` 真实进程重置测试数据库后验证 password grant、8 小时默认 Token、API Key REST/SSE、撤销、Basic 拒绝和高频 API 不再出现约 4 秒密码摘要延迟。

## 文档影响与提升

- [产品需求](../../PRD.md)拥有无 Basic、8 小时网站 Token、API Key 一次性显示与当前角色继承的产品合同。
- [产品设计](../../design.md)拥有 API Keys 页面、会话过期和一次性 Key 的界面流程。
- 本规格批准后先创建一份实现计划，不在计划中重新设计认证模型。
- 实现和验证完成后，把当前 OWIN 管道、Store、schema、Admin Feature、依赖和真实进程证据提升到[系统架构](../../architecture.md)与[测试策略](../../test.md)，并清除其中已失效的 Basic/30 分钟当前事实。
- [Admin 前端目标蓝图](../../architecture/admin-frontend-target-blueprint.md)中的 Basic/会话描述需要随实现同步；在代码完成前不把目标规格冒充当前实现。
- 发布后才把用户可见变化加入 `CHANGELOG.md`；当前设计批准不等于已经发布。