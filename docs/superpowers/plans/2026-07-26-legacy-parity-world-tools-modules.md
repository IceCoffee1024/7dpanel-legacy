---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-26-legacy-parity-world-tools-modules-design.md
last_updated: "2026-07-26"
---

# 旧版本功能对齐第六波：世界工具、地图作业与功能模块实施计划

> **面向智能体执行者：** 必须使用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans` 逐项实施本计划，并用 checkbox（`- [ ]`）跟踪步骤。每个生产行为先取得正确 RED，再做最小实现转 GREEN；底层合同、migration、DI/runtime、OpenAPI 与生成客户端必须串行合流。

**对应规格：** [旧版本功能对齐第六波：世界工具、地图作业与功能模块设计规格](../specs/2026-07-26-legacy-parity-world-tools-modules-design.md)

## 2026-07-26 当前执行记录

- **代码存在：** 经 CodeGraph 与工作区文件核对，`013_WorldToolsAndFeatureModules.sql`、世界只读合同、世界操作与 change set、恢复/undo、地图作业、功能模块 policy/store/use cases、HTTP/OpenAPI、生产 DI/runtime，以及 Admin `world-tools`、`modules` 的 API、composable、组件和页面源码均已存在。代码存在不等同于相关 RED/GREEN 命令或发布门禁已经运行。
- **测试工件现状：** 计划列出的多数后端测试文件和 Admin world-tools 测试已存在；`BlockPrefabOperationUseCaseTests.cs`、`FeatureModuleConsumerCoverageTests.cs`、`featureModules.test.ts` 当前不存在，因此其所在“写 RED”步骤不能视为整体完成。本次收口未取得足以勾选其余后端 RED/GREEN 命令步骤的运行记录。
- **已有聚焦证据：** Admin world-tools API/composable 聚焦测试曾有 `51` 项通过；该证据早于页面完成。主线程本轮执行 Bootstrap Release build，结果为 `0 warning`、`0 error`，但该 build 不替代本计划的后端聚合测试。
- **Admin 聚焦证据：** 页面完成后重新执行 `pnpm typecheck`，结果通过；随后使用明确文件过滤运行 AppShell、路由、i18n 与 Community 聚焦测试，结果为 `6` 个文件、`74` 项测试通过。补充公共 locale key 后，`messages.test.ts` 再次执行并有 `6` 项通过。该证据不包含 world-tools、modules、player-map 或定向 lint，因此任务 10 的完整聚焦 GREEN 步骤仍保持未勾选。
- **未执行门禁：** 未运行本计划后端聚合测试、完整 Admin 聚合门禁、Playwright、publish、真实 7DTD 或危险世界 smoke。一次带字面量 `--` 的错误 Vitest 调用意外扩展为全量测试并出现 `7` 项失败，不能作为聚合门禁证据，也不据此勾选任何步骤。危险 smoke 尚未确认精确测试实例、前置备份和回滚目标，必须继续保持未完成；本次授权也不包含任何 Git 历史操作。
- **checkbox 口径：** 仅对已由当前工件直接证明完成且不以尚未运行命令或真实环境为完成条件的整条步骤勾选；实现与命令合并在同一 checkbox 时，只要命令证据缺失，该步骤仍保持未勾选。

**目标：** 交付世界只读详情、类型化地图/世界持久作业、可验证撤销、受控 XML reload/GC，以及只治理真实消费者的编译期功能模块目录，使所有危险动作都具备固定目标、Owner 授权、持久状态、审计和诚实失败语义。

**架构：** Application 拥有只读标量、各类 operation 参数、确认和模块策略；SevenDays Adapter 只在 `GameThreadDispatcher` 内接触 7DTD/Unity 活对象，并把长区域工作拆成有帧预算的批次；第二波持久作业拥有通用生命周期，SQLite 只增加世界类型化意图、change set 与模块状态。Web 暴露独立 DTO/路由，Admin 通过生成客户端恢复 operation，不从地图 popup 直接执行写操作。

**技术栈：** C# `11.0`、.NET Framework `4.8`、ASP.NET Web API 2/Katana、SQLite、Microsoft.Extensions.DependencyInjection、xUnit、Vue `3.5` Composition API、TypeScript `6.0`、Vue Router、Pinia、Nuxt UI `4`、Vite `8`、Vitest、Vue Test Utils、Hey API、pnpm `11`。

## 权威依据、执行边界与依赖顺序

- 产品与验收以[产品需求](../../PRD.md)、[当前设计](../../design.md)、[当前架构](../../architecture.md)和[测试策略](../../test.md)为准；未来边界参考[后端目标蓝图](../../architecture/backend-target-blueprint.md)、[Admin 目标蓝图](../../architecture/admin-frontend-target-blueprint.md)和[旧版功能对齐目标蓝图](../../architecture/legacy-feature-parity-target-blueprint.md)。Target 文档不是代码存在证据。
- 仓库外旧项目 `7dtd-serveradmin` 只读，用于提取字段、操作意图与失败行为；不得复制其命令、动态 descriptor registry、进程内 undo 或地图即时 mutation 代码。实际 API 必须由 `7dtd-reference/v3.0.1-b4`、编译和最小 smoke 共同确认。
- 本计划是六波次最后一波。任务 1 开始前，第二波必须已经交付可持久查询的作业提交、worker、重启恢复、取消和进度合同；本波只实现 `IWorldOperationJobBridge` 适配，不创建第二张通用 job 表、第二个通用 worker 或另一套生命周期。
- 六波按批准顺序合流时，本波 migration 固定为 `013_WorldToolsAndFeatureModules.sql`；`008` 至 `012` 必须已由前五波占用。若执行基线不满足该顺序，停止 migration 编辑并先让主代理解决编号冲突，禁止静默改号或覆盖已有脚本。
- 任务 1、7、9 串行；任务 2 完成后，任务 3 至 6 可由独立 workers 并行；任务 7 合流全部 handler、恢复和 DI；任务 8 只能在所有真实模块消费者存在后执行；任务 9 再统一 OpenAPI 与 SDK；任务 10 组合 Admin；任务 11 只做一次最终门禁、一次真实 7DTD smoke 会话和文档提升。
- 每类操作只接受自己的请求类型。不得加入万能 payload、通用 action 字符串、脚本平台、事件总线、程序集扫描、动态加载、反射路由、服务端路径或只有测试消费者的抽象。
- 迭代只运行当前任务列出的聚焦测试。除任务 11 外，不运行后端全量测试、Admin 全量 Vitest/lint/build、Playwright、publish 或真实 7DTD。
- 本计划不授权 `git commit`、`git push`、`git reset`、`git revert` 或其他 Git 历史操作；所有检查点保持未提交，等待用户显式授权。

---

### 任务 1：锁定第二波作业桥、世界 operation 元数据与 `013` migration

**文件：**

- 新建 Application：`backend/src/Core/LSTY.SevenDPanel.Application/WorldOperations/WorldOperationModels.cs`、`IWorldOperationStore.cs`、`IWorldOperationJobBridge.cs`。
- 新建 SQLite：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Migrations/013_WorldToolsAndFeatureModules.sql`、`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/WorldOperations/SqliteWorldOperationStore.cs`、`SqliteWorldOperationJobBridge.cs`。
- 新建测试：`backend/tests/LSTY.SevenDPanel.Tests/WorldOperationContractTests.cs`、`SqliteWorldOperationStoreTests.cs`。

