# 模组管理纵向切片实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 展示安全模组清单、当前进程加载状态和下次启动状态，并允许 `Owner` 设置重启后的启用状态。

**Architecture:** Application 合并本地模组目录快照与 SevenDays 当前加载快照；Bootstrap Local Store 只处理 Mods 根目录的直接子目录和 `ModInfo.xml`/`_ModInfo.xml`；Web Adapter 区分只读和 Owner Mutation；Admin 明确显示“当前”与“下次启动”。主设计为[服务器治理功能设计规格](../specs/2026-07-26-server-governance-design.md)。

**Tech Stack:** C# 11/net48、System.Xml、7DTD ModManager、Katana、Microsoft DI、xUnit、Vue 3、TypeScript、Pinia Colada、Nuxt UI、Vitest、OpenAPI/Hey API。

**验证边界:** 临时目录、运行时快照、HTTP 和 Admin 聚焦测试；稳定后一次后端聚合、Admin typecheck/build/api:check；不上传/删除模组，不运行 Playwright、发布或真实 7DTD。

**Git 边界:** 不包含 commit、push 或历史修改。

---

## 文件结构

- `backend/src/Core/LSTY.SevenDPanel.Application/Mods/`：目录/运行时快照、合并用例和状态切换结果。
- `backend/src/Bootstrap/LSTY.SevenDPanel/Mods/LocalModCatalog.cs`：受限目录解析和元数据标记切换。
- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Mods/SevenDaysLoadedModQuery.cs`：当前进程不可变加载快照。
- `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ModsController.cs`：角色化 HTTP 合同。
- `frontend/apps/admin/src/features/mods/`：parser、列表和状态确认。
- `frontend/apps/admin/src/pages/mods.vue`：认证路由。

### Task 1：定义模组 Application 合同

**Files:**
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Mods/ModModels.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Mods/IModCatalog.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Mods/ILoadedModQuery.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Mods/ModUseCases.cs`
- Modify: `backend/src/Core/LSTY.SevenDPanel.Application/Overview/IRecentActivityWriter.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqliteRecentActivityStore.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/ModManagementUseCaseTests.cs`

- [ ] **Step 1: 写磁盘/运行时状态合并测试**

```csharp
[Fact]
public void List_keeps_runtime_and_next_start_states_separate()
{
    var catalog = new StubModCatalog(new ModDiskEntry("Example", "Example", true, false));
    var loaded = new StubLoadedModQuery("Example");

    var result = new ListModsUseCase(catalog, loaded).Execute();

    var mod = Assert.Single(result);
    Assert.True(mod.IsLoadedNow);
    Assert.False(mod.IsEnabledNextStart);
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test backend/7DPanel.sln --configuration Release --filter FullyQualifiedName~ModManagementUseCaseTests`

Expected: FAIL，类型不存在。

- [ ] **Step 3: 实现最小合同**

```csharp
public sealed record ModDiskEntry(
    string DirectoryId, string Name, string DisplayName, string Author,
    string Version, string? Website, string? Description,
    bool IsEnabledNextStart, bool IsProtected);

public sealed record ModView(
    string DirectoryId, string Name, string DisplayName, string Author,
    string Version, string? Website, string? Description,
    bool? IsLoadedNow, bool IsEnabledNextStart, bool IsProtected);

public sealed record LoadedModSnapshot(
    bool Available, IReadOnlyCollection<string> Names);

public interface IModCatalog
{
    IReadOnlyList<ModDiskEntry> List();
    ModStateChangeResult SetEnabled(string directoryId, bool enabled);
}

public interface ILoadedModQuery
{
    LoadedModSnapshot GetLoadedNames();
}
```

- [ ] **Step 4: 实现合并与切换用例**

按 Mod `Name` 与当前加载集合精确比较，目录 ID 只用于安全定位。切换受保护模组返回 `Protected`；目标已经处于期望状态返回幂等 `Unchanged`；成功只改变 `IsEnabledNextStart`。为 `IRecentActivityWriter` 增加 `RecordModStateChangedAsync(actorSubject, directoryId, enabledNextStart, outcome, occurredAtUtc, cancellationToken)`，不记录绝对路径。

- [ ] **Step 5: 跑绿用例测试**

Run: `dotnet test backend/7DPanel.sln --configuration Release --filter FullyQualifiedName~ModManagementUseCaseTests`

Expected: PASS。

### Task 2：实现安全 Local Mod Catalog

**Files:**
- Create: `backend/src/Bootstrap/LSTY.SevenDPanel/Mods/LocalModCatalog.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/LocalModCatalogTests.cs`

- [ ] **Step 1: 写路径、元数据和受保护测试**

```csharp
[Theory]
[InlineData("../Other")]
[InlineData("C:\\Mods\\Other")]
[InlineData("a/b")]
public void Rejects_non_child_directory_ids(string directoryId)
{
    using var fixture = ModCatalogFixture.Create();
    Assert.Equal(ModStateChangeStatus.InvalidDirectory,
        fixture.Catalog.SetEnabled(directoryId, false).Status);
}
```

同时覆盖损坏 XML 被跳过、`ModInfo.xml` 表示启用、`_ModInfo.xml` 表示下次禁用、两者同时存在时拒绝操作、7DPanel 当前目录受保护、符号链接/重解析点不越界。

- [ ] **Step 2: 实现根目录解析**

Bootstrap 从当前 `Mod` 目录的父目录得到 Mods 根目录，并把规范化绝对路径传入 `LocalModCatalog`；HTTP 请求永远只传单个 `DirectoryId`。每次操作重新组合和校验完整路径仍是根目录直接子项。

- [ ] **Step 3: 实现元数据提取**

