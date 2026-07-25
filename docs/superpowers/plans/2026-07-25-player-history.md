---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-25-player-history-design.md
last_updated: "2026-07-25"
---

# 历史玩家快照实施计划

> **面向智能体执行者：** 实施时必须使用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans`，逐任务执行并在每个任务后评审。以下步骤使用复选框跟踪。

**对应规格：** [历史玩家快照设计规格](../specs/2026-07-25-player-history-design.md)

**目标：** 以 EOS `crossplatformIdentity.combinedId` 为规范身份，将扩充后的 31 字段玩家 observation 通过有界单消费者 Channel 非阻塞写入 SQLite，并为 Owner 提供摘要列表、历史时间线和只读快照详情。

**架构：** 现有 `SevenDaysOnlinePlayerProjection.CopyObservation` 在一次 `SavePlayerData` 回调中复制 31 字段产品快照，在线投影先更新，历史写入服务随后只执行 `TryWrite`。Application 拥有不可变历史模型、查询输入和 Store 端口；SQLite Adapter 以摘要、快照和 gap 三表实现事务写入、keyset 查询和确定性降采样；Web Adapter 显式映射 Owner-only DTO；Admin Players Feature 使用页面局部状态展示摘要和分页快照。

**技术栈：** .NET Framework `4.8`、C# `11.0`、`System.Threading.Channels 10.0.10`、Dapper `2.1.79`、DbUp、Microsoft.Data.Sqlite `10.0.10`、ASP.NET Web API 2、Katana、xUnit v3、7DTD Dedicated Server `v3.0.1-b4`、Vue `3.5.40`、TypeScript `6.0.3`、Nuxt UI `4.10.0`、Vite `8.1.5`、Vitest `4.1.6`、Vue Test Utils、Playwright、pnpm `11.13.1`。

## 全局约束

- 当前目录已经是隔离 worktree；执行前只确认分支和用户改动，不创建嵌套 worktree，不覆盖现有未跟踪 spec/plan。
- 本计划只实现一个纵向切片；不增加通用 Event Bus、Repository 框架、后台作业框架、新项目、NuGet/npm 依赖或全局前端 Store。
- 每个有效 EOS Save 都尝试保存；缺少 EOS ID 时只更新在线投影并增加 skip 计数，不按名称、IP、`entityId` 或原生身份降级归并。
- 共享 `PlayerSnapshot` 从 25 字段扩充为 31 字段：新增可空 `PlayGroup`、`LastLoginUtc`、`GameStage`、`ExpToNextLevel`、`SkillPoints` 和 `Bedroll`。
- `LastLoginUtc` 是游戏持久玩家记录中的最近登录时间，不等于 `ObservedAtUtc`，不得用于推断连续在线。
- `GameStage`、`ExpToNextLevel`、`SkillPoints` 无可靠来源时为 `null`；不得继承旧实现的 `0` 占位。真实的零值必须与 `null` 区分。
- Progression 优先读取同一 `entityId` 的 `EntityPlayer.Progression`；仅在实体或 Progression 不可用时解析目标版本 `PlayerDataFile.progressionData`。解析器必须验证版本和长度、恢复 stream position、失败返回 `null`，不得从磁盘调用 `PlayerDataFile.Load`。
- `BedrollPos.y == int.MaxValue` 映射为 `Bedroll = null`；有效床铺一次复制三个坐标。`ACL`、领地、背包、任务位置、售货机、库存、配方、技能明细和玩家档案不进入本切片。
- 入队项只含不可变产品值，不保存 `ClientInfo`、`PlayerDataFile`、`PersistentPlayerData`、`EntityPlayer`、Unity 对象、stream、连接或 delegate。
- Channel 容量固定 `1024`、`SingleReader=true`；生产者只用 `TryWrite`，full 时不能覆盖旧项或阻塞游戏线程。后台只有一个 SQLite writer，降采样也由它在队列空闲时执行。
- 历史列表保持摘要合同，不返回最新完整快照。`isOnline` 不持久化；首版历史页面不因无法查询在线状态而把玩家标成离线。
- 所有历史 API 都是 Owner-only、只读 SQLite，并在游戏未 ready 时仍可查询。历史页面没有踢出、封禁、传送、重置档案或删除历史入口。
- 每项生产行为先写行为 RED 测试，再写最小实现。编译错误不算 RED；必要时先加抛 `NotImplementedException` 的可编译签名。
- 本计划不授权 `git commit`、`git push`、`git reset`、`git revert`、发布、真实 7DTD、浏览器 smoke 或 Playwright；这些操作需用户另行授权。

---

### 任务 1：同步权威产品、设计、目标架构和测试合同

**文件：**

- 修改：`docs/PRD.md`
- 修改：`docs/design.md`
- 修改：`docs/architecture/backend-target-blueprint.md`
- 修改：`docs/architecture/admin-frontend-target-blueprint.md`
- 修改：`docs/test.md`

- [ ] **步骤 1：在 PRD 扩充 `CAP-02`**

  在 `CAP-02` 下增加历史玩家可观察合同：仅有效 EOS observation 入历史、每次 Save 尽力记录、摘要列表和只读时间线、固定 UTC 分级保留、计划内降采样与非计划 gap 区分。保留 `NFR-01`、`NFR-02`、`NFR-05` 链接，不写 Channel、表名或 Vue 组件。

- [ ] **步骤 2：在产品设计加入历史玩家交互**

  记录 `/players/history`、`/players/history/:crossplatformId`、摘要列表、加载更多、只读详情 Slideover、loading/ready/empty/forbidden/failed/stale、降采样/gap/未知值文案，以及历史页无危险操作。

- [ ] **步骤 3：同步两个 Target 蓝图**

  后端蓝图加入 `CopyObservation -> PlayerHistoryWriteService -> SQLite`、三张表、三个 Use Case、三个 Owner-only route 和启停顺序。Admin 蓝图加入 Players Feature 的 history API、页面局部状态、两个路由、时间线和共享纯展示快照详情边界；明确仍是 Target，不是实现证据。

- [ ] **步骤 4：同步测试策略**

  增加 Application 不变量、游戏成员提取兼容、Channel fail-open、SQLite 事务/keyset/降采样、Owner-only OWIN、Admin parser/状态/响应式布局和受控真实进程门禁。保留完整命令在模块 README，只在 `docs/test.md` 写层级与放行标准。

- [ ] **步骤 5：做轻量文档检查**

  ```powershell
  rg -n "CAP-02|player_history|players/history|历史缺口|降采样" docs/PRD.md docs/design.md docs/architecture docs/test.md
  git diff --check -- docs
  git status --short
  ```

  预期：五份权威文档和已批准 spec/plan 在范围内；没有空白错误、未完成占位、当前实现误报或机器路径。

### 任务 2：把共享玩家快照扩充为 31 字段

**文件：**

- 修改：`backend/src/Core/LSTY.SevenDPanel.Application/Players/PlayerSnapshot.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/OnlinePlayerQueryTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs`
- 修改：`frontend/apps/admin/src/features/players/api/onlinePlayers.ts`
- 修改：`frontend/apps/admin/src/features/players/api/onlinePlayers.test.ts`

- [ ] **步骤 1：写 31 字段 Application RED 测试**

  扩充测试工厂并锁定真实零值与未知值：

  ```csharp
  Assert.Equal("Standalone", player.PlayGroup);
  Assert.Equal(lastLoginUtc, player.LastLoginUtc);
  Assert.Equal(143, player.GameStage);
  Assert.Equal(1200, player.ExpToNextLevel);
  Assert.Equal(4, player.SkillPoints);
  Assert.Equal(100f, player.Bedroll!.Value.X);
  Assert.Equal(70f, player.Bedroll.Value.Y);
  Assert.Equal(200f, player.Bedroll.Value.Z);
  ```

  另以 `null` 构造六字段并断言保留；非空白 `PlayGroup`、非负三个进度整数、UTC `LastLoginUtc` 和有限床铺坐标是唯一合法非空值。

- [ ] **步骤 2：运行定向测试并确认 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj `
    --configuration Release `
    --filter "FullyQualifiedName~OnlinePlayerQueryTests" `
    /p:SevenDaysReferenceRoot=((Resolve-Path '7dtd-reference').Path)
  ```

  预期：因六个属性或构造参数尚不存在而 RED；先补可编译签名后，正确 RED 是字段保留或校验断言失败。

- [ ] **步骤 3：实现 31 字段不可变合同**

  在现有 `observedAtUtc` 前加入六个参数，并增加只读属性：

  ```csharp
  string? playGroup,
  DateTimeOffset? lastLoginUtc,
  int? gameStage,
  int? expToNextLevel,
  int? skillPoints,
  PlayerPosition? bedroll,
  DateTimeOffset observedAtUtc
  ```

  `PlayGroup` 非空时拒绝空白；三个进度字段非空时拒绝负数；`LastLoginUtc` 非空时规范化为 UTC；`Bedroll` 复用 `PlayerPosition` 有限值不变量。构造函数只复制，不推导登录、在线或权限状态。

- [ ] **步骤 4：同步在线 Web/TypeScript 合同**

  `PlayersController.OnlinePlayerResponse` 显式增加相同六字段；前端 `OnlinePlayer` 和 parser 增加：

  ```ts
  readonly playGroup: string | null
  readonly lastLoginUtc: string | null
  readonly gameStage: number | null
  readonly expToNextLevel: number | null
  readonly skillPoints: number | null
  readonly bedroll: OnlinePlayerPosition | null
  ```

  nullable integer parser 必须区分 `null` 与 `0`，nullable UTC parser 复用严格 UTC 校验，nullable position 必须全对象或 `null`。

- [ ] **步骤 5：转 GREEN 并检查调用点**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~OnlinePlayerQueryTests|FullyQualifiedName~OwinWebHostTests" /p:SevenDaysReferenceRoot=((Resolve-Path '7dtd-reference').Path)
  pnpm --dir frontend/apps/admin test:unit -- src/features/players/api/onlinePlayers.test.ts
  rg -n "new PlayerSnapshot\(" backend
  ```

  预期：定向测试通过，所有构造调用都显式提供六字段，没有临时重载。

