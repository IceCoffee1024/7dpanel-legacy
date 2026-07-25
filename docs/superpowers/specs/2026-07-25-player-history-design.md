---
state: Current
document_role: Change Record
last_updated: "2026-07-25"
---

# 历史玩家快照设计规格

## 上游与变更范围

本规格提出对[产品需求](../../PRD.md)中 `CAP-02` 的历史玩家能力扩展，并遵循
`NFR-01` 的自托管边界、`NFR-02` 的状态诚实要求和 `NFR-05` 的服务器本地文件
信任边界。当前 `CAP-02` 只定义在线玩家最终一致视图，并未批准离线历史查询；
因此本规格是已审核的 Change Record，不是当前产品合同或实现证据。开始实施前，
必须先获得对 `docs/PRD.md`、[产品设计](../../design.md)、两个 Target 蓝图和
[测试策略](../../test.md)对应影响的独立文档修改授权，并按权威文档职责完成同步。

本规格建立在[在线玩家事件投影设计规格](2026-07-22-online-player-event-projection-design.md)
和[在线玩家详情设计规格](2026-07-24-online-player-details-design.md)已经实现的边界上：
`SavePlayerData` 回调在游戏线程内同步复制一次不可变 `PlayerSnapshot`，在线查询只读取
产品自有值，不持有 `ClientInfo`、`PlayerDataFile`、Unity 对象或其他游戏活对象。
本规格把这次同步复制从现有 25 字段扩充为 31 字段，在同一个 `CopyObservation` 中额外
读取低体积玩家档案和进度事实；历史切片仍只复用构造完成的 observation，不在入队后
二次读取游戏状态，也不改变在线玩家 Join/Save/Disconnect membership、Owner-only 在线
查询、10 秒前端轮询或踢出身份固定语义。

## 决策摘要

- 历史玩家的唯一规范身份是有效的 `crossplatformIdentity.combinedId`，例如
  `EOS_0002d12af0fe4add9c7de0fbc238d431`。
- 缺少跨平台身份的 observation 继续参与在线玩家投影，但不进入历史队列、不创建
  历史玩家，也不使用名称、IP、`entityId` 或原生平台身份降级归并。
- 共享 `PlayerSnapshot` 增加可空 `playGroup`、`lastLoginUtc`、`gameStage`、
  `expToNextLevel`、`skillPoints` 和 `bedroll`；提取失败不使用 `0`、空字符串或哨兵位置
  冒充事实。
- 每次有效 `SavePlayerData` observation 都尝试进入历史队列，即使字段与上一条完全相同。
- 游戏优先：生产者只调用有界 `Channel` 的 `TryWrite`，不等待 SQLite、不为每次事件
  启动独立 `Task.Run`，队列满或持久化失败不得阻塞游戏线程。
- 单一长期后台消费者按入队顺序写 SQLite、记录非计划缺口，并在实时队列空闲时执行
  分批降采样；不引入第二个 SQLite 写入消费者。
- 最近数据保留更高分辨率，超过 30 天后每个固定 UTC 七日桶保留一条代表性快照，
  代表性历史永久保留。
- 首版提供历史玩家列表、玩家摘要和倒序游标分页的完整快照详情；不提供趋势图、导出、
  删除、手动身份合并或从历史快照直接执行危险操作。

## 目标与非目标

### 目标

- 在不延长 `SavePlayerData` 游戏线程数据库等待时间的前提下，尽力记录每次有效玩家
  observation。
- 以 EOS 跨平台身份稳定归档跨会话玩家，保留 observation 当时的 `entityId`、名称、
  原生身份、连接、状态、玩家组、登录、进度、床铺和累计统计事实。
- 用玩家摘要表支持历史玩家搜索与列表，不为每次列表请求聚合永久增长的快照表。
- 用稳定 keyset cursor 查询单个玩家的保留快照，新快照并发写入时不重复或跳过已存在
  的旧记录。
- 用确定性的 UTC 时间桶逐级降低旧数据分辨率，同时永久保留玩家第一条、最新一条和
  30 天以上的每周代表快照。
- 明确区分计划内降采样和队列满、数据库失败、关服排空超时造成的非计划缺口。
- 让 Owner 在 Admin 中搜索历史玩家、打开可深链接详情、分页查看快照，并理解数据时间、
  降采样和缺口语义。

### 非目标

- 不保证每次 Save 都成功落库，也不以阻塞游戏线程换取无缺口历史。
- 不使用无界队列，不为每次 observation 创建独立线程或独立 `Task.Run`。
- 不持久化缺少 `crossplatformIdentity.combinedId` 的 observation，不自动或人工合并
  原生身份、玩家名称、IP 或其他启发式身份。
- 不建立玩家会话、加入/离开事件、在线时长、统计聚合、趋势图或事件回放模型。
- 不持久化 `isOnline`；历史页需要在线提示时，按 EOS ID 与当前在线投影联合判断，无法
  查询当前状态时显示“未知”而不是“离线”。
- 不把 `ACL`、领地块、背包、任务位置、自动售货机位置、库存、配方、技能明细或玩家
  档案等高基数集合复制进每次 Save 快照。
- 不从旧实现继承 `Level = 0`、`GameStage = null`、`ExpToNextLevel = 0` 或
  `SkillPoints = 0` 等占位行为；缺少可靠来源时使用明确的可空值。
- 不增加请求时游戏线程回源、周期扫描、第二套玩家字段复制或通用领域 Event Bus。
- 不从历史页面发起踢出、禁言、封禁或传送；危险操作必须重新经过当前在线玩家身份
  和可用状态确认。
- 不提供自动清空、按玩家删除、隐私擦除、导出或数据库压缩操作；这些能力需要单独的
  产品和运维设计。
