---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-22-online-player-event-projection-design.md
last_updated: "2026-07-23"
---

# 在线玩家事件投影实施计划

> **面向智能体执行者：** 实施时必须使用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans`，逐任务执行并在每个任务后评审。以下步骤使用复选框跟踪。

**对应规格：** [在线玩家事件投影设计规格](../specs/2026-07-22-online-player-event-projection-design.md)

**目标：** 用 `PlayerJoinedGame`、`SavePlayerData` 和 `PlayerDisconnected` 驱动的进程内最终一致投影替换在线玩家请求时主线程快照，让 `GET /api/v1/players/online` 只枚举不可变产品值，同时保持成功 JSON、Owner 授权和 Admin 消费契约。

**架构：** singleton `SevenDaysOnlinePlayerProjection` 同时拥有精确 ModEvents 订阅、在线 membership、`ConcurrentDictionary<int, OnlinePlayerObservation>` 和现有 `IOnlinePlayerQuery` 实现；`OnlinePlayerProjectionRuntime` 在 OWIN 启动前注册投影，并在关服时先停止内层 Host、再注销和清空投影。每次 Save 只同步复制并替换一个玩家，不增加周期对账、主线程回退、PlayerStats patch 或通用投影框架。

**技术栈：** .NET Framework `4.8`、C# `11.0`、`System.Collections.Concurrent.ConcurrentDictionary`、Microsoft DI、Web API 2/Katana、xUnit v3、7DTD Dedicated Server `v3.0.1-b4` ModEvents。

## 全局约束

- 本计划只实施[在线玩家事件投影设计规格](../specs/2026-07-22-online-player-event-projection-design.md)，不重新设计 HTTP 成功字段、Admin 页面、玩家动作、认证或角色。
- 在线玩家投影接受约一个 `NetPackagePlayerData` 上传周期的最终一致窗口、不同玩家观察时间不同以及 `Values.ToArray()` 枚举期间的并发变化。
- 未完成首次有效 `SavePlayerData` 上传的连接不进入投影；不从请求线程或定时器补扫 `ConnectionManager`、`World` 或 `ClientInfo.latestPlayerData`。
- `PlayerJoinedGame` 只记录 entity id 和主身份；没有同身份 observation 的 membership 不进入结果，不从 join 事件复制或伪造玩家状态。
- 不实现 60 秒对账、TTL、后台刷新、SSE 玩家事件、`NetPackagePlayerStats` Harmony patch 或请求时主线程回退。
- 回调返回前同步复制全部字段；字典、Application、Web 和测试结果不得保存 `ClientInfo`、`PlayerDataFile`、`PlatformUserIdentifierAbs`、`EntityPlayer`、Unity 类型或游戏可变集合。
- 使用 `ConcurrentDictionary<int, OnlinePlayerObservation>`；每次 Upsert 替换整个不可变值，断开按 entity id 与主平台身份通过显式 `ICollection<KeyValuePair<TKey, TValue>>.Remove` 条件删除。
- 根对象只保留 `players`；每个玩家携带最后成功复制的 `observedAtUtc`。
- 服务端不定义 observation 年龄阈值或列表级 Fresh/Stale，不因 observation 年龄或首次 observation 缺失拒绝可读结果。
- 无效或字段复制失败的事件不得覆盖旧值，异常不得逃逸到 ModEvents 分发器；日志不得包含玩家名、平台身份或游戏对象文本。
- membership/observation 的 Save 成对提交、Disconnect 成对删除、查询复制与 Stop 禁写/Clear 必须由同一个私有 `updateGate` 线性化；不得让查询看到半提交状态，也不得让已开始复制的回调在 Stop 清空后重新 Upsert。
- `WorldShuttingDown` 和 `GameShutdown` 继续通过既有 `SevenDaysGameLifecycleAdapter -> IModRuntime.Stop` 关闭投影；不重复注册这两个事件。
- 每个生产行为必须先写一个因缺少该行为而失败的测试，观察正确 RED 后才实现最小生产代码。
- 导入、编译或测试发现错误不计为 RED；只允许补齐抛出 `NotImplementedException` 的最小可编译骨架，再观察行为断言失败。
- 本计划不授权 `git commit`、`git push`、`git reset` 或 `git revert`；任务检查点保持未提交，除非用户另行明确授权。

---

### 任务 1：先更新在线玩家产品合同

**文件：**

- 修改：`docs/PRD.md`
- 修改：`docs/architecture/backend-target-blueprint.md`

**接口：**

- `CAP-02` 明确在线玩家列表是客户端上传驱动的最终一致投影，首次有效上传前可以暂缺。
- `NFR-02` 明确允许的陈旧窗口不等于 Fresh 保证；事件链失效或超出批准窗口时不得把旧值显示为已确认新鲜。
- 明确在线端点撤销 `online_player_projection_stale`、`online_player_query_busy`、`game_thread_timeout`、`online_player_snapshot_unavailable`；成功响应只通过逐玩家 `observedAtUtc` 暴露数据年龄事实。

- [x] **步骤 1：写产品语义更新**

  在 `CAP-02` 的 Requirement/Verification 中加入：在线列表以最近一次有效玩家数据上传为准；新连接在首次上传前可以暂不出现；同一列表中的玩家可以来自不同上传时刻；每个已显示玩家返回自己的观察时间，由调用者解释年龄。不要在 PRD 写 `SavePlayerData`、`ConcurrentDictionary` 或 C# 类型。

- [x] **步骤 2：写状态诚实验收**

  在 `NFR-02` 加入 Given/When/Then：当投影 observation 超出批准窗口或事件链不可用时，面板不得把该状态显示为已确认新鲜。若当前前端无法表达该状态，停止实施并先修订 spec，不得继续删除旧失败路径。

- [x] **步骤 3：复核错误合同**

  明确四个在线查询 503 code 从在线列表端点撤销；投影年龄不是服务端错误或根级状态。`game_not_ready`、认证和授权失败保持不变，其他端点拥有的 `game_thread_timeout` 不受影响。

- [x] **步骤 4：验证 PRD 文档**

  ```powershell
  git diff --check -- docs/PRD.md
  Select-String -Path docs/PRD.md -Pattern 'SavePlayerData|ConcurrentDictionary|SevenDaysOnlinePlayerProjection'
  ```

  预期：格式检查通过，PRD 不包含实现标识。完成并由用户批准该产品合同后，才能执行后续代码任务。

- [x] **步骤 5：同步批准 Target**

  在 `docs/architecture/backend-target-blueprint.md` 把在线玩家读取方向更新为 `joined membership + uploaded observations -> event projection -> GET`，明确逐玩家观察时间、无服务端年龄门禁、无周期对账和无 PlayerStats patch。Target 必须继续声明不是当前实现证据。完成后再开始代码任务。

### 任务 2：建立不可变 observation 与并发投影查询

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/OnlinePlayerObservation.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/OnlinePlayerMembership.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/SevenDaysOnlinePlayerProjection.cs`
- 修改：`backend/src/Core/LSTY.SevenDPanel.Application/Players/OnlinePlayerQueryExceptions.cs`
- 重写测试：`backend/tests/LSTY.SevenDPanel.Tests/SevenDaysOnlinePlayerQueryTests.cs`

