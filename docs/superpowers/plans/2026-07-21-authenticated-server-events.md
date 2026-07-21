---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-20-authenticated-server-events-design.md
last_updated: "2026-07-21"
---

# 认证服务器事件与统一错误实施计划

> 后续决策（2026-07-21）：当前[产品需求](../../PRD.md)已批准框架搭建阶段使用已知默认配置凭据和明文 HTTP，并明确未来不采用 Cookie 认证。本计划保留已经执行的历史任务记录；其中旧默认值和 SQLite/Cookie 目标不再作为当前实现或未来方向。

## 主规格

本计划只实现已批准的[认证服务器事件与统一错误设计规格](../specs/2026-07-20-authenticated-server-events-design.md)。产品追踪使用 `CAP-01`、`CAP-02`、`CAP-05`、`NFR-02` 和 `NFR-04`；实施发现不得在本计划中改写规格决策。

## 实施约束

- 保留工作区中已有的日志、Microsoft DI/request scope、配置和文档修改，不回退或重写来源不明的内容。
- 每个行为先写失败测试，再做最小实现；局部测试稳定后才运行后端聚合门禁。
- 旧 `7dtd-serveradmin` 只提供行为证据，不复制其 `MD5`/固定 IV protector、QueryString Token、通配 CORS、默认明文 OAuth 或无界认证尝试。
- 不创建通用领域 Event Bus、反射事件注册表、refresh token、JWT、Cookie 会话、SQLite 身份或前端登录实现。
- 不执行 Git commit、push、reset 或 revert。

## 任务 1：建立统一 Problem Details 与关联标识

修改范围：

- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/Errors/`
- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OwinStartup.cs`
- `backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs`
- 新增针对错误工厂、响应规范化和关联 middleware 的测试文件。

步骤：

- [x] 先覆盖 Path-only `instance`、稳定 `code`、body/Header 同一 traceId、非法传入 traceId 替换和 `application/problem+json`。
- [x] 覆盖未知 API 404、无效请求 400、未处理 API 异常 500，确认健康成功响应保持精确三字段。
- [x] 实现 `RequestCorrelationMiddleware`、Problem Details DTO/工厂、Web API response handler 和 exception handler。
- [x] 确认已有合法 Problem Details、OAuth response 和已开始的 SSE body 不被二次包装。
- [x] 运行相关错误与 Katana 集成测试。

## 任务 2：增加过渡认证配置与依赖

修改范围：

- `backend/src/Bootstrap/LSTY.SevenDPanel/Configuration/PanelHostConfig.cs`
- `backend/src/Bootstrap/LSTY.SevenDPanel/Configuration/PanelHostConfigurationLoader.cs`
- `backend/src/Bootstrap/LSTY.SevenDPanel/config.example.json`
- `backend/src/Runtime/LSTY.SevenDPanel.Hosting/PanelHostOptions.cs`
- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/LSTY.SevenDPanel.Adapters.Web.csproj`
- `backend/tests/LSTY.SevenDPanel.Tests/PanelHostOptionsTests.cs`

步骤：

- [x] 先覆盖认证默认关闭、无默认凭据、enabled 配置校验、用户名/密码空白语义、Token 生命周期 5 到 1440 分钟和失败关闭。
- [x] 增加不可变 `PanelAuthenticationOptions`，确保日志与 `ToString()` 不输出秘密。
- [x] 把嵌套 JSON 配置映射到 Hosting options，并保持健康 Host 在认证配置无效时可启动。
- [x] 安装 `Microsoft.Owin.Security.OAuth 4.2.3`，检查传递依赖与现有 Katana `4.2.3` 对齐。
- [x] 更新模板一致性测试并运行配置定向测试。

## 任务 3：实现 Basic、Bearer 和认证限流

修改范围：

- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/Authentication/`
- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OwinStartup.cs`
- `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- 新增认证单元测试和 Katana 集成测试。

步骤：

- [x] 先覆盖 Basic 非法 Base64、首个冒号分隔、密码内冒号、固定耗时验证、HTTPS 默认限制、临时 `Owner` claims 和双 challenge。
- [x] 实现 Basic `AuthenticationMiddleware<TOptions>` 与 handler，不区分账号不存在和密码错误。
- [x] 先覆盖每地址每分钟 20 次、`Retry-After`、最多 1024 bucket、过期清理和失败关闭，再实现 `AuthenticationRateLimitMiddleware`。
- [x] 先覆盖 password grant、随机 256 bit Token、128 个未到期 Token 上限、最旧 Token 撤销、到期/停止/重启失效和拒绝 QueryString Token。
- [x] 实现共享进程内 `AccessTokenProvider`，接入 OAuth authorization server 与 active bearer middleware；不实现 refresh token。
- [x] 确认 OAuth `invalid_grant` 保持协议响应，受保护 API 的 401/403 使用 Problem Details。
- [x] 运行认证与 OWIN 定向测试。

## 任务 4：把日志流收敛为命名服务器事件流

修改范围：