### 任务 3：在同一次 Save 回调提取附加游戏字段

**文件：**

- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/SevenDaysOnlinePlayerProjection.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/PlayerProgressionSnapshotReader.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/SevenDaysOnlinePlayerQueryTests.cs`

- [ ] **步骤 1：增加可测试的附加值复制边界**

  在 `SevenDaysOnlinePlayerProjection` 增加 internal 产品值结构，只承载六个可空值：

  ```csharp
  internal readonly struct AdditionalPlayerSnapshotValues
  {
      public string? PlayGroup { get; }
      public DateTimeOffset? LastLoginUtc { get; }
      public int? GameStage { get; }
      public int? ExpToNextLevel { get; }
      public int? SkillPoints { get; }
      public PlayerPosition? Bedroll { get; }
  }
  ```

  internal 测试构造允许注入 `Func<ClientInfo, PlayerDataFile, PlayerPlatformIdentity?, AdditionalPlayerSnapshotValues>`；生产构造绑定真实读取函数。该 seam 只隔离现有游戏边界，不公开 Provider 接口。

- [ ] **步骤 2：写来源优先级和 fail-open RED 测试**

  覆盖：Progression 在线值优先、实体/Progression 缺失才 fallback、床铺哨兵变 `null`、有效床铺三轴、持久玩家缺失只使 PPD 三字段为 `null`、附加读取抛异常时基础 25 字段仍成功提交、旧 observation 不被部分对象污染。

- [ ] **步骤 3：写 progression parser RED 测试**

  `PlayerProgressionSnapshotReader` 接收 stream 和已批准版本字节，返回：

  ```csharp
  internal readonly struct PlayerProgressionValues
  {
      public int Level { get; }
      public int ExpToNextLevel { get; }
      public int SkillPoints { get; }
  }
  ```

  用内存 stream 覆盖有效布局、未知版本、长度不足、异常读取和非零初始 position；所有路径结束后 position 必须恢复。未知/截断返回 `false`，不得抛到 Save handler。

- [ ] **步骤 4：实现目标版本提取**

  生产读取顺序固定为：

  ```text
  CrossplatformId -> PersistentPlayerData exact lookup
  entityId -> World.Players.dict exact lookup
  EntityPlayer.Progression -> exp/skill
  otherwise validated progressionData -> exp/skill
  PersistentPlayerData -> playGroup/lastLogin/bedroll
  copy product values -> discard game references
  ```

  `Level` 仍使用 `PlayerDataFile.metadata.Level`。`LastLogin` 只在目标程序集证据确认可无歧义转换 UTC 后写入，否则返回 `null`。附加读取各来源分组 catch：PPD 失败不清空 game stage，progression 失败不清空 play group；只写限频无身份诊断计数。

- [ ] **步骤 5：运行定向测试并做源码边界检查**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~SevenDaysOnlinePlayerQueryTests" /p:SevenDaysReferenceRoot=((Resolve-Path '7dtd-reference').Path)
  rg -n "PlayerDataFile\.Load|Task\.Run|ClientInfo|PersistentPlayerData|EntityPlayer" backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players
  ```

  预期：测试通过；没有 `PlayerDataFile.Load`，没有把游戏对象传到 observation、Channel 或后台任务。

