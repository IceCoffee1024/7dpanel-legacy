---
state: Current
document_role: Design Spec
last_updated: "2026-07-23"
---

# 在线玩家事件投影设计规格

## 上游与变更范围

本规格落实[产品需求](../../PRD.md)中的 `CAP-01`、`CAP-02`、`NFR-01` 和 `NFR-02`，并变更[当前系统架构](../../architecture.md)中在线玩家读模型的数据来源。它替代[在线玩家只读查询纵向切片设计规格](2026-07-21-online-player-query-design.md)中“每次 HTTP 请求进入游戏主线程捕获完整快照”的内部实现方向，并把成功 JSON 从根级快照时间改为每个玩家自己的 `observedAtUtc`；Owner-only 授权、前端轮询入口和玩家动作身份校验保持不变。

当前实现已经证明 `ConnectionManager.Clients.List + World.Players.dict` 的请求时主线程读取路径可用。新的批准方向接受最多约一个 `NetPackagePlayerData` 上传周期的数据陈旧和不同玩家观察时间不一致，以 `ModEvents.SavePlayerData` 驱动进程内最终一致投影，让常规 HTTP 读取不再投递 7DTD 主线程任务。

本规格是待审核的 Change Record，不是当前实现证据。只有代码、自动化和真实 `v3.0.1-b4` 进程验证完成后，才能把结果提升到[当前系统架构](../../architecture.md)和[测试策略](../../test.md)。

## 已验证的游戏证据

只读私有参考子模块 `7dtd-reference/v3.0.1-b4` 表明：

- 远程客户端默认使用 30 秒 `countdownSendPlayerDataFileToServer` 周期，把 `PlayerDataFile.FromPlayer` 结果通过 `NetPackagePlayerData` 发往服务端；玩家死亡会额外请求立即发送。
- 服务端 `NetPackagePlayerData.ProcessPackage` 校验发送者 entity id 后调用 `GameManager.SavePlayerData(ClientInfo, PlayerDataFile)`。
- `GameManager.SavePlayerData` 先更新 `ClientInfo.latestPlayerData` 和游戏持久数据，再触发 `ModEvents.SavePlayerData`；事件参数同时携带本次 `ClientInfo` 与 `PlayerDataFile`。
- `PlayerDataFile.metadata` 提供本次上传的名称和等级；`PlayerDataFile.ecd.stats.Health.Value` 提供本次上传的生命值；`ClientInfo` 提供 entity id、主平台身份、可选跨平台身份和事件时 ping。
- 玩家统计变化还可通过 `NetPackagePlayerStats` 更早写入服务端实体，但 `ModEvents` 没有对应的 PlayerStats 事件。本切片不为该高频网络包增加 Harmony patch。
- `ModEvents.PlayerJoinedGame` 在服务端请求玩家进入游戏时触发并携带 `ClientInfo`；`ModEvents.PlayerDisconnected` 在服务端主线程断开流程中、客户端仍在 `ConnectionManager.Clients` 时触发并携带 `ClientInfo`。

这些证据确定候选事件和字段，不证明产品订阅、线程行为或兼容性已经完成。真实进程 smoke 必须记录首次玩家数据上传、周期更新、断开删除和关服清理。

## 目标

- 用 `SavePlayerData` 事件维护进程内在线玩家最终一致投影，常规 API 查询只读取产品自有不可变值。
- 用 `ConcurrentDictionary<int, OnlinePlayerObservation>` 支持逐玩家 Upsert、条件删除和无锁 API 枚举，不要求跨玩家同一采集时刻。
- 在事件回调返回前同步复制全部批准字段，不保存 `ClientInfo`、`PlayerDataFile`、`PlatformUserIdentifierAbs`、`EntityPlayer` 或其他游戏活对象。
- 用 `PlayerJoinedGame` 维护不含玩家状态的在线 membership，用 `PlayerDisconnected` 及时删除当前会话 membership 与 observation；运行时停止时注销事件并清空投影。
- 保持玩家字段白名单和排序；每个玩家返回自己的 `observedAtUtc`，根对象只保留 `players`。
- 服务端不定义 observation 过期阈值或列表级新鲜度，不因 observation 年龄或首次 observation 缺失拒绝可读结果；调用者可以按场景解释数据年龄。
- 通过确定性自动化和 Windows `v3.0.1-b4` 真实进程证明事件注册、字段复制、最终一致窗口和生命周期行为。

