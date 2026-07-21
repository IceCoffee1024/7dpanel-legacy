---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-21-persistent-authentication-design.md
last_updated: "2026-07-21"
---

# SQLite 持久认证实施计划

> 追溯说明：本计划在提交 `06a83e6 feat(backend): persist authentication state` 完成后补录。全部复选框记录该提交已经实施的工作，不是当前待办，也不证明对应规格曾在实施前获得批准。当前事实和验证门禁以[系统架构](../../architecture.md)与[测试策略](../../test.md)为准。

## 主规格

本计划只记录[SQLite 持久认证设计规格](../specs/2026-07-21-persistent-authentication-design.md)所描述的切片，追踪 `CAP-05`、`NFR-01`、`NFR-02` 和 `NFR-04`。前序认证事件切片中的配置身份和进程内 Token 是被替换对象，不作为第二套并行认证后端保留。

## 实施约束

- Persistence Adapter 独占 SQLite schema、migration、连接、事务和业务 SQL。
- Hosting 只暴露技术中立的 credential/token Store 端口和值对象；Web 与 Application 不引用 SQLite provider。
- `config.json` 只同步固定 `Subject=owner` 的引导身份，不创建第二个引导用户。
- Basic、password grant、Bearer 和 SSE 复验都读取持久用户与 Token 当前状态。
- 不实现用户管理、Cookie、CSRF Token、refresh token、JWT、QueryString Token、审计或日志持久化。
- 本计划只记录历史动作，不授权重新执行发布、真实 7DTD smoke 或 Git 操作。

## 任务 1：建立 Persistence Adapter 与迁移边界

**文件**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/LSTY.SevenDPanel.Adapters.Persistence.Sqlite.csproj`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqliteConnectionFactory.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqliteDatabaseBootstrapper.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Migrations/001_Authentication.sql`
- 修改：`backend/7DPanel.sln`
- 修改：`backend/Directory.Build.props`

- [x] 创建只引用 Hosting 的 SQLite Persistence Adapter，并把它加入解决方案。
- [x] 以 `<ModDirectory>/data/7dpanel.db` 为受控数据库路径，建立短连接工厂和有限 busy timeout。
- [x] 初始化 WAL，并用 DbUp 从 Persistence Adapter 程序集按序执行嵌入 migration。
- [x] 让每个 migration 使用独立事务，失败时保留根异常并阻止认证 Host 启动。
- [x] 保持 SQLite、Dapper、DbUp 和事务类型不进入 Hosting、Application 或 Web Adapter 公共边界。

## 任务 2：定义认证 Store 端口与 SQLite 实现

**文件**

- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/Authentication/IPanelCredentialStore.cs`
- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/Authentication/IPanelAccessTokenStore.cs`
- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/Authentication/PanelUserIdentity.cs`
- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/Authentication/StoredAccessToken.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqliteAuthenticationStore.cs`

- [x] 在 Hosting 定义持久凭据验证、活动用户读取、Token 签发和验证所需的最小技术中立端口。
- [x] 让同一个 `SqliteAuthenticationStore` 实现 credential 与 access-token 两个端口，并独占相关 SQL 和事务。
- [x] 使用带随机盐的 PBKDF2-HMAC-SHA256 保存引导密码摘要，不把明文密码或明文 Token 写入数据库。
- [x] 按稳定 `Subject=owner` 创建或更新唯一引导 `Owner`；配置凭据变化时在同一持久边界撤销旧 Token。
- [x] 实现 Token 到期、删除和严格容量清理，使 Token 能跨 Store 与连接工厂重建验证。

## 任务 3：把 Web 认证桥接到持久状态

**文件**

- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/Authentication/PanelCredentialVerifier.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/Authentication/PanelClaimsIdentityFactory.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/Authentication/PersistentAccessTokenProvider.cs`
- 删除：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/Authentication/InMemoryAccessTokenProvider.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/Authentication/BasicAuthenticationHandler.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/Authentication/PanelOAuthAuthorizationServerProvider.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OwinStartup.cs`

- [x] 删除进程内 Token Provider，使 Basic 与 password grant 都通过 `IPanelCredentialStore` 验证当前用户。
- [x] 由持久用户当前 Subject、用户名和角色统一重建 Basic 与 Bearer claims。
- [x] 用 `PersistentAccessTokenProvider` 把 OAuth ticket 签发和接收桥接到 `IPanelAccessTokenStore`。
- [x] Bearer 接收时同时验证 Token 与关联用户仍处于活动状态，不接受 URL 或 Cookie Token。
- [x] 保留 OAuth 协议错误和现有 Problem Details、限流与 Header-only Bearer 契约。

