---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-26-legacy-parity-game-resource-catalog-design.md
last_updated: "2026-07-26"
---

# 旧版本功能对齐：游戏资源目录实施计划

> **面向智能体执行者：** 实施时使用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans`，按任务顺序执行并在每项后核对受控 diff。每个生产行为先取得正确 RED，再做最小实现转 GREEN；不要把导入、编译或测试发现失败当作行为 RED。

**对应规格：** [旧版本功能对齐：游戏资源目录设计规格](../specs/2026-07-26-legacy-parity-game-resource-catalog-design.md)

**目标：** 交付旧版本功能对齐的第一个只读纵向切片，使三个认证角色可以查询当前 7DTD 进程的物品、方块、本地化和安全图标，只有 `Owner` 可以包含隐藏资源，并在 Admin `/game-resources` 中完整处理目录可用状态。

**架构：** Application 拥有查询、授权裁剪、排序和分页合同；SevenDays Adapter 在 `GameStartDone` 后通过现有有界游戏线程调度器复制游戏标量，再在后台索引批准图标根并原子发布一份不可变快照；Web Adapter 只映射认证、Problem Details、ETag 和 HTTP DTO；Admin 使用生成客户端查询 JSON，使用 Header Bearer fetch 读取不透明资源 ID 对应的 PNG Blob。

**技术栈：** C# `11.0`、.NET Framework `4.8`、ASP.NET Web API 2/Katana、Microsoft.Extensions.DependencyInjection、xUnit、Vue `3.5`、TypeScript `6.0`、Vue Router、Pinia、Nuxt UI `4`、Vite `8`、Vitest、Vue Test Utils、Hey API、pnpm `11`。

## 全局执行约束

- 旧 backend `277996d`、旧 frontend `60fc816` 只作为行为证据；字段语义以当前 `7dtd-reference/v3.0.1-b4` 和编译结果重新确认，不复制旧代码、旧 DTO 或旧页面。
- 当前 7DPanel 基线为 `ad58dcc`；保留执行开始时已有的用户改动，不重写无关文件。
- 不创建 Domain 类型、SQLite migration、审计记录、`_gap` 表、通用 registry、通用文件服务、通用缓存框架或只有测试消费者的生产抽象。
- HTTP 和后台线程不得持有 `ItemClass`、`Localization`、Unity、`FileInfo` 或 `DirectoryInfo` 活对象；游戏对象只在 `GameThreadDispatcher.Enqueue` 回调内转为产品自有标量。
- 图标接口只接受当前快照的不透明 `resourceId`；任何公开 DTO、日志和错误都不得包含图标名、游戏路径、Mod 路径或原始异常文本。
- Admin 页面保持只读，不加入背包、发放物品、商店、奖励、技能或自动化入口。
- 迭代时只运行当前任务的聚焦测试；实现稳定后在任务 9 运行一次后端聚合门禁和一次 Admin 生产构建。默认不运行 Playwright、发布、真实 7DTD、备份恢复或跨平台 smoke。
- 如果当前引用无法确认 Localization 列、Mod 图标覆盖顺序或批准根，先停止对应实现并记录证据缺口；只在该缺口确实阻止完成时增加一次只读 Windows `v3.0.1-b4` 窄 smoke。
- 本计划不授权 `git commit`、`git push`、`git reset` 或 `git revert`。每项检查点保持未提交；只有用户另行授权后才执行 Git 历史操作。

---

### 任务 1：建立 Application 目录合同和查询语义

**文件：**

- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/GameResources/GameResourceCatalogModels.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/GameResources/IGameResourceCatalog.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/GameResources/QueryGameResourcesUseCase.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/GameResources/GetGameResourceIconUseCase.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/GameResourceUseCaseTests.cs`

**固定接口：**

```csharp
public enum GameResourceKind { Item, Block }
public enum GameResourceVisibility { Public, Hidden }
public enum GameResourceIconStatus { Available, Missing, Invalid }
public enum GameResourceCatalogStatus { Building, Available, Unavailable }
public enum GameResourceAccess { Standard, Owner }

public sealed class GameResourceQuery
{
    public string? Search { get; }
    public GameResourceKind? Kind { get; }
    public bool IncludeHidden { get; }
    public string Language { get; }
    public int Page { get; }
    public int PageSize { get; }
}

public interface IGameResourceCatalog
{
    GameResourceCatalogReadResult Read();
    Task<GameResourceIconReadResult> ReadIconAsync(
        string catalogVersion,
        string resourceId,
        CancellationToken cancellationToken);
}
```