- 不新增 NuGet/npm 依赖、项目、通用 Repository 框架、全局前端 Store 或查询缓存库。

## 当前实现证据与复用边界

当前代码已经提供以下可复用事实：

- `SevenDaysOnlinePlayerProjection` 订阅 `PlayerJoinedGame`、`SavePlayerData` 和
  `PlayerDisconnected`；一次成功 Save 同步复制一个完整不可变 `PlayerSnapshot`。
- `PlayerSnapshot` 当前固定保存 25 个批准字段，不泄漏游戏对象；本变更在同一模型和同一
  同步复制点增加 6 个低体积可空字段，不建立历史专用的第二套游戏字段提取器。
- `GET /api/v1/players/online` 只读取内存 observation，按 entity ID 排序，不请求时读取
  游戏活对象。
- SQLite Adapter 已使用 DbUp migration、Dapper、Unix 毫秒 UTC、显式事务和单机数据库。
- `ConsoleCommandAuditService` 已证明有界后台队列、fail-open、gap 和关服排空是本仓库
  可接受的运行时模式，但历史玩家拥有自己的容量、指标、Store 和生命周期，不抽取通用
  队列框架。

本规格不把上述事实扩展解释为历史玩家已经实现。历史表、写入服务、查询 Use Case、API
和 Admin 页面只有在代码与适用验证完成后才能提升到[当前系统架构](../../architecture.md)。

### 旧版后端证据与采纳边界

旧版 `PersistentPlayerDataExtension.ToHistoryPlayer` 和 `ToPlayerDetails` 提供了字段来源证据：

- 旧版 `PlayerId` 来自 `PersistentPlayerData.PrimaryId`，`PlatformId` 来自
  `PersistentPlayerData.NativeId`；新版分别使用明确的 `crossplatformIdentity` 和
  `platformIdentity`，不继承模糊命名。
- `playGroup`、`lastLogin` 和 `bedroll` 来自 `PersistentPlayerData`；床铺不存在的游戏
  哨兵是 `BedrollPos.y == int.MaxValue`。
- `gameStage` 来自在线 `EntityPlayer.gameStage`。
- `expToNextLevel` 和 `skillPoints` 优先来自在线 `EntityPlayer.Progression`；旧版在实体不可用
  时按目标游戏版本的二进制布局解析 `PlayerDataFile.progressionData`。

旧版基础回填会把若干无法提取的进度字段写成 `0`，并会为离线详情从磁盘调用
`PlayerDataFile.Load`。本规格只采纳已经定位到游戏对象的字段来源，不采纳占位值、Save
回调磁盘加载、定时回填、最新状态缓存或旧版页面操作合同。实施前必须以只读游戏版本
参考证据和真实 `v3.0.1-b4` 进程再次验证成员及二进制布局，旧仓库本身不是构建依赖。

## 身份与 observation 合同

### 规范身份

每次 Save observation 在完整 `PlayerSnapshot` 构造成功后检查
`CrossplatformIdentity?.CombinedId`：

```text
valid PlayerSnapshot
  -> update current online projection
  -> crossplatformIdentity.combinedId present and non-blank?
       yes -> TryRecord historical snapshot
       no  -> increment skippedMissingCrossplatformId and stop history path
```

规范身份值必须按现有 `PlayerPlatformIdentity` 已验证值原样保存。产品不改变大小写、不删除
`EOS_` 前缀、不从原生身份推导 EOS ID。当前设计假设目标玩家正常 observation 都携带
跨平台身份；缺失值表示本次数据不满足历史归档前提，不是持久化故障，也不写
`player_history_gaps`。

`entityId` 只表示当次在线实体，允许跨会话变化和被其他身份复用。它保存在快照中用于
还原当时 observation，但不参与历史玩家主键、合并、列表路由或权限判断。

### 快照内容

每条历史快照保存扩充后 `PlayerSnapshot` 的全部 31 个事实：

- `entityId`、`name`；
- 原生 `platformIdentity` 与必有的 `crossplatformIdentity`；
- `deviceType`、可空 `ip`、`ping`、可空 `compatibilityVersion`、可空
  `discordUserId`、`permissionLevel`；
- `position.x/y/z`、`isDead`、`health`、`maxHealth`、`level`；
- 可空 `playGroup`、`lastLoginUtc`、`gameStage`、`expToNextLevel`、`skillPoints` 和
  `bedroll.x/y/z`；
- `score`、`zombieKills`、`playerKills`、`deaths`；
- `totalTimePlayedMinutes`、`distanceWalkedMeters`、`totalItemsCrafted`、
  `longestLifeMinutes`、`currentLifeMinutes`；
- `observedAtUtc`。

即使两次 Save 的字段完全相同，也分别生成不同 `snapshot_id`。历史写入不做字段比较、
去重或 delta 编码；后续容量控制只由已批准的时间桶降采样承担。

### 扩充字段来源与空值语义

| 产品字段 | 游戏线程内来源 | 无可靠值时的结果 |
|---|---|---|
| `playGroup: string?` | 与跨平台规范身份精确匹配的 `PersistentPlayerData.PlayGroup.ToString()` | `null` |
| `lastLoginUtc: DateTimeOffset?` | 同一 `PersistentPlayerData.LastLogin`，按已验证的游戏时间语义规范化为 UTC | `null` |
| `gameStage: int?` | 与 `entityId` 精确匹配的在线 `EntityPlayer.gameStage` | `null` |
| `expToNextLevel: int?` | 在线 `EntityPlayer.Progression.ExpToNextLevel`；实体或 Progression 不可用时使用已验证版本解析器读取 `PlayerDataFile.progressionData` | `null` |
| `skillPoints: int?` | 在线 `EntityPlayer.Progression.SkillPoints`；实体或 Progression 不可用时使用同一已验证版本解析器 | `null` |
| `bedroll: PlayerPosition?` | 同一 `PersistentPlayerData.BedrollPos`；`y == int.MaxValue` 表示不存在 | `null` |