**接口：**

- 产出 internal `OnlinePlayerObservation(PlayerSnapshot player, DateTimeOffset observedAtUtc)`，只读属性为 `Player`、`ObservedAtUtc`。
- 产出 public sealed `SevenDaysOnlinePlayerProjection : IOnlinePlayerQuery, IDisposable`。
- internal 构造函数接收 UTC clock、三个订阅 delegate、字段复制 delegate 和安全日志 delegate；默认构造只组合生产实现，不暴露 Application/Hosting 接口。
- `GetOnlineAsync(CancellationToken)` 只读取字典，不访问游戏对象或 Dispatcher。
- `OnlinePlayersSnapshot` 只产出玩家集合；每个 `PlayerSnapshot` 产出自己的 `ObservedAtUtc`。

- [x] **步骤 1：写投影查询失败测试**

  将现有 `SevenDaysOnlinePlayerQueryTests` 改为投影行为测试，先覆盖无事件的纯查询：

  ```csharp
  [Fact]
  public async Task Query_sorts_observations_and_uses_the_oldest_observed_time()
  {
      var older = new DateTimeOffset(2026, 7, 22, 1, 0, 0, TimeSpan.Zero);
      var newer = older.AddSeconds(20);
      using var projection = CreateProjection(() => newer.AddSeconds(1));
      projection.UpsertForTest(CreateObservation(42, "Zed", newer));
      projection.UpsertForTest(CreateObservation(7, "Amy", older));

      var result = await projection.GetOnlineAsync(CancellationToken.None);

      Assert.Equal(new[] { older, newer }, result.Players.Select(player => player.ObservedAtUtc));
      Assert.Equal(new[] { 7, 42 }, result.Players.Select(player => player.EntityId));
  }
  ```

  另写：无 membership 的空投影返回空集合、已取消 token 返回取消、查询结果不受后续 Upsert 影响、同一 entity id 后续 observation 整体替换旧值、任意年龄 observation 原样返回时间、join 后无 Save 始终不产生占位玩家、不同身份复用同一 entity id 时旧 observation 不返回、断开后恢复空列表。测试 seam 不扩大生产 API。

