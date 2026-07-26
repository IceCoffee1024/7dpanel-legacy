# 黑白名单纵向切片实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 `Owner`/`Admin` 提供 7DTD 原生封禁和白名单的单项维护，为 `Viewer` 提供只读查看。

**Architecture:** Application 使用类型化名单端口和诚实结果，不拼接控制台命令；SevenDays Adapter 在 `GameThreadDispatcher` 内复制或改变 `adminTools`；Web Adapter 强制角色矩阵；Admin 建立封禁/白名单独立 Feature。主设计为[服务器治理功能设计规格](../specs/2026-07-26-server-governance-design.md)。

**Tech Stack:** C# 11/net48、7DTD v3.0.1-b4 API、Katana、Microsoft DI、xUnit、Vue 3、TypeScript、Pinia Colada、Nuxt UI、Vitest、OpenAPI/Hey API。

**验证边界:** 聚焦后端与 Admin 测试，稳定后一次后端聚合、Admin typecheck/build/api:check；不运行 Playwright、发布或真实 7DTD。

**Git 边界:** 不包含 commit、push 或历史修改。

---

## 文件结构

- `backend/src/Core/LSTY.SevenDPanel.Application/AccessLists/`：名单快照、请求、结果、端口和用例。
- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/AccessLists/SevenDaysPlayerAccessControl.cs`：主线程原生实现。
- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/AccessListsController.cs`：名单 HTTP 合同。
- `frontend/apps/admin/src/features/access-lists/`：parser、状态、表格和对话框。
- `frontend/apps/admin/src/pages/access-lists.vue`：受保护路由。

### Task 1：定义名单用例与结果