现有 `level` 继续来自 `PlayerDataFile.metadata.Level`，不因引入 Progression 读取而改变来源。
`lastLoginUtc` 表示游戏持久玩家记录中的最近登录时间，不等于 `observedAtUtc`，也不能证明
玩家在两个时间点之间持续在线。`playGroup` 保存非空枚举名称原值，以允许未来游戏版本
增加值；空白值规范化为 `null`。

`CopyObservation` 在游戏线程内完成以下一次性复制：

1. 按现有规则复制 25 个基础字段。
2. 用当次 `ClientInfo.CrossplatformId` 精确查找 `PersistentPlayerData`，并用当次 `entityId`
   精确查找 `EntityPlayer`；不按名称、IP 或原生身份扫描和猜测。
3. 将上述 6 个字段复制为产品值后立即丢弃游戏对象引用，再把完整不可变快照交给在线投影
   和历史 `TryWrite`。

新增字段是附加事实，不得降低基础 observation 的可用性：`PersistentPlayerData` 缺失、
实体已离开、成员读取异常、无效时间、截断或未知版本的 `progressionData` 只让对应字段为
`null`，不得让已经有效的 25 字段 observation 整体失败。Progression fallback 必须检查
版本和最小长度、保持原 stream position，并在解析失败时快速返回；不得调用
`PlayerDataFile.Load`、访问磁盘、等待锁或把 stream 交给后台线程。兼容性失败只记录限频
诊断和计数，不记录玩家身份、IP、位置或 progression 原始字节。

## 运行时架构

### 数据流

```text
ModEvents.SavePlayerData (game thread)
  -> extended CopyObservation
       -> copy existing 25 fields
       -> copy six optional profile/progression fields
  -> immutable 31-field PlayerSnapshot
  -> existing online projection upsert
  -> valid crossplatformIdentity.combinedId
  -> PlayerHistoryWriteService.TryRecord(snapshot)
       -> bounded Channel capacity 1024
       -> one long-lived background consumer
       -> IPlayerHistoryStore.Append(snapshot)
       -> SQLite transaction
            -> INSERT player_history_snapshots
            -> INSERT/UPDATE player_history_players
       -> when queue is idle or below maintenance threshold
            -> compact at most one bounded batch
```

`SevenDaysOnlinePlayerProjection` 只在成功构造不可变 observation 后把产品值交给同项目内的
具体 `PlayerHistoryWriteService`。该服务依赖 Application 定义的历史 Store 端口，SQLite
Adapter 实现端口；Bootstrap 负责实例注册和生命周期顺序。SevenDays Adapter 不直接引用
Persistence Adapter，Web Adapter 不直接执行 SQL。

Application 只增加历史读写边界实际需要的模型、Store 端口和查询 Use Case。首个非测试
运行时消费者与 SQLite 实现随同一纵向切片交付，不创建没有生产消费者的接口、注册表或
通用生命周期抽象。

### Channel 选择

- 使用 `System.Threading.Channels` 的 bounded channel，容量固定为 `1024`。
- 使用不会在 full 时替生产者等待的 `TryWrite`；full mode 必须让调用方能可靠识别写入
  未被接受，不能静默覆盖旧项或把丢弃误报为成功。
- `SingleReader=true`；生产者线程模型不作为正确性前提，写入端保持线程安全。
- 只在服务启动时创建一个长期消费者任务。每条 Save 不调用独立 `Task.Run`。
- 入队项只包含不可变产品值，包括可空床铺坐标；不保存 `ClientInfo`、`PlayerDataFile`、
  `PersistentPlayerData`、`EntityPlayer`、Unity 对象、stream、delegate 或数据库连接。

容量 `1024` 是首版固定决策，不增加操作员配置。真实进程必须记录队列高水位和 Save
频率；只有证据证明该容量不合适时，后续变更才调整容量或引入配置。

### 启停顺序

启动顺序：

```text
DbUp migration
  -> construct SQLite history store
  -> start PlayerHistoryWriteService consumer
  -> subscribe/start SevenDaysOnlinePlayerProjection
  -> accept game observations
```

停止顺序：

```text
stop/unsubscribe SevenDaysOnlinePlayerProjection
  -> reject late TryRecord calls
  -> complete history Channel writer
  -> drain accepted snapshots within 5-second default timeout
  -> attempt to persist pending gaps
  -> report final counters and unrecovered gaps
  -> dispose history service and SQLite runtime
```

默认排空时间沿用现有命令审计服务的 5 秒边界，测试构造可以注入更短 timeout，但生产
组合不增加新的操作员配置。停止期间没有成功入队的 observation 计入
`rejectedStopping`，与 `queue_full` 区分。排空
超时不阻止宿主继续关服；仍未写入的已接受项按玩家聚合为 `shutdown_timeout` 缺口，并在
日志和停止摘要中报告。若进程在缺口本身落库前结束，停止摘要必须保留
`unrecoveredGaps` 与 `unrecoveredDropped`，不能宣称历史完整。

## SQLite 数据模型

所有 UTC 时间沿用现有 Persistence Adapter 约定，保存为 Unix 毫秒 `INTEGER`。字符串
字段沿用现有 Application 校验，不把空白可选值写成非空事实。迁移只创建以下历史所有者，
不修改既有认证和审计表。

### `player_history_players`

每个 EOS 规范身份一行，拥有历史列表的低成本摘要：