## 非目标

- 不承诺请求时点快照、跨玩家原子一致性或每次 `Values.ToArray()` 对应同一游戏 tick。
- 不承诺生命、等级或 ping 的延迟低于客户端 `PlayerDataFile` 上传周期。
- 不监听或 Harmony patch `NetPackagePlayerStats`、`NetPackageEntityStatChanged` 或其他高频网络包。
- 不增加定时对账、后台线程、周期扫描、请求时主线程回退、成功响应 TTL 缓存或 stale-while-revalidate。
- 不在每次 `SavePlayerData` 后扫描全部连接或比较字典数量；一次保存事件只更新一个玩家。
- 不使用 `Dictionary + ReaderWriterLockSlim`、SQLite、SSE、日志窗口或文件作为在线玩家投影存储。
- 不把未完成首次有效 `SavePlayerData` 上传的连接补入成功列表；此类玩家在第一次有效上传前暂不出现，membership 只用于身份匹配和断开删除。
- 不改变 Admin 10 秒轮询、玩家页面结构、Owner 授权、踢人确认或审计流程。
- 不建立通用 Projection、Repository、Event Bus、缓存框架或游戏事件注册表。

## 一致性合同

在线玩家接口从本规格开始表示：

> 当前 7DTD 进程中，已完成至少一次有效 `SavePlayerData` 上传且尚未观察到断开的玩家最终一致投影。

允许的窗口误差包括：

- 新连接在首次有效上传前暂不出现；默认窗口通常不超过约 30 秒，但产品不把游戏内部计时器当作严格 SLA。
- 同一响应中的不同玩家可以来自不同上传时刻。
- `ConcurrentDictionary.Values.ToArray()` 枚举期间发生的 Upsert 或 Remove 可以反映在本次或下一次响应中。
- Ping、等级和生命值是最近一次有效上传事件时复制的值；服务端实体可能已经通过其他网络包获得更新。
- 刚断开的玩家可以在断开回调与并发 API 枚举竞争期间短暂出现在一个响应中。
- 已收到 `PlayerJoinedGame`、但尚无有效 observation 的玩家不进入 `players`；其他已有 observation 的玩家仍可成功返回。

不允许的永久漂移包括：

- 已处理成功的 `PlayerDisconnected` 之后仍保留同一 entity id 与身份的条目。
- 运行时停止或世界关闭后保留上一轮世界的条目。
- 无效或字段复制失败的事件覆盖上一条有效 observation。
- 服务端因 observation 年龄拒绝或隐藏仍匹配当前 membership 的有效玩家。
- 服务端为尚无 observation 的 membership 伪造玩家状态或观察时间。

同一主平台身份快速断开并重连时，本设计依赖 `ModEvents` 在游戏主线程按旧会话断开、新会话上传的顺序分发。entity id 与主身份都相同时，产品没有额外 session generation 可区分迟到的旧断开事件；真实进程验证必须覆盖一次同身份重连。若观察到旧断开回调晚于新 Save，本规格必须修订为显式会话世代，不能把该竞态归为允许窗口。

首个实现不加入周期对账。若真实进程证据出现永久漂移，先记录事件、线程、订阅或生命周期根因，再单独设计自愈机制；不能预先用定时扫描掩盖事件处理缺陷。

## 数据模型与命名

Application 的公开内部契约保持不变：

```text
IOnlinePlayerQuery.GetOnlineAsync
  -> OnlinePlayersSnapshot
       -> IReadOnlyList<PlayerSnapshot>
            -> ObservedAtUtc
```

SevenDays Adapter 新增内部不可变值：

```csharp
internal sealed class OnlinePlayerObservation
{
    public OnlinePlayerObservation(
        PlayerSnapshot player,
        DateTimeOffset observedAtUtc)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
        ObservedAtUtc = observedAtUtc;
    }

    public PlayerSnapshot Player { get; }
    public DateTimeOffset ObservedAtUtc { get; }
}
```

