---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-26-legacy-parity-player-evidence-actions-design.md
last_updated: "2026-07-26"
---

# 旧版本功能对齐第三波：玩家证据与类型化动作实施计划

## 2026-07-26 当前执行记录

- 已实现纵向切片：`010_PlayerEvidenceActions.sql`、玩家会话/活动/背包/技能证据与 diff、独立 writer/Profile 查询、五类类型化玩家动作及恢复、DI/runtime、Owner HTTP/OpenAPI 均已有当前代码和测试文件；Admin Profile/动作代码实际位于 `frontend/apps/admin/src/features/player-profile/`，任务 11 的文件清单已按当前路径校正。
- 已知执行证据：本轮 `pnpm api:schema` 为 `1/1`、`pnpm api:gen` 成功，Bootstrap Release build 为 `0 warning`、`0 error`，最终 Admin typecheck 通过；当前记录仍没有可明确归属于第三波后端过滤命令或 Player Profile/动作页面聚焦组合的通过结果。测试文件存在本身不记为测试已运行，Bootstrap build 不替代后端聚合测试。
- 未执行门禁与真实缺口：后端/Admin 聚合、第三波 HTTP 与 Admin Feature 聚焦测试、生成无漂移检查、publish、Playwright、浏览器及真实 `v3.0.1-b4` 五动作矩阵均未确认；完整玩家数据重置、固定目标、前后证据和 `ResultUnknown` 仍缺真实环境验证。Current 文档只记录代码与现有证据，不代表发布或真实动作完成。

> **面向智能体执行者：** 优先使用 `superpowers:subagent-driven-development` 在当前会话逐任务实施；若在独立会话按检查点执行，则使用 `superpowers:executing-plans`。

**目标：** 交付以稳定跨平台身份为键的玩家 Profile、会话/活动、背包/技能快照、diff/gap/来源证据，以及发放、删除、技能重置、背包清空和完整玩家数据重置的类型化安全纵向切片。

**架构：** Application 拥有标量合同、分区聚合、diff 与五类动作编排；SevenDays Adapter 只在获准回调或 `GameThreadDispatcher` 内复制/重验游戏对象；独立有界单消费者 writer 将证据写入 SQLite；Web 只暴露 Owner 类型化 DTO；Admin 只消费生成客户端并从 Fresh 在线目标打开危险动作。Pending 先于副作用持久化，所有终态可按 operation ID 恢复，不能确认的已开始动作落为 `ResultUnknown`。

**技术栈：** C# / .NET Framework 4.8、ASP.NET Web API 2 / OWIN、SQLite、xUnit、7DTD v3.0.1-b4；Vue 3.5 Composition API、TypeScript 6、Vue Router 5、Pinia 3、Nuxt UI 4、Vite 8、Vitest 4、Hey API、pnpm 11。

**主设计规格：** [玩家资料、物品证据与类型化动作设计规格](../specs/2026-07-26-legacy-parity-player-evidence-actions-design.md)

**Current / Target 依据：** [产品需求](../../PRD.md)、[当前设计](../../design.md)、[当前架构](../../architecture.md)、[当前测试策略](../../test.md)、[旧功能对齐目标蓝图](../../architecture/legacy-feature-parity-target-blueprint.md)、[后端目标蓝图](../../architecture/backend-target-blueprint.md)、[Admin 目标蓝图](../../architecture/admin-frontend-target-blueprint.md)。Target 文档只定义批准方向，不作为已实现证据。

## 实施边界与执行顺序

- 当前事实：玩家业务键已经是 `crossplatformIdentity.combinedId`；在线投影已有 `PlayerJoinedGame`、`SavePlayerData`、`PlayerDisconnected`；玩家历史、位置、SQLite migration、`GameThreadDispatcher`、kick 固定目标重验、游戏资源目录与 OpenAPI/Hey API 生成链已经存在。
- 本波不扩展现有 kick-only `player_action_audit`，不引入万能 action DTO、JSON payload、动作 registry、通用事件总线、脚本平台或控制台原文执行。
- 仓库外旧项目 `7dtd-serveradmin` 只用于字段与行为核对；实现不得复制旧代码，也不得修改旧项目。
- 主执行者串行拥有任务 1、2、9、10、12 的共享合同、`010` migration、配置/DI/runtime、OpenAPI snapshot/生成客户端和 Current 文档合流。
- 任务 5、6、7 在任务 1–4 合流后可由独立 worker 并行；每个 worker 只修改自己的类型化请求、用例、gateway 和测试，不触碰共享 migration、DI、控制器或生成代码。任务 8 等任务 5–7 合流后执行。
- 迭代阶段只运行任务所列聚焦测试。波次稳定后，任务 12 仅执行一次后端受影响聚合门禁、一次 Admin 受影响聚合门禁和规格要求的一次真实 7DTD 矩阵；不运行 publish 或浏览器 smoke。

## 固定合同

以下命名在任务 1 固定；后续任务不得为方便合并成万能动作合同：

```csharp
public enum PlayerProfileSectionState { Available, Partial, Unavailable, Forbidden }
public enum CatalogResolutionState { Resolved, Unavailable }
public enum SkillValueState { Known, UnsupportedByVersion, NotLoaded, Unknown }
public enum InventoryDiffKind { Added, Removed, QuantityChanged, Moved, AttributesChanged, Uncomparable }
public enum EvidenceLevel { Confirmed, ObservedChange }
public enum PlayerActionStatus { Pending, Succeeded, Rejected, Failed, Cancelled, ResultUnknown }
public enum PlayerItemRemovalMode { Exact, UpToAvailable }
public enum PlayerItemRemovalScope { BagOnly }

public sealed record PlayerTargetStamp(
    string CrossplatformId,
    int EntityId,
    DateTimeOffset OnlineObservedAtUtc,
    string WorldId);

public sealed record InventoryItemScalar(
    string Container,
    int Slot,
    string InternalName,
    int Count,
    int? Quality,
    decimal? UseAmount,
    IReadOnlyList<string> ModInternalNames);
```