`GameResourceCatalogSnapshot` 固定包含 `CatalogVersion`、可空 `GameVersion`、`ObservedAtUtc`、不可变 `Resources` 和不可变 `Warnings`。条目固定包含 spec 已批准的十个字段，不把路径或游戏对象加入 Application。

- [x] **步骤 1：写查询和图标用例失败测试**

  使用内存 Stub catalog 覆盖：`zh-CN`/`en`、非法语言、trim 后搜索、物品/方块筛选、`Standard` 拒绝隐藏项、`Owner` 包含隐藏项、未知 visibility 已在快照中按 hidden 处理、稳定三级排序、页码越界空结果、页大小边界、Building/Unavailable 透传、隐藏图标对 Standard 映射为 Missing。

  代表性断言：

  ```csharp
  [Fact]
  public void Standard_access_cannot_request_hidden_resources()
  {
      var useCase = CreateUseCase(AvailableSnapshot());

      Assert.Throws<GameResourceHiddenForbiddenException>(() =>
          useCase.Execute(new GameResourceQuery(null, null, true, "en", 1, 50),
              GameResourceAccess.Standard));
  }

  [Fact]
  public void Results_are_sorted_then_paged_with_real_total()
  {
      var result = CreateUseCase(UnsortedSnapshot()).Execute(
          new GameResourceQuery("  iron  ", null, false, "en", 2, 1),
          GameResourceAccess.Owner);

      Assert.Equal(2, result.Total);
      Assert.Single(result.Items);
  }
  ```

- [x] **步骤 2：运行测试并确认正确 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~GameResourceUseCaseTests"
  ```

  若文件或导出缺失，先创建签名正确且抛出 `NotImplementedException` 的最小骨架；正确 RED 必须落在权限、筛选、排序、分页或状态断言。

- [x] **步骤 3：实现最小 Application 合同**

  `QueryGameResourcesUseCase` 按隐藏策略、类别、搜索、稳定排序、分页的固定顺序工作；只允许 `zh-CN` 和 `en`，`page` 为 `1..100000`，`pageSize` 为 `1..100`，非空搜索为 `1..100` 字符。`GetGameResourceIconUseCase` 先从同一快照确认条目和 visibility，再把 `catalogVersion` 与 `resourceId` 交给端口，禁止 Standard 探测隐藏 ID。

- [x] **步骤 4：转 GREEN 并记录检查点**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~GameResourceUseCaseTests"
  git diff --check -- backend/src/Core/LSTY.SevenDPanel.Application/GameResources backend/tests/LSTY.SevenDPanel.Tests/GameResourceUseCaseTests.cs
  git status --short
  ```

  预期：聚焦测试通过；只有本任务文件与进入任务前已存在的文档改动。

### 任务 2：在游戏线程复制资源与本地化标量

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/GameResources/GameResourceScalarDraft.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/GameResources/SevenDaysGameResourceDraftReader.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/SevenDaysGameResourceDraftReaderTests.cs`

**固定边界：**

```csharp
internal sealed class GameResourceScalarDraft
{
    public string? GameVersion { get; }
    public DateTimeOffset ObservedAtUtc { get; }
    public IReadOnlyList<GameResourceScalarEntry> Resources { get; }
    public IReadOnlyList<GameResourceIconRootDescriptor> IconRoots { get; }
    public IReadOnlyList<string> Warnings { get; }
}