- `backend/src/Runtime/LSTY.SevenDPanel.Hosting/ServerEvents/`
- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/ConsoleLogs/`
- `backend/src/Runtime/LSTY.SevenDPanel.Hosting/ModHost.cs`
- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/ConsoleLogs/ConsoleLogService.cs`
- 现有日志窗口、Hub、服务和组合运行时测试。

步骤：

- [x] 先扩展测试，使同一有界窗口按一个单调 sequence 保存 `console-log`、`game-ready` 和 `server-stopping`。
- [x] 定义只允许批准 event name 和不可变 JSON-safe payload 的最小 Hosting 契约；不接受任意反射事件或命令。
- [x] 泛化当前窗口与 Hub，保持容量、replay、gap、每客户端 mailbox、总订阅上限和慢客户端隔离。
- [x] 保持日志回调只执行一次非等待投递；sequence 和广播仍在回调线程之外。
- [x] `MarkGameReady` 只发布一次 `game-ready`；停止先拒绝新生产，把 stopping marker 排到已接受日志之后，限时排空后 complete Hub。
- [x] 提供只读 Host/Game readiness snapshot 边界，避免 Web Adapter 依赖可变 `IModRuntime`。
- [x] 运行日志服务、窗口、Hub、组合运行时和生命周期测试。

## 任务 5：实现认证生产 SSE

修改范围：

- 将开发 `ConsoleLogsController`/`ConsoleLogSseSession` 收敛为生产事件 Controller/Session。
- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OwinStartup.cs`
- `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- `backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs`
- `backend/tests/LSTY.SevenDPanel.Tests/DependencyInjectionTests.cs`

步骤：

- [x] 先覆盖匿名、错误 Basic、错误/过期 Bearer 的 401 Problem Details 和 `WWW-Authenticate`，以及权限不足 403。
- [x] 先覆盖无效 `Last-Event-ID` 的 400、订阅上限的建流前 503、无 BOM UTF-8、Cache Header 和 Content-Type。
- [x] 实现 `GET /api/v1/events/stream`，在创建订阅和读取 Welcome 前完成 `[Authorize]`。
- [x] 连接顺序固定为 reserve subscription、Welcome、replay、live、heartbeat；按 sequence 去重 replay/live 竞态。
- [x] Welcome 精确包含 product、version、hostState、gameReadiness 和 `connectedAtUtc`，且不推进事件游标。
- [x] 保留 gap、取消、I/O 失败、Hub complete、mailbox overflow 和 request scope 一次释放。
- [x] 删除开发匿名路由、配置开关和相关警告，迁移测试到生产契约。
- [x] 运行完整 OWIN/Web API 与 DI scope 集成测试。

## 任务 6：发布边界、文档与完成验证

修改范围：

- `backend/scripts/Publish-Mod.ps1`
- `backend/README.md`
- `docs/architecture.md`
- `docs/architecture/backend-target-blueprint.md`
- `docs/architecture/admin-frontend-target-blueprint.md`
- `docs/test.md`
- 本计划状态与完成记录。

步骤：

- [x] 发布脚本要求 OAuth DLL，并保持游戏提供程序集排除规则。
- [x] README 记录配置字段、无默认凭据、HTTPS/受控本机代理要求、token 与 Basic smoke 入口，不复制仓库聚合命令。
- [x] 当前架构只提升已实现并验证的组件、接口、配置、依赖、安全边界和残余风险。
- [x] 后端 Target 蓝图记录临时配置身份向 SQLite/Cookie 身份的替换边界；前端 Target 蓝图把 Bearer 阶段 SSE 调整为 Fetch 型客户端条件。
- [x] 测试策略更新认证、Problem Details、生产 SSE、发布清单和真实进程门禁。
- [x] 运行 `dotnet build backend/7DPanel.sln --configuration Release --no-restore --target:Rebuild`。
- [x] 运行 `dotnet test backend/7DPanel.sln --configuration Release --no-build`。
- [x] 运行文档链接、占位符、语言和 `git diff --check` 审查。
- [x] 执行发布和 Windows `v3.0.1-b4` 真实进程 smoke：临时配置、启动、健康、token、Basic SSE、Bearer SSE、`GameStartDone`、日志、关服、端口释放，并逐字节恢复服主配置。
- [x] 只有全部适用证据通过后才把计划任务标记完成；不创建 Git 提交。

## 完成记录

- 2026-07-21 最终 Release Rebuild 为零警告，后端全量 118 项 xUnit 自动化通过。
- 首次真实进程 smoke 因 Katana self-host 默认创建 DPAPI data protector 而失败；修复为显式拒绝自包含票据，并用抛错 data-protection provider 建立回归测试后，最终二进制完成 Windows `v3.0.1-b4` 全流程。
- 最终流程验证健康、password grant、Basic/Bearer、Welcome、`console-log`、`game-ready`、`server-stopping`、Chrome Admin、正常关服和端口释放；远程日志无托管加载、ModEvent 或 OWIN 启动异常。
- 临时凭据和本地备份已经删除，服主 `config.json` 恢复后与原始 SHA-256 一致；未执行 Git 提交。