- HTTP 的操作者从 Owner claim 取得，不能信任请求体中的 operator；Application 请求保存该操作者、`PlayerTargetStamp`、客户端请求键和 correlation ID。
- 物品 HTTP 请求只接受 `catalogVersion` 与 `resourceId`；Application 在持久化 Pending 前通过当前 `IGameResourceCatalog` 解析并固定 `internalName`、kind、数量上限、可发放性和隐藏状态。
- `resourceId` 只用于当前 HTTP 展示与提交；持久记录保存 `internalName`、kind 和观察/提交时目录版本，不把重启后不稳定的 `resourceId` 当业务键。
- 操作表保持五个类型化参数集合；`IPlayerActionOperationQuery` 只返回固定公共摘要，不读取或暴露万能 payload。

### Task 1：固定 Application 玩家证据、查询与动作公共摘要合同

**Files:**

- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Evidence/PlayerEvidenceModels.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Evidence/PlayerEvidenceQueries.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Evidence/IPlayerEvidenceStore.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Evidence/PlayerInventoryDiffService.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Actions/PlayerActionOperation.cs`
- Create: `backend/tests/LSTY.SevenDPanel.Tests/PlayerEvidenceContractTests.cs`
- Create: `backend/tests/LSTY.SevenDPanel.Tests/PlayerInventoryDiffServiceTests.cs`

- [x] 先写合同测试，固定枚举字符串、UTC 时间、`crossplatformId` 非空校验、容器/槽位唯一性、可空技能值、只读集合防御性复制、keyset 游标排序和公共操作摘要不含磁盘路径/控制台命令/任意 payload。
- [x] 写 diff RED 用例：新增、完全移除、数量变化、同一指纹移动、品质/使用度/mod 变化、目录不可用、无相邻快照、以及 gap 与比较区间相交时的 `Uncomparable`/`isComplete=false`。
- [ ] 运行 RED：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --filter "FullyQualifiedName~PlayerEvidenceContractTests|FullyQualifiedName~PlayerInventoryDiffServiceTests"
  ```

  预期：因证据模型、store 接口和 diff 服务尚不存在而编译失败；失败范围只落在新增测试引用的类型。

- [x] 实现 `PlayerProfile` 与 `PlayerProfileSection<T>`，每个分区独立携带 `state`、`observedAtUtc`、`value`、`gapMetadata`；总体结果不得因单个分区失败而清空其他分区。
- [x] 实现会话、活动、每日摘要、背包快照/条目、技能快照/值、gap、diff、来源关联和 `(observedAtUtc, id)` 降序 keyset 模型；每日摘要的未知计数使用 nullable，不把 gap 当零。
- [x] 定义 `IPlayerEvidenceStore` 的 append/query/compact 方法，以及 `IPlayerActionOperationQuery.Get(operationId)`；接口只传产品自有标量和类型化记录。
- [x] 实现纯函数 `PlayerInventoryDiffService.Compare(previous, current, gaps, confirmedOperations)`：只有成功且前后快照 ID 精确关联的 7DPanel 动作为 `Confirmed`，其他变化一律为 `ObservedChange`。
- [ ] 运行 GREEN：使用同一命令，预期新增合同与 diff 测试全部通过，且未改现有 `IPlayerActions`/kick 合同。

### Task 2：串行建立 `010` migration 与 SQLite 类型化 stores

**Files:**

- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Migrations/010_PlayerEvidenceActions.sql`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqlitePlayerEvidenceStore.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqlitePlayerActionStores.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/LSTY.SevenDPanel.Adapters.Persistence.Sqlite.csproj`
- Create: `backend/tests/LSTY.SevenDPanel.Tests/SqlitePlayerEvidenceStoreTests.cs`
- Create: `backend/tests/LSTY.SevenDPanel.Tests/SqlitePlayerActionStoresTests.cs`