| 列 | 语义 |
|---|---|
| `crossplatform_id TEXT PRIMARY KEY` | EOS 规范身份原值 |
| `latest_name TEXT NOT NULL` | 最新成功持久化快照的名称 |
| `first_observed_utc INTEGER NOT NULL` | 第一条成功持久化 observation 时间 |
| `last_observed_utc INTEGER NOT NULL` | 最新成功持久化 observation 时间 |
| `latest_snapshot_id INTEGER NOT NULL` | 最新保留快照的逻辑指针 |
| `total_observation_count INTEGER NOT NULL` | 累计成功持久化的 Save 数，降采样不减少 |
| `retained_snapshot_count INTEGER NOT NULL` | 当前保留的快照行数 |
| `compacted_snapshot_count INTEGER NOT NULL` | 计划内降采样删除的快照数 |

`total_observation_count = retained_snapshot_count + compacted_snapshot_count` 必须始终成立；
非计划丢失的 observation 从未成功持久化，不进入该等式，由 gap 单独表达。

历史列表按不可变的 `(first_observed_utc DESC, crossplatform_id ASC)` 排序，避免玩家新 Save
改变排序键并破坏进行中的 keyset 分页。搜索只作用于摘要表中的 `latest_name` 和
`crossplatform_id`，使用参数化并正确转义 `%`、`_` 与 escape 字符的 `LIKE`；不扫描
快照表。

### `player_history_snapshots`

每次成功写入追加一行：

| 列组 | 内容 |
|---|---|
| 标识 | `snapshot_id INTEGER PRIMARY KEY AUTOINCREMENT`、`crossplatform_id` |
| 时间与临时实体 | `observed_utc`、`entity_id` |
| 名称与身份 | `name`、原生 identity 的 `combined_id/platform`、跨平台 identity 的 `combined_id/platform` |
| 连接 | `device_type`、可空 `ip`、`ping`、可空 `compatibility_version`、可空 `discord_user_id`、`permission_level` |
| 当前状态 | `position_x/y/z`、`is_dead`、`health`、`max_health`、`level` |
| 玩家档案与进度 | 可空 `play_group`、`last_login_utc`、`game_stage`、`exp_to_next_level`、`skill_points`、`bedroll_x/y/z` |
| 累计统计 | `score`、`zombie_kills`、`player_kills`、`deaths`、四个带单位时长/距离字段、`total_items_crafted` |

`last_login_utc` 与 `observed_utc` 一样保存为 Unix 毫秒 `INTEGER`；其余新增标量使用 SQLite
`TEXT` 或 `INTEGER`，全部允许 `NULL`。床铺必须全有或全空：数据库约束保证
`bedroll_x`、`bedroll_y`、`bedroll_z` 三列不能只写入一部分。`game_stage`、
`exp_to_next_level` 和 `skill_points` 的非空值必须大于或等于 0；Store 拒绝无效值而不是
静默截断。`play_group` 不建立数据库枚举约束，以保留未来游戏版本的新枚举名称。

必须至少建立：

```text
(crossplatform_id, snapshot_id DESC)
(crossplatform_id, observed_utc DESC, snapshot_id DESC)
```

`snapshot_id` 是历史分页和同毫秒 observation 排序的最终稳定键。不得对
`(crossplatform_id, observed_utc)` 设置唯一约束，因为多次 Save 可以共享同一毫秒。

### `player_history_gaps`

非计划缺口按玩家持久化：

| 列 | 语义 |
|---|---|
| `gap_id TEXT PRIMARY KEY` | 产品生成的稳定 gap 标识 |
| `crossplatform_id TEXT NOT NULL` | 受影响 EOS 身份 |
| `started_utc INTEGER NOT NULL` | 第一条丢失 observation 时间 |
| `completed_utc INTEGER NOT NULL` | 最后一条丢失 observation 时间 |
| `dropped_count INTEGER NOT NULL` | 丢失数量，必须大于 0 |
| `reason TEXT NOT NULL` | `queue_full`、`store_failure` 或 `shutdown_timeout` |
| `recorded_utc INTEGER NOT NULL` | 缺口成功落库时间 |

同一玩家、同一连续失败原因可以在内存中聚合。恢复写入时，消费者先尝试持久化该玩家
待处理 gap，再写后续快照；gap 写入失败时继续保留待处理状态，不把后续成功快照解释为
历史重新连续。进程结束前仍未能写入的 gap 只能由停止摘要和日志证明，不能伪造数据库
记录。

### 快照写入事务

单条或一个小型有序批次的 SQLite 事务必须同时完成：

```text
INSERT player_history_snapshots
  -> INSERT player_history_players on first observation
     or UPDATE latest fields and counters
  -> COMMIT
```

事务失败不得留下只有快照没有摘要、只有摘要没有快照或摘要指向不存在快照的状态。只有
Commit 成功后才增加 `persisted` 指标。消费者可以在不改变顺序和失败语义的前提下批量
写入，但首版不以批量抽象为前置条件。

## 分级保留与降采样

### 保留矩阵

历史快照按 observation 年龄逐级降低分辨率：

| observation 年龄 | 固定 UTC 桶宽 | 保留规则 |
|---|---:|---|
| `0～5 分钟` | 不分桶 | 每次 Save 全部保留 |
| `5～15 分钟` | `1 分钟` | 每桶保留最后一条 |
| `15～30 分钟` | `5 分钟` | 每桶保留最后一条 |
| `30 分钟～1 小时` | `10 分钟` | 每桶保留最后一条 |
| `1～6 小时` | `30 分钟` | 每桶保留最后一条 |
| `6～12 小时` | `1 小时` | 每桶保留最后一条 |
| `12～24 小时` | `2 小时` | 每桶保留最后一条 |
| `1～3 天` | `6 小时` | 每桶保留最后一条 |
| `3～7 天` | `12 小时` | 每桶保留最后一条 |
| `7～30 天` | `1 天` | 每桶保留最后一条 |
| `30 天以上` | `7 天` | 每桶保留最后一条并永久保存 |