- `PlayerSnapshot` 表示一次已经脱离游戏类型的单玩家字段复制，并携带该次复制的 `ObservedAtUtc`。
- `OnlinePlayerObservation` 表示在某个 UTC 时刻观察到该玩家在线并完成一次有效上传。
- `OnlinePlayersSnapshot` 表示一次 API 查询从当前投影枚举得到的只读集合。
- 不采用 `ImmutablePlayerProjection`：`Immutable` 是实现性质，且名称没有表达在线玩家或观察时间。
- 不采用单数 `OnlinePlayerSnapshot`：它与现有复数 `OnlinePlayersSnapshot` 只差一个 `s`，且不能准确表达不同玩家观察时间。

投影容器固定为：

```csharp
ConcurrentDictionary<int, OnlinePlayerObservation>
```

键使用当前在线会话的 `entityId`；值同时保存主平台身份。entity id 仍不是持久身份，断开删除必须核对主平台身份，并通过 `ConcurrentDictionary` 显式实现的 `ICollection<KeyValuePair<TKey, TValue>>.Remove` 做键值条件删除，避免迟到回调误删相同 entity id 的新 observation。

服务还维护同样以 entity id 为键的内部 `OnlinePlayerMembership`，只保存主平台身份。`PlayerJoinedGame` 在 `updateGate` 内重查 accepting，Upsert membership，并原子删除同 entity id 下身份不匹配的旧 observation；`SavePlayerData` 在 join 事件缺失时也补建 membership，`PlayerDisconnected` 对两个字典分别做身份匹配的条件删除。membership 不进入 Application 或 HTTP 响应，也不从 `ConnectionManager` 扫描重建。

## SavePlayerData Upsert

```text
ModEvents.SavePlayerData
  -> validate ClientInfo and PlayerDataFile
  -> require client.entityId >= 0
  -> require client.entityId == playerDataFile.id
  -> copy approved strings and integers synchronously
  -> create PlayerSnapshot
  -> create OnlinePlayerObservation(observedAtUtc)
  -> ConcurrentDictionary.AddOrUpdate(entityId, observation)
```

字段来源固定为：

| 投影字段 | 事件时来源 | 规则 |
|---|---|---|
| `EntityId` | `ClientInfo.entityId` | 必须非负并等于 `PlayerDataFile.id` |
| `Name` | `PlayerDataFile.metadata.Name` | 非空；不回退到可变 `ClientInfo.playerName` |
| 主平台身份 | `ClientInfo.PlatformId.CombinedString` / `PlatformIdentifierString` | 两项都必须非空 |
| 跨平台身份 | `ClientInfo.CrossplatformId` | 整体可空；存在时两项都必须有效 |
| `Ping` | `ClientInfo.ping` | 复制事件发生时整数值 |
| `Level` | `PlayerDataFile.metadata.Level` | 复制本次上传 metadata |
| `Health` | `(int)PlayerDataFile.ecd.stats.Health.Value` | `ecd`、`stats`、`Health` 必须可用；保持现有整数响应 |
| `ObservedAtUtc` | 注入的 UTC clock | 在同步字段复制完成后、Upsert 前捕获 |

任一必填字段缺失、转换失败或身份不一致时：

- 本次事件不写入字典；
- 已有的上一条有效 observation 保持不变；
- 回调捕获异常并记录不含玩家名、身份或游戏对象文本的诊断与失败计数；
- 异常不得逃逸到 `ModEvents` 分发器，也不得影响其他 Mod handler。

第一版只保留服务内部最小计数：成功 Upsert、无效事件拒绝、字段复制失败和条件删除失败。除停止摘要外不增加公开统计类型、HTTP 指标或配置。

Save 回调可以在窄锁外复制游戏字段，但 membership 与 observation 的最终 `AddOrUpdate` 必须在同一个私有 `updateGate` 临界区内成对提交，并与 Stop 的禁写和 Clear 线性化：回调进入 gate 后再次检查 accepting，只有仍接收时才提交；Stop 在同一 gate 内把 accepting 设为 false 并清空两个字典。这样查询不会复制到 Save 的半提交状态，已经开始复制但尚未提交的回调在 Stop 后也只能被拒绝，不能在 Clear 后重新写入上一世界状态。`updateGate` 不包围游戏字段复制、事件注销、日志或内层运行时停止。

