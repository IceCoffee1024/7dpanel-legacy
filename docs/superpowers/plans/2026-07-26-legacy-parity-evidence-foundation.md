---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-26-legacy-parity-evidence-foundation-design.md
last_updated: "2026-07-26"
---

# 旧版本功能对齐第一波：运行指标与证据闭环实施计划

## 2026-07-26 当前执行记录

- 已实现纵向切片：十一项类型化运行指标、`008_EvidenceFoundation.sql`、四类游戏事件与 gap、统一审计只读查询、禁言/到期清理、固定 `help` 命令、Owner HTTP，以及 Admin 审计/事件/禁言/指标与双语界面均已有当前代码、配置和测试文件。
- 已知执行证据：`docs/test.md` 记录事件与 `008` 为 `25/25`、统一审计 `20/20`、禁言/到期/`help` 为 `12/12`、DI/生命周期/OWIN/OpenAPI 为 `107/107`、聊天 HTTP `6/6`、游戏内命令 `2/2`、禁言授权 `12/12`、Admin 两组分别 `95/95` 与 `68/68`；本轮另有 `pnpm api:schema` `1/1`、`pnpm api:gen` 成功和 Bootstrap Release build `0 warning`、`0 error`。测试文件存在本身不作为已运行证据，Bootstrap build 也不替代解决方案聚合测试。
- 未执行门禁与真实缺口：最终 Admin typecheck 以及 AppShell/router/i18n/Community 聚焦组合已经通过；后端解决方案 Release 聚合、第一波全部 Feature 专属复验、全量 ESLint/稳定全量 Vitest、真实 `v3.0.1-b4`、Playwright、人工浏览器、publish、Linux、容量饱和和恢复演练仍未闭环。运行指标真实来源、事件真实字段、禁言/到期、`help` 私发及响应式双语仍缺真实环境证据。

> **面向智能体执行者：** 实施时必须使用 `superpowers:subagent-driven-development`（推荐在当前会话按任务分派）或 `superpowers:executing-plans`（在独立执行会话逐项完成），并在每个任务的聚焦 RED/GREEN 检查通过后再进入其依赖任务。

## 目标、依据与边界

本计划只落实[第一波设计规格](../specs/2026-07-26-legacy-parity-evidence-foundation-design.md)，覆盖运行指标、四类游戏事件、统一只读审计、禁言、`help` 游戏命令和游戏聊天双语收口。产品合同仍由[产品需求](../../PRD.md)中的 `CAP-01`、`CAP-05`、`CAP-07`、`NFR-02`、`NFR-03` 管理；当前事实以[系统架构](../../architecture.md)为准，交互事实以[界面设计](../../design.md)为准，验证层级与门禁以[测试策略](../../test.md)为准。批准的未来范围见[旧版本功能对齐目标蓝图](../../architecture/legacy-feature-parity-target-blueprint.md)，该蓝图不是当前实现证据。

本波不建立通用 Event Bus、通用审计写模型、万能搜索或动作 payload、任意脚本平台、任意控制台命令入口，也不预注册经济、商店、传送、投票、兑换或绑定命令。仓库外只读旧项目 `7dtd-serveradmin` 只用于核对字段与可观察行为，不复制其实现代码。`7dtd-reference/` 保持只读。

## 固定合同与执行顺序

### 运行指标合同

现有 `GameOverviewSnapshot` 的游戏分区改为消费固定的 `GameRuntimeMetrics`，每项使用同一个直接生产消费关系下的类型化值对象，不提供任意字典：

```csharp
public sealed record ObservedMetric<T>(
    T? Value,
    string Source,
    string Unit,
    DateTimeOffset ObservedAtUtc,
    RuntimeMetricWarningCode? Warning);

public enum RuntimeMetricWarningCode
{
    ReadFailed,
    Unsupported
}
```

`GameRuntimeMetrics` 固定包含 `GameDayTime`、`IsBloodMoon`、`FramesPerSecond`、`OnlinePlayerCount`、`HistoricalPlayerCount`、`AnimalCount`、`HostileEntityCount`、`ActiveEntityCount`、`ChunkCount`、`DroppedItemCount`、`GameMemoryBytes`。来源和单位固定如下；字段读取异常返回 `null + ReadFailed`，确认当前版本无可靠来源时返回 `null + Unsupported`，不得返回伪造的 `0`。

| 字段 | `v3.0.1-b4` 来源 | API 单位 |
|---|---|---|
| `GameDayTime` | `World.worldTime` 经现有 `GameUtils.WorldTimeToString` 格式化 | `game-clock` |
| `IsBloodMoon` | `World.aiDirector.BloodMoonComponent.BloodMoonActive` | `boolean` |
| `FramesPerSecond` | `GameManager.frameTime` 的有效倒数 | `frames/second` |
| `OnlinePlayerCount` | `World.Players.Count` | `count` |
| `HistoricalPlayerCount` | `GameManager.persistentPlayerCount` | `count` |
| `AnimalCount` | 当前世界实体快照中的 `EntityAnimal` | `count` |
| `HostileEntityCount` | 当前世界实体快照中的 `EntityZombie` | `count` |
| `ActiveEntityCount` | 当前世界活动实体集合 | `count` |
| `ChunkCount` | `Chunk.InstanceCount` | `count` |
| `DroppedItemCount` | 当前世界实体快照中的 `EntityItem` | `count` |
| `GameMemoryBytes` | `GC.GetTotalMemory(false)` | `bytes` |