玩家第一条历史快照和 `latest_snapshot_id` 指向的最新快照始终固定保留，即使它们不是
所在时间桶的最后一条。首版没有会话模型，因此不额外固定“会话首末快照”；若未来批准
会话历史，必须重新设计该保留关系。

### 固定桶算法

时间桶锚定 Unix epoch 的 UTC 整数边界：

```text
bucket = floor(observed_utc / bucket_width_ms)
winner = maximum snapshot_id in
         (crossplatform_id, retention_tier, bucket)
```

不得使用“距离当前时间最近 5 分钟、15 分钟”的动态锚点选择记录。固定桶使相同输入、
相同清理时刻得到确定结果，避免代表记录随当前时间持续漂移。API 始终返回获胜快照真实
的 `observedAtUtc`，不把桶边界伪装为 observation 时间。

每次执行使用注入的同一个 `utcNow` 计算年龄边界。靠近层级边界的快照只属于一个层级；
边界采用左闭右开区间，避免重复保留或同时删除。降采样必须幂等：同一 `utcNow` 重复执行
不会继续删除上一轮已经选中的桶获胜记录。

### 调度与批次

- 只有单一历史消费者可以执行降采样写事务，不启动第二个 SQLite maintenance writer。
- 仅在 Channel 为空或服务维护的近似深度不高于 `256` 时开始一个维护批次；新实时项
  到达后优先恢复消费。
- 每批最多删除 `1000` 行，事务完成后重新检查队列，不用一个长事务清理全部历史。
- 删除成功后在同一事务更新每名受影响玩家的 `retained_snapshot_count` 和
  `compacted_snapshot_count`，保持计数等式。
- 清理失败只记录 `compactionFailures` 并稍后重试；它不删除快照、不产生历史 gap，也不
  阻塞新的 Save 入队。
- 计划内降采样不是数据丢失，不写 `player_history_gaps`。Admin 必须用“已降采样”表达，
  不能显示为系统故障。

按该矩阵，首月每名玩家通常保留约 70～100 条代表记录，加最近 5 分钟的原始 Save；
30 天以后每年最多新增约 52 个七日桶代表值，第一条和最新一条另行固定。

## 后台失败与可观测性

### 生产者行为

游戏事件线程完成上述受限同步复制后，历史生产者只执行不可变值检查和 `TryWrite`：

- 成功入队增加 `accepted`。
- 队列满增加 `droppedFull`，按 EOS 身份聚合 `queue_full` gap。
- 服务已停止接收时增加 `rejectedStopping`。
- 缺少 EOS ID 时增加 `skippedMissingCrossplatformId`，不计入 dropped 或 gap。

任何分支都不得同步打开 SQLite、等待 Channel 空位、sleep、同步重试或等待消费者 Task。

### 消费者行为

SQLite Append 失败时：

- 当前 dequeued snapshot 计入该玩家 `store_failure` gap；
- `storeFailures` 增加，消费者保持存活；
- 可以在后台线程使用有上限的短暂指数退避，退避期间生产者仍只执行 `TryWrite`；
- 队列继续增长到容量后按 `queue_full` 记录额外缺口；
- 存储恢复后先写待处理 gap，再写该玩家后续快照。

日志必须避免每条失败产生无界重复文本；首个失败、状态变化、周期摘要和恢复可以记录。
日志不得包含 IP、Discord ID 或完整玩家快照正文，只记录安全身份摘要、计数、原因和时间。

### 指标与停止摘要

服务至少维护：

```text
accepted
persisted
droppedFull
rejectedStopping
skippedMissingCrossplatformId
storeFailures
compacted
compactionFailures
highWater
pendingAtShutdown
unrecoveredGaps
unrecoveredDropped
```

这些指标首先进入生命周期日志和测试断言，不在本切片增加通用 metrics 服务或新 HTTP
管理端点。

## Application 与 API 边界

### Application 所有权

Application 定义：

- 历史玩家摘要和历史快照查询结果；
- 历史 gap 摘要；
- 持久化 Adapter 需要实现的历史 Store 端口；
- `GetHistoricalPlayersUseCase`、`GetHistoricalPlayerUseCase` 和
  `GetPlayerHistorySnapshotsUseCase`；
- page size、cursor 和 EOS 身份输入验证。

Application 不知道 Dapper、SQLite SQL、Channel、Web DTO、Vue 状态或游戏类型。写入后台
服务由 SevenDays Adapter 拥有，因为它协调游戏 observation 与非阻塞队列；持久表和查询
SQL 由 Persistence Adapter 拥有；Web Adapter 只调用 Use Case 并显式映射 DTO。

### HTTP 路由

新增三个 Owner-only 只读端点：

```text
GET /api/v1/players/history
GET /api/v1/players/history/{crossplatformId}
GET /api/v1/players/history/{crossplatformId}/snapshots
```

历史读取只访问 SQLite，不依赖当前游戏 readiness。游戏尚未就绪或在线投影为空时仍可
返回历史数据；认证失败、Owner 授权失败和数据库读取失败继续使用统一 Problem Details。

### 历史玩家列表

请求：

```text
GET /api/v1/players/history?query=alice&pageSize=50&cursor=...
```

- `query` 可空，只在 `latest_name` 和完整 `crossplatform_id` 上搜索。
- `pageSize` 默认 `50`，有效范围 `1～100`。
- 排序固定为不可变的 `first_observed_utc DESC, crossplatform_id ASC`；列表仍返回最新
  observation 时间供用户判断近期活动。