- [x] 先写 migration/store RED 测试：空库升级、从第二波 `009_JobsBackupsSchedules.sql` 升级、重复 bootstrap、UTC round-trip、snapshot keyset、事务回滚、重复唯一键、并发读写、gap 合并、确定性保留与五类动作幂等冲突。
- [ ] 运行 RED：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --filter "FullyQualifiedName~SqlitePlayerEvidenceStoreTests|FullyQualifiedName~SqlitePlayerActionStoresTests"
  ```

  预期：因 `010`、新 store 和表不存在而失败；已有 migration 测试仍保持绿色。

- [x] 创建以下固定 schema；所有时间保存 Unix 毫秒，外键开启，列表查询索引以 `crossplatform_id, observed_at_utc DESC, id DESC` 为前缀：

  ```sql
  player_sessions(id, crossplatform_id, server_id, world_id, started_at_utc,
                  ended_at_utc NULL, end_reason, last_x NULL, last_y NULL, last_z NULL,
                  completeness)
  player_activity_events(id, crossplatform_id, server_id, world_id, kind,
                         observed_at_utc, correlation_id NULL, completeness)
  player_inventory_snapshots(id, crossplatform_id, server_id, world_id,
                             observed_at_utc, game_version, catalog_version NULL,
                             catalog_resolution, fingerprint, admin_boundary)
  player_inventory_items(snapshot_id, container_kind, slot_index, internal_name,
                         item_kind, count, quality NULL, use_amount NULL,
                         PRIMARY KEY(snapshot_id, container_kind, slot_index))
  player_inventory_item_mods(snapshot_id, container_kind, slot_index, ordinal, internal_name)
  player_skill_snapshots(id, crossplatform_id, server_id, world_id,
                         observed_at_utc, game_version, level NULL, skill_points NULL)
  player_skill_values(snapshot_id, skill_key, state, value NULL, minimum NULL,
                      maximum NULL, next_level_cost NULL, parent_key NULL)
  inventory_gaps(id, crossplatform_id, started_at_utc, ended_at_utc,
                 reason, estimated_lost_count)
  skill_gaps(id, crossplatform_id, started_at_utc, ended_at_utc,
             reason, estimated_lost_count)
  ```

- [x] 在同一 migration 创建五张类型化操作表：`player_grant_item_operations`、`player_remove_item_operations`、`player_reset_skills_operations`、`player_clear_inventory_operations`、`player_reset_data_operations`。重复公共列为 `operation_id`、operator、固定目标、world、client request key、correlation ID、status、created/started/completed UTC、failure code、前后证据 ID；每表只增加本动作的强类型参数列。
- [x] 对每张动作表建立 `(operator_id, client_request_key)` 唯一索引；同键同参数返回原 operation，同键不同参数返回 typed conflict。禁止向表中加入 `payload_json`、command text 或文件路径。
- [x] 实现 `SqlitePlayerEvidenceStore` 的事务 append/query/gap/compact；保留算法按稳定排序优先保留第一条、最新条、fingerprint 变化条和 `admin_boundary=1` 条，其余按固定时间桶留一条，重复 compact 得到相同结果。
- [x] 实现五个 concrete operation store 与固定摘要查询；Pending 和终态条件更新使用 compare-and-set，终态已存在时不可被后到失败覆盖。`010` migration 重新创建第一波 `unified_audit_projection`，把五类动作表的稳定摘要作为专用来源加入查询，不复制背包内容、技能明细或错误正文。
- [ ] 运行 GREEN：使用同一命令，预期新库与 `009 -> 010` 均成功、约束生效、并发/保留/幂等测试通过。

### Task 3：采集会话、活动、背包和技能标量，并形成独立有界 writer

**Files:**

- Create: `backend/src/Runtime/LSTY.SevenDPanel.Hosting/PanelPlayerEvidenceOptions.cs`
- Modify: `backend/src/Runtime/LSTY.SevenDPanel.Hosting/PanelHostOptions.cs`
- Modify: `backend/src/Bootstrap/LSTY.SevenDPanel/Configuration/PanelHostConfig.cs`
- Modify: `backend/src/Bootstrap/LSTY.SevenDPanel/Configuration/PanelHostConfigurationLoader.cs`
- Modify: `backend/src/Bootstrap/LSTY.SevenDPanel/config.example.json`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/PlayerEvidenceDraft.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/PlayerEvidenceWriteService.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/SevenDaysPlayerEvidenceProjection.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/SevenDaysPlayerEvidenceSnapshotReader.cs`
- Create: `backend/tests/LSTY.SevenDPanel.Tests/PlayerEvidenceProjectionTests.cs`
- Create: `backend/tests/LSTY.SevenDPanel.Tests/PlayerEvidenceWriteServiceTests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/PanelHostOptionsTests.cs`

- [x] 先写 RED 测试，固定 `PlayerEvidence.ServerId="local"`、`TimeZoneId="UTC"` 默认值与 IANA/Windows 时区解析失败时整节安全回退；验证 `config.example.json` 与 runtime default 一致。
- [x] 写投影 RED 测试：缺少 combined ID 不写长期证据；join/open session、disconnect/close session；缺边界保持 open/Partial；`SavePlayerData` 复制 bag/toolbelt/equipment、quality、use amount、批准 mod internal name、`Progression.Level`、`SkillPoints` 和批准 progression 键值。
- [x] 写不可变性和版本测试：回调返回后修改原 `PlayerDataFile`/`ItemStack` 不改变 draft；未加载与版本不支持不写成零；目录不可用仍保存 internal name 并标记 `Unavailable`。
- [x] 写 writer RED 测试：回调不等待 SQLite；单消费者有界顺序；队满、store exception、stop drain timeout 分别写玩家范围的 inventory/skill gap；同一 player/server/observed UTC 重复输入幂等。
- [ ] 运行 RED：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --filter "FullyQualifiedName~PlayerEvidenceProjectionTests|FullyQualifiedName~PlayerEvidenceWriteServiceTests|FullyQualifiedName~PanelHostOptionsTests"
  ```

  预期：新增配置和采集类型缺失导致新测试失败；既有宿主配置行为不回归。

- [x] 实现只读 `PanelPlayerEvidenceOptions`，包含 `ServerId`、解析后的 `TimeZoneInfo`、固定 queue capacity/drain timeout/retention defaults；配置只进入宿主 `config.json`，不进入 `serverconfig.xml` 字段目录。
- [x] `SevenDaysPlayerEvidenceProjection` 复用已确认的 `PlayerJoinedGame`、`SavePlayerData`、`PlayerDisconnected` 订阅；只为带 combined ID 的观察创建 session/activity/snapshot draft。位置活动使用本次观察的标量坐标；不能唯一关联稳定身份的事件不入库并由 Profile 报 `Unavailable`。
- [x] `SevenDaysPlayerEvidenceSnapshotReader` 只提供危险动作前后的显式主线程标量读取；不得返回 `PlayerDataFile`、`ItemStack`、`ItemValue`、容器或 Unity 对象。
- [x] 资源解析仅调用当前 `IGameResourceCatalog`；目录未就绪保存原 internal name、kind 和目录不可用状态，不丢弃证据，不缓存跨版本 `resourceId`。
- [x] 实现独立 `PlayerEvidenceWriteService`，使用有界单消费者队列；inventory 与 skill 丢失分别聚合 gap，stop 先停止接收再限时排空，不能把 gap 伪装成变化事件。
- [ ] 运行 GREEN：使用同一命令，预期配置、标量复制、不可变性、session 和 writer/gap 测试全部通过。

### Task 4：交付 Profile、快照、技能、diff、来源与每日摘要查询用例

**Files:**

- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Evidence/GetPlayerProfileUseCase.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Evidence/GetInventorySnapshotsUseCase.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Evidence/GetInventoryDiffsUseCase.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Evidence/GetPlayerSkillsUseCase.cs`
- Create: `backend/tests/LSTY.SevenDPanel.Tests/PlayerEvidenceUseCaseTests.cs`