一次 `GameThreadDispatcher` 回调内只复制标量，成功字段共享同一 `observedAtUtc`。主机分区、游戏分区和游戏分区内各指标分别保持 `Available`、`Stale`、`Unavailable` 或可空 warning 语义；REST snapshot 仍为权威，SSE 只触发刷新。

### 游戏事件、统一审计和禁言 schema

主执行者在一个串行 migration 中创建所有第一波表、索引和只读 view，后续 worker 不再各自修改 migration：

```sql
CREATE TABLE game_events (
  event_id TEXT PRIMARY KEY,
  event_type TEXT NOT NULL CHECK (event_type IN ('PlayerJoined','PlayerLeft','PlayerKilledEntity','PlayerDied')),
  occurred_utc INTEGER NOT NULL,
  observed_utc INTEGER NOT NULL,
  actor_crossplatform_id TEXT NULL,
  actor_platform_id TEXT NULL,
  actor_entity_id INTEGER NULL,
  actor_name TEXT NULL,
  target_crossplatform_id TEXT NULL,
  target_platform_id TEXT NULL,
  target_entity_id INTEGER NULL,
  target_name TEXT NULL,
  game_shutting_down INTEGER NULL
);

CREATE TABLE game_event_gaps (
  gap_id TEXT PRIMARY KEY,
  reason TEXT NOT NULL CHECK (reason IN ('QueueFull','StoreFailure','DrainTimeout')),
  started_utc INTEGER NOT NULL,
  ended_utc INTEGER NULL,
  affected_count INTEGER NOT NULL CHECK (affected_count > 0)
);

CREATE TABLE chat_mute (
  crossplatform_id TEXT PRIMARY KEY,
  display_name TEXT NULL,
  reason TEXT NOT NULL,
  muted_until_utc INTEGER NULL,
  created_by TEXT NOT NULL,
  created_utc INTEGER NOT NULL,
  updated_by TEXT NOT NULL,
  updated_utc INTEGER NOT NULL
);

CREATE TABLE chat_mute_operation (
  operation_id TEXT PRIMARY KEY,
  operation_kind TEXT NOT NULL CHECK (operation_kind IN ('Create','Update','Release','Expire')),
  target_crossplatform_id TEXT NOT NULL,
  actor_subject TEXT NULL,
  occurred_utc INTEGER NOT NULL,
  result TEXT NOT NULL,
  correlation_id TEXT NULL,
  muted_until_utc INTEGER NULL,
  reason TEXT NULL
);
```

`unified_audit_projection` 只以 `UNION ALL` 映射 `player_action_audit`、`console_command_audit`、`server_operation_audit`、`chat_operation_audit` 和 `chat_mute_operation`，列固定为 `source_kind`、`source_id`、`actor_subject`、`target_ref`、`action`、`occurred_utc`、`status`、`correlation_id`、`has_details`。view 不包含消息正文、参数正文、输出正文、凭据、Token、API Key、服务器路径或原始异常；各来源 gap 通过查询元数据返回，不进入 view。

### HTTP、命令与稳定结果码

- `GET /api/v1/audit`：按 `(occurredUtc, sourceKind, sourceId)` 倒序 keyset，筛选 `fromUtc`、`toUtc`、`actor`、`target`、`action`、`sourceKind`、`status`。
- `GET /api/v1/game-events`：operationId 为 `listGameEvents`；独立事件页按 `(occurredUtc, eventId)` 倒序 keyset，筛选时间、事件类型和稳定跨平台身份；gap 位于页面元数据。
- `GET /api/v1/chat/mutes`：按 `(updatedUtc,crossplatformId)` 倒序有界 keyset；`POST /api/v1/chat/mutes`、`PUT /api/v1/chat/mutes/{crossplatformId}`、`DELETE /api/v1/chat/mutes/{crossplatformId}` 使用固定请求类型，不接收动作判别字段。
- 上述新端点均为 `Owner` 专用；概览继续允许 `Owner`、`Admin`、`Viewer`。非法 cursor、筛选、UTC 截止时间或空原因返回稳定 Problem Details。
- 命令目录只注册 `help`，名称比较使用 `OrdinalIgnoreCase`，别名集合为空，参数为空，前缀来自现有聊天设置。已识别命令返回 `chat.command.help.succeeded`、`chat.command.invalid_arguments`、`chat.command.unavailable` 或 `chat.command.failed`；结果只私发调用玩家。未知命令返回“未处理”，继续原版聊天链，绝不转为控制台命令。

### 并行和串行合流