## 断开与停止

`PlayerDisconnected` 回调在 `updateGate` 内执行：

1. 验证 `ClientInfo`、非负 entity id 和主平台身份；
2. 分别读取当前 membership 和 observation；
3. 比较各自保存的主平台身份与断开客户端身份；
4. 把字典转换为对应的 `ICollection<KeyValuePair<TKey, TValue>>`，分别调用 `Remove` 条件删除同一键和值；
5. 身份不匹配时不删除该值并记录内部计数。`updateGate` 使 Save 成对替换与断开成对删除互斥，但仍保留条件删除作为会话身份防线。

运行时生命周期固定为：

```text
Start
  -> subscribe PlayerJoinedGame
  -> subscribe SavePlayerData
  -> subscribe PlayerDisconnected
  -> start existing ConsoleLogRuntime / ModHost

Stop
  -> stop existing ConsoleLogRuntime / ModHost
  -> unregister PlayerDisconnected
  -> unregister SavePlayerData
  -> unregister PlayerJoinedGame
  -> acquire updateGate
       -> reject projection commits
       -> clear observations and memberships
     release updateGate
```

- 三个事件必须在 OWIN 开始接受请求前完成注册，顺序为 `PlayerJoinedGame`、`SavePlayerData`、`PlayerDisconnected`。
- 任一订阅失败时按逆序注销先前订阅并保持原始异常。
- `OnlinePlayerProjectionRuntime.Start` 在投影 Start 失败时不调用尚未启动的内层运行时；内层 Start 失败时调用投影 Stop 作为本层补偿并保持原始异常。外层 `SevenDaysGameLifecycleAdapter` 仍可幂等调用整个 runtime Stop，不依赖它完成本层清理。
- 当前生产 `ModHost.Start` 会捕获 OWIN 启动异常并进入 `Faulted`，不会向 wrapper 抛出；这种情况下没有 HTTP 消费者，投影订阅保留到既有关服 Stop 清理。本切片不改变 `ModHost` 的失败传播合同。
- Stop 幂等；先停止内层 Host，使 readiness 离开 Ready 并关闭 OWIN，再注销和清空投影，避免清空后仍开放的请求把关服状态返回为 200 空列表。内层停止或单个注销失败不能阻止其余注销和清空，最终聚合失败。
- `WorldShuttingDown` 与 `GameShutdown` 已通过 `SevenDaysGameLifecycleAdapter` 调用同一个幂等运行时 Stop，因此投影不重复订阅这两个事件。
- 不把玩家事件加入只表达 Mod 启停的 `ISevenDaysLifecycleEvents`，也不把玩家职责塞入 `ConsoleLogRuntime`。
- 使用专属 `OnlinePlayerProjectionRuntime` 装饰现有 `IModRuntime`，只负责编排投影 Start/Stop 与内层运行时顺序；这是同一变更中已有两个真实运行时组件后的最小组合边界，不扩展成通用 runtime registry。

## 查询与 HTTP 语义

`SevenDaysOnlinePlayerProjection` 同时实现现有 `IOnlinePlayerQuery`：

```text
GetOnlineAsync
  -> honor already-cancelled token
  -> copy memberships and observations under updateGate
  -> sort by Player.EntityId
  -> return matching PlayerSnapshot values with their ObservedAtUtc
```

