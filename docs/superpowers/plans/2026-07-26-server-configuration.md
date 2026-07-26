# 服务器配置纵向切片实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 `Owner` 提供安全、可并发检测、明确重启语义的 `serverconfig.xml` 查看和单字段修改页面。

**Architecture:** Application 定义字段目录、读模型、版本冲突和单字段写入用例；Bootstrap 提供的本地文件 Adapter 负责 XML 读取与同目录替换；Web Adapter 提供 Owner-only HTTP 合同；Admin 使用生成客户端建立独立 Feature。主设计为[服务器治理功能设计规格](../specs/2026-07-26-server-governance-design.md)。

**Tech Stack:** C# 11/net48、System.Xml、Katana Web API、Microsoft DI、xUnit、Vue 3、TypeScript、Pinia Colada、Nuxt UI、Valibot、Vitest、运行时 OpenAPI/Hey API。

**验证边界:** 聚焦后端测试、一次后端聚合测试、聚焦 Vitest、Admin typecheck 与生产构建；不运行 Playwright、发布或真实 7DTD。

**Git 边界:** 本计划不包含 commit、push 或历史修改；需要提交时由用户另行授权。

---

## 文件结构

- `backend/src/Core/LSTY.SevenDPanel.Application/ServerConfiguration/`：字段目录、读写请求/结果、端口和用例。
- `backend/src/Bootstrap/LSTY.SevenDPanel/ServerConfiguration/LocalServerConfigurationStore.cs`：受限 XML 文件实现；Bootstrap 已拥有服务器配置路径和文件 I/O。
- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ServerConfigurationController.cs`：Owner-only HTTP 合同。
- `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`：组合端口和用例。
- `backend/tests/LSTY.SevenDPanel.Tests/ServerConfigurationTests.cs`：目录、解析、冲突、原子替换和 HTTP 授权。
- `frontend/apps/admin/src/features/server-configuration/`：parser、查询/Mutation 状态和页面组件。
- `frontend/apps/admin/src/pages/server-configuration.vue`：Owner-only 路由组合。

### Task 1：锁定 Application 合同

**Files:**
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/ServerConfiguration/ServerConfigurationModels.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/ServerConfiguration/IServerConfigurationStore.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/ServerConfiguration/ServerConfigurationUseCases.cs`
- Modify: `backend/src/Core/LSTY.SevenDPanel.Application/Overview/IRecentActivityWriter.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqliteRecentActivityStore.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/ServerConfigurationTests.cs`

- [ ] **Step 1: 写字段目录和版本冲突失败测试**

```csharp
[Fact]
public void Update_rejects_unknown_read_only_and_stale_fields()
{
    var store = new StubConfigurationStore(version: "v2");
    var useCase = new UpdateServerConfigurationUseCase(store, ServerConfigurationFieldCatalog.Create());

    Assert.Equal(ServerConfigurationUpdateStatus.UnknownField,
        useCase.Execute(new("Missing", "x", "v2")).Status);
    Assert.Equal(ServerConfigurationUpdateStatus.ReadOnly,
        useCase.Execute(new("ServerDisabledNetworkProtocols", "x", "v2")).Status);
    Assert.Equal(ServerConfigurationUpdateStatus.Conflict,
        useCase.Execute(new("ServerName", "x", "v1")).Status);
}
```

- [ ] **Step 2: 运行聚焦测试并确认失败**

Run: `dotnet test backend/7DPanel.sln --configuration Release --filter FullyQualifiedName~ServerConfigurationTests`

Expected: FAIL，因为 ServerConfiguration 类型尚不存在。

- [ ] **Step 3: 实现最小类型化合同**

```csharp
public sealed record ServerConfigurationField(
    string Key, string Value, string Group, ServerConfigurationValueType ValueType,
    bool Editable, bool Sensitive, bool RestartRequired,
    IReadOnlyList<string> AllowedValues, decimal? Minimum, decimal? Maximum);

public sealed record ServerConfigurationSnapshot(
    string Version, DateTimeOffset ReadAtUtc,
    IReadOnlyList<ServerConfigurationField> Fields);

public sealed record UpdateServerConfigurationRequest(string Key, string Value, string Version);

public interface IServerConfigurationStore
{
    ServerConfigurationSnapshot Read(ServerConfigurationFieldCatalog catalog);
    ServerConfigurationUpdateResult Update(
        UpdateServerConfigurationRequest request,
        ServerConfigurationFieldCatalog catalog);
}
```