- [x] **步骤 2：运行测试并确认正确 RED**

  ```powershell
  $referenceRoot = (Resolve-Path '7dtd-reference').Path
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj `
    --configuration Release `
    --filter FullyQualifiedName~SevenDaysOnlinePlayerQueryTests `
    /p:SevenDaysReferenceRoot=$referenceRoot
  ```

  预期：因 `SevenDaysOnlinePlayerProjection` 或 `OnlinePlayerObservation` 尚未实现而失败；补最小可编译骨架后，必须观察排序、最旧时间或替换行为断言失败。

- [x] **步骤 3：实现 observation 和最小并发查询**

  核心状态固定为：

  ```csharp
    private readonly ConcurrentDictionary<int, OnlinePlayerObservation> players =
      new ConcurrentDictionary<int, OnlinePlayerObservation>();
    private readonly ConcurrentDictionary<int, OnlinePlayerMembership> memberships =
      new ConcurrentDictionary<int, OnlinePlayerMembership>();
  private readonly Func<DateTimeOffset> utcClock;
  ```

  `GetOnlineAsync` 先执行 `cancellationToken.ThrowIfCancellationRequested()`，再复制 `players.Values.ToArray()`、按 `Player.EntityId` 排序，并使用：

  ```csharp
    return new OnlinePlayersSnapshot(
      observations.Select(observation => observation.Player));
  ```

  在 `updateGate` 内快速复制 membership 与 observation 数组后释放。按身份过滤并保留每个 `PlayerSnapshot.ObservedAtUtc`，不读取 query clock、不计算年龄；不清空字典，不使用 `Task.Run`、single-flight、Dispatcher 或游戏对象。

- [x] **步骤 4：重跑投影查询测试**

  执行步骤 2 的命令。预期：空列表、排序、最旧时间、取消、替换和返回集合稳定性全部通过。

- [x] **步骤 5：记录任务检查点**

  ```powershell
  git diff --check -- backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players backend/tests/LSTY.SevenDPanel.Tests/SevenDaysOnlinePlayerQueryTests.cs
  git status --short
  ```

  预期：只出现本任务文件和已批准的 spec/plan 文档，保持未提交。

### 任务 3：实现 SavePlayerData 复制、Upsert 和断开删除

**文件：**

- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/SevenDaysOnlinePlayerProjection.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/SevenDaysOnlinePlayerQueryTests.cs`

**接口：**

