# 双层权限管理纵向切片实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 `Owner` 在一个页面分别维护面板用户角色、7DTD 游戏管理员和命令权限，同时保证两套权限不互相映射。

**Architecture:** 面板用户继续由现有 `SqliteAuthenticationStore` 和 Hosting 认证合同拥有，新增原子管理接口；游戏权限由 Application 类型化端口和 SevenDays Dispatcher Adapter 拥有；Web Adapter 提供 Owner-only 合同；Admin 使用三个独立分区。主设计为[服务器治理功能设计规格](../specs/2026-07-26-server-governance-design.md)。

**Tech Stack:** C# 11/net48、Dapper/Microsoft.Data.Sqlite、7DTD API、Katana、Microsoft DI、xUnit、Vue 3、TypeScript、Pinia Colada、Nuxt UI、Valibot、Vitest、OpenAPI/Hey API。

**验证边界:** SQLite/认证、游戏权限、HTTP 和 Admin 聚焦测试；稳定后一次后端聚合、Admin typecheck/build/api:check；不迁移 Owner，不改变明文 HTTP，不运行 Playwright/发布/真实 7DTD。

**Git 边界:** 不包含 commit、push 或历史修改。

---

## 文件结构

- `backend/src/Runtime/LSTY.SevenDPanel.Hosting/Authentication/IPanelUserAdministrationStore.cs`：面板用户管理合同。
- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqliteAuthenticationStore.cs`：同事务保留 Owner 与撤销 Token。
- `backend/src/Core/LSTY.SevenDPanel.Application/GamePermissions/`：游戏管理员和命令权限用例。
- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/GamePermissions/`：主线程原生实现。
- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/PermissionsController.cs`：Owner-only HTTP 合同。
- `frontend/apps/admin/src/features/permissions/`：三分区页面。

### Task 1：扩展面板用户 Store 合同

**Files:**
- Create: `backend/src/Runtime/LSTY.SevenDPanel.Hosting/Authentication/IPanelUserAdministrationStore.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqliteAuthenticationStore.cs`
- Modify: `backend/src/Core/LSTY.SevenDPanel.Application/Overview/IRecentActivityWriter.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqliteRecentActivityStore.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/SqliteAuthenticationStoreTests.cs`

- [ ] **Step 1: 写最后 Owner 和 Token 撤销测试**

```csharp
[Fact]
public void Cannot_disable_or_demote_the_last_enabled_owner()
{
    using var fixture = AuthenticationStoreFixture.Create();
    fixture.Store.EnsureBootstrapOwner("admin", "password");
    var owner = Assert.Single(fixture.Store.ListUsers());

    Assert.Equal(PanelUserMutationStatus.LastOwner,
        fixture.Store.UpdateUser(owner.Subject, owner.Username, "Viewer", false).Status);
    Assert.Equal("Owner", Assert.Single(fixture.Store.ListUsers()).Role);
}