**固定合同：**

```csharp
public enum WorldOperationStatus
{
    Queued, Running, Succeeded, Failed, Cancelled,
    Interrupted, ResultUnknown, RollbackFailed
}

public enum WorldOperationKind
{
    DeleteLandClaim, MoveOnlinePlayer, MoveEntity,
    RefreshMapResources, RenderExploredMap, RenderFullMap,
    CopyRegion, FillRegion, ClearRegion, PasteRegion,
    SetBlock, PlacePrefab, RemovePrefab,
    SpawnEntity, DeleteEntity, CleanupEntities,
    ReloadBlocks, ReloadItems, ReloadEntityClasses, ReloadPrefabs,
    CollectGarbage, UndoChangeSet
}

public interface IWorldOperationJobBridge
{
    WorldOperationReceipt Enqueue(WorldOperationIntent intent);
    WorldOperationRecord Get(string operationId);
    WorldOperationPage Query(WorldOperationQuery query);
    bool RequestCancellation(string operationId, string actorSubject);
}
```

`WorldOperationRecord` 固定组合 `OperationId`、第二波 `JobId`、`ActorSubject`、`Kind`、`WorldId`、`WorldVersion`、可空 `MapResourceVersion`、`CorrelationId`、脱敏 `ConfirmationSummary`、`IsReversible`、可空 `ChangeSetId`、进度、时间和上述状态。状态从第二波 job 与世界回滚结果映射，不另建通用状态机。

migration 固定创建 `world_operations`、`world_operation_entity_targets`、`world_operation_map_targets`、`world_operation_region_targets`、`world_operation_block_targets`、`world_operation_prefab_targets`、`world_operation_maintenance_targets`、`world_change_sets`、`world_change_set_chunks`、`feature_module_states`。每张 target 表使用明确列；禁止 `payload_json`、任意类型名和路径列。关键列如下：

```sql
CREATE TABLE world_operations (
    operation_id TEXT PRIMARY KEY,
    job_id TEXT NOT NULL UNIQUE REFERENCES jobs(id),
    actor_subject TEXT NOT NULL,
    kind TEXT NOT NULL,
    world_id TEXT NOT NULL,
    world_version TEXT NOT NULL,
    map_resource_version TEXT NULL,
    correlation_id TEXT NOT NULL UNIQUE,
    confirmation_summary TEXT NOT NULL,
    is_reversible INTEGER NOT NULL CHECK (is_reversible IN (0, 1)),
    change_set_id TEXT NULL,
    created_at_utc INTEGER NOT NULL,
    submission_failure_code TEXT NULL
);

CREATE TABLE world_change_sets (
    change_set_id TEXT PRIMARY KEY,
    source_operation_id TEXT NOT NULL UNIQUE,
    world_id TEXT NOT NULL,
    world_version TEXT NOT NULL,
    minimum_x INTEGER NOT NULL, minimum_y INTEGER NOT NULL, minimum_z INTEGER NOT NULL,
    maximum_x INTEGER NOT NULL, maximum_y INTEGER NOT NULL, maximum_z INTEGER NOT NULL,
    before_hash TEXT NOT NULL, after_hash TEXT NOT NULL,
    storage_resource_id TEXT NOT NULL UNIQUE,
    created_at_utc INTEGER NOT NULL, expires_at_utc INTEGER NOT NULL
);
```

- [x] **步骤 1：写作业桥和 migration RED**

  覆盖八个终态、第二波 job 状态映射、无 job 的提交失败、operation ID 查询、Owner 取消、规范 UTC、确认摘要脱敏、所有 target 表互斥、change set hash/保留期，以及 migration 从空库和 `012` 基线各执行一次。测试必须断言没有 `payload_json`、`file_path`、`type_name` 和第二张通用 job 表。