- cursor 是 Web/API 边界拥有的版本化、不透明、URL-safe 值，至少编码上一页末项的
  `first_observed_utc` 和 `crossplatform_id`；无效 cursor 返回稳定 400 Problem Details。
- 既有玩家的新快照不会改变列表排序键；并发首次出现的新玩家只会排在当前第一页之前，
  不改变使用既有 cursor 向旧玩家翻页的稳定边界。

每项返回：

- `crossplatformId`、`latestName`；
- `firstObservedAtUtc`、`lastObservedAtUtc`；
- `totalObservationCount`、`retainedSnapshotCount`、`compactedSnapshotCount`；
- `hasGaps`。

列表不返回最新完整 31 字段快照，避免把摘要接口变成重复详情合同。

### 单个玩家摘要

`GET /api/v1/players/history/{crossplatformId}` 返回与列表一致的摘要及 gap 概览。未知 EOS
身份返回稳定 404。路由值必须按 URI component 解码后执行非空、非空白和最大 256 字符
验证，再与数据库中的规范身份精确匹配；日志不把未经验证的长输入直接写入。

### 快照分页

请求：

```text
GET /api/v1/players/history/EOS_xxx/snapshots?pageSize=100&beforeSnapshotId=12345
```

- `pageSize` 默认 `100`，有效范围 `1～200`。
- 首次请求不带 `beforeSnapshotId`，按 `snapshot_id DESC` 返回最新页。
- 下一页只查询 `snapshot_id < beforeSnapshotId`，禁止使用 `OFFSET` 深分页。
- 响应包含完整 31 字段快照、`snapshotId` 和 `nextBeforeSnapshotId`。
- 响应包含 `gaps` 数组，查询与本页
  `[oldestObservedAtUtc, newestObservedAtUtc]` 相交的 gap；跨越多页的同一 gap 可以重复
  返回，前端按 `gapId` 去重并放在相关时间附近，不能只在玩家顶部给出无法定位的永久
  警告。空快照页返回空 `gaps`。
- 计划内降采样不伪造 gap；摘要计数和页面说明表达分辨率已经降低。

快照 DTO 复用扩充后的在线 `PlayerSnapshot` 字段名称、类型、单位和空值语义。新增字段使用
`playGroup`、`lastLoginUtc`、`gameStage`、`expToNextLevel`、`skillPoints` 和可空
`bedroll: { x, y, z }`。历史端点不因值年龄返回 stale，因为它查询的就是带真实
observation 时间的历史事实。

## Admin 信息架构与交互

### 路由与页面

玩家模块增加可深链接的历史入口：

```text
在线玩家       历史玩家
/players       /players/history
```

历史玩家列表和详情都是 Owner-only 受保护路由。列表点击后进入独立详情路由：

```text
/players/history/:crossplatformId
```

详情使用独立页面而不是只使用 Slideover，以容纳长期时间线、深链接和连续分页。点击一条
快照后打开只读详情 Slideover。现有在线详情与历史详情可以在出现两个真实生产消费者时
提取 Feature 内纯展示 `PlayerSnapshotDetails`；在线页面继续单独拥有踢出状态机，历史组件
不接收危险操作 props 或 emits。

### 历史玩家列表

列表显示：

- 最新玩家名称和 EOS ID；
- 首次与最后 observation 时间；
- 累计 Save 数、当前保留快照数；
- 历史质量：完整、已降采样、存在非计划缺口。

页面提供名称/EOS ID 搜索、手动刷新和游标“加载更多”。首版不自动高频轮询历史列表。
桌面使用表格，窄屏使用明确的信息列表；长 EOS ID 可复制且不能造成水平页面溢出。

### 玩家历史详情

顶部摘要显示规范身份、最新名称、首次/最后观察时间、累计 observation、保留数、降采样
数和 gap 状态。下方快照按时间倒序分页，每行或卡片显示：

- observation 时间；
- 当次名称和 `entityId`；
- 等级、游戏阶段、待升级经验、技能点、生命、延迟、位置；
- 分数、击杀、死亡和累计游戏时长摘要。

点击快照打开只读完整详情，按身份、连接、当前状态、进度、累计统计五组展示全部字段。
玩家组和最近登录放入身份组，床铺位置放入当前状态组，游戏阶段、待升级经验和技能点
放入进度组。技术身份和原值不翻译；标签、设备、状态、日期、数字和单位跟随当前语言。
可空字段统一显示“未知”，不存在的床铺显示“未设置”，不能显示游戏哨兵坐标。

页面必须明确说明：

- 最近 5 分钟保留每次成功持久化的 Save；更旧数据按固定 UTC 时间桶降采样；
- “已降采样”是计划内分辨率变化，不代表服务故障；
- “历史缺口”表示某段 Save 因队列或数据库故障未能持久化；
- 每条记录显示真实 `observedAtUtc`，不能解释为时间桶边界或持续在线证明。

### 页面状态

列表和详情分别拥有页面局部状态：

- `loading`：尚无成功数据且请求进行中；
- `ready`：当前响应有效；
- `empty`：没有历史玩家或该玩家没有保留快照；
- `forbidden`：当前身份不是 Owner；
- `failed`：没有可显示旧数据且请求失败；
- `stale`：已有成功数据后刷新失败，继续显示旧数据及原时间并明确提示。

401 或本地会话失效继续由 Auth Store 清理并返回登录页；403 不清除仍可能有效的会话。
快照加载更多失败保留已经加载的页并允许显式重试，不清空时间线、不自动重复请求。