- [x] 先写 RED 测试：稳定 identity 聚合现有历史摘要/位置与新 session/activity/inventory/skills；一个 source 抛错只使对应分区 `Unavailable`；Owner 之外为 `Forbidden`；开放 session、gap、目录不可用和不同 observed UTC 正确传播。
- [x] 写每日摘要测试：以配置时区把 UTC 事件分桶，DST 边界不重复/遗漏；只累计已知 session/login/chat/death/kill/inventory observation 计数，未知或 gap 区间返回 nullable/Partial。
- [x] 写三个 keyset 查询测试：page size 边界、非法 cursor、相同 observed UTC 用 ID 打破平局、下一页无重复；diff 的 source link 只指向已成功且精确关联前后快照的 operation。
- [ ] 运行 RED：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --filter "FullyQualifiedName~PlayerEvidenceUseCaseTests"
  ```

  预期：四个用例尚不存在而失败。

- [x] 实现 `GetPlayerProfileUseCase` 的分区隔离聚合；Profile 不落成新用户表，不按名称/IP/entity/native platform/Discord 猜测身份。
- [x] 实现 snapshot/skill/diff keyset 用例；HTTP 所需图标 `resourceId` 在读取时按当前目录解析，持久 internal name 保持不变。
- [x] 将当前版本没有唯一稳定身份事件的 activity kind 显式设为 `Unavailable`，不得通过日志文本或时间邻近补 death/kill/pickup/craft/trade/drop/cheat 标签。
- [ ] 运行 GREEN：使用同一命令，预期分区隔离、时区、keyset、gap 和来源等级测试全部通过。

### Task 5：并行纵向切片 A——类型化发放物品

**Files:**

- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Actions/GrantItem/GrantItemRequest.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Actions/GrantItem/IGrantItemOperationStore.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Actions/GrantItem/IGrantItemGateway.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Actions/GrantItem/GrantItemUseCase.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/SevenDaysGrantItemGateway.cs`
- Create: `backend/tests/LSTY.SevenDPanel.Tests/GrantItemUseCaseTests.cs`
- Create: `backend/tests/LSTY.SevenDPanel.Tests/SevenDaysGrantItemGatewayTests.cs`

- [x] 先写 RED 测试：同 idempotency key 同参数复用、异参数冲突；Pending store 失败不 dispatch；目录版本/`resourceId`/internal name 固定；隐藏物品缺少强确认拒绝；数量、stack、总上限和 quality 边界。
- [x] 写 gateway RED 测试：dispatcher 内按 combined ID + entity ID + world ID + online observed stamp 重验；目标替换、离线、目录变化、版本不支持、背包容量不足均在副作用前拒绝；不得生成世界掉落实体。
- [x] 写结果测试：成功返回真实授予数并保存前后快照；副作用前取消为 `Cancelled`；开始后连接/回调中断为 `ResultUnknown`；终态 store 失败不改写为动作失败。
- [ ] 运行 RED：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --filter "FullyQualifiedName~GrantItemUseCaseTests|FullyQualifiedName~SevenDaysGrantItemGatewayTests"
  ```

  预期：发放请求、store、用例和 gateway 缺失导致失败。

- [x] 实现 `GrantItemRequest`，固定 operator、`PlayerTargetStamp`、catalogVersion、resourceId、服务端解析 internalName/kind、quantity、optional quality、hidden confirmation、clientRequestKey、correlationId。
- [x] 用例按“解析目录并校验 → 保存 Pending → 读取前置标量快照 → dispatch → 保存后置快照 → compare-and-set 终态”的顺序执行；Pending 之前不得进入游戏线程。
- [x] gateway 在主线程使用已验证的 `ItemValue`/`ItemStack` 创建能力与 bag API；只有能证明全部数量进入批准容器才成功，容量不足不得静默落地或部分成功。
- [ ] 运行 GREEN：使用同一命令，预期发放边界、固定目标、幂等、证据关联和未知结果测试全部通过。

### Task 6：并行纵向切片 B——类型化删除物品

**Files:**

- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Actions/RemoveItem/RemoveItemRequest.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Actions/RemoveItem/IRemoveItemOperationStore.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Actions/RemoveItem/IRemoveItemGateway.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Actions/RemoveItem/RemoveItemUseCase.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/SevenDaysRemoveItemGateway.cs`
- Create: `backend/tests/LSTY.SevenDPanel.Tests/RemoveItemUseCaseTests.cs`
- Create: `backend/tests/LSTY.SevenDPanel.Tests/SevenDaysRemoveItemGatewayTests.cs`