internal sealed class SevenDaysGameResourceDraftReader
{
    public Task<GameResourceScalarDraft> ReadAsync(CancellationToken cancellationToken);
}
```

- [x] **步骤 1：用当前引用确认字段入口并写失败测试**

  只从 `ItemClass.list` 的有效项读取 numeric ID、内部名、`IsBlock()`、`CreativeMode`、最大堆叠、品质能力、iconName 和 tint；从当前 Localization 元数据按列名解析 `schinese` 与 `english`；从当前已加载 Mod 顺序复制根描述。测试注入纯标量采集委托，覆盖空项、无效内部名、未知 creative 枚举按 hidden、无效堆叠为 null、不可确认品质为 null、白色 tint 为 null、非法 tint 增加 warning、缺列时对应语言全 null、重复内部名的最终覆盖顺序和歧义失败。

- [x] **步骤 2：确认 RED 后实现 dispatcher 内复制**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~SevenDaysGameResourceDraftReaderTests"
  ```

  生产入口必须是：

  ```csharp
  return GameThreadDispatcher.Enqueue(
      "7dpanel.game-resources.capture",
      CaptureScalarDraft,
      TimeSpan.FromSeconds(5),
      cancellationToken);
  ```

  `CaptureScalarDraft` 返回前完成所有字符串、整数、布尔、枚举归一化和根描述复制。后台代码不得再次访问 `ItemClass`、`Localization`、`ModManager` 或 Unity 颜色对象。

- [x] **步骤 3：验证字段边界和不可变性**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~SevenDaysGameResourceDraftReaderTests|FullyQualifiedName~GameThreadDispatcherTests"
  git diff --check -- backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/GameResources backend/tests/LSTY.SevenDPanel.Tests/SevenDaysGameResourceDraftReaderTests.cs
  ```

  预期：游戏访问只发生在 dispatcher action；返回集合不可变；测试不依赖真实服务器目录。

### 任务 3：实现批准根图标索引和安全读取

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/GameResources/GameResourceIconIndex.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/GameResources/SevenDaysGameResourceCatalog.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/GameResourceIconIndexTests.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/SevenDaysGameResourceCatalogTests.cs`

**发布模型：**

```csharp
internal sealed class SevenDaysGameResourceCatalog : IGameResourceCatalog
{
    public Task BuildAsync(CancellationToken cancellationToken);
    public GameResourceCatalogReadResult Read();
    public Task<GameResourceIconReadResult> ReadIconAsync(
        string catalogVersion,
        string resourceId,
        CancellationToken cancellationToken);
}
```

- [x] **步骤 1：写叶文件名、根目录和覆盖顺序失败测试**

  每个测试使用独立临时目录，覆盖普通 `.png`、大小写后缀、缺失文件、`/`、`\`、`..`、卷分隔符、控制字符、超过 128 字符、非普通文件、根目录不存在、访问拒绝、reparse/symlink 越界、基础与 Mod 覆盖顺序、目录枚举顺序不影响结果。测试结束只删除测试自己创建且已解析位于测试根内的目录。

- [x] **步骤 2：运行图标测试确认 RED，再实现一次性索引**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~GameResourceIconIndexTests"
  ```

  索引规则固定为：规范化批准根；拒绝越界或 reparse 逃逸；按游戏确认的覆盖顺序建立叶文件名映射；为每个资源生成不可预测 URL-safe ID；JSON 快照只保留状态和 ID，私有索引保留规范文件映射。HTTP 请求期间不得枚举目录。

- [x] **步骤 3：写原子发布和读取时复核失败测试**

  覆盖初始 Building、成功后 Available、构建异常后 Unavailable、只有完整索引才能替换当前快照、同一时刻只允许一个构建、取消不会发布半成品、图标被删/替换/越界后 Missing、旧 `catalogVersion` 拒绝、ETag 随版本/ID/长度/最后写入时间变化、读取内容固定声明 `image/png`。

- [x] **步骤 4：实现 catalog 并转 GREEN**

  图标读取在打开前重新解析规范路径并确认仍位于批准根；只读取单个有界 PNG 为 `byte[]`，捕获文件系统异常并返回稳定 Missing/Unavailable，不记录真实路径。使用 `Interlocked.Exchange` 原子发布不可变 holder，不复制 PNG 到快照内存。

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~GameResourceIconIndexTests|FullyQualifiedName~SevenDaysGameResourceCatalogTests"
  git diff --check -- backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/GameResources backend/tests/LSTY.SevenDPanel.Tests
  ```

  预期：安全、发布和失败状态测试通过；没有新增 Local/Persistence 依赖。

### 任务 4：接入 GameStartDone 生命周期和依赖组合

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/GameResources/GameResourceCatalogRuntime.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/GameResourceCatalogRuntimeTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyInjectionTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyRulesTests.cs`