只读取根元素的 `Name`、`DisplayName`、`Author`、`Version`、`Website` 和 `Description` 的 `value` 属性；缺失可选字段为空，缺失 Name 的目录不进入可操作清单。XML 禁止外部实体解析。

- [ ] **Step 4: 实现下次启动标记切换**

禁用将 `ModInfo.xml` 原子移动为 `_ModInfo.xml`；启用执行反向移动。目标或源状态矛盾返回 Conflict，不覆盖现有文件。保护当前 7DPanel 目录和由显式保护集合提供的运行时必需模组。

- [ ] **Step 5: 运行临时文件系统测试**

Run: `dotnet test backend/7DPanel.sln --configuration Release --filter FullyQualifiedName~LocalModCatalogTests`

Expected: PASS，且错误对象不包含绝对路径。

### Task 3：实现当前加载快照和 HTTP 合同

**Files:**
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Mods/SevenDaysLoadedModQuery.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ModsController.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ModHttpModels.cs`
- Modify: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OpenApi/PanelOpenApiOperationProcessor.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/ModManagementHttpTests.cs`

- [ ] **Step 1: 写角色和诚实状态测试**

```csharp
[Fact]
public async Task Viewer_can_list_but_only_owner_can_change_next_start_state()
{
    Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/v1/mods")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,
        (await viewer.PutAsJsonAsync("/api/v1/mods/Example/state", new { enabled = false })).StatusCode);
}
```

- [ ] **Step 2: 实现当前进程快照**

`SevenDaysLoadedModQuery` 在游戏就绪边界复制 `ModManager.loadedMods.dict.Keys` 为不可变、不区分目录路径的 Name 集合；列表在游戏未就绪时仍返回磁盘条目，并将 `isLoadedNow` 表示为可空未知，而不是 false。

- [ ] **Step 3: 实现 HTTP**

```text
GET /api/v1/mods
PUT /api/v1/mods/{directoryId}/state  body: { enabled: boolean }
```

GET 允许 `Owner,Admin,Viewer`；PUT 只允许 Owner。无效 ID `400`，未知 `404`，保护/无权 `403`，状态冲突 `409`，文件失败 `500`。响应只含 directoryId，不含 Mods 根路径。

- [ ] **Step 4: 注册 DI 并生成客户端**

Workdir: `frontend/apps/admin`

Run separately:

```powershell
pnpm api:schema
pnpm api:gen
```

- [ ] **Step 5: 运行 HTTP/OpenAPI 聚焦测试**

Run: `dotnet test backend/7DPanel.sln --configuration Release --filter "FullyQualifiedName~ModManagementHttpTests|FullyQualifiedName~OwinWebHostOpenApiSnapshotTests"`

Expected: PASS。

### Task 4：实现 Admin 模组页面

**Files:**
- Create: `frontend/apps/admin/src/features/mods/api/mods.ts`
- Create: `frontend/apps/admin/src/features/mods/api/mods.test.ts`
- Create: `frontend/apps/admin/src/features/mods/model/useMods.ts`
- Create: `frontend/apps/admin/src/features/mods/model/useMods.test.ts`
- Create: `frontend/apps/admin/src/features/mods/ui/ModsView.vue`
- Create: `frontend/apps/admin/src/features/mods/ui/ModStateDialog.vue`
- Create: `frontend/apps/admin/src/features/mods/ui/ModsView.test.ts`
- Create: `frontend/apps/admin/src/features/mods/index.ts`
- Create: `frontend/apps/admin/src/pages/mods.vue`
- Modify: `frontend/apps/admin/src/app/AppShell.vue`
- Modify: `frontend/apps/admin/src/app/AppShell.test.ts`
- Modify: `frontend/apps/admin/src/app/i18n/locales/zh-CN.json`
- Modify: `frontend/apps/admin/src/app/i18n/locales/en.json`

- [ ] **Step 1: 写状态语义测试**

```ts
it('does not claim a disabled-next-start mod is unloaded now', async () => {
  const wrapper = mountMods({ role: 'Owner', mods: [{ isLoadedNow: true, isEnabledNextStart: false }] })
  expect(wrapper.text()).toContain('当前已加载')
  expect(wrapper.text()).toContain('下次启动禁用')
  expect(wrapper.text()).toContain('重启后生效')
})
```

- [ ] **Step 2: 实现 parser、查询和 Mutation**

严格验证 directoryId、元数据字符串、可空 loaded 状态和布尔 next-start 状态。Mutation 固定目标、禁止自动重试，成功失效列表并显示重启提示；409 先刷新，401 清会话，403 保持只读。

- [ ] **Step 3: 实现列表与确认**

列表支持本地关键字筛选和排序，不需要服务端分页；名称、作者、版本、网站、描述、当前状态和下次状态分别显示。受保护模组没有切换按钮并显示原因；Admin/Viewer 完全只读。

- [ ] **Step 4: 增加导航和双语**

路由要求认证，所有角色可进入。外部 website 使用 `target="_blank"` 与 `rel="noopener noreferrer"`；不得把 directoryId 拼成可点击本地路径。

- [ ] **Step 5: 运行前端聚焦测试**

Run: `pnpm exec vitest run src/features/mods src/app/AppShell.test.ts`

Workdir: `frontend/apps/admin`

Expected: PASS。

### Task 5：聚合验证和事实提升

**Files:**
- Modify after verification: `docs/architecture.md`
- Modify after verification: `docs/test.md`

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

- [ ] **Step 3: 更新当前事实**

只提升实际完成的目录安全、运行时/下次状态分离和验证证据；明确未验证真实重启后的装载结果、Windows/Linux 文件语义和浏览器 E2E。