- [x] 先写 RED 测试：精确 internal name、quantity、`BagOnly` scope、`Exact`/`UpToAvailable` mode；默认/反序列化不能扩展到 equipment、toolbelt 或其他容器；目录和幂等规则与发放独立。
- [x] 写数量测试：`Exact` 库存不足时零副作用并冲突；`UpToAvailable` 必须由请求显式固定，结果保存真实删除数；同名不同品质/mod 的选择顺序按 container/slot 稳定且可复现。
- [x] 写固定目标、Pending、前后快照、取消、终态 store failure 和 `ResultUnknown` RED 测试，不复用 grant DTO 或 gateway。
- [ ] 运行 RED：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --filter "FullyQualifiedName~RemoveItemUseCaseTests|FullyQualifiedName~SevenDaysRemoveItemGatewayTests"
  ```

  预期：删除专用合同和实现缺失导致失败。

- [x] 实现独立删除用例与 gateway；主线程先扫描固定范围并计算可用数，再一次性应用确定性槽位变更。`Exact` 不能边扫边删，`UpToAvailable` 也必须回报实际数量。
- [x] 成功终态关联动作前后 inventory snapshot，使相邻 diff 可标为 `Confirmed`；失败、拒绝和 unknown 不能生成已确认来源。
- [ ] 运行 GREEN：使用同一命令，预期删除范围、数量、固定目标、幂等和证据测试全部通过。

### Task 7：并行纵向切片 C——技能重置与背包清空

**Files:**

- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Actions/ResetSkills/ResetSkillsRequest.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Actions/ResetSkills/IResetSkillsOperationStore.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Actions/ResetSkills/IResetSkillsGateway.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Actions/ResetSkills/ResetSkillsUseCase.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Actions/ClearInventory/ClearInventoryRequest.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Actions/ClearInventory/IClearInventoryOperationStore.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Actions/ClearInventory/IClearInventoryGateway.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Actions/ClearInventory/ClearInventoryUseCase.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/SevenDaysPlayerResetGateways.cs`
- Create: `backend/tests/LSTY.SevenDPanel.Tests/PlayerPartialResetUseCaseTests.cs`
- Create: `backend/tests/LSTY.SevenDPanel.Tests/SevenDaysPlayerResetGatewayTests.cs`

- [x] 先写 RED 测试，证明两类请求、store、gateway、operation status 和确认摘要互不复用；两类动作都要求 Fresh fixed target、world、client request key、correlation ID 和显式危险确认。
- [x] 固定技能重置范围为当前版本批准的 progression reset；测试保留身份/位置/背包，不执行完整玩家数据重置，也不接受控制台字符串。
- [x] 固定背包清空范围为 bag inventory；测试不清空 equipment、toolbelt 或其他容器，并保存前后 inventory 快照供 Confirmed diff。
- [x] 覆盖 Pending 失败不 dispatch、重验失败零副作用、开始后中断 `ResultUnknown`、终态重写保护和相同 idempotency key 行为。
- [ ] 运行 RED：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --filter "FullyQualifiedName~PlayerPartialResetUseCaseTests|FullyQualifiedName~SevenDaysPlayerResetGatewayTests"
  ```

  预期：两类重置合同和 gateway 缺失导致失败。

- [x] 实现两个专用用例与 gateway；在 dispatcher 内使用当前版本 API 的专用调用，不调用控制台命令，不直接编辑 `.ttp`/`.map` 文件。
- [x] 技能重置保存前后 skill snapshot；背包清空保存前后 inventory snapshot；只有成功终态建立 Confirmed 来源。
- [ ] 运行 GREEN：使用同一命令，预期作用域隔离、证据、固定目标和 unknown 语义全部通过。

### Task 8：完整玩家数据重置与 Pending/终态恢复

**Files:**

- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Actions/ResetPlayerData/ResetPlayerDataRequest.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Actions/ResetPlayerData/IResetPlayerDataOperationStore.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Actions/ResetPlayerData/IResetPlayerDataGateway.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Actions/ResetPlayerData/ResetPlayerDataUseCase.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Players/Actions/PlayerActionRecoveryService.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/SevenDaysResetPlayerDataGateway.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/PlayerActionRecoveryRuntime.cs`
- Create: `backend/tests/LSTY.SevenDPanel.Tests/ResetPlayerDataUseCaseTests.cs`
- Create: `backend/tests/LSTY.SevenDPanel.Tests/PlayerActionRecoveryTests.cs`