- 查询不访问 `ConnectionManager`、`World`、`EntityPlayer`、`PlayerDataFile` 或主线程 Dispatcher。
- 查询不使用 single-flight，不返回 busy，也不等待后台刷新。
- 查询在窄 `updateGate` 内复制两个字典后立即释放；数组元素和 `PlayerSnapshot` 都不可变。锁内不执行排序、时间计算、日志、游戏访问或 HTTP 工作。
- 查询只返回主身份与当前 membership 相同的 observation；即使 entity id 被新身份复用，旧身份 observation 也不得在首次 Save 前进入成功列表。
- Query 不读取 UTC clock、不计算年龄，也不输出根级时间或 stale 标记。membership 为空或所有 membership 都缺少 observation 时返回 200 空数组；缺少 observation 的 membership 不阻止其他玩家返回。
- `GET /api/v1/players/online` 的 Owner-only 授权、game readiness 前置检查和玩家字段白名单保持不变；成功 JSON 根对象只含 `players`，每个玩家增加 `observedAtUtc`。
- 查询路径不产生 `online_player_projection_stale`、`online_player_query_busy`、`game_thread_timeout` 或 `online_player_snapshot_unavailable`。共享 Problem Details 与其他端点不变。
- Admin 可以保留对旧 503 code 的兼容映射，但当前后端不再从在线玩家查询产生这些 code；不要求本切片修改前端。

## 组件与文件边界

| 组件 | 职责 |
|---|---|
| `SevenDaysOnlinePlayerProjection` | 精确 ModEvents 订阅、同步字段复制、并发字典、条件删除、`IOnlinePlayerQuery` |
| `OnlinePlayerObservation` | 单玩家不可变值与观察时间 |
| `OnlinePlayerMembership` | 在线会话主身份，用于当前 observation 身份匹配和断开删除 |
| `OnlinePlayerProjectionRuntime` | 投影与现有 `IModRuntime` 的启动、停止和失败聚合顺序 |
| `PlayersController` | 保持授权、readiness、DTO 映射；删除已不可能的旧查询错误映射 |
| `PanelServiceProviderFactory` | 注册同一个 singleton 投影为具体类型和 `IOnlinePlayerQuery`，组合运行时 |

删除 `SevenDaysOnlinePlayerQuery`，不保留并行的请求时主线程实现、配置开关或回退路径。Application、Hosting、Web 与前端不引用投影内部类型。

## 测试策略

### 投影状态与查询

- `SavePlayerData` 的有效 observation 按 entity id Upsert；同一玩家后续事件整体替换旧值。
- 多玩家可以具有不同 `ObservedAtUtc`；查询按 entity id 排序并保留每个玩家自己的时间。
- 空投影返回 200 所需的 `{ players: [] }`。
- join 后没有 Save 时不显示该玩家，不因等待时间改变响应合同；断开后 membership 删除并返回剩余投影或空列表。
- 任意年龄的有效 observation 都原样返回其 `ObservedAtUtc`；服务端不应用年龄阈值。
- `Values.ToArray()` 后返回集合不受后续字典更新影响。
- 已取消 token 不返回投影。

### 字段复制和失败隔离

- entity id 不匹配、名称缺失、主身份缺失、health 结构缺失时拒绝本次 Upsert。
- 无效更新不覆盖已有有效 observation。
- 跨平台身份整体可空；存在但字段无效时拒绝。
- 回调字段复制异常不逃逸，后续有效事件仍可更新。
- 生产回调完成后，缓存值不包含任何游戏或 Unity 类型。

### 订阅和生命周期

- Start 按 PlayerJoinedGame、SavePlayerData、PlayerDisconnected 顺序订阅，全部成功后才启动内层运行时。
- 投影注册失败时逆序回滚已注册事件且不触碰内层；测试替身的内层 Start 抛出时停止投影并保持原异常；生产 `ModHost` 进入 `Faulted` 但不抛出时由后续整体 Stop 清理订阅。
- Stop 先停止内层 Host，再逆序注销，并在 `updateGate` 内禁写和清空；注销或内层停止失败时继续清理并聚合异常，重复 Stop 不重复注销。
- 用阻塞字段复制的确定性测试证明 Stop 清空后，在途 Save 不能重新 Upsert；用阻塞内层 Stop 的测试证明投影在 OWIN/readiness 停止前不会被清空。
- 用阻塞 Joined 提交的确定性测试证明 Stop 清空后，在途 Joined 不能重新建立 membership；用不同身份复用同一 entity id 的测试证明旧 observation 不会在新会话首次 Save 前返回。
- Dispose 后的测试事件不能更新投影。
- 四类内部计数分类正确；停止摘要只输出一次且不含玩家名、平台身份或游戏对象文本。