- [ ] **步骤 2：运行聚焦测试并确认正确 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~WorldOperationContractTests|FullyQualifiedName~SqliteWorldOperationStoreTests"
  ```

  预期：失败落在缺失世界合同、表或映射；第二波合同缺失时本任务保持 RED，不用本波代码替代它。

- [x] **步骤 3：实现最小持久元数据和第二波适配**

  `SqliteWorldOperationJobBridge` 在一个 SQLite 事务中写世界操作意图、类型化 target 与第二波 job；任何一步失败都回滚且绝不调用 handler。查询时将 job 生命周期与世界回滚覆盖合成 `WorldOperationStatus`，只暴露脱敏错误和相关 ID。`013` migration 重新创建第一波 `unified_audit_projection`，把世界 operation 和模块变更的稳定摘要加入查询，不复制 change set 内容、容器明细或资源路径。

- [ ] **步骤 4：转 GREEN 并检查 migration 边界**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~WorldOperationContractTests|FullyQualifiedName~SqliteWorldOperationStoreTests"
  git diff --check -- backend/src/Core/LSTY.SevenDPanel.Application/WorldOperations backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Migrations/013_WorldToolsAndFeatureModules.sql backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/WorldOperations backend/tests/LSTY.SevenDPanel.Tests/WorldOperationContractTests.cs backend/tests/LSTY.SevenDPanel.Tests/SqliteWorldOperationStoreTests.cs
  ```

  预期：聚焦测试通过；migration 可重复发现但只执行一次，且没有覆盖 `001` 至 `012`。

### 任务 2：补齐世界只读标量、容器详情和服务端资源目录

**文件：**

- 新建 Application：`backend/src/Core/LSTY.SevenDPanel.Application/World/WorldReadModels.cs`、`IWorldSnapshotProjection.cs`、`IWorldToolCatalog.cs`、`QueryWorldUseCases.cs`；修改 `backend/src/Core/LSTY.SevenDPanel.Application/Maps/MapLayerModels.cs`。
- 新建 SevenDays：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/World/SevenDaysWorldScalarSnapshot.cs`、`SevenDaysWorldSnapshotProjection.cs`、`SevenDaysWorldToolCatalog.cs`；修改同项目 `Outbound/Maps/SevenDaysMapLayerModels.cs`、`SevenDaysMapLayerProjection.cs`、`SevenDaysMapProjectionRuntime.cs`。
- 新建测试：`backend/tests/LSTY.SevenDPanel.Tests/WorldReadUseCaseTests.cs`、`SevenDaysWorldSnapshotProjectionTests.cs`；修改 `SevenDaysMapLayerProjectionTests.cs`。

**固定只读模型：** `WorldSummary` 包含 `WorldId`、`WorldVersion`、可空 `Seed`、可空尺寸、可空 `GameVersion`、`MapResourceVersion`、可用范围、`ObservedAtUtc` 和来源状态；`LandClaimSummary`、`VehicleSummary`、`DroneSummary`、`ContainerSummary` 只含服务端 ID、稳定身份、坐标、加载/锁定、燃油/品质、槽位/已用数量和批准物品摘要。`WorldToolCatalogSnapshot` 只发布 block internal name 及不透明 `PrefabResourceId`、`EntityTypeResourceId`，不发布文件名或路径。

- [x] **步骤 1：写字段复制、空值和来源隔离 RED**

  覆盖领地 owner/范围/有效性/最近登录，载具和无人机身份/状态/容器摘要，容器 loaded coverage、slot/item count 与可选条目，世界版本/资源版本/观察时间；单一来源抛错只令对应集合 `Unavailable`，其他集合仍可读；返回集合不可变，未知字段为 `null`。

- [ ] **步骤 2：确认 RED 后实现游戏线程标量复制**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~WorldReadUseCaseTests|FullyQualifiedName~SevenDaysWorldSnapshotProjectionTests|FullyQualifiedName~SevenDaysMapLayerProjectionTests"
  ```

  `SevenDaysWorldSnapshotProjection` 在 dispatcher 回调内读取 `GameManager.Instance.World` 及经 v3.0.1-b4 编译确认的持久玩家、entity、tile entity 和地图 API；回调外仅保留产品标量。现有地图层复用同一次 snapshot，不再把真实图层无条件清空。

- [ ] **步骤 3：转 GREEN 并核对只读边界**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~WorldReadUseCaseTests|FullyQualifiedName~SevenDaysWorldSnapshotProjectionTests|FullyQualifiedName~SevenDaysMapLayerProjectionTests"
  git diff --check -- backend/src/Core/LSTY.SevenDPanel.Application/World backend/src/Core/LSTY.SevenDPanel.Application/Maps backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/World backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Maps backend/tests/LSTY.SevenDPanel.Tests
  ```

  预期：聚焦测试通过；公开模型中不存在 7DTD/Unity 类型、存档目录、磁盘路径或可写文件名。

### 任务 3：交付领地删除、玩家/实体移动和地图资源作业

**文件：**

- 新建 Application：`backend/src/Core/LSTY.SevenDPanel.Application/WorldOperations/MapWorldOperationModels.cs`、`DeleteLandClaimUseCase.cs`、`MoveOnlinePlayerUseCase.cs`、`MoveWorldEntityUseCase.cs`、`SubmitMapJobUseCase.cs`。
- 新建 adapters：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/World/SevenDaysMapWorldOperationHandler.cs`、`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/MapTiles/LocalMapResourcePublisher.cs`。
- 新建测试：`backend/tests/LSTY.SevenDPanel.Tests/MapWorldOperationUseCaseTests.cs`、`SevenDaysMapWorldOperationHandlerTests.cs`、`LocalMapResourcePublisherTests.cs`。

**固定类型：** `DeleteLandClaimRequest` 固定 claim ID、owner stable identity、中心/范围和观察版本；`MoveOnlinePlayerRequest` 固定 entity ID 与 platform identity；`MoveEntityRequest` 固定 entity ID、entity type、owner identity、来源位置和目标坐标。`MapJobKind` 只有 `RefreshResources`、`RenderExplored`、`RenderFull`；完整渲染必须强确认并按 world ID 互斥。