- [x] **步骤 1：写 lifecycle 失败测试**

  `GameResourceCatalogRuntime` 作为现有 `IModRuntime` 装饰层：`Start` 只启动 inner；第一次 `MarkGameReady` 先转发 inner，再启动一次目录构建；重复 ready 不重复构建；`Stop` 取消构建、等待现有有界时限并继续停止 inner；两个停止分支都尝试，异常聚合。不得新订阅静态 ModEvent。

  ```csharp
  public void MarkGameReady()
  {
      inner.MarkGameReady();
      StartCatalogBuildOnce();
  }
  ```

- [x] **步骤 2：确认 RED 后实现 runtime 和 DI**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~GameResourceCatalogRuntimeTests"
  ```

  在 `PanelServiceProviderFactory` 注册同一个 `SevenDaysGameResourceCatalog` 为 `IGameResourceCatalog`，注册两个 Application 用例，并把 `GameResourceCatalogRuntime` 加入现有 runtime 链。不要改 `SevenDaysGameLifecycleAdapter` 或增加第二条 `GameStartDone` 订阅路径。

- [x] **步骤 3：验证组合和项目依赖**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~GameResourceCatalogRuntimeTests|FullyQualifiedName~DependencyInjectionTests|FullyQualifiedName~DependencyRulesTests|FullyQualifiedName~SevenDaysGameLifecycleAdapterTests"
  git diff --check -- backend/src/Bootstrap/LSTY.SevenDPanel backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays backend/tests/LSTY.SevenDPanel.Tests
  ```

  预期：Service Provider build 验证通过；Application 不引用 SevenDays/Web，Web 不直接引用 SevenDays，生命周期仍只有一个 ready 入口。

### 任务 5：交付认证 HTTP、PNG 响应和 OpenAPI 合同

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/GameResourcesController.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/GameResourceHttpModels.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/GameResourcesHttpTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostOpenApiSnapshotTests.cs`
- 修改：`frontend/apps/admin/openapi/7dpanel.v1.json`
- 修改：`frontend/apps/admin/src/shared/api/generated/client.gen.ts`
- 修改：`frontend/apps/admin/src/shared/api/generated/sdk.gen.ts`
- 修改：`frontend/apps/admin/src/shared/api/generated/types.gen.ts`

**路由：**

```csharp
[Authorize]
[RoutePrefix("api/v1/game-resources")]
public sealed class GameResourcesController : ApiController
{
    [HttpGet, Route("")]
    public HttpResponseMessage Get(...);

    [HttpGet, Route("{resourceId}/icon")]
    public Task<HttpResponseMessage> GetIcon(string resourceId);
}
```

- [x] **步骤 1：写 HTTP 行为失败测试**

  覆盖未认证 401、三角色普通目录 200、Standard 请求隐藏 403 `game-resource-hidden-forbidden`、非法 search/kind/language/page/pageSize 400、Building/Unavailable 503 和有界 `Retry-After`、超页 200 空 items、隐藏图标对 Standard 404、缺失图标 404、PNG 200、Bearer 必需、`If-None-Match` 304、取消 token 透传。成功 JSON 断言 camelCase、UTC、nullable 和不含任何路径字段。

- [x] **步骤 2：确认 RED 后实现参数与角色映射**

  Controller 只通过 `User.IsInRole("Owner")` 映射 `GameResourceAccess.Owner`，其余认证角色映射 `Standard`。`kind=all` 映射 null；Controller 不执行筛选或目录枚举。Problem Details 使用 spec 固定 code，不回显输入路径或异常。

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~GameResourcesHttpTests"
  ```

- [x] **步骤 3：实现二进制响应头并转 GREEN**

  图标成功响应使用 `ByteArrayContent`，固定 `Content-Type: image/png`、`X-Content-Type-Options: nosniff`、`Cache-Control: private` 和服务端 ETag；匹配当前 ETag 或 `*` 时清理 content 并返回 304。读取失败统一为稳定 Problem Details。

