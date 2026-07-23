---
state: Current
document_role: Change Record
last_updated: "2026-07-22"
---

# 公开运行时 OpenAPI 与 Swagger UI 设计规格

> 本规格描述尚未实现的 Web Adapter 文档切片，不是当前实现证据。当前系统边界、接口与验证事实分别以[系统架构](../../architecture.md)和[测试策略](../../test.md)为准。

## 目标与范围

本切片为当前 Katana OWIN 与 ASP.NET Web API 2 宿主增加运行时 OpenAPI 3 JSON 和 Swagger UI，使开发者、服主及集成工具可以检查当前 HTTP 契约，并从同一页面试调用公开和受保护 API。

本切片包含：

- `GET /swagger` 的公开 Swagger UI。
- `GET /swagger/v1/swagger.json` 的公开 OpenAPI 3 JSON。
- Web API 2 Controller 路由的运行时反射生成。
- OWIN OAuth middleware 所有的 `POST /api/v1/auth/token` 手工 operation。
- Basic 与 Bearer security scheme，以及受保护 operation 的 security requirement。
- SSE、统一 Problem Details、产品标题与版本等无法仅靠签名准确推断的契约补充。
- Katana 集成、程序集依赖和 Mod 发布清单验证。

本切片不包含：

- `NSwag.Annotations` 或散布在 Controller 上的 NSwag 专属 Attribute。
- OpenAPI 客户端代码生成、Admin 内嵌 API 文档页面或离线文档发布。
- Swagger 访问配置开关、认证、授权、限流或 IP 白名单。
- 把 7DTD 自带 Webserver 的 OpenAPI 文档合并到 7DPanel 文档。
- 改变现有 API 路由、认证协议、响应模型或产品能力。

## 方案与依赖

Web Adapter 显式引用 `NSwag.AspNet.Owin` `14.7.1`。该版本目标为 `.NET Framework 4.6.2+`，与当前 `net48` 编译目标兼容。

第一版不显式引用 `NSwag.Annotations`。路由、HTTP 方法、参数和 DTO 由 Web API 2 Attribute 与类型反射生成；特殊契约由 Web Adapter 内聚的 document/operation processors 补充。只有未来出现无法通过现有类型、标准 Web API Attribute 或集中 processor 表达的逐 action 元数据时，才重新评估 `[OpenApiIgnore]`、`[OpenApiOperation]` 或 `[OpenApiTags]`。

选择运行时生成而不是静态构建产物，原因是当前 Controller 与 OWIN middleware 同进程部署，运行时文档可以直接反映实际发布程序集。代价是生产 Mod 增加 NSwag、NJsonSchema、Namotion.Reflection 及其传递依赖；因此发布清单和真实 Mono 加载证据属于本切片的必要边界。

## OWIN 管线与公开访问

`OwinStartup` 继续拥有 HTTP pipeline。顺序保持为：

```text
RequestCorrelation
-> ProblemDetails boundary
-> authentication rate limit when enabled
-> request scope
-> OAuth token endpoint
-> Basic authentication
-> Bearer authentication
-> public Swagger/OpenAPI middleware
-> Web API
-> SPA fallback
-> Admin static files
```

Swagger middleware 位于认证 middleware 之后，但不要求身份。没有 Authorization Header 的请求可以直接访问 `/swagger` 和 `/swagger/v1/swagger.json`；携带无效凭据时仍沿用现有认证 middleware 的失败关闭行为，不能为 Swagger 创建第二套认证语义。

`ShouldUseSpaFallback` 显式排除 `/swagger` 和 `/swagger/*`。Swagger middleware 未处理的路径不得返回 Admin `index.html`。`/api`、`/assets` 和现有 fallback 规则保持不变。

OpenAPI 与 UI 响应经过现有 request correlation 和外层异常边界。生成失败不得泄露异常、程序集路径或服务器文件系统；尚未开始的响应进入统一 500 Problem Details，已经开始的 UI/JSON 响应只记录 trace id 并结束。

## 文档生成职责

新增一个 Web Adapter 内部配置单元，集中拥有以下职责：

- 指定 Controller assembly、OpenAPI 3 schema、文档标题 `7DPanel API`、文档版本 `v1` 和当前 `ProductInfo.Version`。
- 配置固定 UI 与 JSON 路径，不读取请求参数决定程序集或文件路径。
- 注册文档与 operation processors。
- 不解析运行时 Controller 响应，不依赖 Application、SevenDays 或 SQLite 实现。

Web API 2 Controller 自动生成以下当前路由：

- `GET /health`
- `GET /api/v1/health`
- `GET /api/v1/events/stream`
- `POST /api/v1/console/commands`
- `GET /api/v1/players/online`
- `POST /api/v1/players/{entityId}/kick`

processor 必须使用稳定 route 与 operation 标识，不依赖反射枚举顺序。

## OWIN Token 契约

`POST /api/v1/auth/token` 由 `OAuthAuthorizationServerMiddleware` 消费，不属于 Web API Controller，必须由 document processor 手工加入。

请求为 `application/x-www-form-urlencoded`：

| 字段 | 要求 |
|---|---|
| `grant_type` | 必填，固定为 `password` |
| `username` | 必填，当前由 SQLite 引导 Owner 凭据校验 |
| `password` | 必填，不写入日志或示例真实值 |