- [x] **步骤 1：写固定目标、重验证和发布安全 RED**

  覆盖 identity/类型/位置/version 漂移、实体消失或 ID 复用、越界目标、普通确认与强确认、同世界完整渲染冲突、202 receipt、取消前后边界、临时根限制、manifest/尺寸校验、reparse/path traversal、原子提升版本和失败保留上一版本。

- [ ] **步骤 2：确认 RED 后实现四个独立入口**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~MapWorldOperationUseCaseTests|FullyQualifiedName~SevenDaysMapWorldOperationHandlerTests|FullyQualifiedName~LocalMapResourcePublisherTests"
  ```

  每个用例只规范化并入队自己的 target；handler 开始副作用前重读世界 snapshot。浏览器 tile source reload 不进入此 handler。地图输出先写服务端临时根，校验后由 `LocalMapResourcePublisher` 原子切换 `mapResourceVersion`。

- [ ] **步骤 3：转 GREEN 并记录纵向检查点**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~MapWorldOperationUseCaseTests|FullyQualifiedName~SevenDaysMapWorldOperationHandlerTests|FullyQualifiedName~LocalMapResourcePublisherTests"
  git diff --check -- backend/src/Core/LSTY.SevenDPanel.Application/WorldOperations backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/World backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/MapTiles backend/tests/LSTY.SevenDPanel.Tests
  ```

  预期：三类危险目标不可互换；失败不覆盖最后有效地图资源。

### 任务 4：交付区域复制、填充、清空、粘贴和 change set

**文件：**

- 新建 Application：`backend/src/Core/LSTY.SevenDPanel.Application/WorldOperations/RegionOperationModels.cs`、`RegionOperationUseCases.cs`、`IWorldChangeSetMetadataStore.cs`、`IWorldChangeSetBlobStore.cs`。
- 新建 adapters：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/WorldOperations/SqliteWorldChangeSetMetadataStore.cs`、`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/WorldOperations/LocalWorldChangeSetBlobStore.cs`、`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/World/SevenDaysRegionOperationHandler.cs`、`WorldOperationBatchExecutor.cs`。
- 新建测试：`backend/tests/LSTY.SevenDPanel.Tests/RegionOperationUseCaseTests.cs`、`WorldChangeSetStoreTests.cs`、`SevenDaysRegionOperationHandlerTests.cs`。

**固定接口：**

```csharp
public interface IWorldChangeSetMetadataStore
{
    WorldChangeSetDescriptor Create(WorldChangeSetDraft draft);
    WorldChangeSetDescriptor Read(string changeSetId);
    void MarkApplied(string changeSetId, string afterHash);
}

public interface IWorldChangeSetBlobStore
{
    WorldChangeSetBlobReceipt Write(WorldChangeSetBlobDraft draft);
    WorldChangeSetBlobReadResult Read(string storageResourceId, string expectedHash);
}

public sealed class WorldRegion
{
    public WorldCoordinate Minimum { get; }
    public WorldCoordinate Maximum { get; }
    public long Volume { get; }
}
```

- [x] **步骤 1：写四种独立用例和批次执行 RED**

  覆盖坐标规范化、体积上限、跨世界、未加载 chunk、非法 block 资源、强确认、copy 只生成 change set、fill/clear/paste 前置快照、每批上限、帧预算替身、进度单调、队列容量、部分应用转 `ResultUnknown` 而非成功。

- [ ] **步骤 2：确认 RED 后实现受控 change set 存储**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~RegionOperationUseCaseTests|FullyQualifiedName~WorldChangeSetStoreTests|FullyQualifiedName~SevenDaysRegionOperationHandlerTests"
  ```

  `SqliteWorldChangeSetMetadataStore` 只保存 operation、范围、hash、资源 ID 与保留期；`LocalWorldChangeSetBlobStore` 只接受服务端生成 `storage_resource_id`，压缩块带长度与 SHA-256。读取时二者共同复核批准根、reparse、长度、before/after hash 和保留期。后台线程可压缩标量块，但不得访问 chunk、BlockValue 或 Unity 对象。

- [ ] **步骤 3：分批进入游戏线程并转 GREEN**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~RegionOperationUseCaseTests|FullyQualifiedName~WorldChangeSetStoreTests|FullyQualifiedName~SevenDaysRegionOperationHandlerTests"
  git diff --check -- backend/src/Core/LSTY.SevenDPanel.Application/WorldOperations backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/WorldOperations backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/WorldOperations backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/World backend/tests/LSTY.SevenDPanel.Tests
  ```

  预期：聚焦测试通过；每个批次在 dispatcher 内结束，取消只发生在安全批次边界。

### 任务 5：交付批准方块和 Prefab 操作

**文件：**

- 新建 Application：`backend/src/Core/LSTY.SevenDPanel.Application/WorldOperations/BlockPrefabOperationModels.cs`、`SetBlockUseCase.cs`、`PlacePrefabUseCase.cs`、`RemovePrefabUseCase.cs`。
- 新建 SevenDays：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/World/SevenDaysBlockPrefabOperationHandler.cs`。
- 新建测试：`backend/tests/LSTY.SevenDPanel.Tests/BlockPrefabOperationUseCaseTests.cs`、`SevenDaysBlockPrefabOperationHandlerTests.cs`。

**固定边界：** `SetBlockRequest` 只含 block internal name、精确坐标、批准 rotation/shape、世界版本和确认；Prefab 请求只含 `PrefabResourceId`、锚点、批准旋转、已知边界和世界版本。放置与删除是不同类型；无法形成完整 change set 时 `IsReversible=false` 且必须强确认。