- [x] **步骤 4：刷新 OpenAPI 和生成客户端**

  在 `OwinWebHostOpenApiSnapshotTests.cs` 增加 `AssertGameResourcesContractSemantics`，精确断言两个 operationId、响应码、Bearer security、查询参数、必填/nullable 字段，以及图标 200 的唯一 `image/png` 和 200/304 ETag headers。先用仓库既有环境变量刷新 snapshot，清除变量后再验证运行时文档与 snapshot 一致，最后生成 Admin 客户端：

  ```powershell
  $env:SEVENDPANEL_UPDATE_ADMIN_OPENAPI_SNAPSHOT = "1"
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~Openapi_document_matches_admin_codegen_snapshot"
  Remove-Item Env:SEVENDPANEL_UPDATE_ADMIN_OPENAPI_SNAPSHOT
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~GameResourcesHttpTests|FullyQualifiedName~Openapi_document_matches_admin_codegen_snapshot"
  Set-Location frontend/apps/admin
  pnpm api:gen
  Set-Location ../../..
  ```

  预期：JSON 查询和 `image/png` 端点均有唯一稳定 operationId；生成 SDK 暴露目录查询。`api:gen` 只修改受控 snapshot/generated 文件，不手改生成代码。

- [x] **步骤 5：记录未提交合同检查点**

  ```powershell
  git diff --check -- backend frontend/apps/admin/openapi frontend/apps/admin/src/shared/api/generated
  git status --short
  ```

  当前生成变更未提交时不运行基于 Git 基线的 `pnpm api:check`，因为它会把本切片的预期生成 diff 报为漂移；用户授权提交后再把该命令作为基线门禁执行。

### 任务 6：实现 Admin 目录 API、URL 筛选和查询状态机

**文件：**

- 新建：`frontend/apps/admin/src/features/game-resources/api/gameResources.ts`
- 新建：`frontend/apps/admin/src/features/game-resources/api/gameResources.test.ts`
- 新建：`frontend/apps/admin/src/features/game-resources/model/gameResourceFilters.ts`
- 新建：`frontend/apps/admin/src/features/game-resources/model/gameResourceFilters.test.ts`
- 新建：`frontend/apps/admin/src/features/game-resources/model/useGameResources.ts`
- 新建：`frontend/apps/admin/src/features/game-resources/model/useGameResources.test.ts`
- 新建：`frontend/apps/admin/src/features/game-resources/index.ts`

**公开模型：**

```ts
export type GameResourceKind = 'item' | 'block'
export type GameResourceVisibility = 'public' | 'hidden'
export type GameResourceIconStatus = 'available' | 'missing' | 'invalid'
export type GameResourceViewState =
  | 'loading' | 'success' | 'empty' | 'stale'
  | 'building' | 'unavailable' | 'forbidden'
```

- [x] **步骤 1：写 parser 和请求失败测试**

  `fetchGameResources` 调用任务 5 生成的 SDK，并对未知响应做运行时验证。测试拒绝未知 kind/visibility/iconStatus、非法时间、非法分页、空 resourceId、非六位 tint、额外路径字段；允许 nullable gameVersion/localizedName/maxStack/hasQuality/iconTintHex。构造 query 时省略空搜索和 `kind=all`，不传 Authorization 字符串参数，由全局生成客户端注入 Header。