- production `CopyObservation(ClientInfo client, PlayerDataFile playerData, DateTimeOffset observedAtUtc): OnlinePlayerObservation`。
- Save callback 验证并调用 copy，然后 `AddOrUpdate(entityId, observation, (_, _) => observation)`。
- Disconnect callback 使用 entity id、主平台 identity 和 `ICollection<KeyValuePair<int, OnlinePlayerObservation>>.Remove` 条件删除。
- internal 测试构造函数用最小 delegate seam 驱动 Save/Disconnect，不新增公开事件接口。

- [x] **步骤 1：写单玩家事件投影失败测试**

  通过可控订阅 delegate 捕获 Save 与 Disconnect handler，覆盖：

  ```csharp
  [Fact]
  public async Task Save_upserts_one_immutable_observation_and_later_save_replaces_it()
  {
      var fixture = new ProjectionFixture();
      using var projection = fixture.CreateProjection();
      projection.Start();

      fixture.RaiseSave(CreateSource(entityId: 7, level: 10, health: 80));
      fixture.RaiseSave(CreateSource(entityId: 7, level: 11, health: 75));

      var player = Assert.Single((await projection.GetOnlineAsync(CancellationToken.None)).Players);
      Assert.Equal(7, player.EntityId);
      Assert.Equal(11, player.Level);
      Assert.Equal(75, player.Health);
      Assert.Equal(2, fixture.CopyCount);
  }
  ```

  另写：join 创建 membership 但不创建 observation，并删除同 entity id 下身份不匹配的旧 observation；Save 在漏 join 时补建 membership；不同玩家只更新自身；无效事件不覆盖旧 observation；null/负 entity id/id mismatch/空名称/缺主身份/缺 health 被拒绝；跨平台身份可空；复制异常不逃逸且下一事件仍成功；断开身份匹配删除 membership 与 observation、身份不匹配不删除、并发替换后的条件删除不误删新值。

- [x] **步骤 2：运行测试并确认 RED**

  执行任务 2 的定向命令。预期：因 Save/Disconnect 尚未更新字典或无效事件覆盖旧值而出现行为断言失败。

- [x] **步骤 3：实现生产字段复制**

  生产复制固定读取：

  ```csharp
  var player = new PlayerSnapshot(
      client.entityId,
      playerData.metadata.Name,
      CreatePlatformIdentity(client.PlatformId),
      CreateOptionalPlatformIdentity(client.CrossplatformId),
      client.ping,
      playerData.metadata.Level,
      checked((int)playerData.ecd.stats.Health.Value));
  return new OnlinePlayerObservation(player, observedAtUtc);
  ```

  在读取前显式验证 `client.entityId == playerData.id`、名称、身份和 health 对象链。回调只把计数和不含敏感数据的固定诊断交给 `log`；不得记录异常对象的游戏类型 `ToString()`。

- [x] **步骤 4：实现 Upsert 与条件删除**

  Save 回调捕获全部异常并保留旧值。游戏字段复制在窄锁外完成；最终 Upsert 进入私有 `updateGate` 后再次检查 accepting，只有仍接收时才提交。Disconnect 使用当前字典值的 identity 比较后执行：

  ```csharp
    ((ICollection<KeyValuePair<int, OnlinePlayerObservation>>)players).Remove(
      new KeyValuePair<int, OnlinePlayerObservation>(entityId, current));
  ```

  不按数量对账，不枚举游戏世界，不访问 `latestPlayerData`。

- [x] **步骤 5：重跑投影测试并转 GREEN**

  执行任务 2 的定向命令。预期：全部 Save、拒绝、失败隔离、条件删除和查询测试通过。