```text
任务 1（固定 Application 合同）
  ├─ 任务 2（运行指标纵向切片）──────────────┐
  └─ 任务 3（008 migration，主执行者串行）  │
       ├─ 任务 4（游戏事件纵向切片）         │
       ├─ 任务 5（统一审计纵向切片）         ├─ 任务 7（DI/HTTP/OpenAPI 串行合流）
       └─ 任务 6（禁言/help 纵向切片）       │
                                                ├─ 任务 8（审计/事件/禁言 Admin）
                                                └─ 任务 9（概览与聊天双语 Admin）
                                                     └─ 任务 10（一次聚合、一次真实窄验收、文档提升）
```

任务 3、7、10 由主执行者串行完成。任务 2 可与任务 3 并行；migration 合流后，任务 4、5、6 可由不同 worker 并行；任务 7 完成生成合同后，任务 8、9 可并行。任何 worker 发现需改共享 migration、`PanelServiceProviderFactory.cs`、OpenAPI snapshot 或生成目录时，只提交变更意图，由主执行者在对应串行任务合流。

## 任务 1：固定 Application 类型与纯逻辑合同

**文件：**

- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Overview/GameRuntimeMetrics.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/GameEvents/GameEventContracts.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/GameEvents/GameEventPorts.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/GameEvents/GameEventQueries.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Audit/UnifiedAuditContracts.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Audit/UnifiedAuditQuery.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Chat/ChatMuteContracts.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Chat/ChatMuteUseCases.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Chat/GameChatCommands.cs`
- Modify: `backend/src/Core/LSTY.SevenDPanel.Application/Chat/ChatPorts.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/EvidenceFoundationApplicationTests.cs`

- [x] 先写纯逻辑测试，固定指标 warning、UTC 要求、事件身份不按名称/entity ID 合并、事件和审计 cursor 排序、禁言永久/临时/恰好到期、命令名称冲突和未知命令未处理。
- [ ] 运行 RED：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~EvidenceFoundationApplicationTests
  ```

  预期：因上述合同与用例尚不存在而编译或断言失败；不得通过放宽断言得到绿色。
- [x] 实现 `GameEventRecord`、`GameEventSubject`、`GameEventGap`、`GameEventPage`、`GameEventQuery` 和 `IGameEventStore`；事件 ID 为服务端生成的字符串 GUID，所有时间要求 UTC，稳定身份可空。
- [x] 实现 `UnifiedAuditEntry`、`AuditSourceGap`、`UnifiedAuditPage`、`UnifiedAuditFilter` 和 `IUnifiedAuditQuery`；cursor 只携带固定排序三元组，不携带 SQL 或任意筛选对象。
- [x] 实现 `ChatMuteRecord`、`ChatMuteOperation`、`ChatMutePage`、`ChatMuteCursor`、`IChatMuteStore`、`ChatMuteUseCases`；创建/更新/解除在一个 Store 事务内同时写当前状态与专用 operation，提交后再通过 `IChatRuntimeConfiguration` 原子替换不可变快照，失败时保留旧快照。
- [x] 实现 `GameChatCommandDescriptor`、`GameChatCommandContext`、`GameChatCommandResult`、`IGameChatCommandHandler`、`GameChatCommandCatalog` 和 `HelpGameChatCommandHandler`；目录只接纳具名类型化处理器，不暴露运行期注册 API 给插件或脚本。
- [x] 运行 GREEN：执行 `dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~EvidenceFoundationApplicationTests`，预期退出码 `0`，并覆盖所有固定结果码与边界。

## 任务 2：交付诚实运行指标的后端到 HTTP 纵向切片

**文件：**

- Modify: `backend/src/Core/LSTY.SevenDPanel.Application/Overview/GameOverviewSnapshot.cs`
- Modify: `backend/src/Core/LSTY.SevenDPanel.Application/Overview/GetOverviewUseCase.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Overview/SevenDaysGameOverviewQuery.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OverviewHttpModels.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OverviewController.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/SevenDaysGameOverviewQueryTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/OverviewUseCaseTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/OverviewHttpTests.cs`

- [x] 先扩展三层测试，断言十一项指标的值、来源、单位、同一 `observedAtUtc`、逐字段 `ReadFailed`、版本不支持时 `Unsupported`、dispatcher 外不保留游戏对象、游戏分区 stale 不污染主机分区。
- [ ] 运行 RED：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~SevenDaysGameOverviewQueryTests|FullyQualifiedName~OverviewUseCaseTests|FullyQualifiedName~OverviewHttpTests"
  ```

  预期：新增指标合同断言失败，既有权限裁剪与缓存测试仍保持原语义。
- [x] 在一次 `CaptureOnGameThread` 中取得 `GameManager`、`World`、实体集合和固定标量；对每个字段独立捕获读取失败。实体只遍历一次并同时计算动物、敌对、活动和掉落物数量。
- [x] 将现有 FPS、游戏时钟、在线/历史人数迁入 `GameRuntimeMetrics`，同步更新所有直接消费者，不保留第二套互相漂移的 HTTP 别名字段。
- [x] 保持 4 秒缓存、5 秒 dispatcher 启动超时和 in-flight 合并；成功采样统一在回调结果转换时赋一个 `observedAtUtc`，超时保留最近成功快照并标记 stale，没有历史值时 unavailable。
- [x] 在 HTTP DTO 中显式列出十一项，不使用 `Dictionary<string, object>`；继续执行现有 Owner/Admin/Viewer 读取和主机敏感字段裁剪。
- [ ] 运行 GREEN：执行同一三类过滤命令，预期退出码 `0`，指标部分失败、全部不可用和角色矩阵均通过。

## 任务 3：串行合流 SQLite migration 与索引

**文件：**

- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Migrations/008_EvidenceFoundation.sql`
- Create: `backend/tests/LSTY.SevenDPanel.Tests/EvidenceFoundationMigrationTests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj`（仅当现有 migration 测试资源需显式包含新 SQL）