- [ ] **步骤 1：写目录约束、边界和回滚证据 RED**

  覆盖未知/隐藏 block、非法 rotation/shape、未知 prefab ID、客户端伪造文件名/XML/路径、越界或重叠、版本漂移、回滚证据生成失败、部分放置和原始异常脱敏。

- [ ] **步骤 2：确认 RED 后实现最小 handler**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~BlockPrefabOperationUseCaseTests|FullyQualifiedName~SevenDaysBlockPrefabOperationHandlerTests"
  ```

  仅使用任务 2 的目录解析输入；适配器在游戏线程把标量转换为 v3.0.1-b4 已编译 API 所需值，并在第一处写入前持久化 change set descriptor。

- [ ] **步骤 3：转 GREEN 并检查无路径泄漏**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~BlockPrefabOperationUseCaseTests|FullyQualifiedName~SevenDaysBlockPrefabOperationHandlerTests"
  git diff --check -- backend/src/Core/LSTY.SevenDPanel.Application/WorldOperations backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/World backend/tests/LSTY.SevenDPanel.Tests
  ```

  预期：方块与 Prefab 不能借同一 mode 改变危险含义，审计与结果中没有文件路径。

### 任务 6：交付实体维护、类型化 XML reload 与 GC

**文件：**

- 新建 Application：`backend/src/Core/LSTY.SevenDPanel.Application/WorldOperations/MaintenanceOperationModels.cs`、`EntityMaintenanceUseCases.cs`、`ReloadGameResourceUseCase.cs`、`CollectGameGarbageUseCase.cs`。
- 新建 SevenDays：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/World/SevenDaysEntityMaintenanceHandler.cs`、`SevenDaysRuntimeMaintenanceHandler.cs`。
- 新建测试：`backend/tests/LSTY.SevenDPanel.Tests/EntityMaintenanceUseCaseTests.cs`、`RuntimeMaintenanceOperationTests.cs`。

**固定类型：** entity spawn 使用不透明 `EntityTypeResourceId`、数量、中心和半径；delete 固定 entity ID/type/观察位置；cleanup 固定批准 entity category、范围和数量上限。`WorldReloadResourceKind` 只有 `Blocks`、`Items`、`EntityClasses`、`Prefabs`。GC 请求无命令文本和参数，只返回 accepted、completed 或 `ResultUnknown`。

- [x] **步骤 1：写实体、reload 与 GC 安全 RED**

  覆盖未知 entity ID/type、玩家和受保护实体拒绝、数量/范围上限、ID 复用、批量强确认、四类 reload 分派、任意文件/XML/控制台原文拒绝、GC 超时为 `ResultUnknown` 且不声称性能改善。

- [ ] **步骤 2：确认 RED 后实现两个明确 handler**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~EntityMaintenanceUseCaseTests|FullyQualifiedName~RuntimeMaintenanceOperationTests"
  ```

  实体 handler 与 runtime maintenance handler 不共享通用 command payload；每个 switch 只接受封闭 enum，并在 dispatcher 内调用经编译确认的入口。

- [ ] **步骤 3：转 GREEN 并记录检查点**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~EntityMaintenanceUseCaseTests|FullyQualifiedName~RuntimeMaintenanceOperationTests"
  git diff --check -- backend/src/Core/LSTY.SevenDPanel.Application/WorldOperations backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/World backend/tests/LSTY.SevenDPanel.Tests
  ```

  预期：聚焦测试通过；客户端不能扩展 reload 类型或实体类别。

### 任务 7：实现撤销、关服恢复、固定 handler 分派和 DI/runtime 合流

**文件：**

- 新建 Application/SevenDays：`backend/src/Core/LSTY.SevenDPanel.Application/WorldOperations/UndoWorldChangeSetUseCase.cs`；`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/World/SevenDaysUndoOperationHandler.cs`、`WorldOperationJobHandler.cs`、`WorldOperationRuntime.cs`。
- 修改组合根：`backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`。
- 新建测试：`backend/tests/LSTY.SevenDPanel.Tests/UndoWorldChangeSetUseCaseTests.cs`、`WorldOperationRecoveryTests.cs`；修改 `DependencyInjectionTests.cs`。

**固定恢复规则：** 撤销请求固定 source operation/change set/world version/当前范围 hash，成功撤销创建新 operation。重启时，未开始副作用的 job 可保持 `Queued`；已开始且无可证明 checkpoint 的 job 为 `ResultUnknown`；安全批次边界已持久化但未完成的 job 为 `Interrupted`；回滚开始后失败为 `RollbackFailed`。不删除原 operation，不猜测重放。

- [x] **步骤 1：写冲突、损坏、过期和恢复 RED**

  覆盖非 7DPanel change set、world version 变化、第三方修改导致 hash 不符、损坏/超期、重复撤销、撤销中断、回滚失败、关服各阶段映射，以及 decorator `Start`/`MarkGameReady`/`Stop`/`Dispose` 顺序和异常聚合。

- [ ] **步骤 2：确认 RED 后实现撤销与封闭分派**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~UndoWorldChangeSetUseCaseTests|FullyQualifiedName~WorldOperationRecoveryTests|FullyQualifiedName~DependencyInjectionTests"
  ```

  `WorldOperationJobHandler` 对 `WorldOperationKind` 使用穷尽 switch 调用任务 3 至 6 的真实 handler；它不是动态 registry。`WorldOperationRuntime` 复用第二波 worker，并作为 `IModRuntime` 显式 decorator 接入现有链，最外层仍由组合根唯一拥有。