成功响应描述当前 OAuth JSON 字段，包括 `access_token`、`token_type` 和 `expires_in`；示例只使用占位符。协议错误继续描述为 OAuth JSON，而不是 Problem Details。该 operation 是公开端点，不附加 Basic 或 Bearer security requirement。

## 认证与安全描述

文档声明：

- `Basic`：HTTP Basic，适用于当前认证 SSE 建连。
- `Bearer`：HTTP Bearer，适用于当前受保护 API 和 SSE。

processor 根据 Web API 2 的 `[Authorize]` 与角色元数据给 operation 增加 security requirement。文档只表达“满足列出的任一认证方案后仍需服务端角色授权”，不能把 Swagger UI 的 `Authorize` 状态描述为权限证明。

Swagger UI 不保存产品自定义 Token，不增加 Cookie、localStorage 插件或 refresh token。NSwag UI 自身的浏览器状态属于上游 Swagger UI 默认行为；本切片不把 Token 注入 URL、OpenAPI JSON、服务端日志或示例。

公开 Swagger 会暴露所有已记录路径、字段和批准错误码，这是用户明确批准的部署边界。文档不得包含数据库路径、玩家 IP、原始异常、堆栈、默认真实凭据、访问 Token 或未批准的 DTO 属性。

## SSE 与 Problem Details

`GET /api/v1/events/stream` 必须描述：

- 成功 Content-Type 为 `text/event-stream`。
- 支持 `Last-Event-ID` Header。
- 响应为长连接命名事件流，不是可缓冲 JSON 数组。
- Basic 或 Bearer 均可认证。
- 建流前错误使用 Problem Details；响应开始后的错误不能改写为 JSON。

普通 Web API operation 的错误响应引用统一 Problem Details schema，至少覆盖实际 Controller 声明或映射的 400、401、403、404、409、429、500 和 503。processor 只补充文档元数据，不能虚构实现不存在的状态码或把 OAuth token 错误改写为 Problem Details。

## 发布与兼容性

`dotnet publish` 必须携带运行时生成所需的实际依赖。发布脚本在依赖解析后校验至少包含直接和传递的 NSwag/NJsonSchema/Namotion 程序集；精确文件名以 `14.7.1` restore 和 publish 输出为证据写入，不提前猜测或复制无运行时消费者的包。

继续禁止发布游戏提供的 `Newtonsoft.Json.dll`。新增依赖必须与游戏提供的 JSON 程序集及当前 binding redirect 策略兼容；不得通过复制另一个 Newtonsoft.Json 版本覆盖游戏文件。

本地 `net48` build、Katana 测试和 publish 只能证明编译、托管宿主及布局。Windows `v3.0.1-b4` 真实 7DTD Mono 进程仍需验证所有新增程序集加载、Swagger JSON、UI 资源和正常关服；没有执行该 smoke 时必须在当前架构与测试文档中保留明确缺口。

## 验证

### Katana 集成

- 匿名 `GET /swagger/v1/swagger.json` 返回 200、OpenAPI JSON Content-Type 和 `openapi` 3.x 标识。
- 匿名 `GET /swagger` 返回 200 HTML，并引用固定 JSON 路径。
- JSON 包含所有批准 Controller 路由和手工 token route。
- token operation 使用 form-urlencoded，且没有 security requirement。
- 受保护 operation 包含 Basic/Bearer 中与实现一致的 requirement；健康和 Swagger 端点不被错误标记。
- SSE 包含 `text/event-stream`、`Last-Event-ID` 与长连接说明。
- 主要请求、成功响应和 Problem Details schema 只包含批准字段。
- `/swagger` 与 `/swagger/*` 不进入 SPA fallback；未知路径保持明确 404。
- Swagger 请求不调用游戏 Gateway、玩家动作或审计端口。

### 架构与发布

- Web Adapter 是唯一直接引用 `NSwag.AspNet.Owin` 的产品项目。
- Application、Hosting、SevenDays 与 Persistence 不引用 NSwag、NJsonSchema 或 Namotion。
- 不新增 `NSwag.Annotations`，Controller 不出现 NSwag 专属 Attribute。
- Release Rebuild 和后端全量测试通过。
- `dotnet publish` 与发布脚本清单验证通过，且不发布禁止程序集。
- 未执行真实 7DTD smoke 时，不宣称 Mono 运行兼容已经通过。

## 文档影响

- 该能力不改变产品目标或 API 行为，[产品需求](../../PRD.md)保持不变。
- 实现并验证后，在[系统架构](../../architecture.md)记录公开运行时文档边界、依赖与管线顺序。
- 在[测试策略](../../test.md)记录 Katana、发布清单和真实 Mono 证据状态。
- 若新增精确模块命令或公开路径说明，在最近的 `backend/README.md` 记录；根 README 只在仓库聚合入口变化时更新。
- 后端目标蓝图不因当前 Web Adapter 文档 middleware 改变；`CHANGELOG.md` 只在该能力作为发布内容交付时更新。

## 完成定义

- 两个公开 Swagger 端点在真实 Katana Host 中可访问。
- OpenAPI 3 JSON 覆盖 Controller 与 OWIN token 完整契约，并正确描述认证、SSE 与 Problem Details。
- 未引入 `NSwag.Annotations`，没有改变现有 API 或认证行为。
- 发布物包含精确运行时依赖且不覆盖游戏 Newtonsoft.Json。
- 自动化、发布清单与当前文档同步；真实 Mono 未验证时保留证据缺口。