- [x] 先写临时 SQLite 测试，从空库运行全部 DbUp migration，断言四个新表、必要索引和 `unified_audit_projection` 精确列；再从已有 007 schema 升级并验证专用表数据不变。
- [ ] 运行 RED：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~EvidenceFoundationMigrationTests
  ```

  预期：缺少 008 migration、表、view 或索引而失败。
- [x] 按本计划固定 schema 创建 `game_events`、`game_event_gaps`、`chat_mute`、`chat_mute_operation`；所有 UTC 存为 Unix milliseconds，布尔值受 `0/1` CHECK 约束。
- [x] 创建 `game_events(occurred_utc DESC,event_id DESC)`、稳定身份/类型筛选索引，`game_event_gaps(started_utc DESC,gap_id DESC)`，`chat_mute(updated_utc DESC,crossplatform_id DESC)`、`chat_mute(muted_until_utc)` 和 `chat_mute_operation(occurred_utc DESC,operation_id DESC)`。
- [x] 使用 `DROP VIEW IF EXISTS unified_audit_projection` 后 `CREATE VIEW`，将五个专用来源映射到相同类型；`has_details` 只对已有专用详情路由的来源置 `1`。不得迁移或复制专用正文。
- [x] 增加敏感列扫描测试，保证 view SQL 和查询结果不出现 chat message、console arguments/output、密码、Token、API Key、路径或异常正文。
- [x] 运行 GREEN：执行同一 migration 过滤命令，预期退出码 `0`，空库、007 升级、view 投影和敏感字段断言全部通过。

## 任务 4：交付四类游戏事件、gap 与生命周期

**文件：**

- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqliteGameEventStore.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Inbound/GameEvents/SevenDaysGameEventAdapter.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/GameEvents/GameEventWriteService.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/GameEvents/SevenDaysGameEventRuntime.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/GameEventCursorCodec.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/GameEventHttpModels.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/GameEventsController.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/SqliteGameEventStoreTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/SevenDaysGameEventRuntimeTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/GameEventsHttpTests.cs`

- [x] 先写 Store 测试，固定 `(occurredUtc,eventId)` 倒序 keyset、时间/类型/稳定身份筛选、相同时间不重不漏、事件与 gap 分离、空稳定身份仍可保存。
- [x] 先写 runtime 测试，固定 `PlayerJoinedGame`、`PlayerDisconnected`、`EntityKilled` 到四种事件的映射；一次 `EntityKilled` 在适用时分别产生 `PlayerKilledEntity` 和 `PlayerDied`，但不因相同名称或 entity ID 合并身份。
- [x] 先写 HTTP 测试，固定 Owner `200`、Admin/Viewer `403`、未认证 `401`，覆盖事件筛选、稳定 cursor、独立 gap 元数据和非法筛选 Problem Details。
- [ ] 运行 RED：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~SqliteGameEventStoreTests|FullyQualifiedName~SevenDaysGameEventRuntimeTests|FullyQualifiedName~GameEventsHttpTests"
  ```

  预期：Store、adapter 和 runtime 尚不存在而失败。
- [x] `SevenDaysGameEventAdapter` 只从 `SPlayerJoinedGameData.ClientInfo`、`SPlayerDisconnectedData.ClientInfo/GameShuttingDown`、`SEntityKilledData.KilledEntitiy/KillingEntity` 复制 `CrossplatformId`、`PlatformId`、entity ID、显示名和固定详情；不持有 `ClientInfo` 或 `Entity` 引用。
- [x] `GameEventWriteService` 使用容量 `256`、单消费者、有界 channel；回调仅调用 `TryWrite`。队满聚合 `QueueFull`，事件写失败聚合 `StoreFailure`，停止期限 `5s` 未排空记录 `DrainTimeout`；恢复写入后先持久化待提交 gap，再继续新事件。
- [x] `SevenDaysGameEventRuntime.Start()` 必须先启动 writer 再注册三个静态事件；`Stop()` 先注销生产者，再完成 channel、在 `5s` 内排空，最后停止内层 runtime。重复启停和启动失败清理均保持幂等。
- [x] `GameEventsController` 使用显式 DTO 和 `GameEventCursorCodec`，operationId 为 `listGameEvents`；只允许 Owner，查询失败返回 `gameEventsUnavailable`，非法 cursor 返回 `invalidGameEventCursor`。
- [x] 运行 GREEN：执行同一三类过滤命令，预期退出码 `0`，包括回调非阻塞、queue full、SQLite 故障恢复、HTTP 权限、关服排空和顺序断言。

## 任务 5：交付统一审计只读投影与独立查询

**文件：**

- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqliteUnifiedAuditQuery.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/AuditCursorCodec.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/AuditHttpModels.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/AuditController.cs`
- Create: `backend/tests/LSTY.SevenDPanel.Tests/SqliteUnifiedAuditQueryTests.cs`
- Create: `backend/tests/LSTY.SevenDPanel.Tests/AuditHttpTests.cs`