[Fact]
public void Role_or_password_change_revokes_existing_access_tokens()
{
    using var fixture = AuthenticationStoreFixture.Create();
    fixture.Store.EnsureBootstrapOwner("admin", "password");
    var owner = Assert.Single(fixture.Store.ListUsers());
    var token = fixture.IssueAccessToken(owner.Subject);

    Assert.Equal(PanelUserMutationStatus.Updated,
        fixture.Store.ResetPassword(owner.Subject, "new-password").Status);
    Assert.False(fixture.IsAccessTokenActive(token));
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test backend/7DPanel.sln --configuration Release --filter FullyQualifiedName~SqliteAuthenticationStoreTests`

Expected: FAIL，新接口不存在。

- [ ] **Step 3: 定义管理合同**

```csharp
public sealed record PanelUserRecord(
    string Subject, string Username, string Role, bool Enabled, DateTimeOffset UpdatedAtUtc);

public interface IPanelUserAdministrationStore
{
    IReadOnlyList<PanelUserRecord> ListUsers();
    PanelUserMutationResult CreateUser(string username, string password, string role, bool enabled);
    PanelUserMutationResult UpdateUser(string subject, string username, string role, bool enabled);
    PanelUserMutationResult ResetPassword(string subject, string password);
    PanelUserMutationResult DeleteUser(string subject);
}
```

只接受 `Owner/Admin/Viewer`；subject 创建后固定并由安全随机 ID 生成，固定 bootstrap subject `owner` 不因用户名变化而改变。

- [ ] **Step 4: 在现有 Store 中实现原子事务**

每个更新事务先读取目标和启用 Owner 数量，再执行用户变更与该 subject 的 Access Token 撤销；若操作会留下零个启用 Owner，则返回 `LastOwner` 且不写任何变化。密码继续使用现有版本化摘要，读取合同不选择摘要列。扩展 `IRecentActivityWriter`，分别记录面板用户动作和游戏权限动作的 actor、目标安全标识、动作、等级/角色与结果；绝不记录密码摘要。

- [ ] **Step 5: 跑绿 SQLite 测试**

Run: `dotnet test backend/7DPanel.sln --configuration Release --filter FullyQualifiedName~SqliteAuthenticationStoreTests`

Expected: PASS，包含并发最后 Owner、用户名冲突、角色校验、禁用/删除/重置密码和 Token 撤销。

### Task 2：实现游戏管理员与命令权限端口

**Files:**
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/GamePermissions/GamePermissionModels.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/GamePermissions/IGamePermissionControl.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/GamePermissions/GamePermissionUseCases.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/GamePermissions/SevenDaysGamePermissionControl.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/GamePermissionTests.cs`

- [ ] **Step 1: 写等级边界和主线程测试**

```csharp
[Theory]
[InlineData(-1)]
[InlineData(2001)]
public async Task Rejects_permission_level_outside_native_range(int level)
{
    var result = await useCase.UpsertCommandAsync(new("tele", level), CancellationToken.None);
    Assert.Equal(GamePermissionMutationStatus.Invalid, result.Status);
    Assert.Equal(0, port.CallCount);
}
```

- [ ] **Step 2: 实现合同**

```csharp
public sealed record GameAdminEntry(string PlayerId, string DisplayName, int PermissionLevel);
public sealed record CommandPermissionEntry(string Command, int PermissionLevel, string? Description);
public interface IGamePermissionControl
{
    Task<IReadOnlyList<GameAdminEntry>> GetAdminsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<CommandPermissionEntry>> GetCommandsAsync(CancellationToken cancellationToken);
    Task<GamePermissionMutationResult> UpsertAdminAsync(GameAdminEntry entry, CancellationToken cancellationToken);
    Task<GamePermissionMutationResult> RemoveAdminAsync(string playerId, CancellationToken cancellationToken);
    Task<GamePermissionMutationResult> UpsertCommandAsync(string command, int level, CancellationToken cancellationToken);
    Task<GamePermissionMutationResult> RemoveCommandAsync(string command, CancellationToken cancellationToken);
}
```

- [ ] **Step 3: 实现 SevenDays Adapter**

在 Dispatcher 内复制 `adminTools.Users.GetUsers()` 和 `adminTools.Commands.GetCommands()`；命令描述来自 `SdtdConsole.Instance.GetCommand(command)?.GetDescription()`，单项描述失败返回 null 而不使整个列表失败。写入调用原生权限管理入口，结果区分未就绪、不存在、冲突、原生拒绝和未知。

- [ ] **Step 4: 运行游戏权限测试**

Run: `dotnet test backend/7DPanel.sln --configuration Release --filter FullyQualifiedName~GamePermissionTests`

Expected: PASS，证明所有游戏对象访问位于 Dispatcher 内且没有面板角色转换。

### Task 3：实现 Owner-only HTTP 合同

**Files:**
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/PanelUsersController.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/GamePermissionsController.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/PermissionHttpModels.cs`
- Modify: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OpenApi/PanelOpenApiOperationProcessor.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/PermissionHttpTests.cs`

- [ ] **Step 1: 写 Owner-only、密码不回读测试**

```csharp
[Fact]
public async Task User_list_is_owner_only_and_never_contains_password_material()
{
    var response = await owner.GetAsync("/api/v1/panel-users");
    var body = await response.Content.ReadAsStringAsync();
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
    Assert.Equal(HttpStatusCode.Forbidden,
        (await admin.GetAsync("/api/v1/panel-users")).StatusCode);
}
```

- [ ] **Step 2: 实现路由**

```text
GET/POST        /api/v1/panel-users
PUT/DELETE      /api/v1/panel-users/{subject}
POST            /api/v1/panel-users/{subject}/password
GET              /api/v1/game-permissions/admins
PUT/DELETE       /api/v1/game-permissions/admins/{playerId}
GET              /api/v1/game-permissions/commands
PUT/DELETE       /api/v1/game-permissions/commands/{command}
```

所有 Controller `[Authorize(Roles = "Owner")]`。用户名/密码/角色/等级错误 `400`；目标不存在 `404`；用户名或最后 Owner 冲突 `409`；游戏未就绪 `503`；内部错误脱敏 `500`。

- [ ] **Step 3: 注册 DI、OpenAPI 与生成客户端**

`SqliteAuthenticationStore` 同时注册为 `IPanelUserAdministrationStore`；注册游戏端口和用例。Workdir `frontend/apps/admin`，分别运行：

```powershell
pnpm api:schema
pnpm api:gen
```

- [ ] **Step 4: 运行 HTTP 聚焦测试**

Run: `dotnet test backend/7DPanel.sln --configuration Release --filter FullyQualifiedName~PermissionHttpTests`

Expected: PASS。

### Task 4：实现 Admin 权限管理 Feature

**Files:**
- Create: `frontend/apps/admin/src/features/permissions/api/permissions.ts`
- Create: `frontend/apps/admin/src/features/permissions/api/permissions.test.ts`
- Create: `frontend/apps/admin/src/features/permissions/model/usePanelUsers.ts`
- Create: `frontend/apps/admin/src/features/permissions/model/useGamePermissions.ts`
- Create: `frontend/apps/admin/src/features/permissions/model/permissions.test.ts`
- Create: `frontend/apps/admin/src/features/permissions/ui/PermissionsView.vue`
- Create: `frontend/apps/admin/src/features/permissions/ui/PanelUsersSection.vue`
- Create: `frontend/apps/admin/src/features/permissions/ui/GameAdminsSection.vue`
- Create: `frontend/apps/admin/src/features/permissions/ui/CommandPermissionsSection.vue`
- Create: `frontend/apps/admin/src/features/permissions/ui/PermissionsView.test.ts`
- Create: `frontend/apps/admin/src/features/permissions/index.ts`
- Create: `frontend/apps/admin/src/pages/permissions.vue`
- Modify: `frontend/apps/admin/src/app/AppShell.vue`
- Modify: `frontend/apps/admin/src/app/AppShell.test.ts`
- Modify: `frontend/apps/admin/src/app/i18n/locales/zh-CN.json`
- Modify: `frontend/apps/admin/src/app/i18n/locales/en.json`

- [ ] **Step 1: 写三分区与权限分离测试**

```ts
it('never maps native level to a panel role', () => {
  const parsed = parseGameAdmin({ playerId: 'EOS_1', displayName: 'P', permissionLevel: 0 })
  expect(parsed).toEqual({ playerId: 'EOS_1', displayName: 'P', permissionLevel: 0 })
  expect('role' in parsed).toBe(false)
})
```

- [ ] **Step 2: 实现严格 parser 和三个独立状态控制器**

面板用户、游戏管理员和命令权限分别查询、失效和提交；一个分区失败不清空其他分区。密码字段不进入查询模型、URL、通知或持久存储；关闭对话框立即清空。

- [ ] **Step 3: 实现页面和确认**

面板用户支持创建、编辑、重置密码、删除；最后 Owner `409` 显示明确冲突。游戏等级输入限制 `0..2000` 并持续显示“数值越小权限越高”。命令描述只读，编辑时 command 固定。

- [ ] **Step 4: 增加 Owner-only 路由和导航**

```vue
<route lang="json">
{"meta":{"requiresAuth":true,"roles":["Owner"]}}
</route>
```

- [ ] **Step 5: 运行前端聚焦测试**

Run: `pnpm exec vitest run src/features/permissions src/app/AppShell.test.ts`

Workdir: `frontend/apps/admin`

Expected: PASS。

### Task 5：聚合验证和事实提升

**Files:**
- Modify after verification: `docs/architecture.md`
- Modify after verification: `docs/test.md`
- Modify implementation summary: `backend/README.md`

- [ ] **Step 1: 一次后端聚合**

Run separately:

```powershell
dotnet build backend/7DPanel.sln --configuration Release
dotnet test backend/7DPanel.sln --configuration Release --no-build
```

- [ ] **Step 2: 一次 Admin 聚合**

Workdir: `frontend/apps/admin`

Run separately:

```powershell
pnpm typecheck
pnpm build
pnpm api:check
```

- [ ] **Step 3: 更新事实但不宣称迁移完成**

在当前架构和测试文档记录用户管理、最后 Owner、游戏权限与聚焦证据；明确 `config.json` 引导、开发默认凭据和明文 HTTP 仍保留，且未运行真实 7DTD/浏览器 E2E。