### 任务 4：建立 Application 历史模型、Store 端口和查询 Use Case

**文件：**

- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Players/History/PlayerHistoryModels.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Players/History/IPlayerHistoryStore.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Players/History/GetHistoricalPlayersUseCase.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Players/History/GetHistoricalPlayerUseCase.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Players/History/GetPlayerHistorySnapshotsUseCase.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/PlayerHistoryUseCaseTests.cs`

- [ ] **步骤 1：写输入和不可变结果 RED 测试**

  锁定模型：

  ```csharp
  HistoricalPlayerSummary(
      string crossplatformId,
      string latestName,
      DateTimeOffset firstObservedAtUtc,
      DateTimeOffset lastObservedAtUtc,
      long totalObservationCount,
      long retainedSnapshotCount,
      long compactedSnapshotCount,
      bool hasGaps)

  HistoricalPlayerSnapshot(long snapshotId, PlayerSnapshot player)
  PlayerHistoryGap(string gapId, string crossplatformId, DateTimeOffset startedAtUtc,
      DateTimeOffset completedAtUtc, long droppedCount, PlayerHistoryGapReason reason,
      DateTimeOffset recordedAtUtc)
  ```

  `PlayerHistoryGapReason` 仅允许 `QueueFull`、`StoreFailure`、`ShutdownTimeout`。

  同一文件还定义以下不可变查询值，避免后续任务发明第二套分页合同：

  ```csharp
  HistoricalPlayersCursor(DateTimeOffset firstObservedAtUtc, string crossplatformId)
  HistoricalPlayersQuery(string? query, int pageSize, HistoricalPlayersCursor? cursor)
  HistoricalPlayersPage(IReadOnlyList<HistoricalPlayerSummary> players,
      HistoricalPlayersCursor? nextCursor)
  HistoricalPlayerGapSummary(long gapCount, long droppedObservationCount)
  HistoricalPlayerDetails(HistoricalPlayerSummary player,
      HistoricalPlayerGapSummary gapSummary)
  HistoricalPlayerSnapshotsQuery(string crossplatformId, int pageSize,
      long? beforeSnapshotId)
  HistoricalPlayerSnapshotsPage(IReadOnlyList<HistoricalPlayerSnapshot> snapshots,
      long? nextBeforeSnapshotId, IReadOnlyList<PlayerHistoryGap> gaps)
  ```

- [ ] **步骤 2：定义 Store 的最小生产接口**

  ```csharp
  public interface IPlayerHistoryStore
  {
      void Append(PlayerSnapshot snapshot);
      void AppendGap(PlayerHistoryGap gap);
      int Compact(DateTimeOffset utcNow, int maximumDeleteCount);
      HistoricalPlayersPage GetPlayers(HistoricalPlayersQuery query);
      HistoricalPlayerDetails? GetPlayer(string crossplatformId);
      HistoricalPlayerSnapshotsPage GetSnapshots(HistoricalPlayerSnapshotsQuery query);
  }
  ```

  `HistoricalPlayersQuery` 包含可空 query、`1..100` page size 和可空结构化 cursor；快照 query 包含 EOS ID、`1..200` page size、可空正数 `beforeSnapshotId`。Web 负责不透明 cursor 编解码，Application 不知道 Base64/URL。

- [ ] **步骤 3：实现模型和 Use Case 校验**

  三个 Use Case 只验证输入、调用 Store、冻结返回集合。EOS ID 非空白且最长 256；列表 query trim 后空值变 `null`；未知玩家保持 `null`，不以异常字符串表达 404。

- [ ] **步骤 4：运行 Application 定向测试**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~PlayerHistoryUseCaseTests" /p:SevenDaysReferenceRoot=((Resolve-Path '7dtd-reference').Path)
  ```

  预期：合法输入原样转发；无效 EOS、page size、cursor 和负计数被拒绝；Store 返回的可变源数组不能改变结果。