字段目录首期只把 `ServerName`、`ServerDescription`、`ServerMaxPlayerCount`、`ServerPort`、`GameWorld`、`WorldGenSeed`、`GameName`、`GameDifficulty`、`DayNightLength` 和 `PlayerKillingMode` 标为可编辑；未收录键只读，所有密码/Token 类键敏感且只读。为现有 `IRecentActivityWriter` 增加 `RecordServerConfigurationChangedAsync(actorSubject, key, restartRequired, outcome, occurredAtUtc, cancellationToken)`，由 `SqliteRecentActivityStore` 记录消息键与安全参数，不记录值或路径。

- [ ] **Step 4: 实现查询和更新用例并跑绿**

Run: `dotnet test backend/7DPanel.sln --configuration Release --filter FullyQualifiedName~ServerConfigurationTests`

Expected: PASS Application 合同测试。

### Task 2：实现安全 XML Store

**Files:**
- Create: `backend/src/Bootstrap/LSTY.SevenDPanel/ServerConfiguration/LocalServerConfigurationStore.cs`
- Modify: `backend/src/Bootstrap/LSTY.SevenDPanel/LSTY.SevenDPanel.csproj`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/ServerConfigurationTests.cs`

- [ ] **Step 1: 添加真实临时目录测试**

```csharp
[Fact]
public void Store_preserves_unknown_nodes_and_rejects_stale_version()
{
    using var fixture = ServerConfigFixture.Create(
        "<ServerSettings><property name=\"ServerName\" value=\"Old\"/>" +
        "<property name=\"FutureField\" value=\"keep\"/></ServerSettings>");
    var before = fixture.Store.Read(ServerConfigurationFieldCatalog.Create());

    var result = fixture.Store.Update(new("ServerName", "New", before.Version), fixture.Catalog);
    var after = fixture.Store.Read(fixture.Catalog);

    Assert.Equal(ServerConfigurationUpdateStatus.Updated, result.Status);
    Assert.Contains("FutureField", File.ReadAllText(fixture.Path));
    Assert.Equal(ServerConfigurationUpdateStatus.Conflict,
        fixture.Store.Update(new("ServerName", "Again", before.Version), fixture.Catalog).Status);
}
```

- [ ] **Step 2: 实现 XML 读取、敏感值裁剪和版本摘要**

使用 `XmlDocument` 读取精确 `ServerSettings/property[@name][@value]`；版本使用原始文件字节的 SHA-256 十六进制摘要。敏感字段的响应 `Value` 固定为空字符串，并由 `Sensitive=true` 表达。

- [ ] **Step 3: 实现同目录替换**

写入 `.7dpanel.tmp`，再次读取目标摘要确认版本仍匹配，保存完整 XML 后使用 `File.Replace`；目标平台不支持时使用同目录备份加 `File.Move`，失败恢复原文件并清理临时文件。不得改写非目标属性或注释。

- [ ] **Step 4: 运行聚焦测试**

Run: `dotnet test backend/7DPanel.sln --configuration Release --filter FullyQualifiedName~ServerConfigurationTests`

Expected: PASS，包括未知节点保留、敏感裁剪、类型错误、版本冲突和失败后原文件不变。

### Task 3：接入 HTTP、DI 与 OpenAPI

**Files:**
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ServerConfigurationController.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ServerConfigurationHttpModels.cs`
- Modify: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OpenApi/PanelOpenApiOperationProcessor.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/ServerConfigurationTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostOpenApiSnapshotTests.cs`

- [ ] **Step 1: 写角色与状态码测试**

```csharp
[Theory]
[InlineData(null, HttpStatusCode.Unauthorized)]
[InlineData("Admin", HttpStatusCode.Forbidden)]
[InlineData("Viewer", HttpStatusCode.Forbidden)]
[InlineData("Owner", HttpStatusCode.OK)]
public async Task Configuration_requires_owner(string? role, HttpStatusCode expected)
{
    using var host = ServerConfigurationHttpFixture.Create();
    using var client = host.CreateClient(role);
    var response = await client.GetAsync("/api/v1/server-configuration");
    Assert.Equal(expected, response.StatusCode);
}
```

- [ ] **Step 2: 实现 Controller**

```csharp
[Authorize(Roles = "Owner")]
[RoutePrefix("api/v1/server-configuration")]
public sealed class ServerConfigurationController : ApiController
{
    [HttpGet, Route("")]
    public HttpResponseMessage Get() => Request.CreateResponse(HttpStatusCode.OK, get.Execute());