- [x] 先写 RED 测试：缺少完整前置 player/inventory/skill snapshot 必须拒绝，强制参数或隐藏分支不能绕过；固定 identity/entity/world 在 prepare 后变化必须零副作用。
- [x] 写两阶段状态测试：Pending 持久化并关联前置证据后才能标记 started；started 后任何断连/timeout/进程恢复都不得自动重试，不能确认终态时为 `ResultUnknown` 并带人工核对提示。
- [x] 写恢复测试：五类操作的已提交终态重试持久化；启动时 stale Pending 未 started 可安全标记 `Cancelled`，started 且无可验证终态标记 `ResultUnknown`；Succeeded/Rejected/Failed 不可被恢复任务覆盖。
- [ ] 运行 RED：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --filter "FullyQualifiedName~ResetPlayerDataUseCaseTests|FullyQualifiedName~PlayerActionRecoveryTests"
  ```

  预期：完整重置与恢复服务缺失导致失败。

- [x] 实现专用两阶段用例：prepare 读取并持久化完整标量证据，execute 再次重验固定目标和世界并调用专用 gateway；HTTP/审计永不返回实际文件路径。
- [x] gateway 封装当前版本已验证的完整 player data reset API；不得直接复制旧项目文件删除代码，也不得通过 console command 执行。
- [x] 实现恢复 runtime，启动后只查询/归档既有 Pending，不重放游戏副作用；所有类型通过 concrete store 转成固定公共摘要供 operation GET 查询。
- [ ] 运行 GREEN：使用同一命令，预期前置证据、两阶段重验、无自动重试和恢复语义全部通过。

### Task 9：串行合流配置、migration、DI 与 runtime 生命周期

**Files:**

- Modify: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/OnlinePlayerProjectionRuntime.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/PlayerEvidenceRuntime.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/DependencyInjectionTests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/DependencyRulesTests.cs`
- Create: `backend/tests/LSTY.SevenDPanel.Tests/PlayerEvidenceRuntimeTests.cs`

- [x] 先写 RED 测试，要求所有 read use case、五类 typed store/use case/gateway、snapshot reader、writer、projection、recovery 与 `IPlayerActionOperationQuery` 可从唯一 composition root 解析；Application 不引用 Web/SQLite/SevenDays。
- [x] 写 runtime RED 测试：数据库 bootstrap/migration 完成后才启动 writer；writer 接收后才订阅 projection；停止时先退订、再停止接收、最后限时排空；recovery 在动作 API 可用前扫描一次。
- [ ] 运行 RED：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --filter "FullyQualifiedName~DependencyInjectionTests|FullyQualifiedName~DependencyRulesTests|FullyQualifiedName~PlayerEvidenceRuntimeTests"
  ```

  预期：新服务尚未注册、runtime 顺序尚未接入而失败。

- [x] 在 `PanelServiceProviderFactory` 逐个注册 concrete store 与 typed gateway；共享固定摘要查询是只读消费者，不注册动作 registry 或 service locator。
- [x] 将 runtime 链串行为“SQLite bootstrap → 游戏资源目录 → player evidence writer/projection → action recovery → 既有 chat/map/后续 runtime”；保留既有在线/历史玩家 runtime，避免重复拥有其状态。
- [x] 明确 shutdown 逆序和 drain timeout 日志；日志只含 operation/correlation/stable player ID 与状态，不含 token、路径、游戏对象或命令。
- [ ] 运行 GREEN：使用同一命令，预期解析、依赖方向、启动/停止、gap 与恢复顺序测试全部通过。

### Task 10：串行交付 Owner HTTP、OpenAPI snapshot 与 Hey API 客户端

**Files:**

- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/PlayerEvidenceHttpModels.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/PlayerEvidenceCursorCodec.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/PlayerEvidenceController.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/PlayerActionHttpModels.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/PlayerActionsController.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OpenApi/PanelOpenApiOperationProcessor.cs`
- Create: `backend/tests/LSTY.SevenDPanel.Tests/PlayerEvidenceWebContractTests.cs`
- Create: `backend/tests/LSTY.SevenDPanel.Tests/PlayerActionsWebContractTests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostOpenApiSnapshotTests.cs`
- Modify: `frontend/apps/admin/openapi/7dpanel.v1.json`
- Regenerate: `frontend/apps/admin/src/shared/api/generated/`