- [x] **步骤 2：确认 RED 后实现 API parser**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm test -- src/features/game-resources/api/gameResources.test.ts
  Set-Location ../../..
  ```

  解析后对根、warnings、items 和每个条目做 `Object.freeze`；无效成功响应抛出共享 `HttpError('invalid', ...)`，错误文本不包含响应 body。

- [x] **步骤 3：写 URL 筛选和 composable 失败测试**

  覆盖 URL 默认值、刷新恢复、非法 query 归一化、非 Owner 强制移除 `includeHidden`、搜索 250ms debounce、其他筛选立即请求、翻页、请求取消、同一时刻 single-flight、成功/Empty、Building 有界重试、Unavailable 手动重试、403 Forbidden、刷新失败保留最后成功页和原 `observedAtUtc` 并进入 Stale、卸载释放 timer/controller。

- [x] **步骤 4：实现最小状态机并转 GREEN**

  `useGameResources` 不进入 Pinia；它拥有当前 URL filters、请求 controller、retry timer 和最后成功页。搜索只在写入 route query 前 debounce，网络层不维护第二份输入；新请求开始时取消旧请求，aborted 不覆盖页面状态。

  ```powershell
  Set-Location frontend/apps/admin
  pnpm test -- src/features/game-resources/api/gameResources.test.ts src/features/game-resources/model
  pnpm typecheck
  Set-Location ../../..
  ```

  预期：聚焦测试和类型检查通过；无 timer、watcher 或 AbortController 泄漏。

### 任务 7：实现 Header Bearer 图标与响应式只读页面

**文件：**

- 新建：`frontend/apps/admin/src/features/game-resources/model/useGameResourceIcon.ts`
- 新建：`frontend/apps/admin/src/features/game-resources/model/useGameResourceIcon.test.ts`
- 新建：`frontend/apps/admin/src/features/game-resources/ui/GameResourceIcon.vue`
- 新建：`frontend/apps/admin/src/features/game-resources/ui/GameResourceIcon.test.ts`
- 新建：`frontend/apps/admin/src/features/game-resources/ui/GameResourcesView.vue`
- 新建：`frontend/apps/admin/src/features/game-resources/ui/GameResourcesView.test.ts`
- 修改：`frontend/apps/admin/src/features/game-resources/index.ts`

- [x] **步骤 1：写认证图标生命周期失败测试**

  覆盖：URL 只由 `encodeURIComponent(resourceId)` 构造；fetch 固定 `credentials: 'omit'` 和 `Authorization` Header；不把 Bearer 放入 URL；进入视口才请求；响应非 PNG、401/403/404/5xx 显示同一占位；资源 ID 替换、组件卸载和会话失效时 abort 并 `URL.revokeObjectURL`；迟到响应不能覆盖新资源。

- [x] **步骤 2：确认 RED 后实现图标 composable/component**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm test -- src/features/game-resources/model/useGameResourceIcon.test.ts src/features/game-resources/ui/GameResourceIcon.test.ts
  Set-Location ../../..
  ```

  使用 `IntersectionObserver` 注入点保持确定性测试；无浏览器 observer 时可立即加载但仍遵守 Header Bearer。组件只接收 `resourceId`、`iconStatus` 和 alt，不接收图标名、路径或 tint 查询参数。

- [x] **步骤 3：写页面状态、字段和权限失败测试**

  mock `useGameResources`，覆盖 Loading skeleton、Success 元数据、Empty 清除筛选、Stale 保留 rows、Building、Unavailable、Partial warning、Forbidden；同一 fixture 在桌面表格和窄屏条目中都能看到本地化回退、内部名、类型、堆叠、品质、可见性、tint 色块/文本和复制按钮。非 Owner 不渲染隐藏开关；页面不存在发放、删除、商店、奖励或自动化动作。

- [x] **步骤 4：实现 Nuxt UI 页面并转 GREEN**

  `GameResourcesView` 使用 `UDashboardPanel`、`UDashboardNavbar`、`UInput`、`USelect`/分段控件、`UCheckbox`、`UTable`、`UBadge`、`USkeleton` 和 `UButton`。桌面表格从 `md` 起显示；窄屏单列条目不依赖横向滚动。复制操作只调用 `navigator.clipboard.writeText(internalName)`，成功反馈由 i18n key 提供。

  ```powershell
  Set-Location frontend/apps/admin
  pnpm test -- src/features/game-resources
  pnpm typecheck
  Set-Location ../../..
  ```

  预期：页面和图标行为测试通过；Blob URL 和观察器均被释放。

### 任务 8：接入路由、导航、搜索和双语文案

**文件：**

- 新建：`frontend/apps/admin/src/pages/game-resources.vue`
- 修改：`frontend/apps/admin/src/app/AppShell.vue`
- 修改：`frontend/apps/admin/src/app/AppShell.test.ts`
- 修改：`frontend/apps/admin/src/app/router.test.ts`
- 修改：`frontend/apps/admin/src/app/i18n/locales/zh-CN.json`
- 修改：`frontend/apps/admin/src/app/i18n/locales/en.json`
- 修改：`frontend/apps/admin/src/app/i18n/messages.test.ts`