- [ ] **步骤 3：转 GREEN 并核对串行合流**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~UndoWorldChangeSetUseCaseTests|FullyQualifiedName~WorldOperationRecoveryTests|FullyQualifiedName~DependencyInjectionTests"
  git diff --check -- backend/src/Core/LSTY.SevenDPanel.Application/WorldOperations backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/World backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs backend/tests/LSTY.SevenDPanel.Tests
  ```

  预期：聚焦测试通过；没有第二个 worker、handler registry 或自动猜测修复。

### 任务 8：在全部真实消费者存在后加入编译期功能模块治理

**文件：**

- 新建模块核心：`backend/src/Core/LSTY.SevenDPanel.Application/Modules/FeatureModuleModels.cs`、`FeatureModulePolicy.cs`、`IFeatureModuleStateStore.cs`、`FeatureModuleGate.cs`、`FeatureModuleUseCases.cs`；新建 `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Modules/SqliteFeatureModuleStateStore.cs`。
- 修改真实消费者/组合根：`backend/src/Core/LSTY.SevenDPanel.Application/WorldOperations/DeleteLandClaimUseCase.cs`、`MoveOnlinePlayerUseCase.cs`、`MoveWorldEntityUseCase.cs`、`SubmitMapJobUseCase.cs`、`RegionOperationUseCases.cs`、`SetBlockUseCase.cs`、`PlacePrefabUseCase.cs`、`RemovePrefabUseCase.cs`、`EntityMaintenanceUseCases.cs`、`ReloadGameResourceUseCase.cs`、`CollectGameGarbageUseCase.cs`、`UndoWorldChangeSetUseCase.cs`、`backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`。
- 新建测试：`backend/tests/LSTY.SevenDPanel.Tests/FeatureModulePolicyTests.cs`、`FeatureModuleConsumerCoverageTests.cs`。

**固定模块集合：**

```csharp
public enum FeatureModuleId
{
    IdentityAndAuthorization, Audit, RuntimeHealth,
    Overview, PlayerHistoryAndMap, Console, Chat, GameResources,
    Backups, AnnouncementsAndScheduling, PlayerItems,
    EconomyAndRewards, TeleportAndVoting, Automation,
    Discord, GeoIp, WorldTools
}
```

`FeatureModulePolicy.Describe(FeatureModuleId)` 使用穷尽 switch 返回 `IsToggleable`、依赖、设置摘要字段、健康来源、停用模式 `Immediate|Drain|RestartRequired`、保留数据说明和真实 consumer ID。`Overview` 以及保障身份、授权、审计、runtime health 和最低管理面的依赖链不可停用；客户端不能改变 `IsToggleable`。

- [ ] **步骤 1：写封闭集合、依赖和真实消费者覆盖 RED**

  覆盖 17 个且仅 17 个 ID、无程序集扫描、依赖环检测；`IdentityAndAuthorization`、`Audit`、`RuntimeHealth` 明确 `IsToggleable=false`，其他依赖它们的保护模块按策略拒绝。运行 job 阻止停用或进入 `Drain`，停用后历史仍可读而新动作被 gate 拒绝，启用不重放事件。coverage test 从组合根的显式映射断言每个可切换模块至少有一个非测试 runtime consumer，缺少消费者时不允许显示模块条目。

- [ ] **步骤 2：确认 RED 后实现最小 policy/store/gate**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~FeatureModulePolicyTests|FullyQualifiedName~FeatureModuleConsumerCoverageTests"
  ```

  在任务 3 至 6 的提交用例和前五波已经存在的真实 Application 命令入口注入 `FeatureModuleGate`；只在提交新动作前检查，查询历史不被屏蔽。模块停用不删除 SQLite、change set、配置或 job，不自动取消 `ResultUnknown`。

- [ ] **步骤 3：转 GREEN 并核对无动态 registry**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~FeatureModulePolicyTests|FullyQualifiedName~FeatureModuleConsumerCoverageTests|FullyQualifiedName~DependencyInjectionTests"
  git diff --check -- backend/src/Core/LSTY.SevenDPanel.Application/Modules backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Modules backend/src/Core/LSTY.SevenDPanel.Application/WorldOperations backend/src/Bootstrap/LSTY.SevenDPanel backend/tests/LSTY.SevenDPanel.Tests
  ```

  预期：模块状态可持久恢复；所有条目均来自编译期 switch 和显式 DI consumer 映射。

### 任务 9：串行发布 Web DTO、独立路由、OpenAPI 和生成客户端

**文件：**

- 新建 Web：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/WorldHttpModels.cs`、`WorldController.cs`、`WorldOperationHttpModels.cs`、`WorldOperationsController.cs`、`MapJobsController.cs`、`ModulesController.cs`；修改同项目 `Inbound/Http/OpenApi/PanelOpenApiOperationProcessor.cs`。
- 新建测试：`backend/tests/LSTY.SevenDPanel.Tests/WorldToolsHttpTests.cs`、`FeatureModulesHttpTests.cs`；修改 `OwinWebHostOpenApiSnapshotTests.cs`。
- 更新生成输入：`frontend/apps/admin/openapi/7dpanel.v1.json`。
- 更新生成输出：`frontend/apps/admin/src/shared/api/generated/`。

**固定路由：** `GET /api/v1/world/summary|land-claims|vehicles|drones|containers|catalogs/*`；`POST /api/v1/world-operations/land-claims/delete|players/move|entities/move|regions/copy|regions/fill|regions/clear|regions/paste|blocks/set|prefabs/place|prefabs/remove|entities/spawn|entities/delete|entities/cleanup|xml/reload|gc|undo`；`GET /api/v1/world-operations/{operationId}`；`POST /api/v1/map-jobs/refresh-resources|render-explored|render-full` 与资源版本查询；`GET /api/v1/modules`、`POST /api/v1/modules/{moduleId}/enable|disable`。