- [x] 先写 HTTP RED 测试，固定 Owner-only GET/POST、URL encoding、keyset、DTO camelCase/nullability、operation ID、correlation ID、Problem Details 与响应不含 object/path/token/command。
- [x] 固定 GET：`/api/v1/players/{crossplatformId}/profile`、`/inventory-snapshots`、`/inventory-diffs`、`/skills`，后三者接受 `pageSize` 与 opaque `cursor` 并返回 gap metadata。
- [x] 固定 POST：`/api/v1/player-actions/grant-item`、`remove-item`、`reset-skills`、`clear-inventory`、`reset-player-data`；固定恢复 GET：`/api/v1/player-actions/{operationId}`。每个 POST 使用独立 request/response class。
- [x] 固定状态：非法输入/游标 `400`，非 Owner `403`，玩家/operation/resource 不存在 `404`，stale target/catalog change/quantity/space/idempotency conflict `409`，版本或品质不支持 `422`，game/catalog 未就绪 `503`；Pending 返回 `202`，已知终态返回 `200`。
- [ ] 运行 RED：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --filter "FullyQualifiedName~PlayerEvidenceWebContractTests|FullyQualifiedName~PlayerActionsWebContractTests|FullyQualifiedName~OwinWebHostOpenApiSnapshotTests"
  ```

  预期：路由/DTO 缺失且 OpenAPI snapshot 不匹配。

- [x] 实现两个控制器与 cursor codec；operator/correlation 仅从认证 identity 和 request middleware 取得。历史玩家现有页面/路由不增加 POST 入口。
- [ ] 更新 OpenAPI schema，逐个列出五类 request/response 与状态枚举，不用 `additionalProperties` 代替 typed 参数；更新 snapshot 后运行同一命令，预期 HTTP 与 snapshot 测试通过。
- [ ] 生成客户端并检查无漂移：

  ```powershell
  pnpm --dir frontend/apps/admin api:gen
  git diff --exit-code -- frontend/apps/admin/src/shared/api/generated
  ```

  预期：第一次命令更新生成代码；将生成结果纳入本任务后第二次重新生成，再执行 diff 检查无新增变化。这里的 `git diff` 只读，不 stage、不 commit。

### Task 11：Admin 玩家 Profile、证据分区与固定目标危险操作

**Files:**

- Create: `frontend/apps/admin/src/features/player-profile/api/playerEvidence.ts`
- Create: `frontend/apps/admin/src/features/player-profile/api/playerActions.ts`
- Create: `frontend/apps/admin/src/features/player-profile/model/usePlayerProfile.ts`
- Create: `frontend/apps/admin/src/features/player-profile/model/usePlayerEvidence.ts`
- Create: `frontend/apps/admin/src/features/player-profile/model/usePlayerActions.ts`
- Create: `frontend/apps/admin/src/features/player-profile/ui/PlayerProfileView.vue`
- Create: `frontend/apps/admin/src/features/player-profile/ui/PlayerProfileSummary.vue`
- Create: `frontend/apps/admin/src/features/player-profile/ui/PlayerInventoryPanel.vue`
- Create: `frontend/apps/admin/src/features/player-profile/ui/PlayerSkillsPanel.vue`
- Create: `frontend/apps/admin/src/features/player-profile/ui/PlayerActivityPanel.vue`
- Create: `frontend/apps/admin/src/features/player-profile/ui/PlayerEvidenceBadge.vue`
- Create: `frontend/apps/admin/src/features/player-profile/ui/PlayerActionDialogFrame.vue`
- Create: `frontend/apps/admin/src/features/player-profile/ui/playerProfileUi.ts`
- Create: `frontend/apps/admin/src/features/player-profile/ui/GrantItemDialog.vue`
- Create: `frontend/apps/admin/src/features/player-profile/ui/RemoveItemDialog.vue`
- Create: `frontend/apps/admin/src/features/player-profile/ui/ResetSkillsDialog.vue`
- Create: `frontend/apps/admin/src/features/player-profile/ui/ResetPartialDialog.vue`
- Create: `frontend/apps/admin/src/features/player-profile/ui/ResetFullDialog.vue`
- Create: `frontend/apps/admin/src/pages/players/profile/[crossplatformId].vue`
- Modify: `frontend/apps/admin/src/features/players/ui/OnlinePlayerDetailsSlideover.vue`
- Modify: `frontend/apps/admin/src/features/players/ui/HistoricalPlayerView.vue`
- Modify: `frontend/apps/admin/src/features/players/ui/PlayersSectionNavigation.vue`
- Modify: `frontend/apps/admin/src/app/router.test.ts`
- Modify: `frontend/apps/admin/src/app/i18n/locales/zh-CN.json`
- Modify: `frontend/apps/admin/src/app/i18n/locales/en.json`
- Create: `frontend/apps/admin/src/features/player-profile/api/playerProfileApi.test.ts`
- Create: `frontend/apps/admin/src/features/player-profile/model/playerProfileComposables.test.ts`

- [x] 先写 API/composable RED 测试，mock 生成客户端，固定 abort、认证 header、opaque cursor、各分区独立 Loading/Partial/Stale/Unavailable/Forbidden、operation polling 与 stale response 不覆盖新玩家。
- [ ] 写视图 RED 测试：桌面 `UTable` + `USlideover`、窄屏单列条目、gap 与 `Confirmed`/`ObservedChange` badge、可空技能显示“未知/未加载/版本不支持”、中英文案均存在。
- [ ] 写危险动作 RED 测试：只在 Owner + Fresh online target 显示；对话框固定玩家/world/entity/observed time、物品/数量/范围/影响/catalog version；刷新数据不能替换已打开目标，只能使其失效并要求关闭重开。
- [ ] 写历史只读测试：`HistoricalPlayerView` 可以进入资料读取，但不渲染 grant/remove/reset 按钮；直接路由也必须由 Fresh online 分区和服务端重验共同解锁动作。
- [ ] 运行 RED：

  ```powershell
  pnpm --dir frontend/apps/admin exec vitest run src/features/players src/app/router.test.ts src/app/i18n
  ```

  预期：新 API/composable/components/route/i18n key 不存在导致相关测试失败。

- [x] 使用 `<script setup lang="ts">` 保持 route page 为薄组合层；composable 拥有请求、取消和 operation polling，展示组件只接受 typed props/emits。不得手写一套与生成 schema 重复的 DTO。
- [x] 用 Nuxt UI v4 `UTable`、`UBadge`、`USlideover`、`UModal`、`UForm`、`UAlert`、`USkeleton` 组合五个独立确认表单；typed submit 只发送各自字段，不使用万能 `actionType + payload`。
- [x] 完整重置使用独立二次强确认并清晰显示仅可丢弃玩家/可恢复世界提示；`ResultUnknown` 保持 operation ID 和人工核对提示，不自动重发。
- [x] 更新在线详情的 Fresh Profile 入口、玩家分区导航和双语；历史视图只增加只读资料链接/说明，不增加动作入口。
- [ ] 运行 GREEN：使用同一命令，预期 API、状态、响应式结构、固定目标、历史只读、路由和双语聚焦测试全部通过。

### Task 12：一次聚合门禁、一次真实 7DTD 矩阵与 Current 文档提升

**Files:**

- Modify after verified implementation: `docs/architecture.md`
- Modify after verified implementation: `docs/design.md`
- Modify after verified implementation: `docs/test.md`
- Modify only when the user-visible wave is released: `CHANGELOG.md`

- [ ] 确认任务 1–11 的聚焦 GREEN 结果已保存；此处不重复运行聚焦集合，不运行 publish，不运行浏览器 smoke。
- [ ] 后端受影响聚合门禁只运行一次：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release
  ```

  预期：全部后端测试通过，`010` 新库/升级、Application、SQLite、SevenDays、Web、OpenAPI 和 DI/runtime 均无失败。