### 任务 5：创建 SQLite migration 和历史 Store

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Migrations/004_PlayerHistory.sql`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqlitePlayerHistoryStore.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/SqlitePlayerHistoryStoreTests.cs`

- [ ] **步骤 1：写 migration 和事务 RED 测试**

  使用临时 SQLite 数据库执行现有 bootstrapper，断言三张表、两个快照索引、外键和 CHECK 存在；重复 Upgrade 幂等。写入一条 31 字段快照后断言摘要和快照同时存在；在摘要更新前注入失败时两者都不存在。

- [ ] **步骤 2：创建三表 schema**

  `004_PlayerHistory.sql` 必须包含：

  ```sql
  CREATE TABLE player_history_players (
      crossplatform_id TEXT NOT NULL PRIMARY KEY,
      latest_name TEXT NOT NULL,
      first_observed_utc INTEGER NOT NULL,
      last_observed_utc INTEGER NOT NULL,
      latest_snapshot_id INTEGER NOT NULL,
      total_observation_count INTEGER NOT NULL CHECK (total_observation_count > 0),
      retained_snapshot_count INTEGER NOT NULL CHECK (retained_snapshot_count > 0),
      compacted_snapshot_count INTEGER NOT NULL CHECK (compacted_snapshot_count >= 0),
      CHECK (total_observation_count = retained_snapshot_count + compacted_snapshot_count)
  );

  CREATE TABLE player_history_snapshots (
      snapshot_id INTEGER PRIMARY KEY AUTOINCREMENT,
      crossplatform_id TEXT NOT NULL,
      observed_utc INTEGER NOT NULL,
      entity_id INTEGER NOT NULL CHECK (entity_id >= 0),
      name TEXT NOT NULL,
      platform_combined_id TEXT NOT NULL,
      platform_name TEXT NOT NULL,
      crossplatform_combined_id TEXT NOT NULL,
      crossplatform_name TEXT NOT NULL,
      device_type TEXT NOT NULL,
      ip TEXT NULL,
      ping INTEGER NOT NULL,
      compatibility_version TEXT NULL,
      discord_user_id TEXT NULL,
      permission_level INTEGER NOT NULL,
      position_x REAL NOT NULL,
      position_y REAL NOT NULL,
      position_z REAL NOT NULL,
      is_dead INTEGER NOT NULL CHECK (is_dead IN (0, 1)),
      health INTEGER NOT NULL,
      max_health INTEGER NOT NULL,
      level INTEGER NOT NULL,
      play_group TEXT NULL,
      last_login_utc INTEGER NULL,
      game_stage INTEGER NULL CHECK (game_stage IS NULL OR game_stage >= 0),
      exp_to_next_level INTEGER NULL CHECK (exp_to_next_level IS NULL OR exp_to_next_level >= 0),
      skill_points INTEGER NULL CHECK (skill_points IS NULL OR skill_points >= 0),
      bedroll_x REAL NULL,
      bedroll_y REAL NULL,
      bedroll_z REAL NULL,
      score INTEGER NOT NULL,
      zombie_kills INTEGER NOT NULL,
      player_kills INTEGER NOT NULL,
      deaths INTEGER NOT NULL,
      total_time_played_minutes REAL NOT NULL CHECK (total_time_played_minutes >= 0),
      distance_walked_meters REAL NOT NULL CHECK (distance_walked_meters >= 0),
      total_items_crafted INTEGER NOT NULL CHECK (total_items_crafted >= 0),
      longest_life_minutes REAL NOT NULL CHECK (longest_life_minutes >= 0),
      current_life_minutes REAL NOT NULL CHECK (current_life_minutes >= 0),
      FOREIGN KEY (crossplatform_id) REFERENCES player_history_players(crossplatform_id)
          DEFERRABLE INITIALLY DEFERRED,
      CHECK (crossplatform_combined_id = crossplatform_id),
      CHECK ((bedroll_x IS NULL AND bedroll_y IS NULL AND bedroll_z IS NULL) OR
             (bedroll_x IS NOT NULL AND bedroll_y IS NOT NULL AND bedroll_z IS NOT NULL))
  );

  CREATE TABLE player_history_gaps (
      gap_id TEXT NOT NULL PRIMARY KEY,
      crossplatform_id TEXT NOT NULL,
      started_utc INTEGER NOT NULL,
      completed_utc INTEGER NOT NULL CHECK (completed_utc >= started_utc),
      dropped_count INTEGER NOT NULL CHECK (dropped_count > 0),
      reason TEXT NOT NULL CHECK (reason IN ('queue_full', 'store_failure', 'shutdown_timeout')),
      recorded_utc INTEGER NOT NULL
  );

  CREATE INDEX ix_player_history_snapshots_player_id
      ON player_history_snapshots(crossplatform_id, snapshot_id DESC);
  CREATE INDEX ix_player_history_snapshots_player_time
      ON player_history_snapshots(crossplatform_id, observed_utc DESC, snapshot_id DESC);
  ```

  迁移还要为 `latest_snapshot_id` 增加延迟外键不可行时的事务级存在性测试；不要制造循环插入外键。