- [x] 先写 SQLite 测试，为玩家动作、控制台命令、服务器操作、聊天操作和禁言操作各插入一条专用记录，断言稳定摘要映射、来源筛选、组合筛选、同时间三元 cursor 和专用表零变更。
- [x] 先写 HTTP 测试，固定 Owner `200`、Admin/Viewer `403`、未认证 `401`，并覆盖非法时间、来源、状态、limit 和 cursor 的稳定 Problem Details。
- [ ] 运行 RED：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~SqliteUnifiedAuditQueryTests|FullyQualifiedName~AuditHttpTests"
  ```

  预期：只读查询和 `/api/v1/audit` 尚不存在而失败。
- [x] `SqliteUnifiedAuditQuery` 只查询 `unified_audit_projection`，使用参数化 SQL 和有界 `limit`；不得向 view 或专用表写入、更新、删除。
- [x] 只把具有专用审计 gap Store 的来源映射为 `sourceGaps` 元数据；当前 console command gap 可以返回，`chat_history_gap` 不得冒充 `chat_operation_audit` gap，没有专用 gap Store 的来源返回空集合。查询失败返回 `auditUnavailable`，不修改任何来源。
- [x] `AuditCursorCodec` 只编码/验证 `occurredUtc + sourceKind + sourceId`；变更筛选后旧 cursor 由调用方清除，非法 cursor 返回 `invalidAuditCursor`。
- [x] `AuditController` 的 operationId 固定为 `listAuditEntries`，返回显式 DTO；`hasDetails` 只提供专用详情定位信息，不回填正文。
- [x] 运行 GREEN：执行同一两类过滤命令，预期退出码 `0`，敏感字段排除、gap 分离和权限矩阵全部通过。

## 任务 6：交付禁言状态机、`help` 命令与聊天广播边界

**文件：**

- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqliteChatMuteStore.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Inbound/Chat/SevenDaysGameChatCommandReplySender.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/Chat/ChatMuteExpiryService.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/Chat/ChatRuntimeState.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/Chat/SevenDaysChatRuntime.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Inbound/Chat/SevenDaysChatMessageCoordinator.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ChatMuteHttpModels.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ChatController.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/SqliteChatMuteStoreTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/ChatMuteAndCommandTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/SevenDaysChatRuntimeTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/ChatHttpTests.cs`

- [x] 先写 Store/use-case 测试，固定永久、未来截止、恰好到期、创建、更新、解除、并发更新和每批最多 `100` 条到期清理；每次成功变更追加专用 operation，失败不替换快照。
- [x] 先写协调器测试：只拦截具稳定跨平台身份、来源为 Player、频道为 Global 且当前有效禁言的公开广播；其他玩家、系统、管理员、服务端发送和非 Global 消息继续原流程。被阻止正文可按现有聊天历史设置留在聊天证据中，但禁言操作记录只保存长度和目标，不保存正文。
- [x] 先写命令测试：配置前缀识别 `/help`，目录仅列出实际启用的 `help`，带参数返回 `chat.command.invalid_arguments`，结果只发给调用者并停止 vanilla；未知 `/name` 继续 vanilla、绕开彩色重写且不触发任何控制台执行。
- [ ] 运行 RED：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~SqliteChatMuteStoreTests|FullyQualifiedName~ChatMuteAndCommandTests|FullyQualifiedName~SevenDaysChatRuntimeTests|FullyQualifiedName~ChatHttpTests"
  ```

  预期：禁言 Store、命令目录、HTTP 合同和新广播断言失败。
- [x] `ChatRuntimeState.Load()` 同时载入设置、Profile 和当前 mute；使用一个不可变 snapshot 替换。所有 mute mutation 与到期清理通过同一串行门保证“SQLite 成功后替换”，清理后从 Store 重建 mute 子快照，避免并发更新被旧清理结果覆盖。
- [x] `ChatMuteExpiryService` 每 `60s` 执行一次有界清理；自动到期的 `actor_subject` 为 `null`、operation 为 `Expire`，失败只记录稳定告警并保留旧快照，下轮重试。
- [x] 协调器在彩色重写决定前读取一次 snapshot；有效 mute 返回 `StopHandlersAndVanilla`。已识别 `help` 同步生成类型化结果，由当前游戏线程上的 reply sender 私发；不阻塞 SQLite、不调用 `Task.Run`。
- [x] 在 `ChatController` 增加四个固定 mute 路由，operationId 分别为 `listChatMutes`、`createChatMute`、`updateChatMute`、`releaseChatMute`；全部服务端强制 Owner，写入失败映射稳定错误码且不回显异常。
- [x] 运行 GREEN：执行同一四类过滤命令，预期退出码 `0`，广播边界、快照原子性、到期审计、help 和未知命令放行全部通过。

## 任务 7：串行合流组合根、事件路由、OpenAPI 与生成客户端

**文件：**

- Modify: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Outbound/Hosting/OwinWebHost.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OpenApi/PanelOpenApiOperationProcessor.cs`（仅增加新 operation 的既有 Bearer/角色元数据规则）
- Test: `backend/tests/LSTY.SevenDPanel.Tests/DependencyInjectionTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostOpenApiSnapshotTests.cs`
- Modify by snapshot command: `frontend/apps/admin/openapi/7dpanel.v1.json`
- Generate only: `frontend/apps/admin/src/shared/api/generated/`