    [HttpPut, Route("{key}")]
    public HttpResponseMessage Put(string key, UpdateServerConfigurationHttpRequest body)
        => Map(update.Execute(new(key, body.Value, body.Version)));
}
```

映射：Updated `200`；输入错误 `400`；Conflict `409 configuration_version_conflict`；不可写 `403 configuration_field_read_only`；I/O 故障安全 `500 configuration_write_failed`。响应不含物理路径。

- [ ] **Step 3: 注册端口/用例并刷新 OpenAPI**

从 `PanelHostConfig` 解析服务器配置相对路径，在 Bootstrap 验证并构造 Store。运行：

Run separately:

```powershell
pnpm api:schema
pnpm api:gen
```

Workdir: `frontend/apps/admin`

Expected: 新增服务器配置 GET/PUT operation，生成文件不手改。

- [ ] **Step 4: 运行 HTTP/OpenAPI 聚焦测试**

Run: `dotnet test backend/7DPanel.sln --configuration Release --filter "FullyQualifiedName~ServerConfigurationTests|FullyQualifiedName~OwinWebHostOpenApiSnapshotTests"`

Expected: PASS。

### Task 4：实现 Admin Feature 与页面

**Files:**
- Create: `frontend/apps/admin/src/features/server-configuration/api/serverConfiguration.ts`
- Create: `frontend/apps/admin/src/features/server-configuration/api/serverConfiguration.test.ts`
- Create: `frontend/apps/admin/src/features/server-configuration/model/useServerConfiguration.ts`
- Create: `frontend/apps/admin/src/features/server-configuration/model/useServerConfiguration.test.ts`
- Create: `frontend/apps/admin/src/features/server-configuration/ui/ServerConfigurationView.vue`
- Create: `frontend/apps/admin/src/features/server-configuration/ui/ServerConfigurationView.test.ts`
- Create: `frontend/apps/admin/src/features/server-configuration/index.ts`
- Create: `frontend/apps/admin/src/pages/server-configuration.vue`
- Modify: `frontend/apps/admin/src/app/AppShell.vue`
- Modify: `frontend/apps/admin/src/app/AppShell.test.ts`
- Modify: `frontend/apps/admin/src/app/i18n/locales/zh-CN.json`
- Modify: `frontend/apps/admin/src/app/i18n/locales/en.json`

- [ ] **Step 1: 写严格 parser 和状态测试**

```ts
it('rejects an invalid field and preserves stale data after refresh failure', async () => {
  const controller = useServerConfiguration({ fetchConfiguration: sequence(validSnapshot, networkError) })
  await controller.refresh()
  await controller.refresh()
  expect(controller.state.value).toBe('stale')
  expect(controller.snapshot.value?.version).toBe('v1')
})
```

- [ ] **Step 2: 实现 API mapper 与 Colada 查询/Mutation**

页面状态固定为 `loading | empty | fresh | stale | failed | forbidden`；Mutation 不自动重试。`409` 映射为 `conflict` 并保留输入，`401` 走现有会话失效，`403` 进入权限页状态。

- [ ] **Step 3: 实现分组页面与单字段编辑对话框**

已知字段按响应 group 分区；未知只读；布尔/枚举/数值/文本使用对应 Nuxt UI 控件；敏感字段只显示已设置状态。确认文案包含旧值、新值和生效时机，不包含敏感值。

- [ ] **Step 4: 增加 Owner-only 路由和导航**

```vue
<route lang="json">
{"meta":{"requiresAuth":true,"roles":["Owner"]}}
</route>
```

导航和搜索入口只对 Owner 出现，直接访问由现有路由守卫进入 `/forbidden`。

- [ ] **Step 5: 运行前端聚焦测试**

Run: `pnpm exec vitest run src/features/server-configuration src/app/AppShell.test.ts`

Workdir: `frontend/apps/admin`

Expected: PASS。

### Task 5：稳定后聚合验证与文档提升

**Files:**
- Modify after verified implementation: `docs/architecture.md`
- Modify after verified implementation: `docs/test.md`
- Modify if app scope changes: `frontend/apps/admin/README.md`

- [ ] **Step 1: 后端一次聚合验证**

Run:

```powershell
dotnet build backend/7DPanel.sln --configuration Release
dotnet test backend/7DPanel.sln --configuration Release --no-build
```

Expected: build/test PASS。

- [ ] **Step 2: Admin 一次聚合验证**

Run: `pnpm typecheck && pnpm build && pnpm api:check`

Workdir: `frontend/apps/admin`

Expected: PASS；不运行 lint 全量、Playwright 或真实服务器。

- [ ] **Step 3: 提升已验证事实**

只把实际存在且已验证的服务器配置边界写入 `docs/architecture.md`；在 `docs/test.md` 记录本次聚焦和聚合证据，保留未执行真实 OWIN/7DTD 的明确缺口。