历史详情不显示踢出入口。若未来需要从历史玩家定位当前在线玩家，必须先重新查询在线
端点并通过 `{entityId, platformIdentity.combinedId}` 当前身份固定规则，不能直接复用历史
快照中的旧 `entityId`。

## 安全与隐私边界

- 所有历史端点只允许 `Owner`，与当前敏感在线玩家字段授权保持一致。
- SQLite 位于服务器本地信任边界，沿用 `NFR-05`；本切片不增加静态加密。
- API 不返回数据库路径、SQL、堆栈、Channel 内部状态或未经授权的历史字段。
- EOS ID、原生身份、IP、Discord ID、最近登录、玩家位置和床铺位置属于敏感管理数据；
  不得进入匿名页面、错误详情、前端持久会话、URL 查询日志之外的产品日志正文或通知文本。
- 历史路由包含 EOS ID 是已批准的管理深链接；前端不得把 Bearer Token 或其他凭据放入
  路由、query、浏览器历史或复制结果。
- 不提供历史修改或删除端点，API Key 和 Access Token 继续按当前角色和凭据类型授权。

## 验证设计

### Application

- EOS 身份、page size、cursor 输入和历史查询结果保持不可变且拒绝无效值。
- 扩充后的 `PlayerSnapshot` 对新增字符串、UTC 时间、非负进度整数和全有或全空床铺坐标
  执行明确校验；`null` 保持未知语义，不转换为占位值。
- 列表 keyset 在相同 `firstObservedAtUtc` 下使用 EOS ID 稳定排序，既有玩家新 Save 不会
  移动当前分页位置。
- 快照 keyset 严格使用 exclusive `beforeSnapshotId`。
- 未知玩家、空结果和 gap 摘要具有明确结果，不用异常文本定义业务状态。

### SevenDays 历史写入服务

- `CopyObservation` 在同一 Save 回调中按 EOS Primary ID 和 `entityId` 精确取得附加来源；
  找不到来源时对应字段为 `null`，不扫描玩家列表、不按名称/IP 猜测、不从磁盘加载档案。
- `EntityPlayer.Progression` 是进度字段首选来源；实体不存在时，版本匹配且长度合法的
  `progressionData` fallback 得到相同产品值，并恢复 stream 原始 position。未知版本、
  截断数据和读取异常返回 `null` 且不丢失基础 observation。
- `BedrollPos.y == int.MaxValue` 映射为 `bedroll = null`；有效床铺一次性复制三个坐标。
- `PersistentPlayerData.LastLogin` 只在时间语义可验证且可规范化为 UTC 时写入；无效值为
  `null`，不得用 `observedAtUtc` 替代。
- 每次带有效 EOS ID 的成功 Save 都调用一次 `TryWrite`，字段相同也不去重。
- 缺少 EOS ID 时在线投影仍更新，历史服务只增加 skip 指标。
- 生产者在队列可用、队列满和停止中都同步快速返回，不调用 Store、不等待消费者。
- 单消费者保持 accepted 顺序，Store 写入异常不会终止 worker。
- 队列满、Store failure 和 shutdown timeout 分别生成正确玩家级 gap。
- 存储恢复后先写 pending gap，再写该玩家后续快照。
- 启动失败、重复 Start/Stop、停止排空、排空超时和迟到 observation 具有确定结果。
- 停止摘要的 accepted、persisted、dropped、pending 和 gap 计数守恒。

### SQLite

- migration 创建三张历史表、约束和索引，并在重复启动时保持幂等升级。
- 快照与摘要在同一事务提交；故障注入不会留下半写状态。
- 6 个新增字段完整 round-trip；可空值、未来 `playGroup` 枚举名称、UTC 毫秒和床铺三列
  完整性约束正确，负数进度字段被拒绝。
- 同一毫秒的同一玩家 Save 可以保存多条，`snapshot_id` 保持严格顺序。
- 首条插入、后续更新、名称变化、跨会话 `entityId` 变化和计数更新正确。
- 列表搜索只依赖摘要表；列表和快照 keyset 在并发新增数据时无旧页重复或跳项。
- gap 聚合、幂等插入和时间范围正确。
- 每个保留层级覆盖边界前一毫秒、精确边界和后一毫秒；UTC 桶获胜项确定。
- 降采样永不删除第一条和最新一条，同一 `utcNow` 重复执行幂等。
- 每批最多删除 1000 行，计数等式在 Commit 后成立；事务失败不改变快照或计数。
- 30 天以上每个七日桶代表记录永久保留。

### Web API

- 三个路由 Owner 成功，Admin/Viewer/匿名被拒绝；网站 Access Token 与 API Key 按当前
  Header Bearer 规则工作。
- 历史查询在游戏未就绪时仍读取 SQLite，不调用在线 Query、Dispatcher 或游戏对象。
- 列表和详情字段白名单、camelCase、UTC 格式、integer/finite number/null 语义正确。
- 无效 query、page size、cursor、EOS ID 和 snapshot cursor 返回稳定 Problem Details。
- 快照分页返回完整 31 字段、真实 observation 时间和相关 gap，不把降采样标为故障。
- SQLite 读取失败不泄漏 SQL、路径或异常堆栈。
- OpenAPI 描述 Owner-only Bearer、安全分页参数、响应和 Problem Details。

### Admin

- API parser 严格验证历史摘要、计数、cursor、完整快照和 gap；任一无效响应不覆盖上次
  成功结果。
- 历史列表覆盖搜索、加载更多、相同时间排序、长 EOS ID、复制和空状态。
- 深链接详情刷新可恢复；未知玩家、403、请求失败和快照空页状态明确。
- 时间线加载下一页不重复现有项，失败保留已加载页并可重试。
- 在线和历史详情共享展示时不共享危险操作状态；历史路径不存在踢出入口。
- 新增字段覆盖全部非空、全部为空、无床铺和未来 `playGroup` 枚举名称响应；`null`、`0`
  与“未设置床铺”具有不同展示语义。
