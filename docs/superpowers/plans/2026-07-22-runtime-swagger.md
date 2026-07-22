---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-22-runtime-swagger-design.md
last_updated: "2026-07-22"
---

# 公开运行时 OpenAPI 与 Swagger UI 实施计划

## 主规格与完成边界

本计划只实现已批准的[公开运行时 OpenAPI 与 Swagger UI 设计规格](../specs/2026-07-22-runtime-swagger-design.md)。能力边界保持为公开 `GET /swagger`、公开 `GET /swagger/v1/swagger.json`、Web API 2 Controller 运行时反射文档和手工 OWIN token operation；不改变现有 API、认证、SSE 或错误响应行为。

完成时必须同时具备 Katana 行为证据、项目依赖规则、实际 publish 布局证据和 Current 文档同步。Windows `v3.0.1-b4` 真实 7DTD Mono smoke 不在本计划默认执行范围；没有该证据时，不得宣称真实游戏进程兼容已经通过。

## 实施约束

- 所有修改只发生在 `feat/runtime-swagger` 的独立 worktree，不修改或覆盖主工作区中的动态控制台文档变更。
- 严格执行 RED、确认失败原因、最小 GREEN、确认通过；局部切片稳定后只运行一次后端聚合门禁。
- Web Adapter 只新增 `NSwag.AspNet.Owin` `14.7.1`；不新增 `NSwag.Annotations`，不在 Controller 上添加 NSwag 专属 Attribute。
- 使用 NSwag OWIN API 分别注册 `UseOpenApi` JSON middleware 和 UI-only `UseSwaggerUi`；通过 JSON generator settings 的 `DocumentProcessors` 和 `OperationProcessors` 注册集中处理器，并把 UI 固定指向 JSON 路径。
- 保持 `Newtonsoft.Json.dll` 为游戏提供程序集，不把另一个版本复制到 Mod 发布物。
- 不增加 Swagger 开关、访问认证、限流、客户端生成、Admin 内嵌页面、Cookie、Token 持久化插件或 7DTD 自带 Webserver 文档合并。
- 测试不得断言未实现状态码或未批准字段；普通 API 的 Problem Details 与 token endpoint 的 OAuth JSON 必须分开描述。
- 不执行 Git commit、push、merge、reset、revert 或分支删除；这些操作需要用户另行明确授权。

## 任务 1：建立公开 Swagger UI 与 OpenAPI JSON

修改范围：

- `backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs`
- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/LSTY.SevenDPanel.Adapters.Web.csproj`
- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OwinStartup.cs`
- 新增 `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OpenApi/OpenApiConfiguration.cs`

步骤：