- [x] **步骤 1：写 HTTP/OpenAPI RED**

  覆盖只读授权、所有写入口和模块切换 Owner-only、每路由独立 DTO、确认/强确认、无效坐标/范围/版本、审计不可用 fail-closed、202 body 与 operation ID、查询八状态、404/409/422/503 Problem Details、错误脱敏，以及 schema 不出现 path/XML/script/command/typeName/payload。

- [ ] **步骤 2：确认 RED 后实现 Controller 和 OpenAPI 显式描述**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~WorldToolsHttpTests|FullyQualifiedName~FeatureModulesHttpTests|FullyQualifiedName~OwinWebHostOpenApiSnapshotTests"
  ```

  所有 POST 返回 `202 Accepted` 及 receipt，不返回终态。OpenAPI processor 显式列出 enum、nullable、202 和 Problem Details；不把 Application 对象直接作为 HTTP 请求模型。

- [x] **步骤 3：受控更新 snapshot 并只生成一次 SDK**

  ```powershell
  $env:SEVENDPANEL_UPDATE_ADMIN_OPENAPI_SNAPSHOT='1'
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~OwinWebHostOpenApiSnapshotTests.Openapi_document_matches_admin_codegen_snapshot"
  Remove-Item Env:SEVENDPANEL_UPDATE_ADMIN_OPENAPI_SNAPSHOT
  Set-Location frontend/apps/admin
  pnpm api:gen
  Set-Location ../../..
  ```

  预期：snapshot 与生成 SDK 只包含批准路由；不手工编辑 `src/shared/api/generated/`。

- [ ] **步骤 4：复验合同转 GREEN**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~WorldToolsHttpTests|FullyQualifiedName~FeatureModulesHttpTests|FullyQualifiedName~OwinWebHostOpenApiSnapshotTests"
  git diff --check -- backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostOpenApiSnapshotTests.cs backend/tests/LSTY.SevenDPanel.Tests/WorldToolsHttpTests.cs backend/tests/LSTY.SevenDPanel.Tests/FeatureModulesHttpTests.cs frontend/apps/admin/openapi frontend/apps/admin/src/shared/api/generated
  ```

  预期：HTTP 和 snapshot 聚焦测试通过，生成目录与输入一致。

### 任务 10：组合 Admin 世界工具、operation 恢复、地图操作面板和模块页

**文件：**

- 新建 world-tools feature：`frontend/apps/admin/src/features/world-tools/api/worldTools.ts`、`model/useWorldResources.ts`、`model/useWorldOperations.ts`、`ui/WorldToolsView.vue`、`ui/WorldReadDetails.vue`、`ui/WorldOperationPanel.vue`、`ui/WorldOperationConfirmDialog.vue`、`ui/WorldOperationHistory.vue`、`worldTools.test.ts`。
- 新建 modules feature：`frontend/apps/admin/src/features/modules/api/modules.ts`、`model/useFeatureModules.ts`、`ui/FeatureModulesView.vue`、`featureModules.test.ts`；新建页面 `frontend/apps/admin/src/pages/world-tools.vue`、`pages/modules.vue`。
- 修改地图/壳层：`frontend/apps/admin/src/features/player-map/ui/MapFeatureDetails.vue`、`PlayerMapView.vue`、`frontend/apps/admin/src/app/AppShell.vue`、`AppShell.test.ts`、`router.test.ts`。
- 修改双语：`frontend/apps/admin/src/app/i18n/locales/zh-CN.json`、`en.json`、`frontend/apps/admin/src/app/i18n/messages.test.ts`。

**固定交互：** 只读详情显示来源状态、`ObservedAtUtc` 与可空字段；地图 popup 只 emit 固定 selection，写操作在 `WorldOperationPanel` 中重新取当前版本。普通确认和强确认都持续展示目标、世界、范围、资源版本、预计影响与可撤销性。202 receipt 写入 URL/query 可恢复 operation ID，并轮询到终态；刷新后继续显示 `Queued`/`Running`/`Interrupted`/`ResultUnknown`，不把 202 toast 当成功。

- [ ] **步骤 1：写 transport、状态恢复和确认 RED**

  使用生成 SDK mock 覆盖只读 Success/Partial/Stale/Unavailable、过期 snapshot 禁止动作、每个独立 POST 的请求形状、强确认文本、取消轮询、迟到响应隔离、页面刷新恢复、八状态 badge、RollbackFailed 告警、模块依赖/健康/draining/保护模块，以及 Owner 之外不渲染写控件。

- [ ] **步骤 2：确认 RED 后实现 Composition API 状态层**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm test -- src/features/world-tools src/features/modules src/features/player-map/ui/PlayerMapView.test.ts
  Set-Location ../../..
  ```

  composable 返回 readonly 状态和明确 actions；请求切换使用 AbortController 清理。组件使用类型化 props/emits，不修改 props，不保存通用 payload。

- [x] **步骤 3：实现 Nuxt UI 页面、路由和双语窄屏布局**

  `WorldToolsView` 使用 `UDashboardPanel`、`UDashboardNavbar`、`UTabs`、`UTable`、`UBadge`、`UAlert`、`UProgress`、`UFormField` 和 `UModal`；模块页用依赖摘要与独立启停 modal。桌面表格在窄屏改为单列摘要/抽屉，不依赖横向滚动；危险按钮、状态和错误均有中英 key。

- [ ] **步骤 4：转 GREEN 并检查路由/i18n**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm test -- src/features/world-tools src/features/modules src/features/player-map/ui/PlayerMapView.test.ts src/app/AppShell.test.ts src/app/router.test.ts src/app/i18n/messages.test.ts
  pnpm typecheck
  pnpm exec eslint src/features/world-tools src/features/modules src/features/player-map/ui/MapFeatureDetails.vue src/features/player-map/ui/PlayerMapView.vue src/pages/world-tools.vue src/pages/modules.vue src/app/AppShell.vue
  Set-Location ../../..
  ```

  预期：聚焦 Vitest、类型检查和定向 lint 通过；无 timer、watcher、AbortController 或 overlay 泄漏。