- [x] **步骤 6：执行源码边界检查**

  ```powershell
  rg "ClientInfo|PlayerDataFile|PlatformUserIdentifierAbs|EntityPlayer" `
    backend/src/Core backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web
  ```

  预期：没有因本任务新增的游戏类型泄漏；这些类型只出现在 SevenDays Adapter 的事件复制边界。

### 任务 4：实现精确事件订阅和幂等生命周期

**文件：**

- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/SevenDaysOnlinePlayerProjection.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/OnlinePlayerProjectionRuntime.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/SevenDaysOnlinePlayerQueryTests.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/OnlinePlayerProjectionRuntimeTests.cs`

**接口：**

- `SevenDaysOnlinePlayerProjection.Start()` 依次注册 `ModEvents.PlayerJoinedGame`、`ModEvents.SavePlayerData` 和 `ModEvents.PlayerDisconnected`；`Stop()` 拒绝新回调、逆序注销并清空。
- `OnlinePlayerProjectionRuntime : IModRuntime, IDisposable` 接收投影和内层 `IModRuntime`，只编排顺序。
- `MarkGameReady()` 只转发内层，不产生玩家 observation。

- [x] **步骤 1：写订阅生命周期失败测试**

  在投影测试中用记录型 subscribe delegate 验证：

  ```text
  Start: subscribe-joined -> subscribe-save -> subscribe-disconnect
  Stop: dispose-disconnect -> dispose-save -> dispose-joined -> updateGate(reject-and-clear)
  ```

  另覆盖第二或第三个订阅失败时逆序注销先前订阅并保留原异常、重复 Start 不重复注册、重复 Stop 不重复注销、Dispose 后事件不写入、注销失败仍尝试其余注销和清空，以及 Save 阻塞在复制阶段或 Joined 阻塞在提交阶段时 Stop 清空后不能重新写入。

- [x] **步骤 2：写组合运行时失败测试**

  在 `OnlinePlayerProjectionRuntimeTests` 覆盖：

  ```csharp
    Assert.Equal(
      new[] { "projection:start", "inner:start", "inner:ready", "inner:stop", "projection:stop" },
      trace);
  ```

  另覆盖投影 Start 失败不启动或停止内层、内层 Start 失败时停止投影并保留原异常、内层 Stop 失败仍停止并清空投影、重复 Stop 幂等并聚合失败。用可阻塞的内层 Stop 证明投影直到内层停止完成后才注销和清空。