- [x] 先扩展 DI/OWIN/OpenAPI 测试，断言 DbUp 在任何事件订阅和 HTTP 接收前完成；writer 在订阅前启动；新 Store/use case/controller 都来自唯一组合根；三组端点、operationId、Owner security 和 Header Bearer 正确。
- [ ] 运行 RED：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~DependencyInjectionTests|FullyQualifiedName~OwinWebHostTests|FullyQualifiedName~OwinWebHostOpenApiSnapshotTests"
  ```

  预期：组合根缺少注册、路由缺少映射且 OpenAPI snapshot 漂移。
- [x] 在 `PanelServiceProviderFactory` 注册具体 Persistence Adapter、Application use case、指标查询、事件 writer/runtime、mute expiry 和 `help` 目录；不得注册第二容器、程序集扫描 handler registry 或通用事件分发器。
- [x] 合成 runtime 顺序为 migration 完成后启动游戏事件 writer/聊天 writer，随后订阅生产者，再启动其余 runtime；停止顺序相反并有界排空。
- [x] 在 OWIN 映射 `/api/v1/audit`、`/api/v1/game-events`、`/api/v1/chat/mutes`，保证静态路由优先级不会被现有 chat/history 路由吞掉。
- [x] 在 `frontend/apps/admin` 运行一次受控 snapshot 与生成：

  ```powershell
  pnpm api:schema
  pnpm api:gen
  dotnet test ../../../backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~OwinWebHostTests.Openapi_document_matches_admin_codegen_snapshot
  ```

  预期：三个命令均退出 `0`；snapshot 包含唯一 operationId 和 Owner security，生成目录由 Hey API 覆盖生成且没有手工编辑。
- [x] 再执行本任务三类完整过滤命令，预期退出码 `0`，确认 snapshot 刷新后无漂移。

## 任务 8：交付 Owner 审计工作台、游戏事件标签与禁言管理页

**文件：**

- Create: `frontend/apps/admin/src/features/audit/api/audit.ts`
- Create: `frontend/apps/admin/src/features/audit/api/audit.test.ts`
- Create: `frontend/apps/admin/src/features/audit/model/audit.ts`
- Create: `frontend/apps/admin/src/features/audit/model/useAuditWorkspace.ts`
- Create: `frontend/apps/admin/src/features/audit/model/useAuditWorkspace.test.ts`
- Create: `frontend/apps/admin/src/features/audit/ui/AuditWorkspace.vue`
- Create: `frontend/apps/admin/src/features/audit/ui/AuditWorkspace.test.ts`
- Create: `frontend/apps/admin/src/features/game-events/api/gameEvents.ts`
- Create: `frontend/apps/admin/src/features/game-events/api/gameEvents.test.ts`
- Create: `frontend/apps/admin/src/features/game-events/model/useGameEvents.ts`
- Create: `frontend/apps/admin/src/features/game-events/model/useGameEvents.test.ts`
- Create: `frontend/apps/admin/src/features/game-events/ui/GameEventsTable.vue`
- Create: `frontend/apps/admin/src/features/game-chat/api/chatMutes.ts`
- Create: `frontend/apps/admin/src/features/game-chat/model/useChatMutes.ts`
- Create: `frontend/apps/admin/src/features/game-chat/model/useChatMutes.test.ts`
- Create: `frontend/apps/admin/src/features/game-chat/ui/ChatMutesView.vue`
- Create: `frontend/apps/admin/src/pages/audit.vue`
- Create: `frontend/apps/admin/src/pages/game-chat/mutes.vue`
- Modify: `frontend/apps/admin/src/app/AppShell.vue`
- Modify: `frontend/apps/admin/src/app/AppShell.test.ts`
- Modify: `frontend/apps/admin/src/app/router.test.ts`

- [x] 先写严格 parser 测试：拒绝缺字段、错误 enum、非 UTC 时间和非法 cursor；API adapter 只调用本任务对应的生成 operation，不自行拼写 Bearer 或越过 generated client。
- [x] 先写 composable/component 测试：审计与游戏事件为独立 tab、独立 endpoint 和独立 cursor；筛选变更清 cursor；失败保留最后成功页并显示 stale；gap 独立提示；mute mutation 锁定、防重复提交和成功刷新。
- [x] 先写路由测试：`/audit` 与 `/game-chat/mutes` 仅 Owner；Admin/Viewer 即使直接输入 URL 也被角色守卫拒绝；服务端 403 不因导航隐藏被解释为 404。
- [ ] 运行 RED：

  ```powershell
  pnpm exec vitest run src/features/audit src/features/game-events src/features/game-chat/model/useChatMutes.test.ts src/app/AppShell.test.ts src/app/router.test.ts
  ```

  工作目录：`frontend/apps/admin`。预期：新 Feature、页面与导航尚不存在而失败。
- [x] 使用生成 DTO 后立即做运行期 parser；`useAuditWorkspace` 和 `useGameEvents` 各自维护 `loading/ready/stale/failed/forbidden`，不以一个 tab 失败清空另一个 tab。
- [x] `AuditWorkspace.vue` 使用 Nuxt UI v4 的 `UTabs`、`UTable`、`UBadge`、`UPagination` 和明确 loading/empty/error 状态；专用详情仅在 `hasDetails` 为真时显示跳转入口。
- [x] `ChatMutesView.vue` 使用固定创建/编辑表单和解除确认；永久禁言以 `mutedUntilUtc = null` 表示，技术身份不翻译，过期状态依据服务器 UTC 字段而非浏览器猜测。
- [x] 运行 GREEN：执行同一 Vitest 命令，预期退出码 `0`，桌面与窄屏断言、Owner 路由、stale 保留、gap 分离和 mutation 锁均通过。

## 任务 9：交付概览指标展示与聊天四页完整双语

**文件：**

- Modify: `frontend/apps/admin/src/features/server-status/model/overview.ts`
- Modify: `frontend/apps/admin/src/features/server-status/api/overview.ts`
- Modify: `frontend/apps/admin/src/features/server-status/api/overview.test.ts`
- Modify: `frontend/apps/admin/src/features/server-status/model/useOverview.test.ts`
- Modify: `frontend/apps/admin/src/features/server-status/ui/formatOverview.ts`
- Modify: `frontend/apps/admin/src/features/server-status/ui/ServerInformationPanel.vue`
- Modify: `frontend/apps/admin/src/features/server-status/ui/OverviewStatusSummary.vue`
- Create: `frontend/apps/admin/src/features/server-status/ui/GameRuntimeMetricsPanel.test.ts`
- Modify: `frontend/apps/admin/src/features/game-chat/ui/LiveChatView.vue`
- Modify: `frontend/apps/admin/src/features/game-chat/ui/ChatComposer.vue`
- Modify: `frontend/apps/admin/src/features/game-chat/ui/ChatOnlinePlayers.vue`
- Modify: `frontend/apps/admin/src/features/game-chat/ui/ChatMessageViewport.vue`
- Modify: `frontend/apps/admin/src/features/game-chat/ui/ChatHistoryView.vue`
- Modify: `frontend/apps/admin/src/features/game-chat/ui/ChatSettingsView.vue`
- Modify: `frontend/apps/admin/src/features/game-chat/ui/ColoredChatView.vue`
- Modify: `frontend/apps/admin/src/features/game-chat/ui/ColoredChatProfileDialog.vue`
- Modify: `frontend/apps/admin/src/features/game-chat/ui/ColoredChatPreview.vue`
- Modify: `frontend/apps/admin/src/features/game-chat/model/gameChatManagement.ts`
- Modify: `frontend/apps/admin/src/features/game-chat/model/useChatSettings.ts`
- Modify: `frontend/apps/admin/src/features/game-chat/model/useColoredChat.ts`
- Modify: `frontend/apps/admin/src/features/game-chat/ui/LiveChatView.test.ts`
- Modify: `frontend/apps/admin/src/features/game-chat/ui/GameChatManagementViews.test.ts`
- Modify: `frontend/apps/admin/src/app/i18n/locales/zh-CN.json`
- Modify: `frontend/apps/admin/src/app/i18n/locales/en.json`
- Modify: `frontend/apps/admin/src/app/i18n/messages.test.ts`

- [x] 先扩展 overview parser/model 测试，固定十一项类型、来源、单位、warning、同次时间；页面测试区分 `0`、`null`、unavailable、stale 和部分可用，窄屏不横向溢出。
- [x] 先扩展聊天测试，把页面标题、按钮、表头、状态、验证、确认、反馈和稳定错误码全部改为 locale key 断言；`zh-CN` 与 `en` 必须同键。
- [ ] 运行 RED：

  ```powershell
  pnpm exec vitest run src/features/server-status src/features/game-chat src/app/i18n/messages.test.ts
  ```

  工作目录：`frontend/apps/admin`。预期：新指标 parser/组件和缺失双语键导致断言失败。
- [x] 通过 `computed` 或模板中的 `t()` 生成频道、来源、颜色权限和操作标签，使切换 locale 后响应式更新；不得把已翻译文本存进领域 model。
- [x] 指标面板显示 value、单位、采样时间和 warning；`null` 显示本地化“未知/不支持/读取失败”，真实 `0` 保留数字 `0`。概览请求失败继续显示最后成功 snapshot 并标记 stale。
- [x] 移除九个聊天组件和三个 model 文件中的用户可见硬编码；技术跨平台 ID、entity ID、模板变量、颜色值、协议名和原始游戏消息保持原样。
- [x] 为审计、游戏事件、禁言、运行指标和聊天补齐两个 locale 文件；`messages.test.ts` 递归比较完整 key 集并检查关键英文页面不回退中文。
- [x] 运行 GREEN：执行同一 Vitest 命令，预期退出码 `0`，overview 状态、聊天四页、审计/禁言新增键和双语一致性全部通过。

## 任务 10：一次聚合门禁、一次真实 `v3.0.1-b4` 窄验收与文档提升

**文件：**

- Modify: `docs/architecture.md`
- Modify: `docs/design.md`
- Modify: `docs/test.md`
- Verify only: `docs/architecture/legacy-feature-parity-target-blueprint.md`
- Verify only: `README.md`
- Verify only: `backend/README.md`
- Verify only: `frontend/apps/admin/README.md`

- [ ] 所有聚焦 GREEN 稳定后，从仓库根只运行一次后端聚合门禁：

  ```powershell
  dotnet restore backend/7DPanel.sln
  dotnet build backend/7DPanel.sln --configuration Release --no-restore
  dotnet test backend/7DPanel.sln --configuration Release --no-build --no-restore
  ```

  预期：三个命令退出 `0`，Release 构建和全部后端测试一次通过；失败时修复根因并只重跑受影响聚焦测试，最终聚合复验需记录为修复后的唯一最终门禁结果。
- [ ] 从 `frontend/apps/admin` 只运行一次 Admin 受影响聚合门禁：

  ```powershell
  pnpm lint
  pnpm typecheck
  pnpm test
  pnpm build
  ```

  预期：四个命令退出 `0`；生成 DTO、Vue 类型、完整 Vitest、ESLint 和 Vite 生产构建均通过。`pnpm api:check` 因包含 Git 基线比较不在本计划自动执行。
- [ ] 在受控 Windows `v3.0.1-b4` 环境仅执行一次真实窄路径，不运行 publish 或浏览器：

  ```powershell
  backend\scripts\Start-Server.cmd
  backend\scripts\Test-HealthEndpoint.cmd
  backend\scripts\Stop-Server.cmd
  ```

  启动与停止命令之间，用一个合成玩家完成加入、离开、击杀、死亡；以 Owner 创建永久禁言并验证 Global 广播被阻止、解除后恢复；执行当前配置前缀加 `help` 并确认结果只私发。保存概览 JSON、四类事件及 gap 元数据、禁言 operation、help 结果码、服务端日志和正常关服排空证据。预期健康检查成功、十一项指标诚实显示值或稳定 warning、四类事件可独立检索、gap 不伪装为事件、禁言和 help 行为符合规格。
- [ ] 不执行真实容量饱和、Playwright、浏览器人工 smoke、publish、Linux、恢复演练或全套发布物检查；这些边界没有被本波规格授权。
- [x] 仅依据实际通过的自动化与真实证据更新 `docs/architecture.md`：提升已实现组件、数据流、生命周期、依赖矩阵和 migration 008；未验证真实事实继续写为缺口。
- [x] 更新 `docs/design.md`：记录概览指标的 unknown/stale/partial 表达、Owner 审计双标签、禁言交互、桌面/窄屏状态和双语规则；不复制 API schema。
- [ ] 更新 `docs/test.md`：记录聚焦测试、后端/Admin 各一次最终聚合结果和一次真实窄路径证据；精确写明未执行的 Playwright、publish、Linux 和发布 smoke。
- [ ] 只核对目标蓝图条目与 Current 文档链接是否仍准确；实现没有改变批准的未来目标，因此不因“已实现”重写目标蓝图。README 中现有聚合命令未变化，不重复写入或修改。
- [ ] 最后按规格各节逐项核对：用户结果、当前边界、运行指标、游戏事件、统一审计、禁言、命令与双语、权限接口、生命周期失败恢复、非目标、精简验证和完成条件均能映射到任务 1–10；所有类型、enum、result code、operationId、route 和 schema 名称一致。

## 完成与 Git 边界

完成实现时必须同时满足：十一项指标具有来源、单位、可空值和采样时间；四类事件与三类 gap 可诚实检索；统一审计保持只读且不泄漏敏感正文；禁言状态和专用 operation 原子一致；命令目录只有真实 `help` 消费者且未知命令原版放行；聊天四页与新增页面 `zh-CN`/`en` 同键；一次后端聚合、一次 Admin 聚合和一次真实 `v3.0.1-b4` 窄路径具有成功证据。

本计划不授权自动执行 `git commit`、`git push`、`git reset`、`git revert`、创建分支或 Pull Request。任何 Git 提交、推送或集成操作必须由用户另行显式授权。