### Web、DI 与依赖规则

- 既有 Owner 空/多玩家 200、字段白名单、排序、readiness 和匿名认证测试继续通过。
- 删除 busy/timeout/unavailable 的在线玩家 Controller 测试；这些错误仍由拥有对应路径的其他端点测试。
- DI 证明具体投影、`IOnlinePlayerQuery` 和运行时使用同一 singleton 实例。
- 依赖规则证明不新增项目、包、Adapter-to-Adapter 引用、后台队列、定时器或 Harmony patch。

### 产品合同与失败语义

- `docs/PRD.md` 先明确在线玩家采用最终一致投影、首次上传前缺席和逐玩家观察时间。
- 为在线查询不再产生 projection stale、busy、主线程启动超时和快照基础设施不可用四个 503 code 建立明确验收；投影年龄不是服务端错误或根级状态，调用者只消费每个玩家的 `observedAtUtc`。

### 真实进程

- 启动后、玩家首次上传前允许列表为空；观察首个 `SavePlayerData` 后玩家在下一次查询出现。
- 至少等待两个上传周期，验证 observation 更新但 HTTP 查询不产生 `7DPanel.Players.Online` 主线程任务。
- 玩家断开后验证条目被移除；关服后确认停止摘要、事件注销、OWIN 停止和端口释放。
- 同一测试身份快速重连一次，验证旧断开不会删除新 observation；若失败则停止发布并回到 session generation 设计。
- 比较事件时 ping、metadata level、health 与 HTTP 响应，确认字段来源和允许的陈旧窗口。
- 不执行 Linux smoke，保留现有 Linux 兼容缺口。

## 文档影响

- 本变更改变在线列表的新鲜度、首次可见时间和稳定失败路径，是外部可观察合同变化；批准后实施必须先更新 `docs/PRD.md`，再修改代码。PRD 只定义产品语义与验收，不复制 ConcurrentDictionary、ModEvents 或字段映射实现。
- 实现并验证后更新 `docs/architecture.md` 的在线玩家数据来源、生命周期、并发和风险；删除“每次请求主线程快照”和在线查询 single-flight 的 Current 描述。
- 更新 `docs/test.md` 的在线玩家风险、自动化数量和真实进程证据；记录约 30 秒最终一致窗口和 `NetPackagePlayerStats` 无 ModEvent 的限制。
- 更新 `docs/architecture/backend-target-blueprint.md` 中仍把在线玩家查询描述为请求时主线程读取的批准目标，避免 Target 与新方向冲突。
- 更新 `backend/README.md` 的当前 API 数据语义；根 `README.md` 仅在当前实现摘要需要同步时修改。
- 不修改 `docs/design.md`、Admin Target 蓝图、前端 README 或 `CHANGELOG.md`。

## 批准检查点

批准本规格即确认：

- 接受 `SavePlayerData` 驱动、通常约 30 秒窗口的最终一致在线玩家投影；
- 首次有效上传前玩家可以暂不出现在列表；
- 接受同一响应内不同玩家观察时间不同，以及枚举期间更新落入本次或下一次响应；
- 服务端不定义 observation 过期阈值或列表级 Fresh/Stale；
- 不增加 PlayerStats Harmony patch、周期对账、请求时主线程回退或 TTL 缓存；
- 使用 `ConcurrentDictionary<int, OnlinePlayerObservation>`，回调中同步复制不可变产品值；
- 使用 `PlayerJoinedGame` membership 做身份匹配和断开删除，但不从该事件复制玩家状态；
- 名称和等级来自 `PlayerDataFile.metadata`，生命来自上传的 `ecd.stats.Health.Value`，身份和 ping 来自同次事件的 `ClientInfo`；
- 断开使用 entity id 与主平台身份条件删除，运行时停止时注销并清空；
- 根对象只返回 `players`，每个玩家携带自己的 `observedAtUtc`；
- 撤销在线查询不再可能产生的 projection stale、busy、主线程启动超时和快照基础设施不可用错误路径；
- 只有真实进程完成首次上传、周期更新、断开和关服验证后，才能把事件投影提升为 Current 架构事实。