- [x] RED：在真实 Katana Host 中增加 `Anonymous_openapi_document_is_public`，断言 `GET /swagger/v1/swagger.json` 返回 200、JSON Content-Type、`openapi` 为 3.x、标题为 `7DPanel API`、文档版本为 `v1`，并至少包含 `/health` 与 `/api/v1/health`。先运行：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~OwinWebHostTests.Anonymous_openapi_document_is_public"
  ```

  预期因当前请求进入 Web API/静态资源边界并返回 404 而失败；若不是该原因，先修正测试宿主或断言，不写生产实现。

- [x] GREEN：只在 Web Adapter 项目加入 `<PackageReference Include="NSwag.AspNet.Owin" Version="14.7.1" />`，执行 restore；新增内部 `OpenApiConfiguration.Configure(IAppBuilder)`，由 `OwinStartup` 在 Basic/Bearer middleware 之后、`UseWebApi` 之前分别注册 `UseOpenApi(...)` 与 UI-only `UseSwaggerUi(...)`。显式设置 JSON 路径、UI 路径、固定 UI document route、OpenAPI 3 schema、标题、`v1` 和 `ProductInfo.Version`。

- [x] 重新构建测试项目并运行同一测试，确认最小 JSON 切片通过：

  ```powershell
  dotnet build backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-restore -p:SevenDaysReferenceRoot="$env:SEVENDPANEL_REFERENCE_ROOT"
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~OwinWebHostTests.Anonymous_openapi_document_is_public"
  ```

- [x] RED：增加 `Anonymous_swagger_ui_uses_fixed_document_path`，断言 `GET /swagger` 返回 200 HTML，最终页面引用 `/swagger/v1/swagger.json`；增加带真实 Admin asset root 的 `/swagger` 与 `/swagger/missing` 测试，证明请求不返回 Admin `index.html`。

- [x] GREEN：在 `ShouldUseSpaFallback` 中显式排除 `/swagger` 与 `/swagger/*`；保持 `/api`、`/assets` 和其他无扩展名前端路由规则不变。运行本任务全部 Swagger Katana 测试，并回归现有静态资源与 SPA fallback 测试。

## 任务 2：补齐完整 OpenAPI 契约

修改范围：

- `backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs`
- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OpenApi/OpenApiConfiguration.cs`
- 新增 `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OpenApi/PanelOpenApiDocumentProcessor.cs`
- 新增 `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OpenApi/PanelOpenApiOperationProcessor.cs`

处理器接口：

- `PanelOpenApiDocumentProcessor : IDocumentProcessor` 只处理全局 metadata、security schemes 和 Controller 反射无法发现的 token path。
- `PanelOpenApiOperationProcessor : IOperationProcessor` 使用 `OperationProcessorContext` 的 Controller/action attributes 与稳定 path/method 处理 security、SSE 和实际错误响应；不得依赖反射枚举顺序。
- 两个处理器都是 Web Adapter 内部无状态类型，不解析运行时响应，也不依赖 Application、SevenDays 或 SQLite 实现。

步骤：

- [x] RED：增加一个完整文档结构测试，使用 `JObject` 检查以下批准路由及方法全部存在：`GET /health`、`GET /api/v1/health`、`GET /api/v1/events/stream`、`POST /api/v1/console/commands`、`GET /api/v1/players/online`、`POST /api/v1/players/{entityId}/kick`、`POST /api/v1/auth/token`。当前应只因 token path 缺失而失败。

- [x] GREEN：注册 `PanelOpenApiDocumentProcessor`，手工加入 token operation。请求 body 必须是 `application/x-www-form-urlencoded`，包含必填 `grant_type`、`username`、`password`，其中 `grant_type` 限定为 `password`；成功响应只描述 `access_token`、`token_type`、`expires_in`，协议错误保持 OAuth JSON，operation 不附加 security requirement，示例不得含真实凭据或 Token。

- [x] RED：增加 security 测试，断言 `components.securitySchemes` 同时声明 HTTP `basic` 的 `Basic` 和 HTTP `bearer` 的 `Bearer`；健康与 token operation 无 security；受 `[Authorize]` Controller/action 具有与实现一致的替代认证 requirements，角色限制仍由服务端执行。

- [x] GREEN：在 document processor 中加入两个 scheme，在 operation processor 中根据 Web API 2 `[Authorize]`/角色 metadata 添加 security requirements。不得把 Swagger UI 的授权状态描述为角色授权证明，也不得给公开 operation 增加认证要求。

- [x] RED：增加 SSE 测试，断言 `/api/v1/events/stream` 声明可选 `Last-Event-ID` Header、200 `text/event-stream`、Basic/Bearer 两种选择，并包含长连接命名事件流及“响应开始后不能改写为 JSON”的说明。

- [x] GREEN：只对稳定 path/method 的 SSE operation 补充上述 metadata；不要把 SSE 200 schema 生成为 JSON 数组或普通 DTO。

- [x] RED：按当前 Controller 和外层 handler 的真实行为断言普通 API 的已实现错误响应引用统一 `ApiProblemDetails` schema，覆盖实际出现的 400、401、403、404、409、429、500、503 子集；断言 token 错误不引用 Problem Details，schema 不包含异常、堆栈、数据库路径、玩家 IP、凭据或 Token 字段。

- [x] GREEN：operation processor 以稳定 route/method 表补充各 operation 实际状态码，复用一个 camelCase Problem Details schema；不虚构响应，不改变 `ApiProblemDetailsHandler`、Controller 或 OAuth middleware 行为。

- [x] 增加副作用断言：使用现有测试 Gateway、在线玩家查询、踢出动作和审计替身请求 JSON 与 UI，确认所有调用计数保持为零。运行：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~OwinWebHostTests.Openapi|FullyQualifiedName~OwinWebHostTests.Swagger"
  ```

  若 xUnit 过滤器不能按预期组合，使用各新增测试的完整 `FullyQualifiedName` 分别执行，不扩大到全量测试来掩盖局部失败。

## 任务 3：锁定依赖方向与发布布局

修改范围：

- `backend/tests/LSTY.SevenDPanel.Tests/DependencyRulesTests.cs`
- `backend/scripts/Publish-Mod.ps1`

步骤：

- [x] RED：新增依赖规则测试，解析所有产品 `.csproj`，断言只有 `LSTY.SevenDPanel.Adapters.Web.csproj` 直接引用 `NSwag.AspNet.Owin` `14.7.1`；任何项目都不引用 `NSwag.Annotations`；Application、Hosting、SevenDays、Persistence 和 Bootstrap 不直接引用名称以 `NSwag`、`NJsonSchema` 或 `Namotion` 开头的包。

- [x] GREEN：如 package 接线已符合规则，此测试应在不改生产依赖的情况下通过；若 restore 暴露直接包需求，先确认是否有 Web Adapter 的非测试运行时消费者，不能为测试便利扩散依赖。

- [x] 在 package restore 后执行一次隔离 publish 到临时目录，并列出实际新增托管 DLL：

  ```powershell
  dotnet publish backend/src/Bootstrap/LSTY.SevenDPanel/LSTY.SevenDPanel.csproj --configuration Release --no-restore -p:PublishProfile=FolderProfile -p:PublishDir='.artifacts/swagger-publish/' -p:SevenDaysReferenceRoot="$env:SEVENDPANEL_REFERENCE_ROOT"
  ```

  只把 `14.7.1` 实际运行时闭包中出现的 NSwag、NJsonSchema、Namotion 程序集写入发布清单；不凭包名猜测，不把编译期或无运行时消费者的资产加入 required list。

- [x] RED：扩展 `Publish_script_enforces_runtime_dependency_boundary`，要求刚刚由 publish 证实的精确文件名，同时继续断言 `Newtonsoft.Json.dll` 在 forbidden list 且不在 required list。先运行该单测，预期因 `Publish-Mod.ps1` 尚未列出新增 DLL 而失败。

- [x] GREEN：更新 `Publish-Mod.ps1` 的 `$requiredNames`；不得弱化 forbidden 清理、SQLite native 布局或 Admin asset 校验。运行依赖规则测试，并执行 owning publish script 验证真实布局。脚本若要求 Admin 构建前置条件，按 `backend/scripts/README.md` 的现有入口提供，而不是绕过发布脚本断言。

## 任务 4：同步 Current 文档并完成验证

修改范围：

- `backend/README.md`
- `docs/architecture.md`
- `docs/test.md`
- 本计划的任务状态与完成记录

明确不修改：

- `docs/PRD.md`
- `docs/design.md`
- `docs/architecture/backend-target-blueprint.md`
- `docs/architecture/admin-frontend-target-blueprint.md`
- 根 `README.md`
- `CHANGELOG.md`

上述文件只有在实施事实证明产品能力、交互、目标边界、仓库聚合命令或已发布内容发生变化时才重新评估；本规格当前明确这些边界不变。

步骤：

- [x] `backend/README.md` 记录两个公开路径、运行时反射范围、token endpoint 手工补充、无 Swagger 访问控制和不覆盖游戏 `Newtonsoft.Json.dll`；不复制根 README 的聚合命令。

- [x] `docs/architecture.md` 只提升已经实现并验证的 Web Adapter OpenAPI 配置单元、OWIN pipeline 顺序、公开披露边界、NSwag/NJsonSchema/Namotion 依赖和游戏提供 JSON 程序集边界。

- [x] `docs/test.md` 记录 Katana 文档契约、架构依赖、publish manifest 门禁以及真实 `v3.0.1-b4` Mono smoke 的证据状态。未执行真实游戏 smoke 时明确保留缺口。

- [x] 运行 Release Rebuild：

  ```powershell
  dotnet build backend/7DPanel.sln --configuration Release --no-restore --target:Rebuild -p:SevenDaysReferenceRoot="$env:SEVENDPANEL_REFERENCE_ROOT"
  ```

- [x] 运行后端全量测试：

  ```powershell
  dotnet test backend/7DPanel.sln --configuration Release --no-build
  ```

- [x] 运行最终 publish 脚本，检查公开文档所需程序集、Swagger UI 嵌入资源和 Admin 静态资源均在预期布局，且禁止程序集不存在。不得用单独 `dotnet publish` 代替最终脚本门禁。

- [x] 审核全部改动：确认无占位标记、占位路径、真实凭据、Token、异常堆栈、机器本地发布目录或 NSwag Attribute；运行 `git diff --check`，并检查主规格每项完成定义都有对应自动化或明确证据缺口。

- [x] 只有上述适用证据全部通过后才勾选任务并写完成记录。若未执行真实 7DTD smoke，完成记录必须明确“Windows Katana 与 publish 已验证，Unity Mono 加载未验证”；不创建 Git 提交。

## 完成记录

2026-07-22：实现与文档同步已完成。后续契约加固集中 OpenAPI 路由常量与 Problem Details schema/response 构造，锁定 token operation 的 tag、说明、必填字段、无 refresh token、500 Problem Details、大小写等价 path 冲突和重复注册失败，并精确检查各 operation 响应状态码集合。隔离 Release Rebuild 成功，后端全量 231 项测试全部通过；Admin 生产构建和隔离临时目录中的 `Publish-Mod.ps1` 门禁通过，发布脚本确认 11 个 NSwag/NJsonSchema/Namotion/System.Text.Json 运行时程序集存在并移除 `Newtonsoft.Json.dll`。Windows Katana 与 publish 已验证，Unity Mono 加载未验证；未启动真实 7DTD 服务端，未创建 Git 提交。