- [x] **步骤 3：运行生命周期测试并确认 RED**

  ```powershell
  $referenceRoot = (Resolve-Path '7dtd-reference').Path
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj `
    --configuration Release `
    --filter "FullyQualifiedName~SevenDaysOnlinePlayerQueryTests|FullyQualifiedName~OnlinePlayerProjectionRuntimeTests" `
    /p:SevenDaysReferenceRoot=$referenceRoot
  ```

  预期：因订阅回滚、停止顺序或 runtime 类型尚未实现而失败。

- [x] **步骤 4：实现精确 ModEvents 订阅**

  默认生产订阅保存 `PlayerJoinedGame`、`SavePlayerData` 和 `PlayerDisconnected` 三个精确 delegate。Save delegate 为：

  ```csharp
  ModEvents.ModEventHandlerDelegate<ModEvents.SSavePlayerDataData> saveCallback =
      delegate(ref ModEvents.SSavePlayerDataData data)
      {
          HandleSave(data.ClientInfo, data.PlayerDataFile);
      };
  ```

  Joined 与 Disconnected 使用各自对应精确 delegate。订阅 token 幂等调用同一事件的 `UnregisterHandler`；注册失败 best-effort 清理，不引入通用事件注册表。

- [x] **步骤 5：实现 OnlinePlayerProjectionRuntime**

  Start 先启动投影再启动内层；投影 Start 失败时不触碰内层。测试替身内层 Start 抛出时 best-effort 停止投影但不遮蔽原异常；生产 `ModHost.Start` 的既有语义是失败后进入 Faulted 而不抛出，本切片不改变它，投影由后续 Stop 清理。Stop 用原子门禁确保幂等，先停止内层 Host，再停止投影；即使内层失败也继续注销、禁写和清空，最后聚合失败。MarkGameReady 只转发。

- [x] **步骤 6：重跑生命周期与既有日志运行时测试**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj `
    --configuration Release `
    --filter "FullyQualifiedName~SevenDaysOnlinePlayerQueryTests|FullyQualifiedName~OnlinePlayerProjectionRuntimeTests|FullyQualifiedName~ConsoleLogServiceTests|FullyQualifiedName~SevenDaysGameLifecycleAdapterTests" `
    /p:SevenDaysReferenceRoot=$referenceRoot
  ```

  预期：投影、组合运行时、日志顺序和既有 Mod 生命周期测试全部通过。

### 任务 5：切换 DI 和 Web 查询路径并删除旧主线程 Query

**文件：**

- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/PlayersController.cs`
- 删除：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/SevenDaysOnlinePlayerQuery.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyInjectionTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyRulesTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs`

**接口：**

- 同一 singleton `SevenDaysOnlinePlayerProjection` 暴露为 `IOnlinePlayerQuery` 并交给 `OnlinePlayerProjectionRuntime`。
- 最外层 `IModRuntime` 顺序为 `OnlinePlayerProjectionRuntime -> ConsoleLogRuntime -> ModHost`。
- `PlayersController.Get` 保持 readiness 和成功 DTO，删除在线查询不再产生的 busy/timeout/unavailable catch，并映射 stale 异常。

- [x] **步骤 1：写 DI 和依赖规则失败测试**

  在 `DependencyInjectionTests` 断言：

  ```csharp
  var projection = provider.GetRequiredService<SevenDaysOnlinePlayerProjection>();
  Assert.Same(projection, provider.GetRequiredService<IOnlinePlayerQuery>());
  ```

  并通过记录型运行时或反射现有 Provider 测试证明最终 `IModRuntime` 包含投影 wrapper。依赖规则要求删除 `SevenDaysOnlinePlayerQuery` 注册，不增加 package/project reference、Timer、Harmony patch 或通用 registry。

- [x] **步骤 2：更新 Web 回归测试并确认 RED**

  保留匿名 401、Owner 空/多玩家 200、camelCase 字段白名单、排序和 game-not-ready 503。删除仅注入 `OnlinePlayerProjectionStaleException`、`OnlinePlayerQueryBusyException`、`TimeoutException`、`OnlinePlayerSnapshotUnavailableException` 的在线列表测试；新增根对象只含 `players`、每个玩家返回 `observedAtUtc`、旧 observation 仍成功返回的测试。另由投影测试断言成功路径不依赖 Dispatcher，不在 Controller 测试内部窥探实现。

  运行：

  ```powershell
  $referenceRoot = (Resolve-Path '7dtd-reference').Path
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj `
    --configuration Release `
    --filter "FullyQualifiedName~DependencyInjectionTests|FullyQualifiedName~DependencyRulesTests|FullyQualifiedName~OwinWebHostTests" `
    /p:SevenDaysReferenceRoot=$referenceRoot
  ```

  正确 RED 必须是新 singleton/runtime 断言失败，不能只靠删除旧测试得到 GREEN。

- [x] **步骤 3：切换组合根**

  用同一实例注册：

  ```csharp
  services.AddSingleton(serviceProvider =>
      new SevenDaysOnlinePlayerProjection(log));
  services.AddSingleton<IOnlinePlayerQuery>(serviceProvider =>
      serviceProvider.GetRequiredService<SevenDaysOnlinePlayerProjection>());
  ```

  创建 `ConsoleLogRuntime` 后，再创建 `OnlinePlayerProjectionRuntime` 并把它映射为唯一 `IModRuntime`。不得让 Web、Application 或 Hosting 引用具体投影类型。