- [x] **步骤 1：写路由、导航和 i18n 失败测试**

  断言 `/game-resources` 只有 `requiresAuth: true`，不设置角色裁剪；Owner/Admin/Viewer 都能在侧栏“玩家与世界”/“Players and world”组及 Dashboard Search 找到“游戏资源”/“Game resources”；现有玩家入口仍在同一组；快捷键 `g-r` 进入页面；非 Owner 直接 URL 仍可进入普通目录。`messages.test.ts` 要求两个 locale 拥有相同 `gameResources.*` 和新增分组 key。

- [x] **步骤 2：确认 RED 后组合页面**

  ```vue
  <route lang="json">
  { "meta": { "requiresAuth": true } }
  </route>

  <script setup lang="ts">
  import { GameResourcesView } from '../features/game-resources'
  </script>

  <template><GameResourcesView /></template>
  ```

  `AppShell.vue` 提取 `playerAndWorldNavigation`，包含现有玩家入口和游戏资源入口；主导航用“玩家与世界”分组承载这两个 children，Dashboard Search 展平复用相同 child 的 label/icon/to。保留 `g-p` 并增加 `g-r`。隐藏筛选只由 Feature 根据 `auth.role === 'Owner'` 显示，不通过隐藏导航实现授权。

- [x] **步骤 3：转 GREEN 并执行前端聚焦门禁**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm test -- src/features/game-resources src/app/AppShell.test.ts src/app/router.test.ts src/app/i18n/messages.test.ts
  pnpm typecheck
  pnpm exec eslint src/features/game-resources src/pages/game-resources.vue src/app/AppShell.vue
  Set-Location ../../..
  ```

  预期：页面 route map、三个角色导航和双语 key 全部通过；定向 lint 无错误。

### 任务 9：聚合验证并把实现证据提升到 Current 文档

**文件：**

- 修改：`docs/architecture.md`
- 修改：`docs/design.md`
- 修改：`docs/test.md`
- 修改：`docs/architecture/legacy-feature-parity-target-blueprint.md`
- 修改：`docs/architecture/backend-target-blueprint.md`
- 修改：`docs/architecture/admin-frontend-target-blueprint.md`
- 修改：`backend/README.md`
- 修改：`frontend/apps/admin/README.md`
- 按实际命令影响评估后修改：`README.md`
- 更新：本计划

- [x] **步骤 1：运行用户批准的精简后端门禁**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-restore --filter "LSTY.SevenDPanel.Tests.GameResource"
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-restore --filter "LSTY.SevenDPanel.Tests.DependencyInjectionTests"
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-restore --filter "LSTY.SevenDPanel.Tests.OwinWebHostTests.Openapi_document_matches_admin_codegen_snapshot"
  ```

  结果：游戏资源 `55/55`、组合根 `12/12`、OpenAPI `1/1` 通过。测试构建覆盖六个产品项目；按用户要求未重复解决方案全量测试、publish 或真实 7DTD。