- [ ] Admin 受影响聚合门禁只运行一次，各命令在同一门禁阶段各执行一次：

  ```powershell
  pnpm --dir frontend/apps/admin exec vue-tsc --noEmit
  pnpm --dir frontend/apps/admin exec vitest run
  pnpm --dir frontend/apps/admin exec vite build
  ```

  预期：类型检查、全部 Admin 测试和生产构建通过；生成客户端无类型漂移。

- [ ] 完整玩家数据重置属于本路线允许暂停的破坏性真实环境操作；执行真实矩阵前向用户确认精确测试玩家、世界备份和恢复目标。确认后在受控 7DTD v3.0.1-b4 环境只执行一次：使用一个可丢弃测试玩家和可恢复世界，先记录 combined ID/entity/world/catalog/在线观察 stamp 与基线 Profile，再依次采集保存事件、发放 1 个批准低价值物品、删除该物品、技能重置、背包清空，最后执行完整玩家数据重置。
- [ ] 每个真实动作使用唯一 client request key；动作后通过 `GET /api/v1/player-actions/{operationId}` 和 Profile/inventory/skills 查询记录终态、前后证据、diff/source/gap。任一步返回 stale/unknown 时停止后续破坏性动作并人工核对，绝不重放相同或新 key。
- [ ] 完整重置前再次确认测试玩家可丢弃、世界备份可恢复且当前 target stamp 未变化；完成后恢复世界/测试玩家基线。不得在真实玩家或不可恢复世界执行。
- [ ] 将已实现且已通过门禁的事实提升到 `docs/architecture.md`：表/保留/gap、writer/runtime、标量复制、目录解析、五类 gateway、Pending/终态/恢复、依赖矩阵和经验证的 v3.0.1-b4 API；不得用 Target 条目冒充实现证据。
- [ ] 更新 `docs/design.md`：Profile 信息架构、分区状态、桌面/窄屏、证据标签、固定目标确认、历史只读和 `ResultUnknown` 恢复流程；更新 `docs/test.md`：新增追踪、聚焦层级、聚合门禁与本次真实矩阵证据。聚合命令未变化，因此不改 README。
- [ ] 只有本波实际发布后才在 `CHANGELOG.md` 增加 Owner 可见 Profile/证据/类型化动作条目；未发布时保持 changelog 不变。目标蓝图未发生批准方向变化，因此不修改三份 Target 蓝图。

## 规格逐节映射自查

| 规格节 | 计划任务 | 完成证据 |
| --- | --- | --- |
| 上游、状态与波次职责 | 1、4、12 | CAP/NFR 追踪、Current 提升、后续消费者只依赖标量/类型化合同 |
| 用户结果 | 4、5、6、7、8、11 | Profile、证据、五类动作及 Admin 入口 |
| 稳定身份与资料聚合 | 1、3、4 | combined ID、分区状态、session、时区每日摘要 |
| 背包与技能观察 / 采集边界 | 1、3 | scalar draft、目录状态、空值/版本语义、不可变性 |
| 持久化、保留与 gap | 2、3 | `010`、keyset、确定性 compact、独立 writer、两类 gap |
| 物品变化与来源证据 | 1、2、4、5、6、7 | 派生 diff、Confirmed 精确关联、ObservedChange、gap overlap |
| 类型化玩家动作 / 共同规则 | 2、5–10 | 五张操作表、五类请求/use case/gateway、Pending、重验、幂等 |
| 发放与删除 | 5、6 | 目录/隐藏/数量/空间、BagOnly、Exact/UpToAvailable、真实数量 |
| 玩家重置 | 7、8 | 技能、背包、完整数据三种独立流程、前置证据、ResultUnknown |
| 接口与 Admin | 10、11 | 固定 GET/POST/operation 路由、生成客户端、响应式分区页面 |
| 权限、安全与审计 | 1、2、5–11 | Owner、无路径/token/command、typed audit、correlation ID |
| 非目标 | 全任务边界 | 无身份猜测、日志推断、通用 registry/repository、脚本平台或第四波能力 |
| 精简验证 | 各任务 RED/GREEN、12 | 聚焦迭代、后端/Admin 各一次聚合、一次受控真实矩阵 |
| 完成条件 | 4–12 | 诚实聚合、diff/source/gap 分离、固定目标、可恢复终态、后续可消费合同 |

## 最终一致性与 Git 边界

- [ ] 全文检查只有一个 primary dated design spec，所有 Current/Target 链接均存在且没有第二个 primary spec。
- [ ] 对照固定合同检查 C#、SQLite、OpenAPI、生成 TypeScript、Vue props/emits 和 i18n 中的枚举/nullable/UTC/keyset 字段一致。
- [ ] 搜索并确认计划和实现中没有占位标记，也没有万能 payload、动作 registry、控制台命令或旧项目代码复制。
- [ ] 确认每个规格章节均映射到至少一个任务，实际代码/config/test 状态再写入 Current 文档，Target 文档不作为完成证据。
- [ ] 本计划不授权 `git commit`、`git push`、`git reset`、`git revert` 或自动 stage；任何 Git 提交类操作必须由用户另行显式授权。