## 任务 4：增加认证 SSE 周期复验

**文件**

- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ServerEventsController.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ServerEventSseSession.cs`

- [x] 建流时把已认证 Subject 和原始 Header Bearer Token 传给 request-scoped SSE session。
- [x] 使用 credential/token Store 复验用户和 Token 当前状态，不依赖建流时 claims 的永久有效性。
- [x] 以独立截止时间触发复验，使持续事件写出不会推迟不超过 15 秒的授权检查。
- [x] Token 更早到期时以到期时间为边界；撤销、到期或用户禁用后停止写出并释放连接资源。
- [x] Basic SSE 复验当前用户状态，Bearer SSE 同时复验用户和 Token，任何失败都不得降级为匿名。

## 任务 5：组合初始化、程序集位置与释放顺序

**文件**

- 新建：`backend/src/Bootstrap/LSTY.SevenDPanel/Compatibility/AssemblyLocationPatch.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/ModMain.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/LSTY.SevenDPanel.csproj`

- [x] 在组合 SQLite/OWIN 前，使用游戏提供的 Harmony 只恢复当前 Mod 内位置为空的程序集 `Assembly.Location`。
- [x] 由 Bootstrap 解析数据库路径、执行 migration、同步引导 Owner，并把同一 Store 实例注册为两个 Hosting 端口。
- [x] 保持 Bootstrap 为唯一组合根，使用 Microsoft DI 的作用域与验证规则构建运行时。
- [x] 初始化失败时清理候选资源并保持认证失败关闭；正常停止时释放 OWIN、Store 和连接池。
- [x] 不发布 Harmony，也不覆盖已有程序集位置或其他 Mod 的程序集。

## 任务 6：覆盖持久化、认证和依赖规则

**文件**

- 新建：`backend/tests/LSTY.SevenDPanel.Tests/SqliteAuthenticationStoreTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/AuthenticationTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/ServerEventSseSessionTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyInjectionTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyRulesTests.cs`

- [x] 覆盖 migration 幂等、WAL、稳定引导 `owner`、凭据轮换撤销和明文不落盘。
- [x] 覆盖 Token 跨 Store/连接工厂重建、到期、撤销和严格容量行为。
- [x] 覆盖 Basic/password grant 使用持久用户、Bearer 重建当前身份以及无效状态失败关闭。
- [x] 使用可控时间覆盖 SSE 周期复验、Token 更早到期和用户失效后的连接关闭。
- [x] 以依赖规则禁止 Hosting 与 Web 引用 provider，并要求 Persistence Adapter、当时五个产品 DLL 和唯一 Mod 入口满足发布边界；Application 由后续控制台命令切片创建。

## 任务 7：更新发布布局与权威文档

**文件**

- 修改：`backend/scripts/Publish-Mod.ps1`
- 修改：`backend/scripts/README.md`
- 修改：`backend/README.md`
- 修改：`docs/PRD.md`
- 修改：`docs/architecture.md`
- 修改：`docs/architecture/backend-target-blueprint.md`
- 修改：`docs/test.md`

- [x] 发布 Dapper、DbUp、Microsoft.Data.Sqlite、SQLitePCLRaw、所需托管依赖和 Windows/Linux x64 native asset。
- [x] 保持游戏提供程序集、旧 System.Data.SQLite/SQLite.Interop 和 Mod 根目录 native SQLite 的排除规则。
- [x] 把已实现的引导 Owner、持久 Token、SSE 复验、Persistence Adapter 和残余平台风险提升到当前权威文档。
- [x] 明确完整用户管理、审计和 Linux 官方进程证据仍未实现，不从本切片推导完整 `CAP-05`。
- [x] 后续把提交过程中出现的自定义 SQLite loader 方案替换为当前标准 Batteries 布局；当前细节只由系统架构和测试策略维护。

## 完成记录

- 提交 `06a83e6` 创建 SQLite Persistence Adapter、首个认证 migration、稳定引导 Owner、持久不透明 Bearer Token 和认证 SSE 周期复验。
- 该提交同步更新产品合同、当前架构、Target 蓝图、测试策略、发布脚本和后端运维说明。
- 同日后续修正删除了临时自定义 native loader、ResourceManager shim 和显式 provider 绑定，当前实现改用标准 Batteries；本文不把被替代方案保留为目标设计。
- Windows `v3.0.1-b4` 当前二进制的最终兼容证据、依赖版本和精确自动化数量由[测试策略](../../test.md)记录；Linux 官方进程仍是已知缺口。
- 未通过本追溯计划执行新的构建、发布、真实进程 smoke 或 Git 提交。