- [x] **步骤 2：运行一次最终 Admin 门禁**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm test -- src/features/game-resources src/app/AppShell.test.ts src/app/router.test.ts src/app/i18n/messages.test.ts
  pnpm typecheck
  pnpm exec eslint src/features/game-resources src/pages/game-resources.vue src/app/AppShell.vue
  pnpm build
  pnpm api:gen
  Set-Location ../../..
  ```

  预期：聚焦 Vitest、typecheck、定向 lint 和生产构建通过；第二次 `api:gen` 不再产生新的内容漂移。默认不重复 Admin 全量 Vitest/lint，也不运行 Playwright。

- [x] **步骤 3：仅按实际证据更新 Current 文档**

  - `docs/architecture.md`：记录已实现的 Application 端口、SevenDays 标量复制/图标索引、runtime 链、Web Controller、Admin Feature 和依赖方向；不记录旧版未来模块已经存在。
  - `docs/design.md`：把 `/game-resources` 的实际导航、筛选、桌面/窄屏和状态提升为当前界面事实。
  - `docs/test.md`：记录精确命令、通过数量、未执行 Playwright/publish/真实 7DTD，以及确实存在的证据缺口。
  - 三份 Target blueprint：只把本切片已验证条目提升为已采用，保留背包、商店、奖励和后续阶段为目标。
  - backend/Admin README：只增加所属运行或验证说明；根 README 仅在聚合命令或仓库入口实际变化时修改。
  - `docs/PRD.md` 保持产品级功能对齐状态，不因第一切片完成就宣称 `CAP-08`～`CAP-12` 全部实现。

- [x] **步骤 4：执行精简文档与工作区校验**

  ```powershell
  git diff --check
  git status --short
  ```

  同时检查本轮 Markdown 本地链接、front matter、简体中文政策和占位表达。确认 `docs/architecture.md` 中的每项当前事实都有代码或验证输出支撑，且未把私有 `7dtd-reference/` 内容纳入产品发布物。

- [x] **步骤 5：记录未提交完成检查点**

  将实际完成项勾选，并在本计划记录自动化数量、未执行门禁及原因。保持工作区未提交，等待用户审阅并明确授权 Git 操作；授权提交后在干净生成基线上运行：

  ```powershell
  Set-Location frontend/apps/admin
  pnpm api:check
  Set-Location ../../..
  ```

## 实施结果（2026-07-26）

- Application、SevenDays、Web 和 Admin 四域由并发子代理实现，主代理串行完成 DI/runtime、OpenAPI snapshot、Hey API 生成 SDK 和真实页面 transport 集成；旧项目只用于字段与行为证据，没有复制其代码或 UI。
- 后端聚焦门禁为 `55/55`、`12/12`、`1/1`；Admin 聚焦集合完成 `12` 个文件、`99/99` 项断言，happy-dom 仍输出已知 `AbortError`/worker 退出噪声。修正请求生命周期后，相关 `2` 个文件、`8/8` 项以成功状态完成复验；typecheck、聚焦 ESLint 和 Vite build 成功。
- OpenAPI snapshot 在受控更新变量下刷新，清除变量后复验通过；`pnpm api:gen` 成功，`/game-resources` 使用生成 `gameResourcesGet()` 的真实 adapter。未手工编辑生成目录。
- 按用户明确要求缩减验证，未运行后端/前端全量测试、全量 lint、Playwright、publish、真实 7DTD、Windows/Linux smoke 或旧系统黑盒。`pnpm api:check` 保留到授权提交后的干净生成基线。
- Critical/Important 限定复审未在时间盒内产出结论，已按用户要求终止，不继续扩大复审范围；交付结论以主代理的受控 diff 检查和上述聚焦自动化证据为准。
- README 的聚合命令和运行入口没有变化，因此未为本切片追加重复命令；PRD 保持 `In Review`，`CAP-08` 至 `CAP-12` 没有被提前标记完成。

## 完成标准

- 三个认证角色能查询 public 物品与方块，只有 `Owner` 能查询 hidden；服务端授权和图标防探测都有行为测试。
- `GameStartDone` 只触发一次有界构建；游戏活对象仅在 dispatcher 内转换为标量，HTTP 只读不可变快照。
- 目录查询实现两种语言、字段空值、稳定搜索/排序/分页和 Building/Unavailable/Partial 诚实状态。
- 图标只通过不透明 ID 读取，批准根、覆盖顺序、路径穿越、reparse 逃逸、文件替换、ETag/304 和路径脱敏均有聚焦测试。
- Admin `/game-resources` 对 Owner/Admin/Viewer 可达，URL 可恢复筛选，图标使用 Header Bearer Blob，并完整处理 Loading、Success、Empty、Stale、Building、Unavailable、Partial 和 Forbidden。
- 页面保持只读，不出现本切片外写能力或通用资源基础设施。
- 后端 Release build/聚合测试，以及 Admin 聚焦 Vitest、typecheck、定向 lint、生产构建和 OpenAPI 生成达到任务 9 记录的结果；未运行的高成本门禁明确记录。
- Current 架构、设计、测试和所属 README 只陈述实现证据；PRD 和后续迁移阶段不被提前标记完成。