- [x] **步骤 4：简化 Controller 并删除旧 Query**

  删除在线列表 Get 中对以下异常的 catch：

  ```text
  OnlinePlayerQueryBusyException
  TimeoutException
  OnlinePlayerSnapshotUnavailableException
  ```

  `PlayersController.Get` 不捕获投影过期异常，把每个玩家的 `ObservedAtUtc` 映射到成功响应的 `observedAtUtc` 字段；根 DTO 不包含时间或 stale 字段。

  删除 `SevenDaysOnlinePlayerQuery.cs`，并确认没有生产引用或遗留 `7DPanel.Players.Online` Dispatcher operation name。Application 异常类型只有在全仓库没有其他消费者时才删除；先用 code usage/搜索确认，不能顺带删除玩家动作需要的异常。

- [x] **步骤 5：重跑纵向切片测试**

  执行步骤 2 的命令，再执行：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj `
    --configuration Release `
    --filter "FullyQualifiedName~OnlinePlayerQueryTests|FullyQualifiedName~SevenDaysOnlinePlayerQueryTests|FullyQualifiedName~OnlinePlayerProjectionRuntimeTests|FullyQualifiedName~GameThreadDispatcherTests" `
    /p:SevenDaysReferenceRoot=$referenceRoot
  ```

  预期：DI、依赖、Katana、Application、投影和 Dispatcher 其他消费者全部通过。

- [x] **步骤 6：检查旧路径已移除**

  ```powershell
  rg "SevenDaysOnlinePlayerQuery|7DPanel\.Players\.Online|online_player_query_busy|online_player_snapshot_unavailable" backend/src backend/tests
  ```

  预期：旧具体 Query、投影过期异常和在线端点专属错误路径无残留，`observedAtUtc` 有 Query、Controller 和 Admin 测试；旧 Application 异常类没有实际生产消费者时删除并更新测试。

### 任务 6：准备待提升文档

**文件：**

- 准备修改：`docs/architecture.md`
- 准备修改：`docs/test.md`
- 准备修改：`backend/README.md`
- 视 smoke 结果准备修改：`README.md`
- 更新：`docs/superpowers/plans/2026-07-22-online-player-event-projection.md`

**接口：**

- Target 已在任务 1 随产品合同同步；本任务只整理待 smoke 验证的 Current 修改清单，不把未验证事实写入权威 Current 文件。
- 不修改 PRD、UI 设计、Admin Target 或 CHANGELOG。

- [ ] **步骤 1：准备 Current 架构差异清单**

  记录 smoke 通过后需要在 `docs/architecture.md` 替换的事实：

  ```text
  GET -> main-thread snapshot -> independent single-flight
  ```

  目标结构为：

  ```text
  PlayerJoinedGame membership + SavePlayerData observations + PlayerDisconnected removal
    -> ConcurrentDictionary projection -> GET
  ```

  清单还包括约 30 秒最终一致窗口、首次上传前缺席、逐玩家 `observedAtUtc`、无服务端年龄门禁、无周期对账、无 PlayerStats patch 和停止清理顺序。不要在本步骤修改 `docs/architecture.md`。

- [ ] **步骤 2：准备测试证据清单**

  记录自动化结果和任务 7 必须补充的真实 ModEvents 证据。不要修改 `docs/test.md`，不要删除现有主线程真实 smoke 的历史证据，也不要把它改写成事件投影证据。

- [ ] **步骤 3：准备入口摘要差异**

  记录 `backend/README.md` 与根 README 在 smoke 通过后需要更新的当前实现摘要；本步骤不修改这些 Current/Entry 文档。

- [ ] **步骤 4：执行文档语义审查**

  逐项确认：

  - `docs/` 全部新增或修改文字为简体中文；
  - spec 仍是 Change Record，Current 文档只写实现证据；
  - plan 只链接一个 primary spec；
  - Target 蓝图不冒充当前实现；
  - 没有未决占位文本、机器路径、凭据、重复产品合同或失效命令。

- [ ] **步骤 5：执行 Markdown 机械检查**

  ```powershell
  git diff --check -- docs/superpowers docs/PRD.md docs/architecture/backend-target-blueprint.md
  $changedDocs = git diff --name-only -- '*.md'
  foreach ($document in $changedDocs) {
      Select-String -Path $document -Pattern 'TBD|TODO|C:\\Users\\|D:\\Projects\\' -CaseSensitive
  }
  ```

  预期：`git diff --check` 无输出；占位符和机器路径扫描无匹配，Current/README 尚未因未验证实现而变化。