- [ ] **步骤 3：实现 Append、gap 和 DTO 映射**

  `Append` 要求非空 EOS ID，在一个 `BEGIN IMMEDIATE` 事务中：先识别首条/既有摘要，插入快照，再 insert/update 摘要并 Commit。快照到摘要的外键延迟到 Commit 检查，因此首条事务不会暴露中间状态。首条摘要计数为 `1/1/0`；后续更新名称、最后时间、latest ID、total 和 retained。`AppendGap` 使用 `INSERT OR IGNORE` 保证 gap ID 幂等；gap 不依赖摘要外键，因此第一条成功快照之前发生的丢失也能先落 gap，不伪造空玩家摘要。

- [ ] **步骤 4：实现列表和快照 keyset 查询**

  列表固定排序 `(first_observed_utc DESC, crossplatform_id ASC)`，cursor 条件为：

  ```sql
  WHERE first_observed_utc < @FirstObservedUtc
     OR (first_observed_utc = @FirstObservedUtc AND crossplatform_id > @CrossplatformId)
  ORDER BY first_observed_utc DESC, crossplatform_id ASC
  LIMIT @Take;
  ```

  query 只匹配摘要 `latest_name`/`crossplatform_id`，使用 `ESCAPE '\'` 并转义 `\`、`%`、`_`。快照使用 `snapshot_id < @BeforeSnapshotId ORDER BY snapshot_id DESC LIMIT @Take`，多取一条生成 next cursor；gaps 只返回与当前页 observation 时间范围相交项。

- [ ] **步骤 5：实现确定性分级降采样**

  `Compact(utcNow, 1000)` 在单个事务中按 spec 十个年龄层计算 epoch UTC bucket，排除每名玩家第一条和 `latest_snapshot_id`，每桶以最大 `snapshot_id` 为 winner，最多删除 1000 个非 winner。删除后按玩家更新 retained/compacted，保持计数等式；同一 `utcNow` 重复执行必须返回 `0`。

- [ ] **步骤 6：运行 SQLite 定向测试**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~SqlitePlayerHistoryStoreTests" /p:SevenDaysReferenceRoot=((Resolve-Path '7dtd-reference').Path)
  ```

  预期：31 字段/null/真实零 round-trip、同毫秒多条、名称和 entity 变化、LIKE 转义、keyset、gap、所有保留边界、首末固定、幂等和事务回滚通过。

### 任务 6：实现有界 Channel 单消费者和 gap 守恒

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/PlayerHistoryWriteService.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/PlayerHistoryWriteServiceTests.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/SevenDaysOnlinePlayerProjection.cs`

- [ ] **步骤 1：写同步生产者 RED 测试**

  用可注入小容量 Channel 和阻塞 Store 证明：`TryRecord` 成功、full、stopping 都同步返回；full 不覆盖旧项；缺 EOS 返回 skipped；Store 永不在生产者线程调用；字段相同的两条 Save 都 accepted。

- [ ] **步骤 2：写消费者/gap/停止 RED 测试**

  覆盖 accepted 顺序、Store 异常后 worker 存活、`queue_full`/`store_failure`/`shutdown_timeout` 分玩家聚合、恢复后先写 pending gap 再写后续快照、5 秒可注入停止排空、停止计数守恒和重复 Start/Stop。

- [ ] **步骤 3：实现固定 Channel 和生命周期**

  生产构造固定：

  ```csharp
  var options = new BoundedChannelOptions(1024)
  {
      SingleReader = true,
      SingleWriter = false,
      FullMode = BoundedChannelFullMode.Wait,
  };
  channel = Channel.CreateBounded<PlayerSnapshot>(options);
  ```

  只调用 `Writer.TryWrite`，绝不调用 `WriteAsync`。服务启动时创建一个长期 consumer Task；停止时先拒绝新项、Complete writer、在 timeout 内排空。日志只含固定消息、计数和原因，不含 EOS、IP、位置或快照正文。

- [ ] **步骤 4：实现 pending gap 和空闲 compaction**

  pending gap key 为 `{crossplatformId, reason}`，窗口保存 first/last observed time 和 dropped count。消费者写每条玩家快照前先写该玩家 pending gap；失败则放回。仅在 reader 暂无项或近似深度 `<= 256` 时调用 `store.Compact(clock(), 1000)` 一批，新项到达后立即恢复实时消费。

- [ ] **步骤 5：连接在线投影**

  `HandleSave` 成功构造 observation 后顺序固定：

  ```csharp
  UpsertForTest(observation);
  historyWriteService.TryRecord(observation.Player);
  ```

  在线更新成功不因 history full/stop/store failure 回滚。缺 EOS 的 `TryRecord` 只增加 skip。

- [ ] **步骤 6：运行 Channel 定向测试**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~PlayerHistoryWriteServiceTests|FullyQualifiedName~SevenDaysOnlinePlayerQueryTests" /p:SevenDaysReferenceRoot=((Resolve-Path '7dtd-reference').Path)
  ```

  预期：无测试挂起；accepted/persisted/dropped/pending/gap 计数守恒，基础在线投影测试保持通过。