- “已降采样”“历史缺口”“未知”使用不同中英文文案和视觉语义。
- 桌面、`390x844` 和 320 CSS 像素宽度无水平页面溢出，Slideover 焦点进入与返回正确。
- 完整 Vitest、lint、typecheck、Vite production build 和适用真实 OWIN Playwright 通过。

### 真实进程

- Windows `v3.0.1-b4` 受控玩家产生连续多次 Save，验证每次有效 EOS observation 入队且
  游戏线程不等待 SQLite。
- 在真实在线实体和受控实体缺失两条路径下核对玩家组、最近登录、游戏阶段、待升级经验、
  技能点和床铺；确认 fallback 与当前目标版本布局一致、不会改变 progression stream
  position，且成员不可用时历史仍保存基础字段。
- 多名玩家同时在线时验证摘要归属、跨会话 entity ID 变化和历史分页。
- 持有 SQLite 写锁超过正常 timeout，确认在线投影与游戏继续运行、历史服务产生
  `store_failure`/`queue_full` 证据，释放锁后恢复并先落 gap。
- 以足够数据触发至少两个保留层级，验证降采样不会压住实时写入。
- 正常关服排空后进程和 listener 释放；故障注入的排空超时有明确未恢复摘要。
- 记录 Save 回调耗时、Channel high-water、SQLite 写入延迟和游戏帧影响；没有证据前不把
  容量 `1024` 宣称为性能基线。
- Linux x64 仍由候选发布门禁验证 SQLite native、线程和文件系统行为，本规格不以本地
  单元测试替代。

## 预期实施边界

实施计划应限制在一个完整纵向切片，预计涉及：

- Application Players：把共享 `PlayerSnapshot` 扩充为 31 字段，并增加历史模型、Store
  端口和三个查询 Use Case；
- SevenDays Players/Runtime：在现有 `CopyObservation` 一次性复制 6 个附加字段，复用完整
  在线 observation 的具体历史 Channel 服务和生命周期；
- Persistence SQLite：一份新 migration、历史 Store、查询和分批降采样；
- Bootstrap：明确注册和启停顺序；
- Web Players：三个 Owner-only route、DTO、cursor 和 OpenAPI；
- Admin Players Feature：历史 API、页面局部查询状态、列表、详情路由、快照时间线和只读
  详情；
- 对应后端、前端、浏览器测试；
- 获得额外文档授权后，对权威 living docs 和 Target 蓝图进行范围同步。

不创建新项目、通用后台作业框架、通用 Repository、共享前端 package 或独立历史服务
进程。若实施发现新增字段不能在现有同步复制内安全取得、目标版本 progression 布局无法
验证或复制成本明显影响游戏线程，必须停止并修订本规格；不得保存游戏活对象、异步读取
stream、在 Save 回调加载玩家文件或增加第二次回源来绕过边界。

## 文档影响与提升条件

本次授权只创建本 Change Record，以下权威文档保持不变：

- [产品需求](../../PRD.md)：实施前先扩展 `CAP-02`，定义历史玩家结果、EOS-only 收录、
  降采样和缺口验收；本规格不能替代产品合同。
- [产品设计](../../design.md)：批准历史玩家路由、列表、详情、页面状态和双语规则。
- [后端目标架构蓝图](../../architecture/backend-target-blueprint.md)：加入历史 Channel、
  SQLite 表、查询和生命周期目标，明确它们不是当前证据。
- [Admin 前端目标架构蓝图](../../architecture/admin-frontend-target-blueprint.md)：加入
  Players Feature 历史路由、局部状态和只读详情边界。
- [测试策略](../../test.md)：增加分级保留、gap、数据库故障、真实进程和浏览器门禁。
- [当前系统架构](../../architecture.md)：只有实现和验证完成后，才提升真实项目、表、路由、
  生命周期和证据；设计阶段不得写成 Current 实现。

不修改 `README.md` 或 `CHANGELOG.md`：本规格不改变当前可运行命令，也不是已发布行为。

## 书面批准检查点

批准本规格即确认：

- 只有有效 `crossplatformIdentity.combinedId` 的 observation 进入历史，缺失时不降级
  归并；
- `PlayerSnapshot` 在同一个 Save 同步复制点扩充为 31 字段，新增玩家组、最近登录、游戏
  阶段、待升级经验、技能点和床铺；不可用时为 `null`，不继承旧版占位值；
- `ACL`、领地、背包、任务、售货机、库存、配方、技能明细和玩家档案不进入本切片；
- 每次有效 Save 都尝试记录，字段相同也不去重，但游戏线程只执行有界 Channel
  `TryWrite`；
- 容量固定为 `1024`，单一长期消费者写 SQLite，队列满、Store failure 和关服超时以
  玩家级 gap 表达；
- SQLite 使用玩家摘要、不可变快照和 gap 三类数据，摘要与快照事务一致；
- 快照按本规格 UTC 固定时间桶逐级降采样，第一条、最新一条和 30 天以上每周代表记录
  永久保留；
- 历史 API Owner-only、只读 SQLite、使用 keyset cursor，并在游戏未就绪时仍可查询；
- Admin 提供历史玩家列表、独立详情路由、分页快照和只读完整详情，明确区分降采样、
  非计划缺口和未知值；
- 历史快照不能直接作为危险操作目标；
- 实施前先获得权威 product/design/target/test 文档修改授权并完成同步，再创建一份只链接
  本规格为 primary design 的实施计划。