**Files:**
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/AccessLists/AccessListModels.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/AccessLists/IPlayerAccessControl.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/AccessLists/AccessListUseCases.cs`
- Modify: `backend/src/Core/LSTY.SevenDPanel.Application/Overview/IRecentActivityWriter.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqliteRecentActivityStore.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/AccessListUseCaseTests.cs`

- [ ] **Step 1: 写权限无关的结果映射测试**

```csharp
[Fact]
public async Task Update_returns_native_conflict_without_retrying()
{
    var port = new StubPlayerAccessControl(AccessListMutationResult.Conflict("changed"));
    var result = await new UpdateBanUseCase(port, audit).ExecuteAsync(
        new BanRequest("EOS_1", "Player", DateTimeOffset.UtcNow.AddDays(1), "reason"),
        CancellationToken.None);

    Assert.Equal(AccessListMutationStatus.Conflict, result.Status);
    Assert.Equal(1, port.CallCount);
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test backend/7DPanel.sln --configuration Release --filter FullyQualifiedName~AccessListUseCaseTests`

Expected: FAIL，类型不存在。

- [ ] **Step 3: 实现最小合同**

```csharp
public sealed record BanEntry(string PlayerId, string DisplayName,
    DateTimeOffset? BannedUntilUtc, string? Reason);
public sealed record WhitelistEntry(string PlayerId, string DisplayName);
public enum AccessListMutationStatus
{
    Succeeded, NotFound, Conflict, GameNotReady, NativeRejected, Unknown
}
public interface IPlayerAccessControl
{
    Task<IReadOnlyList<BanEntry>> GetBansAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<WhitelistEntry>> GetWhitelistAsync(CancellationToken cancellationToken);
    Task<AccessListMutationResult> UpsertBanAsync(BanRequest request, CancellationToken cancellationToken);
    Task<AccessListMutationResult> RemoveBanAsync(string playerId, CancellationToken cancellationToken);
    Task<AccessListMutationResult> UpsertWhitelistAsync(WhitelistRequest request, CancellationToken cancellationToken);
    Task<AccessListMutationResult> RemoveWhitelistAsync(string playerId, CancellationToken cancellationToken);
}
```

用例对空白 ID、截止时间和原因长度执行验证，调用端口一次。为 `IRecentActivityWriter` 增加 `RecordAccessListChangedAsync(actorSubject, list, action, playerId, outcome, occurredAtUtc, cancellationToken)`，由现有 SQLite writer 保存安全参数；原因只保存受限摘要，不保存服务端路径或凭据。

- [ ] **Step 4: 跑绿聚焦测试**

Run: `dotnet test backend/7DPanel.sln --configuration Release --filter FullyQualifiedName~AccessListUseCaseTests`

Expected: PASS。

### Task 2：实现 SevenDays 主线程 Adapter

**Files:**
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/AccessLists/SevenDaysPlayerAccessControl.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/SevenDaysPlayerAccessControlTests.cs`

- [ ] **Step 1: 写 Dispatcher 所有权测试**

```csharp
[Fact]
public async Task GetBans_copies_entries_inside_dispatcher()
{
    var dispatcher = new RecordingGameThreadDispatcher();
    var sut = new SevenDaysPlayerAccessControl(dispatcher, fixture.NativeGateway);

    var result = await sut.GetBansAsync(CancellationToken.None);

    Assert.True(dispatcher.WasUsed);
    Assert.All(result, item => Assert.NotNull(item.PlayerId));
}
```

- [ ] **Step 2: 实现原生网关**

在 Dispatcher 委托内读取 `GameManager.Instance.adminTools.Blacklist.GetBanned()` 与 `Whitelist.GetUsers()`，立即复制 CombinedString、Name、BannedUntil 和 BanReason。写入调用 7DTD 原生名单管理入口；不得从 HTTP worker 保存活动游戏对象或用命令字符串代替类型化动作。

- [ ] **Step 3: 处理停止、未就绪和异常**

Dispatcher 拒绝映射为 `GameNotReady`；已开始后异常映射为 `NativeRejected` 或 `Unknown`，不得自动重试。更新动作必须以一个 Adapter 调用表达，不能让前端执行删除后新增。

- [ ] **Step 4: 运行 Adapter 测试**

Run: `dotnet test backend/7DPanel.sln --configuration Release --filter FullyQualifiedName~SevenDaysPlayerAccessControlTests`

Expected: PASS，覆盖读取、增改、移除、目标不存在和 Dispatcher 拒绝。

### Task 3：提供 HTTP 合同与生成客户端

**Files:**
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/AccessListsController.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/AccessListHttpModels.cs`
- Modify: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OpenApi/PanelOpenApiOperationProcessor.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/AccessListHttpTests.cs`

- [ ] **Step 1: 写角色矩阵测试**

```csharp
[Theory]
[InlineData("Owner", HttpStatusCode.OK)]
[InlineData("Admin", HttpStatusCode.OK)]
[InlineData("Viewer", HttpStatusCode.Forbidden)]
public async Task All_roles_read_but_only_owner_and_admin_write(string role, HttpStatusCode writeStatus)
{
    using var host = AccessListHttpFixture.Create();
    using var client = host.CreateClient(role);
    Assert.Equal(HttpStatusCode.OK,
        (await client.GetAsync("/api/v1/access-lists/bans")).StatusCode);
    Assert.Equal(writeStatus,
        (await client.PutAsJsonAsync("/api/v1/access-lists/bans/EOS_1",
            new { displayName = "Player", bannedUntilUtc = DateTimeOffset.UtcNow.AddDays(1), reason = "reason" })).StatusCode);
}
```

- [ ] **Step 2: 实现路由**

```text
GET    /api/v1/access-lists/bans
PUT    /api/v1/access-lists/bans/{playerId}
DELETE /api/v1/access-lists/bans/{playerId}
GET    /api/v1/access-lists/whitelist
PUT    /api/v1/access-lists/whitelist/{playerId}
DELETE /api/v1/access-lists/whitelist/{playerId}
```

Controller 级允许 `Owner,Admin,Viewer`，每个 Mutation 再拒绝 `Viewer`。输入 `400`，目标不存在 `404`，冲突 `409`，游戏未就绪 `503`，未知 `500`；全部使用稳定 Problem Details code。

- [ ] **Step 3: 注册 DI 并刷新 OpenAPI/客户端**

Workdir: `frontend/apps/admin`

Run separately:

```powershell
pnpm api:schema
pnpm api:gen
```

Expected: 六个 operation 进入快照和生成客户端。

- [ ] **Step 4: 运行 HTTP 聚焦测试**

Run: `dotnet test backend/7DPanel.sln --configuration Release --filter FullyQualifiedName~AccessListHttpTests`

Expected: PASS。

### Task 4：实现 Admin 访问名单页面

**Files:**
- Create: `frontend/apps/admin/src/features/access-lists/api/accessLists.ts`
- Create: `frontend/apps/admin/src/features/access-lists/api/accessLists.test.ts`
- Create: `frontend/apps/admin/src/features/access-lists/model/useAccessLists.ts`
- Create: `frontend/apps/admin/src/features/access-lists/model/useAccessLists.test.ts`
- Create: `frontend/apps/admin/src/features/access-lists/ui/AccessListsView.vue`
- Create: `frontend/apps/admin/src/features/access-lists/ui/BanDialog.vue`
- Create: `frontend/apps/admin/src/features/access-lists/ui/WhitelistDialog.vue`
- Create: `frontend/apps/admin/src/features/access-lists/ui/AccessListsView.test.ts`
- Create: `frontend/apps/admin/src/features/access-lists/index.ts`
- Create: `frontend/apps/admin/src/pages/access-lists.vue`
- Modify: `frontend/apps/admin/src/app/AppShell.vue`
- Modify: `frontend/apps/admin/src/app/AppShell.test.ts`
- Modify: `frontend/apps/admin/src/app/i18n/locales/zh-CN.json`
- Modify: `frontend/apps/admin/src/app/i18n/locales/en.json`

- [ ] **Step 1: 写 parser、URL 和角色测试**

```ts
it('keeps viewer read-only and does not render bulk actions', async () => {
  const wrapper = mountAccessLists({ role: 'Viewer', bans: [ban] })
  expect(wrapper.text()).toContain('Player')
  expect(wrapper.find('[data-testid="add-ban"]').exists()).toBe(false)
  expect(wrapper.find('[data-testid="bulk-delete"]').exists()).toBe(false)
})
```

- [ ] **Step 2: 实现两个查询和单项目标 Mutation**

状态固定为 `loading | empty | fresh | stale | failed | forbidden | game-not-ready`。一次只允许一个固定目标 Mutation；成功失效对应名单查询；失败保留表单；`unknown` 先刷新而不自动重放。

- [ ] **Step 3: 实现双页签和确认**

使用 route query `tab=ban|whitelist` 与 `q`；永久封禁显示明确文本。编辑对话框固定目标 ID，确认中显示目标、动作和后果。不得使用多选列或批量菜单。

- [ ] **Step 4: 增加导航与双语**

路由要求认证但不限制角色；编辑控件由当前服务端确认角色决定，服务端 403 仍映射为只读/Forbidden 反馈。

- [ ] **Step 5: 运行前端聚焦测试**

Run: `pnpm exec vitest run src/features/access-lists src/app/AppShell.test.ts`

Workdir: `frontend/apps/admin`

Expected: PASS。

### Task 5：聚合验证和事实提升

**Files:**
- Modify after verification: `docs/architecture.md`
- Modify after verification: `docs/test.md`

- [ ] **Step 1: 运行一次后端聚合**

Run separately:

```powershell
dotnet build backend/7DPanel.sln --configuration Release
dotnet test backend/7DPanel.sln --configuration Release --no-build
```

- [ ] **Step 2: 运行一次 Admin 聚合**

Workdir: `frontend/apps/admin`

Run separately:

```powershell
pnpm typecheck
pnpm build
pnpm api:check
```

- [ ] **Step 3: 更新当前事实**

将已实现的名单端口、角色矩阵和验证证据提升到 `docs/architecture.md`/`docs/test.md`；明确未运行真实 7DTD 和浏览器 E2E。