### 任务 7：注册依赖并固定启动/停止顺序

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/PlayerHistoryRuntime.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyInjectionTests.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/PlayerHistoryRuntimeTests.cs`

- [ ] **步骤 1：写生命周期顺序 RED 测试**

  用记录 Action 断言启动：`history.Start -> inner.Start`；停止：`inner.Stop -> history.Stop`。其中 inner 是现有 `OnlinePlayerProjectionRuntime`，所以最终顺序为 DB migration、history consumer、online projection、Web；停止为 Web、online projection、history drain。

- [ ] **步骤 2：实现 runtime decorator**

  `PlayerHistoryRuntime : IModRuntime, IDisposable` 只负责顺序和异常聚合：history start 失败不启动 inner；inner start 失败回滚 history；Stop 幂等且两个 stop 都尝试执行。

- [ ] **步骤 3：注册最小依赖图**

  在 `PanelServiceProviderFactory` 注册：

  ```csharp
  services.AddSingleton<SqlitePlayerHistoryStore>();
  services.AddSingleton<IPlayerHistoryStore>(sp => sp.GetRequiredService<SqlitePlayerHistoryStore>());
  services.AddSingleton<PlayerHistoryWriteService>();
  services.AddSingleton<GetHistoricalPlayersUseCase>();
  services.AddSingleton<GetHistoricalPlayerUseCase>();
  services.AddSingleton<GetPlayerHistorySnapshotsUseCase>();
  services.AddSingleton<PlayerHistoryRuntime>();
  ```

  生产 `SevenDaysOnlinePlayerProjection` 构造注入 history service；最终 `IModRuntime` 指向 `PlayerHistoryRuntime`。不得注册第二个 Store 或 consumer 实例。

- [ ] **步骤 4：运行 DI/lifecycle 测试**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~DependencyInjectionTests|FullyQualifiedName~PlayerHistoryRuntimeTests|FullyQualifiedName~ModHostTests" /p:SevenDaysReferenceRoot=((Resolve-Path '7dtd-reference').Path)
  ```

  预期：ValidateOnBuild 成功；三 Use Case、单 Store、单 write service 可解析；重复 stop 无重复排空。

### 任务 8：增加 Owner-only 历史 Web API 和 OpenAPI

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/PlayerHistoryHttpModels.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/PlayerHistoryCursorCodec.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/PlayersController.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OpenApi/PanelOpenApiOperationProcessor.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs`

- [ ] **步骤 1：写三个路由和授权 RED 测试**

  覆盖 Owner 200、Admin/Viewer 403、匿名 401、API Key/Bearer；游戏 not ready 时仍查询 SQLite。覆盖未知 EOS 404、无效 EOS/query/page size/cursor/before ID 400 和数据库异常脱敏 500。

- [ ] **步骤 2：实现版本化列表 cursor**

  `PlayerHistoryCursorCodec` 编码 `{ version: 1, firstObservedUtcMs, crossplatformId }` 为 URL-safe Base64；解码拒绝非 Base64、未知版本、字段缺失、非 UTC 范围、空白/超长 EOS 和尾随垃圾。cursor 只在 Web 边界存在。

- [ ] **步骤 3：定义固定响应合同**

  ```text
  GET /api/v1/players/history
    -> { players: HistoricalPlayerSummary[], nextCursor: string|null }

  GET /api/v1/players/history/{crossplatformId}
    -> { player: HistoricalPlayerSummary, gapSummary: { gapCount, droppedObservationCount } }

  GET /api/v1/players/history/{crossplatformId}/snapshots
    -> { snapshots: HistoricalSnapshot[], nextBeforeSnapshotId: number|null, gaps: PlayerHistoryGap[] }
  ```

  `HistoricalSnapshot` = `snapshotId` 加完整 31 字段；UTC 全部用 invariant `O` 格式。原因映射为 `queue_full | store_failure | shutdown_timeout`。

- [ ] **步骤 4：实现 controller 路由**

  在现有 Owner-only `PlayersController` 注入三个 Use Case。历史方法不得检查 `IPanelRuntimeStatus.GameReadiness`；先校验 query/cursor，再调用 Use Case。所有 DTO 显式白名单映射，不序列化 Application/game/SQLite 类型。

- [ ] **步骤 5：补 OpenAPI 和转 GREEN**

  OpenAPI 移除 `CancellationToken` 参数，描述 page size、cursor、before ID、Owner Bearer、200/400/401/403/404/500 Problem Details 和三种响应。运行：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~OwinWebHostTests" /p:SevenDaysReferenceRoot=((Resolve-Path '7dtd-reference').Path)
  ```

  预期：三个历史路由在未 ready 时仍返回 Store 结果；在线路由原有 503 语义不变。

### 任务 9：实现 Admin 历史 API parser 和页面局部查询状态

**文件：**