### 任务 11：执行一次聚合门禁、一次真实 7DTD smoke 并提升 Current 文档

**文件：**

- 修改 Current：`docs/architecture.md`、`docs/design.md`、`docs/test.md`。
- 修改 Target：`docs/architecture/legacy-feature-parity-target-blueprint.md`、`backend-target-blueprint.md`、`admin-frontend-target-blueprint.md`。
- 按实际命令变化修改：`backend/README.md`、`frontend/apps/admin/README.md`；仅在仓库级聚合入口变化时修改 `README.md`。
- 更新：`docs/superpowers/plans/2026-07-26-legacy-parity-world-tools-modules.md`。

- [ ] **步骤 1：运行一次后端受影响聚合门禁**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-restore
  ```

  预期：全部后端测试通过；该命令同时编译产品项目、migration、DI、Web/OpenAPI 和 SevenDays adapter。只在所有聚焦测试稳定后执行一次；若失败则回到对应聚焦测试修复，修复稳定后再执行一次最终聚合复验，不因普通测试重跑暂停确认。

- [ ] **步骤 2：运行一次 Admin 聚合门禁**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm test
  pnpm typecheck
  pnpm lint
  pnpm build
  pnpm api:gen
  Set-Location ../../..
  ```

  预期：Vitest、类型、lint、Vite production build 通过，第二次生成不产生新内容变化；未提交生成基线不运行 `api:check`，也不运行 Playwright。

- [ ] **步骤 3：在一次隔离会话中执行规格要求的真实游戏 API 最小 smoke**

  本步骤包含删除、填充、Prefab、实体和撤销，属于本路线允许暂停的破坏性真实环境操作；执行前向用户确认精确测试实例、测试世界、前置备份和回滚目标。确认后只向该隔离实例发布一次当前候选构建，再使用与 `7dtd-reference/v3.0.1-b4` 一致的 Windows 测试服。先创建并验证可恢复备份，再在可丢弃测试世界为以下 API 类别各执行一个最小样例：世界/领地/载具/无人机/容器读取，领地删除，玩家与实体移动，地图刷新与渲染，区域 copy/fill/clear/paste，block，Prefab 放置/删除，entity spawn/delete/cleanup，四类 reload，GC，change set undo。记录 operation ID、终态、地图版本和恢复结果；任何备份不可恢复时跳过全部危险写 smoke 并把发布门禁记为失败。

  预期：每类真实 API 至多一个样例；危险动作只触及已备份测试世界；`ResultUnknown`、`RollbackFailed` 或版本冲突保持原状态，不人工改写为成功。不运行浏览器 smoke。

- [ ] **步骤 4：仅按已验证证据更新 Current/Target 文档**

  - `docs/architecture.md`：记录实际 Application 合同、第二波 job 复用、SQLite 表、dispatcher 批次、change set、runtime/DI、Web/Admin 依赖和编译期模块治理；不把未 smoke 的 7DTD API 写成已支持。
  - `docs/design.md`：记录 `/world-tools`、`/modules`、地图只读 popup、操作面板、确认、operation 恢复、双语与窄屏的实际行为。
  - `docs/test.md`：记录聚焦层级、一次聚合命令、真实 smoke 范围、备份证据、未运行 Playwright/publish 和所有残余限制。
  - 三份 Target blueprint：只把本波已实现并验证的条目提升为当前事实；保留无法验证或由其他波次阻塞的条目为目标。
  - README 只维护所属运行/验证入口；根 README 的聚合命令未变化时不追加重复内容。`docs/PRD.md` 的产品合同未变化时不改写能力定义。

- [ ] **步骤 5：执行计划自查和未提交交付检查**

  ```powershell
  rg -n "payload_json|通用事件总线|程序集扫描" docs/superpowers/plans/2026-07-26-legacy-parity-world-tools-modules.md
  git diff --check
  git status --short
  ```

  预期：禁止项搜索只命中明确的禁止约束和本步骤参数，不出现为生产合同；计划正文没有占位实现；所有本地链接可解析；spec 的上游/用户结果、世界只读、持久操作、地图与实体、区域/方块/Prefab、维护、撤销、模块、接口/Admin、安全、精简验证和完成条件均能映射到任务 1 至 11；类型和状态名一致；工作区保持未提交。

## 完成标准

- 世界、领地、载具、无人机、容器和工具目录只返回不可变标量、观察时间与诚实完整性；来源失败不污染其他 read model。
- 每个危险动作有独立 request/use case/route、固定目标、Owner 授权、确认、服务端重验证、第二波 job、审计和八状态查询；HTTP 202 与终态严格分离。
- 地图原子发布失败保留上一版本；区域与 Prefab 操作有批次预算和 change set，部分结果不显示成功；撤销只处理 7DPanel 自有且 hash/version 兼容的证据。
- reload 只接受四个编译期资源类型，GC 和实体维护不接受命令原文；浏览器不能提交路径、XML、脚本、任意类型名或万能 payload。
- 模块目录只有 17 个编译期 ID 和真实消费者，其中身份授权、审计和运行时健康不可停用；停用遵守依赖、draining/重启影响和运行 job，不删除数据、不自动重放、不实现动态插件框架。
- 后端与 Admin 各一次聚合门禁通过；规格要求的真实 7DTD API 每类至多一个 smoke，并为危险写动作保留可恢复前置备份；未运行 Playwright 和 publish 的事实已记录。
- Current 文档只提升代码、自动化和 smoke 支撑的事实；计划完成项与实际结果已回写。任何 Git 提交、推送或历史修改仍须用户另行显式授权。