### 任务 7：聚合验证、Windows smoke 与 Current 提升

**文件：**

- 更新：`docs/test.md`
- 更新：`docs/architecture.md`
- 更新：`backend/README.md`
- 视当前摘要需要更新：`README.md`
- 更新：`docs/superpowers/plans/2026-07-22-online-player-event-projection.md`

- [ ] **步骤 1：运行 Release Rebuild**

  ```powershell
  $referenceRoot = (Resolve-Path '7dtd-reference').Path
  dotnet restore backend/7DPanel.sln /p:SevenDaysReferenceRoot=$referenceRoot
  dotnet build backend/7DPanel.sln `
    --configuration Release `
    --no-restore `
    --target:Rebuild `
    /p:SevenDaysReferenceRoot=$referenceRoot
  ```

  预期：零警告、零错误。

- [ ] **步骤 2：运行后端全量测试**

  ```powershell
  dotnet test backend/7DPanel.sln `
    --configuration Release `
    --no-build `
    --no-restore `
    /p:SevenDaysReferenceRoot=$referenceRoot
  ```

  预期：全部测试通过，无跳过或未解释失败；记录实际测试数量。

- [ ] **步骤 3：发布并启动受控 Windows 测试服**

  按 `backend/scripts/README.md` 使用已有环境文件执行：

  ```bat
  backend\scripts\Publish-Mod.cmd
  backend\scripts\Start-Server.cmd
  backend\scripts\Test-HealthEndpoint.cmd
  ```

  只对隔离测试部署执行，不修改服主生产配置。保留发布清单、服务端日志和原始配置 hash。

- [ ] **步骤 4：验证首次上传与周期更新**

  连接一个受控测试玩家：

  1. 记录加入后、首次有效 Save 前的 `/api/v1/players/online`；允许为空。
  2. 从服务端日志或临时无敏感诊断确认一次 Save Upsert 后，再请求接口并验证玩家出现。
  3. 至少等待两个 30 秒上传周期，确认 observation 更新；接口字段白名单保持不变。
  4. 扫描日志，确认没有 `7DPanel.Players.Online` 主线程任务、ModEvent handler 异常、类型加载异常或玩家敏感身份日志。
  5. 比较事件时 level、health、ping 与响应，记录允许的窗口，不把不同采集时刻误报为失败。

- [ ] **步骤 5：验证断开与关服**

  玩家正常断开后轮询接口，确认匹配 observation 被删除。随后运行：

  ```bat
  backend\scripts\Stop-Server.cmd
  backend\scripts\Test-HealthEndpoint.cmd -ExpectUnavailable
  ```

  确认 OWIN 先停止，随后投影注销并清空、进程退出、端口释放，且配置与数据库按脚本契约保留。

  使用同一测试身份快速重连一次，确认旧断开不会删除新 observation；如果观察到该竞态，停止提升 Current，回到 spec 增加 session generation。

- [ ] **步骤 6：提升 Current 或保留现状**

  只有完成步骤 3 至 5 才在 `docs/architecture.md`、`docs/test.md`、`backend/README.md` 和必要的根 README 中把事件投影写为 Current，并记录真实证据。若缺少受控玩家或远程环境，Current 文档继续如实说明代码已切换但真实事件兼容未验证，不能用旧主线程查询 smoke 推导新事件投影通过，也不能宣称变更完成。

- [ ] **步骤 7：最终工作区审查**

  ```powershell
  git diff --check
  git status --short
  git diff --stat
  ```

  预期：只包含本规格、计划及其批准范围内的实现/文档文件；不包含 `7dtd-reference/` 修改、构建产物、凭据、环境文件或无关重构，保持未提交等待用户决定。