- 新建：`frontend/apps/admin/src/features/players/api/playerSnapshot.ts`
- 新建：`frontend/apps/admin/src/features/players/api/historyPlayers.ts`
- 新建：`frontend/apps/admin/src/features/players/api/historyPlayers.test.ts`
- 修改：`frontend/apps/admin/src/features/players/api/onlinePlayers.ts`
- 新建：`frontend/apps/admin/src/features/players/model/useHistoricalPlayers.ts`
- 新建：`frontend/apps/admin/src/features/players/model/useHistoricalPlayers.test.ts`
- 新建：`frontend/apps/admin/src/features/players/model/useHistoricalPlayer.ts`
- 新建：`frontend/apps/admin/src/features/players/model/useHistoricalPlayer.test.ts`
- 修改：`frontend/apps/admin/src/features/players/index.ts`

- [ ] **步骤 1：抽取共享只读快照 parser**

  `playerSnapshot.ts` 拥有 31 字段 `PlayerSnapshot`、identity/device/position 类型和严格 parser；`onlinePlayers.ts` 只解析 `{ players }` 并复用。任一字段无效拒绝整个响应，不用默认值修复服务端数据。

- [ ] **步骤 2：写历史 parser RED 测试**

  覆盖摘要、计数、UTC、31 字段快照、gap、next cursor；拒绝负数计数、无效 EOS、空名称、非整数 ID、未知 reason、部分床铺、无效 UTC、NaN/Infinity 和错误根对象。返回对象与数组必须 freeze。

- [ ] **步骤 3：实现三 fetch 函数**

  ```ts
  fetchHistoricalPlayers(header, { query, pageSize, cursor }, signal)
  fetchHistoricalPlayer(header, crossplatformId, signal)
  fetchHistoricalSnapshots(header, crossplatformId, { pageSize, beforeSnapshotId }, signal)
  ```

  所有 query 用 `URLSearchParams`，EOS path 用 `encodeURIComponent`，凭据只放 Authorization header。

- [ ] **步骤 4：实现列表 composable**

  `useHistoricalPlayers` 使用 `shallowRef` 保存不可变页，暴露 `loading|ready|empty|forbidden|failed|stale`、search、refresh、loadMore、retry。新 search 取消旧请求并清页；refresh 失败保留旧结果进入 stale；loadMore 失败保留已加载项；按 EOS ID 防御性去重。

- [ ] **步骤 5：实现详情 composable**

  `useHistoricalPlayer` 并行加载 summary 和首张 snapshot 页，后续只加载快照 next page。相同 `snapshotId` 去重，gap 按 `gapId` 去重；404、403、failed、stale、empty 分开；不轮询、不查询在线状态、不暴露危险动作。

- [ ] **步骤 6：运行前端 model 定向测试**

  ```powershell
  pnpm --dir frontend/apps/admin test:unit -- src/features/players/api/historyPlayers.test.ts src/features/players/model/useHistoricalPlayers.test.ts src/features/players/model/useHistoricalPlayer.test.ts
  ```

  预期：parser 严格拒绝坏响应；竞态旧请求不能覆盖新状态；失败不清空成功数据。

### 任务 10：实现历史列表、详情路由和只读快照展示

**文件：**

- 新建：`frontend/apps/admin/src/features/players/ui/PlayersSectionNavigation.vue`
- 新建：`frontend/apps/admin/src/features/players/ui/PlayerSnapshotDetails.vue`
- 修改：`frontend/apps/admin/src/features/players/ui/OnlinePlayerDetailsSlideover.vue`
- 新建：`frontend/apps/admin/src/features/players/ui/HistoricalPlayersView.vue`
- 新建：`frontend/apps/admin/src/features/players/ui/HistoricalPlayerView.vue`
- 新建：`frontend/apps/admin/src/features/players/ui/HistoricalSnapshotTimeline.vue`
- 新建：`frontend/apps/admin/src/features/players/ui/HistoricalSnapshotDetailsSlideover.vue`
- 新建：`frontend/apps/admin/src/features/players/ui/HistoricalPlayersView.test.ts`
- 新建：`frontend/apps/admin/src/features/players/ui/HistoricalPlayerView.test.ts`
- 新建：`frontend/apps/admin/src/features/players/ui/PlayerSnapshotDetails.test.ts`
- 新建：`frontend/apps/admin/src/pages/players/history/index.vue`
- 新建：`frontend/apps/admin/src/pages/players/history/[crossplatformId].vue`
- 修改：`frontend/apps/admin/src/pages/players.vue`
- 修改：`frontend/apps/admin/src/app/i18n/locales/zh-CN.json`
- 修改：`frontend/apps/admin/src/app/i18n/locales/en.json`
- 修改：`frontend/apps/admin/src/app/router.test.ts`
- 修改：`frontend/apps/admin/src/app/AppShell.test.ts`
- 新建：`frontend/apps/admin/tests/e2e/admin-player-history.spec.ts`

- [ ] **步骤 1：写路由和页面状态 RED 测试**

  锁定 `/players/history` 与 `/players/history/:crossplatformId` 都需要 auth、刷新可深链接恢复、未知玩家/403/failed/empty/stale 可区分。列表覆盖搜索、刷新、加载更多、长 EOS ID 复制和移动列表布局。

- [ ] **步骤 2：提取纯展示快照详情**

  `PlayerSnapshotDetails.vue` 只接收 `PlayerSnapshot`，按身份、连接、当前状态、进度、累计统计五组展示。`playGroup`/`lastLoginUtc` 在身份组，bedroll 在当前状态，game stage/exp/skill 在进度组；`null` 显示“未知”，`bedroll=null` 显示“未设置”。组件无 kick props/emits。

  `OnlinePlayerDetailsSlideover` 复用该组件，但继续单独拥有 unavailable 警告和 kick footer；历史 Slideover 只有关闭按钮。

- [ ] **步骤 3：实现摘要列表**

  `HistoricalPlayersView` 包含 `PlayersSectionNavigation`、名称/EOS 搜索、手动刷新、桌面表格/窄屏列表、首次/最后观察、累计/保留计数和质量状态。质量规则：`hasGaps` 优先“存在历史缺口”；否则 compacted > 0 为“已降采样”；否则“完整”。不在列表请求最新完整 snapshot。

- [ ] **步骤 4：实现详情时间线**

  `HistoricalPlayerView` 顶部显示 summary/gap 概览；timeline 倒序显示 observation 时间、名称/entity、等级/进度、生命/延迟/位置和统计摘要。gap 放在相交时间附近，按 ID 去重；“加载更多”失败保留已加载页。点击快照只打开 read-only Slideover。

- [ ] **步骤 5：补双语和响应式语义**

  加入历史导航、质量、gap 原因、未知/未设置、最近登录、玩家组、进度、保留说明和错误状态中英文键。技术 ID、设备值和 gap reason 原值不翻译；日期、数字和单位使用当前 locale。320 CSS px 不得水平溢出。

- [ ] **步骤 6：运行 UI 定向测试**

  ```powershell
  pnpm --dir frontend/apps/admin test:unit -- src/features/players/ui/PlayerSnapshotDetails.test.ts src/features/players/ui/HistoricalPlayersView.test.ts src/features/players/ui/HistoricalPlayerView.test.ts src/app/router.test.ts src/app/AppShell.test.ts
  ```

  预期：两路由、六状态、加载更多、只读详情、焦点返回、双语和窄屏断言通过；历史 DOM 不存在 kick/reset/ban/teleport 控件。

### 任务 11：聚合验证、实现证据提升和交付检查

**文件：**

- 修改：`docs/architecture.md`
- 修改：`docs/architecture/backend-target-blueprint.md`
- 修改：`docs/architecture/admin-frontend-target-blueprint.md`
- 修改：`docs/test.md`
- 仅在发布后修改：`CHANGELOG.md`

- [ ] **步骤 1：运行后端聚合验证**

  ```powershell
  $referenceRoot = (Resolve-Path '7dtd-reference').Path
  dotnet build backend/7DPanel.sln --configuration Release /p:SevenDaysReferenceRoot=$referenceRoot
  dotnet test backend/7DPanel.sln --configuration Release --no-build --no-restore /p:SevenDaysReferenceRoot=$referenceRoot
  ```

  预期：build exit `0`，全部测试通过且测试数非零。

- [ ] **步骤 2：运行 Admin 聚合验证**

  ```powershell
  pnpm --dir frontend/apps/admin lint
  pnpm --dir frontend/apps/admin typecheck
  pnpm --dir frontend/apps/admin test:unit
  pnpm --dir frontend/apps/admin build
  ```

  预期：四条命令 exit `0`。Playwright 和真实 7DTD 不在默认本地验证中运行。

- [ ] **步骤 3：做关键边界源码检查**

  ```powershell
  rg -n "PlayerDataFile\.Load|WriteAsync\(|Task\.Run\(" backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players
  rg -n "ACL|LandClaim|Backpack|QuestPosition|Vending|Inventory" backend/src/Core/LSTY.SevenDPanel.Application/Players/History backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqlitePlayerHistoryStore.cs
  rg -n --glob 'Historical*.vue' "kick|ban|teleport|reset" frontend/apps/admin/src/features/players/ui
  ```

  预期：没有 Save 回调磁盘加载、阻塞 Channel 写、每 Save Task、高基数集合或历史危险操作。

- [ ] **步骤 4：在获得授权后执行受控边界验证**

  真实 `v3.0.1-b4` 只验证：31 字段来源、LastLogin UTC 语义、Progression 在线/fallback 一致、Save 回调不等待 SQLite、queue/store failure gap、正常关服排空。Playwright 只验证真实 OWIN Owner 登录、历史列表/详情/窄屏和游戏 not ready 仍可读历史。没有授权时明确记录“未运行”，不以单元测试替代。

- [ ] **步骤 5：提升已验证 Current 事实**

  只有代码和适用测试通过后，才把真实组件、三表、三路由、生命周期顺序和验证证据写入 `docs/architecture.md`/`docs/test.md`；从两个 Target 蓝图移除已实现部分或标为已提升。不要把未运行的真实进程/浏览器门禁写成已验证。

- [ ] **步骤 6：最终文档与工作树检查**

  ```powershell
  rg -n "TBD|TODO|D:\\|C:\\Users\\" docs
  git diff --check
  git status --short
  ```

  预期：没有占位符、机器路径或空白错误；只包含本切片文件和用户原有改动。未发布时不改 `CHANGELOG.md`，未获授权时不提交。

## 实施顺序与检查点

1. 任务 1 先同步权威合同；未完成前不改代码。
2. 任务 2～4 建立共享 31 字段和 Application 边界。
3. 任务 5～7 完成 SQLite、Channel 和生命周期，形成可写可查的后端纵向切片。
4. 任务 8 完成 Owner-only API 后再开始任务 9～10 Admin。
5. 任务 11 只在实现稳定后运行一次聚合验证并提升 Current 文档。

每个任务结束只运行该任务的定向测试和 `git diff --check`。不要在中间任务重复执行 publish、完整 Playwright、真实 7DTD 或全仓聚合；聚合命令集中在任务 11。
