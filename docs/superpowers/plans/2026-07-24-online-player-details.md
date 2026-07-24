---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-24-online-player-details-design.md
last_updated: "2026-07-24"
---

# 在线玩家详情实施计划

> **面向智能体执行者：** 实施时必须使用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans`，逐任务执行并在每个任务后评审。以下步骤使用复选框跟踪。

**对应规格：** [在线玩家详情设计规格](../specs/2026-07-24-online-player-details-design.md)

**目标：** 将在线玩家事件投影和 Owner-only API 扩展为同次观察的固定 25 字段合同，并让 Admin 通过紧凑主表和只读详情抽屉安全查看完整身份、连接、位置与统计信息。

**架构：** Application 继续拥有产品不可变快照，并新增产品自有位置值和设备枚举；SevenDays Adapter 只在现有 `SavePlayerData` 回调中同步复制游戏值，Web Adapter 显式映射固定 DTO。Admin Players Feature 严格解析完整响应，以 `OnlinePlayersView` 的页面局部状态锁存详情目标和 unavailable，Table、List 与 Slideover 保持 props down/events up，现有 `selectedPlayer` 只保存固定踢出目标。

**技术栈：** .NET Framework `4.8`、C# `11.0`、ASP.NET Web API 2、Katana、xUnit v3、7DTD Dedicated Server `v3.0.1-b4`、Vue `3.5.40` Composition API、TypeScript `6.0.3`、Nuxt UI `4.10.0`、Vite `8.1.5`、Vitest `4.1.6`、Vue Test Utils、happy-dom、Playwright、pnpm `11.13.1`。

## 全局约束

- 实施前从当前文档提交创建永久 worktree `.worktrees/online-player-details` 和分支 `feat/online-player-details`；不得删除该 worktree 或触碰其他 worktree。任何改变 Git 历史的命令仍需用户明确授权；若目标路径或分支已存在，停止并报告，不自行复用、rebase、reset 或合并冲突。
- API 每个玩家固定返回 25 个 camelCase 字段；根对象只含 `players`，不增加详情端点、分页、离线历史、根级捕获时间或服务端 stale 字段。
- 一名玩家的 25 字段必须来自同一次成功 `SavePlayerData` observation，并共享一个 `observedAtUtc`；任一必填值无效时保留上一条完整 observation，不提交部分更新。
- 查询继续只读产品自有不可变值，不访问 `EntityPlayer`、`ConnectionManager`、`World`、`ClientInfo.latestPlayerData` 或其他游戏活对象，不投递游戏主线程任务。
- 不保存 `ClientInfo`、`PlayerDataFile`、`PlatformUserIdentifierAbs`、Unity `Vector3`、权限对象或游戏可变集合；不增加 progression 解析、`PlayerStats` patch、周期扫描、请求时回源或通用投影框架。
- `crossplatformIdentity`、`ip`、`compatibilityVersion` 和 `discordUserId` 是可空值；Discord `0` 映射 `null`，非零值使用 invariant 十进制字符串。
- `deviceType` 只允许 `linux | mac | windows | playStation | xbox | unknown`；未知游戏枚举值映射 `unknown`。
- `permissionLevel` 使用游戏现有 `GameManager.Instance.adminTools.Users.GetUserPermissionLevel(ClientInfo)` 结果；不增加 `isAdmin` 或为测试引入权限 Provider 抽象。原生身份、跨平台身份、组权限和默认 `1000` 四种路径必须进入受控真实进程验证矩阵。
- `health` 和 `maxHealth` 先拒绝非有限值，再沿用 C# `(int)` 向零截断；不推导或修正 `health <= maxHealth`。
- `position` 和浮点统计必须有限；累计分钟、距离和制作数量不得为负。API 保留浮点精度，Admin 只在展示时四舍五入坐标和距离。
- Admin 所有传输空值统一显示“未知”；分钟值转换为天、小时、分钟，坐标与距离使用当前语言数字格式显示整数。
- 详情选择键固定为 `{ entityId, platformIdentity.combinedId }`。成功刷新确认缺失或身份变化后锁存最后 observation 和 unavailable 到关闭；后续同身份重现也不自动恢复。
- 详情踢出只在现有授权允许、查询 `state === 'fresh'`、会话未失效且 unavailable 未锁存时启用。Stale、Offline、Forbidden、Session expired 或最近明确 `game_not_ready` 时不得从旧详情发起新踢出。
- 详情目标和现有踢出 `selectedPlayer` 必须分离；打开确认后，列表刷新、详情刷新、抽屉关闭或 unavailable 变化都不能替换固定动作目标。
- 前端使用 Vue 3 Composition API、`<script setup lang="ts">`、`shallowRef` 保存替换式不可变值、`computed` 派生能力、`watch` 处理成功快照副作用；不把详情或玩家快照放入 Pinia。
- `OnlinePlayerDetailsSlideover` 使用 Nuxt UI `USlideover` 的受控 `v-model:open`、`#body` 和 `#footer`；不安装新 npm/NuGet 依赖，不创建嵌套卡片或共享格式化包。
- 每个生产行为先写因缺少该行为而失败的测试，确认正确 RED 后再写最小实现。导入、编译或测试发现错误不算 RED；只允许先补齐抛出 `NotImplementedException`/`Error` 的最小可编译签名，再观察行为断言失败。
- 本计划不授权 `git commit`、`git push`、`git reset`、`git revert`、发布、启动真实 7DTD 或浏览器 smoke；执行这些边界前必须满足仓库门禁并获得适用授权。

---

### 任务 1：扩展 Application 不可变玩家合同

**文件：**

- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Players/PlayerPosition.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Players/PlayerDeviceType.cs`
- 修改：`backend/src/Core/LSTY.SevenDPanel.Application/Players/PlayerSnapshot.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/OnlinePlayerQueryTests.cs`

**接口：**

- 产出 `public readonly struct PlayerPosition`，构造函数 `PlayerPosition(float x, float y, float z)`，只读属性为 `X`、`Y`、`Z`；构造时拒绝任一非有限轴值。
- 产出 `public enum PlayerDeviceType { Linux, Mac, Windows, PlayStation, Xbox, Unknown }`。
- `PlayerSnapshot` 保留现有身份和观察时间属性，并新增 `DeviceType`、`Ip`、`CompatibilityVersion`、`DiscordUserId`、`PermissionLevel`、`Position`、`IsDead`、`MaxHealth`、`Score`、`ZombieKills`、`PlayerKills`、`Deaths`、`TotalTimePlayedMinutes`、`DistanceWalkedMeters`、`TotalItemsCrafted`、`LongestLifeMinutes`、`CurrentLifeMinutes`。
- `Ip`、`CompatibilityVersion`、`DiscordUserId` 与 `CrossplatformIdentity` 可空；`TotalItemsCrafted` 使用 `uint`，位置和分钟/距离使用 `float`，其余批准整数使用 `int`。

- [ ] **步骤 1：增加可编译的迁移骨架**

  先增加 `PlayerPosition`、`PlayerDeviceType` 和完整 25 参数 `PlayerSnapshot` 构造签名；新构造函数暂时抛出 `NotImplementedException`。为保持其他程序集可编译，暂时保留旧 8 字段构造重载并继续原行为；它只用于分阶段迁移，必须在任务 3 全部调用点改完后删除。这一步只建立可编译边界，不计为 RED。

- [ ] **步骤 2：写完整快照和位置值 RED 测试**

  在 `OnlinePlayerQueryTests.cs` 增加统一 `CreatePlayer()` 工厂，并先锁定完整值：

  ```csharp
  [Fact]
  public void PlayerSnapshot_preserves_the_complete_observation()
  {
      var observedAtUtc = new DateTimeOffset(2026, 7, 24, 9, 30, 0, TimeSpan.Zero);
      var player = CreatePlayer(observedAtUtc);

      Assert.Equal(PlayerDeviceType.Windows, player.DeviceType);
      Assert.Equal("192.0.2.10", player.Ip);
      Assert.Equal("V 3.0.1", player.CompatibilityVersion);
      Assert.Equal("18446744073709551615", player.DiscordUserId);
      Assert.Equal(1000, player.PermissionLevel);
      Assert.Equal(100.5f, player.Position.X);
      Assert.Equal(51f, player.Position.Y);
      Assert.Equal(200.25f, player.Position.Z);
      Assert.False(player.IsDead);
      Assert.Equal(93, player.Health);
      Assert.Equal(100, player.MaxHealth);
      Assert.Equal(827, player.Score);
      Assert.Equal(317, player.ZombieKills);
      Assert.Equal(2, player.PlayerKills);
      Assert.Equal(4, player.Deaths);
      Assert.Equal(4823.5f, player.TotalTimePlayedMinutes);
      Assert.Equal(127540.75f, player.DistanceWalkedMeters);
      Assert.Equal(2360u, player.TotalItemsCrafted);
      Assert.Equal(920.25f, player.LongestLifeMinutes);
      Assert.Equal(134.5f, player.CurrentLifeMinutes);
      Assert.Equal(observedAtUtc, player.ObservedAtUtc);
  }
  ```

  另写 `PlayerPosition` 拒绝 `NaN`、正/负 Infinity，以及 `PlayerSnapshot` 拒绝负 entity ID、空名称、空主身份、负累计值和非有限分钟/距离。可空四字段分别以 `null` 构造并断言保留；不要把空字符串自动变为合法值。

- [ ] **步骤 3：运行 Application 定向测试并确认行为 RED**

  ```powershell
  $referenceRoot = (Resolve-Path '7dtd-reference').Path
  $trxPath = 'backend/tests/LSTY.SevenDPanel.Tests/TestResults/plan/application-online-players.trx'
  Remove-Item $trxPath -ErrorAction SilentlyContinue
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj `
    --configuration Release `
    --filter "FullyQualifiedName~LSTY.SevenDPanel.Tests.OnlinePlayerQueryTests." `
    --logger "trx;LogFileName=application-online-players.trx" `
    --results-directory backend/tests/LSTY.SevenDPanel.Tests/TestResults/plan `
    /p:SevenDaysReferenceRoot=$referenceRoot
  if ($LASTEXITCODE -ne 0) { throw 'Application online-player tests failed.' }
  [xml]$trx = Get-Content $trxPath
  $counters = $trx.SelectSingleNode("//*[local-name()='Counters']")
  if ($null -eq $counters -or [int]($counters.GetAttribute('total')) -eq 0) { throw 'No Application online-player tests ran.' }
  ```

  预期：测试项目成功编译，正确 RED 是完整构造抛出 `NotImplementedException` 或字段/不变量断言失败；类型、属性或其他调用点编译失败不算 RED。

- [ ] **步骤 4：实现产品自有位置、设备和快照不变量**

  `PlayerPosition` 使用私有辅助判断 `float.IsNaN(value) || float.IsInfinity(value)`。`PlayerSnapshot` 构造函数参数按传输合同顺序排列：

  ```csharp
  public PlayerSnapshot(
      int entityId,
      string name,
      PlayerPlatformIdentity platformIdentity,
      PlayerPlatformIdentity? crossplatformIdentity,
      PlayerDeviceType deviceType,
      string? ip,
      int ping,
      string? compatibilityVersion,
      string? discordUserId,
      int permissionLevel,
      PlayerPosition position,
      bool isDead,
      int health,
      int maxHealth,
      int level,
      int score,
      int zombieKills,
      int playerKills,
      int deaths,
      float totalTimePlayedMinutes,
      float distanceWalkedMeters,
      uint totalItemsCrafted,
      float longestLifeMinutes,
      float currentLifeMinutes,
      DateTimeOffset observedAtUtc)
  ```

  必填字符串拒绝空白；三个可空字符串允许 `null`，非空时拒绝空白。累计浮点只接受有限非负值。快照只赋值，不计算展示文本、不读取游戏对象、不保存源集合。

- [ ] **步骤 5：更新 Application 测试调用并转 GREEN**

  在 `OnlinePlayerQueryTests.cs` 内只通过 `CreatePlayer()` 创建完整默认玩家，个别测试用参数覆盖观察时间、名称或身份。执行步骤 3 命令，预期 Application 测试全部通过，原集合复制和 Use Case 转发行为保持不变。其他程序集暂时通过旧构造重载编译；任务 2、3 迁移完成后必须删除该重载。

- [ ] **步骤 6：记录 Application 检查点**

  ```powershell
  git diff --check -- backend/src/Core/LSTY.SevenDPanel.Application backend/tests/LSTY.SevenDPanel.Tests/OnlinePlayerQueryTests.cs
  git status --short
  ```

  预期：只出现本任务源文件、测试和已批准文档；保持未暂存、未提交。

### 任务 2：在 SavePlayerData 回调复制完整游戏 observation

**文件：**

- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/SevenDaysOnlinePlayerProjection.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/SevenDaysOnlinePlayerQueryTests.cs`

**接口：**

- 生产 Save 订阅把原始 `ClientInfo?`、`PlayerDataFile?` 同步传给实例级 `HandleSave`，不得在调用 `HandleSave` 前执行字段复制。
- internal 测试构造函数把 Save 订阅改为 `Func<Action<ClientInfo?, PlayerDataFile?>, IDisposable>`，并接收 `Func<ClientInfo?, PlayerDataFile?, OnlinePlayerObservation>` 复制 delegate；这只是现有外部事件边界的 internal seam，不新增公开接口或 Provider。
- 默认生产构造将复制 delegate 绑定到 `CopyObservation(ClientInfo?, PlayerDataFile?, Func<DateTimeOffset>)`。该函数先复制并验证全部游戏值，最后调用 UTC clock 一次，再用同一时间构造 `PlayerSnapshot` 和 `OnlinePlayerObservation`。
- 新增私有 `NormalizeOptionalString(string?): string?` 和有限/非负数值校验辅助；新增由生产路径直接调用的 internal static `CopyNullableIp(Func<string?> readIp): string?`、`TruncateFiniteToInt(float value, string fieldName): int`、`FormatDiscordUserId(ulong): string?`、`MapDeviceType(ClientInfo.EDeviceType): PlayerDeviceType` 供确定性边界测试复用。
- `CopyNullableIp(() => client.ip)` 是唯一捕获单字段 getter 异常并返回 `null` 的路径；它同时把空白结果归一化为 `null`。权限、身份、位置、生命和统计复制失败拒绝整次 observation。

- [ ] **步骤 1：增加 Save 边界和映射 helper 的可编译骨架**

  先把 internal Save 订阅签名改为 `Func<Action<ClientInfo?, PlayerDataFile?>, IDisposable>`，增加实例级 `HandleSave(ClientInfo?, PlayerDataFile?)`、clock 形态的生产 `CopyObservation` 和四个 internal static helper 签名。临时实现统一抛 `NotImplementedException`；只调整现有 `ProjectionFixture` 到新签名并执行一次构建，确认解决方案可编译。这一步不计 RED，也不能先实现复制行为。

- [ ] **步骤 2：把投影夹具升级为完整不可变 observation**

  更新 `CreateObservation(...)`，让现有 Save/替换/身份/停止测试构造 25 字段 `PlayerSnapshot`。新增一次 Save 的完整字段断言，并保留旧结果稳定性：

  ```csharp
  [Fact]
  public async Task Save_replaces_one_complete_observation_without_mutating_prior_results()
  {
      var fixture = new ProjectionFixture();
      using var projection = fixture.CreateProjection();
      projection.Start();
      fixture.RaiseSave(CreateObservation(7, "Before", fixture.UtcNow, score: 10));
      var before = await projection.GetOnlineAsync(CancellationToken.None);

      fixture.RaiseSave(CreateObservation(7, "After", fixture.UtcNow.AddSeconds(1), score: 827));
      var after = await projection.GetOnlineAsync(CancellationToken.None);

      Assert.Equal(10, Assert.Single(before.Players).Score);
      Assert.Equal(827, Assert.Single(after.Players).Score);
      Assert.Equal(fixture.UtcNow.AddSeconds(1), after.Players[0].ObservedAtUtc);
  }
  ```

  同时直接测试纯映射辅助的设备六值、Discord `0`/`ulong.MaxValue`，并通过完整 observation 断言生命 `93.9f -> 93` 与 `-0.9f -> 0`、位置三轴、死亡状态、四项战斗计数、三个分钟字段、距离和制作数量。

- [ ] **步骤 3：写生产回调异常边界和映射 RED 测试**

  `ProjectionFixture` 捕获原始双参数 Save handler；测试调用 `RaiseSave(null, null)`，由注入复制 delegate 返回完整 observation 或抛出异常。必须证明复制异常不逃出回调、旧 observation 不变、只记录固定脱敏日志，下一次有效 Save 仍成功。完整 observation 测试必须断言 `PlayerSnapshot.ObservedAtUtc` 与 `OnlinePlayerObservation.ObservedAtUtc` 相等。

  直接测试生产会调用的 helper：`TruncateFiniteToInt(93.9f) == 93`、`TruncateFiniteToInt(-0.9f) == 0`，并拒绝 `NaN`/Infinity；`CopyNullableIp` 分别覆盖正常值、空白值和 getter 抛异常。不要直接 `new ClientInfo()`：其公开构造函数访问全局 `ConnectionManager.Instance`，普通 net48 单元测试不能把该路径伪装成真实运行时。“全部字段验证后才调用 UTC clock”的顺序由步骤 6 对生产 `CopyObservation` 的源码边界检查锁定，真实进程只证明时间随新 Save 更新。`PlayerDataFile`/`ClientInfo` 精确成员来源通过编译边界、只读参考源码复核和任务 6 真实进程验证。不得为此增加网络栈替身、权限 Provider 或 source DTO。

- [ ] **步骤 4：运行 SevenDays 定向测试并确认行为 RED**

  ```powershell
  $referenceRoot = (Resolve-Path '7dtd-reference').Path
  $trxPath = 'backend/tests/LSTY.SevenDPanel.Tests/TestResults/plan/sevendays-online-players.trx'
  Remove-Item $trxPath -ErrorAction SilentlyContinue
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj `
    --configuration Release `
    --filter "FullyQualifiedName~LSTY.SevenDPanel.Tests.SevenDaysOnlinePlayerQueryTests." `
    --logger "trx;LogFileName=sevendays-online-players.trx" `
    --results-directory backend/tests/LSTY.SevenDPanel.Tests/TestResults/plan `
    /p:SevenDaysReferenceRoot=$referenceRoot
  if ($LASTEXITCODE -ne 0) { throw 'SevenDays online-player tests failed.' }
  [xml]$trx = Get-Content $trxPath
  $counters = $trx.SelectSingleNode("//*[local-name()='Counters']")
  if ($null -eq $counters -or [int]($counters.GetAttribute('total')) -eq 0) { throw 'No SevenDays online-player tests ran.' }
  ```

  正确 RED 必须是原始回调复制异常逃逸、旧值被覆盖、snapshot/wrapper 时间不一致，或新字段/设备/Discord/截断断言失败；不能只因旧 `PlayerSnapshot` 构造调用未更新而停止。

- [ ] **步骤 5：实现同次游戏字段复制**

  `SubscribeSave` 只同步转发原始事件值：

  ```csharp
  delegate(ref ModEvents.SSavePlayerDataData data)
  {
      handler(data.ClientInfo, data.PlayerDataFile);
  }
  ```

    `HandleSave` 在同一个 `try` 中执行 `copyObservation(client, playerData)` 和 `UpsertForTest`，任何复制或提交前验证异常都写固定日志且不逃出 ModEvents。生产 `CopyObservation` 先把全部源值读入局部变量并完成验证：

  ```csharp
    var entityId = RequireMatchingEntityId(client, playerData);
    var name = RequirePlayerName(playerData.metadata.Name);
    var platformIdentity = CreateIdentity(client.PlatformId);
    var crossplatformIdentity = CreateOptionalIdentity(client.CrossplatformId);
    var deviceType = MapDeviceType(client.device);
    var ip = CopyNullableIp(() => client.ip);
    var ping = client.ping;
    var compatibilityVersion = NormalizeOptionalString(client.compatibilityVersion);
    var discordUserId = FormatDiscordUserId(client.DiscordUserId);
    var permissionLevel = GameManager.Instance.adminTools.Users.GetUserPermissionLevel(client);
    var position = new PlayerPosition(
      playerData.ecd.pos.x,
      playerData.ecd.pos.y,
      playerData.ecd.pos.z);
    var isDead = playerData.bDead;
    var health = TruncateFiniteToInt(playerData.ecd.stats.Health.Value, "health");
    var maxHealth = TruncateFiniteToInt(
      playerData.ecd.stats.Health.ModifiedMax,
      "max health");
    var level = playerData.metadata.Level;
    var score = playerData.score;
    var zombieKills = playerData.zombieKills;
    var playerKills = playerData.playerKills;
    var deaths = playerData.deaths;
    var totalTimePlayedMinutes = RequireNonNegativeFinite(
      playerData.totalTimePlayed,
      "total time played");
    var distanceWalkedMeters = RequireNonNegativeFinite(
      playerData.distanceWalked,
      "distance walked");
    var totalItemsCrafted = playerData.totalItemsCrafted;
    var longestLifeMinutes = RequireNonNegativeFinite(
      playerData.longestLife,
      "longest life");
    var currentLifeMinutes = RequireNonNegativeFinite(
      playerData.currentLife,
      "current life");
    var observedAtUtc = utcClock();

  var player = new PlayerSnapshot(
      entityId,
      name,
      platformIdentity,
      crossplatformIdentity,
      deviceType,
      ip,
      ping,
      compatibilityVersion,
      discordUserId,
      permissionLevel,
      position,
      isDead,
      health,
      maxHealth,
      level,
      score,
      zombieKills,
      playerKills,
      deaths,
      totalTimePlayedMinutes,
      distanceWalkedMeters,
      totalItemsCrafted,
      longestLifeMinutes,
      currentLifeMinutes,
      observedAtUtc);
    return new OnlinePlayerObservation(player, observedAtUtc);
  ```

    `observedAtUtc` 之后不得再读取 `client`、`playerData`、权限或其他游戏对象；两个构造函数只能消费局部产品值。`GameManager.Instance`、`adminTools` 或 `Users` 不可用时拒绝整次 observation。若实际 `Vector3` 成员或 metadata/stats 可见性与固定参考不符，停止并修订规格；不得从 `EntityPlayer`、`latestPlayerData` 或请求线程补值。

- [ ] **步骤 6：重跑投影测试和游戏类型泄漏检查**

  执行步骤 4 命令，然后执行：

  ```powershell
  rg "ClientInfo|PlayerDataFile|PlatformUserIdentifierAbs|EntityPlayer|UnityEngine" `
    backend/src/Core backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web
  ```

  预期：投影测试全部通过；游戏类型没有新增到 Application 或 Web Adapter。已有无关匹配必须逐项说明，不能删除其他边界。

  随后复读生产 `CopyObservation`，确认 `utcClock()` 在所有必填字段、权限、位置、生命和统计复制及验证之后恰好出现一次，并且随后只用于构造同一 `PlayerSnapshot` 与 `OnlinePlayerObservation`；若顺序不满足，任务不得完成。

### 任务 3：显式映射 Web 25 字段合同

**文件：**

- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/PlayersController.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs`

**接口：**

- `OnlinePlayerPositionResponse(float x, float y, float z)` 只读输出 `X`、`Y`、`Z`。
- `OnlinePlayerResponse` 构造函数和属性按批准 25 字段顺序显式列出；`DeviceType` 输出固定 camelCase 枚举字符串，`ObservedAtUtc` 保持 invariant round-trip `O` 格式。
- `PlayersController.ToResponse(PlayerSnapshot)` 逐属性映射，不直接序列化 Application 快照，不改变 Owner-only、readiness 或 entity ID 排序。

- [ ] **步骤 1：增加完整 DTO 可编译骨架**

  先增加 `OnlinePlayerPositionResponse` 和完整 `OnlinePlayerResponse` 构造/属性签名，未实现映射暂时抛出 `NotImplementedException`。保留旧 DTO 构造重载仅用于让现有 Katana 测试编译；这一步不计 RED，步骤 5 转 GREEN 前必须迁移所有调用并删除旧重载和任务 1 的旧 `PlayerSnapshot` 重载。

- [ ] **步骤 2：把 Katana JSON 白名单测试改为固定 25 字段**

  更新 `Owner_with_multiple_players_returns_camel_case_fields_and_sorted_results` 的玩家工厂和精确字段集合：

  ```csharp
  var expectedFields = new[]
  {
      "compatibilityVersion", "crossplatformIdentity", "currentLifeMinutes",
      "deaths", "deviceType", "discordUserId", "distanceWalkedMeters",
      "entityId", "health", "ip", "isDead", "level", "longestLifeMinutes",
      "maxHealth", "name", "observedAtUtc", "permissionLevel", "ping",
      "platformIdentity", "playerKills", "position", "score",
      "totalItemsCrafted", "totalTimePlayedMinutes", "zombieKills"
  };
  ```

  断言根对象仍只有 `players`、两个玩家仍按 entity ID 排序、`position` 只含 `x/y/z`、设备枚举、四个 nullable 字段、Discord 大整数字符串、权限、整数生命和带单位字段值。保留匿名 401、Owner 空列表、game-not-ready 不调用 Query 和旧 observation 原时间测试。

- [ ] **步骤 3：运行 Katana 测试并确认行为 RED**

  ```powershell
  $referenceRoot = (Resolve-Path '7dtd-reference').Path
  $trxPath = 'backend/tests/LSTY.SevenDPanel.Tests/TestResults/plan/owin-web-host.trx'
  Remove-Item $trxPath -ErrorAction SilentlyContinue
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj `
    --configuration Release `
    --filter "FullyQualifiedName~LSTY.SevenDPanel.Tests.OwinWebHostTests." `
    --logger "trx;LogFileName=owin-web-host.trx" `
    --results-directory backend/tests/LSTY.SevenDPanel.Tests/TestResults/plan `
    /p:SevenDaysReferenceRoot=$referenceRoot
  if ($LASTEXITCODE -ne 0) { throw 'OWIN Web Host tests failed.' }
  [xml]$trx = Get-Content $trxPath
  $counters = $trx.SelectSingleNode("//*[local-name()='Counters']")
  if ($null -eq $counters -or [int]($counters.GetAttribute('total')) -eq 0) { throw 'No OWIN Web Host tests ran.' }
  ```

  预期：输出明确显示选择并执行了 `OwinWebHostTests`，测试数大于 0。正确 RED 是完整 DTO 抛出 `NotImplementedException`、玩家对象仍只有旧 8 字段或位置/空值/设备映射不符；零测试不得视为 GREEN。

- [ ] **步骤 4：实现位置 DTO、设备映射和完整响应 DTO**

  增加穷尽 switch：

  ```csharp
  private static string ToDeviceType(PlayerDeviceType deviceType)
  {
      switch (deviceType)
      {
          case PlayerDeviceType.Linux: return "linux";
          case PlayerDeviceType.Mac: return "mac";
          case PlayerDeviceType.Windows: return "windows";
          case PlayerDeviceType.PlayStation: return "playStation";
          case PlayerDeviceType.Xbox: return "xbox";
          default: return "unknown";
      }
  }
  ```

  `ToResponse` 构造 `OnlinePlayerPositionResponse` 并逐项传入全部值。DTO 不归一化、不舍入、不推导 `isAdmin`，不捕获投影异常。

- [ ] **步骤 5：迁移构造调用、删除临时重载并转 GREEN**

  更新 `SevenDaysOnlinePlayerQueryTests.cs`、`OwinWebHostTests.cs` 和生产投影的全部 `PlayerSnapshot` 调用，删除任务 1 的旧 8 字段重载和本任务旧 DTO 重载。先执行步骤 3 命令，再执行：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj `
    --configuration Release `
    --filter "FullyQualifiedName~OwinWebHostTests|FullyQualifiedName~ApiProblemDetailsTests" `
    /p:SevenDaysReferenceRoot=$referenceRoot
  ```

  预期：25 字段 JSON、Owner/readiness、OpenAPI 和统一错误边界全部通过；OpenAPI 仍只有既有在线列表路由。

- [ ] **步骤 6：记录后端纵向检查点**

  ```powershell
  git diff --check -- backend
  git status --short
  ```

  预期：后端修改仅限 Application 值、SevenDays 复制、Web DTO 和对应测试；没有项目、包、路由或数据库变化。

### 任务 4：严格解析 25 字段并实现 Players Feature 格式化

**文件：**

- 修改：`frontend/apps/admin/src/features/players/api/onlinePlayers.ts`
- 修改：`frontend/apps/admin/src/features/players/api/onlinePlayers.test.ts`
- 新建：`frontend/apps/admin/src/features/players/model/onlinePlayerFormatting.ts`
- 新建：`frontend/apps/admin/src/features/players/model/onlinePlayerFormatting.test.ts`
- 修改：`frontend/apps/admin/src/features/players/index.ts`

**接口：**

- `OnlinePlayerDeviceType = 'linux' | 'mac' | 'windows' | 'playStation' | 'xbox' | 'unknown'`。
- `OnlinePlayerPosition = Readonly<{ x: number; y: number; z: number }>`。
- `OnlinePlayer` 精确包含 25 字段；所有解析结果、身份、位置和数组继续冻结。
- 产出纯函数 `formatNullable(value: string | null): string`、`formatRoundedNumber(value: number, locale?: string): string`、`formatPosition(position, locale?): string`、`formatDurationMinutes(value, locale?): string`、`formatDeviceType(value): string`；默认 locale 为 `zh-CN`，测试可显式传入 locale。

- [ ] **步骤 1：把 parser 合法夹具扩展到完整合同**

  在 `onlinePlayers.test.ts` 把 `validPlayer` 改为规格 JSON 的 25 字段对象。深冻结测试额外修改原始 `position.x` 并断言解析结果不变。四个 nullable 字段分别测试 `null` 和合法非空值；Discord 使用 `18446744073709551615`，不能转成 number。

  增加表驱动拒绝：缺少任一批准键、未知设备、位置缺轴/非对象、可空字段错误类型、空白可选字符串、fractional integer、负累计字段、非有限 position/分钟/距离、错误 UTC、错误 identity。数组中第二名玩家无效时必须拒绝整个响应。

- [ ] **步骤 2：运行 parser 测试并确认 RED**

  在 `frontend/apps/admin` 执行：

  ```powershell
  pnpm exec vitest run src/features/players/api/onlinePlayers.test.ts
  ```

  正确 RED 是新字段缺失、非法值被接受或位置未冻结；不能只停在 TypeScript 夹具缺属性。

- [ ] **步骤 3：实现严格 parser**

  增加 `parseFiniteNumber`、`parseNonNegativeNumber`、`parseNonNegativeInteger`、`parseNullableNonBlankString` 和 `parsePosition`。`parsePlayer` 显式读取全部 25 个键并构造：

  ```ts
  position: Object.freeze({
    x: parseFiniteNumber(position.x),
    y: parseFiniteNumber(position.y),
    z: parseFiniteNumber(position.z),
  })
  ```

  不保留未知字段引用，不接受数字字符串，不把 `null` 改为空字符串，不根据角色删字段。完成后重跑步骤 2，预期 parser 测试全部通过。

- [ ] **步骤 4：增加格式化模块可编译骨架**

  先创建 `onlinePlayerFormatting.ts`，导出接口区列出的全部函数签名，每个函数暂时抛出 `new Error('Not implemented')`；在 `index.ts` 完成必要的类型/函数 export。运行 `pnpm typecheck` 确认模块与签名可编译。这一步不计 RED。

- [ ] **步骤 5：写格式化 RED 测试**

  在 `onlinePlayerFormatting.test.ts` 覆盖：

  ```ts
  expect(formatNullable(null)).toBe('未知')
  expect(formatRoundedNumber(127540.75, 'zh-CN')).toBe('127,541')
  expect(formatPosition({ x: 100.5, y: -1.5, z: 200.25 }, 'zh-CN'))
    .toBe('101, -1, 200')
  expect(formatDurationMinutes(0.49, 'zh-CN')).toBe('少于 1 分钟')
  expect(formatDurationMinutes(134.5, 'zh-CN')).toBe('2 小时 15 分钟')
  expect(formatDurationMinutes(4823.5, 'zh-CN')).toBe('3 天 8 小时 24 分钟')
  expect(formatDeviceType('playStation')).toBe('PlayStation')
  ```

  同时覆盖恰好 1 分钟、60 分钟、1440 分钟、只含小时/天、`linux/mac/windows/xbox/unknown`。坐标按 `Math.round`，负半数遵循 JavaScript `Math.round` 语义；分钟先 `Math.round` 再向下分解，不输出秒或小数。

- [ ] **步骤 6：实现纯格式化并转 GREEN**

  只在 Players Feature 内使用 `Intl.NumberFormat(locale, { maximumFractionDigits: 0 })`。`formatNullable` 只处理传输 `null`；parser 已禁止空白字符串。执行：

  ```powershell
  pnpm exec vitest run src/features/players/api/onlinePlayers.test.ts src/features/players/model/onlinePlayerFormatting.test.ts
  pnpm typecheck
  ```

  预期：parser/格式化测试和类型检查通过；没有安装依赖或修改全局 i18n。

### 任务 5：实现紧凑主表、移动列表和详情状态机

**文件：**

- 修改：`frontend/apps/admin/src/features/players/ui/OnlinePlayersTable.vue`
- 修改：`frontend/apps/admin/src/features/players/ui/OnlinePlayersList.vue`
- 新建：`frontend/apps/admin/src/features/players/ui/OnlinePlayerDetailsSlideover.vue`
- 新建：`frontend/apps/admin/src/features/players/ui/OnlinePlayerDetailsSlideover.test.ts`
- 修改：`frontend/apps/admin/src/features/players/ui/OnlinePlayersView.vue`
- 修改：`frontend/apps/admin/src/features/players/ui/OnlinePlayersView.test.ts`

**接口：**

- `OnlinePlayersTable` 和 `OnlinePlayersList` props 保持 `players`、`canKick`；emits 改为 `viewDetails(player)`、`kickPlayer(player)`。主列表不再拥有身份复制。
- `OnlinePlayerDetailsSlideover` 使用 `defineModel<boolean>('open', { required: true })`，props 为 `player: OnlinePlayer | null`、`unavailable: boolean`、`canKick: boolean`，emits 为 `copyValue(value)`、`kickPlayer(player)`。
- `OnlinePlayersView` 新增 `detailsKey`、`detailsPlayer`、`detailsUnavailable` 三个 `shallowRef`；现有 `selectedPlayer` 保持踢出确认专用。
- `detailsCanKick = authorizedToKick && state === 'fresh' && !sessionExpired && !detailsUnavailable`；列表现有踢出能力只使用 `authorizedToKick`，不改变已批准的身份重验和确认语义。

- [ ] **步骤 1：增加详情组件可编译骨架**

  先创建 `OnlinePlayerDetailsSlideover.vue`，只声明批准的 typed model、props 和 emits，并提供空的 `USlideover` body/footer slots；在 View 中只完成 import，不接状态。这一步只消除缺文件/导入错误，不计 RED。

- [ ] **步骤 2：先改写主表/移动列表行为测试**

  删除旧“不得渲染 IP/位置/击杀/死亡”和“未绑定”主表断言，改为：Table 列固定为玩家、状态、等级、延迟、设备、更新时间、操作；List 显示玩家、存活/生命、等级、延迟、更新时间。两者均有明确详情按钮并 emit 原玩家，主表/列表文本不包含 IP、完整身份、位置或累计统计。

  ```ts
  wrapper.getComponent(OnlinePlayersTable).vm.$emit('viewDetails', player)
  expect(wrapper.emitted('viewDetails')).toEqual([[player]])
  ```

  保留行级 observation 过期提示和固定踢出目标测试。正确测试通过 role、aria-label、文本与 emit 观察行为，不依赖 Nuxt UI 内部 DOM。

- [ ] **步骤 3：写 Slideover 四分区与格式化 RED 测试**

  在独立测试中以 `USlideover` stub 声明 `open`、`title`、`description` props 和 `update:open` emit，把 `title` 渲染为 heading，并暴露 header/body/footer slots，断言：

  - 标题持续包含玩家名和存活/死亡；
  - 身份、连接、当前状态、累计统计四个标题均存在；
  - 全部 25 字段都有对应展示，空 `crossplatformIdentity/ip/compatibilityVersion/discordUserId` 都显示“未知”；
  - 位置显示 `101, 51, 200`，距离显示 `127,541`，分钟值显示天/小时/分钟；
  - 原生身份、可选跨平台身份、Discord 与 IP 只有非空时出现带 Lucide copy 图标和明确 `aria-label` 的按钮；
  - unavailable 告警存在时保留最后值并禁用踢出；`canKick=false` 时不出现可用危险按钮；
  - close 通过 `update:open(false)`，kick emit 当前完整玩家。

- [ ] **步骤 4：运行组件测试并确认行为 RED**

  ```powershell
  pnpm exec vitest run src/features/players/ui/OnlinePlayersView.test.ts src/features/players/ui/OnlinePlayerDetailsSlideover.test.ts
  ```

  预期测试项目成功编译；正确 RED 是详情入口、分区、格式化或 emit 行为尚未实现，组件缺失、导入或类型错误不算 RED。

- [ ] **步骤 5：实现主表、移动列表与受控 Slideover**

  Table 使用稳定列宽和现有 `UTable`，状态单元显示死亡/存活 badge 与 `health / maxHealth`；设备使用 `formatDeviceType`；更新时间保留观察时间和 90 秒过期 badge。详情使用 `i-lucide-panel-right-open` 图标按钮，提供 `aria-label="查看玩家详情：<name>"`；危险操作继续使用 `i-lucide-log-out`。

  Slideover 使用：

  ```vue
  <USlideover
    v-model:open="open"
    :title="player ? `${player.name} · ${player.isDead ? '死亡' : '存活'}` : '玩家详情'"
    :description="player ? `entity ${player.entityId}` : undefined"
    :ui="{ content: 'w-full max-w-xl', body: 'overflow-y-auto' }"
  >
    <template #body>...</template>
    <template #footer>...</template>
  </USlideover>
  ```

  四区使用语义化 `<section>`、`<h3>`、`<dl>` 和分隔线，不创建卡片。窄屏内容使用 `minmax(0, 1fr)`，长身份 `overflow-wrap:anywhere`，按钮保持固定尺寸，不让动态文本改变布局。

- [ ] **步骤 6：写详情刷新和危险操作状态机 RED 测试**

  扩展 `mountOnlinePlayersView` 返回可写 `stateRef`、`errorCodeRef`、`snapshotState`，并 stub 新 Slideover 的 props/emits。覆盖：

  ```text
  open A -> fresh A2 same key       => details updates to A2
  open A -> fresh empty             => preserves A, unavailable=true
  open A -> fresh empty -> fresh A3 => remains unavailable with A
  open A -> same entity/new identity=> preserves A, unavailable=true
  close -> reopen A3                => new target, unavailable=false
  stale/offline/forbidden/session expired/game-not-ready => details kick disabled
  open kick confirmation -> refresh/close/unavailable    => fixed selectedPlayer unchanged
  ```

  请求失败仅改变 `state/errorCode` 而不替换成功 `snapshot` 时，不得锁存 unavailable。401 回调测试必须证明本地 `sessionExpired` 立即禁用详情动作并沿用现有 `/login?redirect=/players` 跳转。

- [ ] **步骤 7：实现页面局部详情状态**

  使用：

  ```ts
  interface SelectedPlayerKey {
    entityId: number
    combinedId: string
  }

  const detailsKey = shallowRef<SelectedPlayerKey | null>(null)
  const detailsPlayer = shallowRef<OnlinePlayer | null>(null)
  const detailsUnavailable = shallowRef(false)
  const sessionExpired = shallowRef(false)
  ```

  增加 `detailsOpen` writable `computed`：getter 为 `detailsPlayer.value !== null`，setter 在 false 时调用 `closeDetails()`，并将它传给 Slideover 的 `v-model:open`。`openDetails(player)` 同时保存键、完整 observation 并清除 unavailable。`watch([state, snapshot], ([nextState, nextSnapshot]) => ...)` 只处理 `nextState === 'fresh'` 且 snapshot 非空的新成功快照；unavailable 已锁存时直接返回。匹配必须同时比较 entity ID 和原生 `combinedId`；未匹配时只设置 unavailable，不替换最后玩家。`closeDetails()` 清除三个值。认证回调先设置 `sessionExpired=true`，再调用现有 redirect。

  复制函数改为通用 `copyValue(value: string)`，成功/失败文案不得回显敏感值或浏览器异常。详情 kick handler 必须先检查 `detailsCanKick.value` 和非空 `detailsPlayer.value`，再调用现有 `openKickDialog(detailsPlayer.value)`；不能只依赖子组件禁用按钮。后续状态变化不修改 `selectedPlayer`。

- [ ] **步骤 8：重跑组件测试和 Players Feature 门禁**

  ```powershell
  pnpm exec vitest run src/features/players
  pnpm lint
  pnpm typecheck
  pnpm build
  ```

  预期：Players Feature 测试、lint、类型检查和生产构建全部通过；没有新依赖、Pinia 状态或第二个详情 API。

### 任务 6：执行跨边界验证并提升 Current 文档

**文件：**

- 修改：`frontend/apps/admin/tests/e2e/admin-online-players.spec.ts`
- 修改：`docs/architecture.md`
- 修改：`docs/architecture/backend-target-blueprint.md`
- 修改：`docs/architecture/admin-frontend-target-blueprint.md`
- 修改：`docs/test.md`
- 修改：`backend/README.md`

**接口：**

- Playwright 在受控 Owner 环境中拦截在线玩家 GET，返回固定合成 25 字段响应，只验证 Admin 展示与状态；真实 7DTD smoke 单独证明字段来源和值兼容。
- 实现和验证完成后，Current 架构与 Backend README 描述完整已实现字段和 Admin 详情，不再声称 API 排除 IP、位置或战斗统计。
- Target 蓝图删除或压缩已经提升为 Current 的在线玩家字段/详情段落，只保留尚未实现的角色授权等未来方向；`docs/test.md` 只把实际执行且有输出证据的门禁写成当前证据。

- [ ] **步骤 1：增加合成 25 字段浏览器用例**

  在真实 Owner 登录前注册 `page.route('**/api/v1/players/online', ...)`，返回两名完整玩家，其中一名含四个 nullable 值、长身份、负坐标和大距离。分别在桌面、`390x844`、320 CSS 像素运行：

  ```ts
  await page.getByRole('button', { name: '查看玩家详情：Player' }).click()
  await expect(page.getByRole('heading', { name: '身份' })).toBeVisible()
  await expect(page.getByText('未知', { exact: true })).toHaveCount(4)
  await expect(page.getByText('101, 51, 200')).toBeVisible()
  await expect(page.getByText('127,541')).toBeVisible()
  ```

  验证焦点进入抽屉、Escape/关闭按钮后焦点返回触发按钮、body 内滚动、无页面水平溢出。通过连续 route 响应和手动刷新验证同身份更新、缺失后锁存 unavailable、同身份重现不恢复、关闭后重开恢复。浏览器 stub 不断言游戏字段来源。

- [ ] **步骤 2：运行所有自动化聚合门禁**

  从仓库根执行后端：

  ```powershell
  $referenceRoot = (Resolve-Path '7dtd-reference').Path
  dotnet restore backend/7DPanel.sln /p:SevenDaysReferenceRoot=$referenceRoot
  dotnet build backend/7DPanel.sln --configuration Release --no-restore /p:SevenDaysReferenceRoot=$referenceRoot
  dotnet test backend/7DPanel.sln --configuration Release --no-build --no-restore /p:SevenDaysReferenceRoot=$referenceRoot
  ```

  在 `frontend/apps/admin` 执行：

  ```powershell
  pnpm lint
  pnpm typecheck
  pnpm test
  pnpm build
  ```

  预期：所有命令退出码为 0；记录测试数量和任何既有 warning。不得用定向测试结果替代聚合门禁。

- [ ] **步骤 3：运行受控浏览器门禁**

  仅在 `SEVENDPANEL_ADMIN_URL`、`PANEL_USERNAME`、`PANEL_PASSWORD` 指向受控 OWIN 环境且用户授权后，在 `frontend/apps/admin` 执行：

  ```powershell
  pnpm exec playwright test admin-online-players.spec.ts
  ```

  预期：命令只发现并运行 `admin-online-players.spec.ts`，测试数大于 0；桌面、390 和 320 视口的详情、状态、焦点、滚动、复制名称与无溢出断言通过。若环境变量缺失导致 skipped，必须报告未验证，不能记为通过证据。

- [ ] **步骤 4：运行 Windows v3.0.1-b4 真实进程 smoke**

  仅在用户授权发布和启动受控测试服后，按脚本指南执行：

  ```powershell
  backend\scripts\Publish-Mod.cmd
  backend\scripts\Start-Server.cmd
  backend\scripts\Test-HealthEndpoint.cmd
  ```

  用环境中的 Owner 凭据取得 Access Token，连接一个受控玩家并等待至少两次 `SavePlayerData`。两次调用 `GET /api/v1/players/online`，记录脱敏后的 25 字段名称、`observedAtUtc` 更新、设备、权限、位置、生命、统计和单位；断开玩家后确认条目删除，再执行：

  ```powershell
  backend\scripts\Stop-Server.cmd
  backend\scripts\Test-HealthEndpoint.cmd -ExpectUnavailable
  ```

  权限矩阵必须分别覆盖原生身份规则、跨平台身份规则、Steam 组规则和无匹配默认 `1000`，并证明多项同时匹配时返回最小值。无法稳定操纵的统计必须明确记录证据限制，不用非零值推导单位正确。真实进程只证明字段、权限和事件兼容；“HTTP 请求不投递主线程任务”由自动化与源码依赖检查证明。不得把 Token、IP、Discord ID 或完整平台身份写入文档和命令历史。

- [ ] **步骤 5：从验证事实提升 Current 文档**

  步骤 2 聚合自动化成功后即可把“代码已实现并通过自动化”的事实提升到 Current；步骤 3/4 的浏览器和真实进程证据只有实际成功执行后才能写成已验证事实，未执行或 skipped 时必须在 `docs/test.md` 保留明确缺口：

  - `docs/architecture.md` 写入已实现的 25 字段不可变投影、Web DTO、Admin Slideover 和 unavailable 状态所有权；
  - `backend/README.md` 将旧 8 字段/排除 IP、位置、战斗统计描述替换为完整当前合同的链接摘要；
  - 两个 Target 蓝图删除已提升的重复实现细节，只保留仍为 Target 的授权或其他未来职责；
  - `docs/test.md` 把实际通过的测试和环境写为证据，未运行 browser/真实进程时继续保留为未满足门禁；
  - 不修改 `CHANGELOG.md`，因为尚未发布。

- [ ] **步骤 6：执行最终文档与范围审计**

  ```powershell
  git diff --check
  git status --short
  ```

  再执行：

  ```powershell
  rg "TBD|TODO|D:\\|C:\\" docs/PRD.md docs/design.md docs/architecture.md docs/test.md docs/superpowers
  ```

  预期：无占位符或机器路径。仓库没有自有 PRD/lifecycle 校验命令，因此不得虚构该门禁；使用 `managing-project-lifecycle` 执行语义复读和可用的辅助审计，确认本 plan 只链接一个 primary spec、本地链接有效、Current/Target/Change Record 角色一致、无凭据，且 `docs/architecture.md` 只写已实现和已获证据的事实。最后报告未执行的真实环境门禁，不提交或发布。

## 计划自检

- **规格覆盖：** 任务 1 覆盖产品不可变合同；任务 2 覆盖同次 Save 来源、失败和单位；任务 3 覆盖固定 HTTP DTO；任务 4 覆盖严格 parser 与格式化；任务 5 覆盖主表、四区抽屉、unavailable 和 Fresh-only 详情踢出；任务 6 覆盖浏览器、真实进程和 Current 文档提升。
- **类型一致：** 后端统一使用 `PlayerPosition`、`PlayerDeviceType` 和 25 参数 `PlayerSnapshot`；前端统一使用 `OnlinePlayerPosition`、`OnlinePlayerDeviceType` 和 25 字段 `OnlinePlayer`；详情事件统一传递完整 `OnlinePlayer`。
- **边界一致：** 没有新增项目、路由、数据库、依赖、全局 Store、请求时游戏访问、通用 projection/详情抽象或仅测试使用的权限接口。
- **执行纪律：** 每项行为均有 RED、最小实现、GREEN 和窄门禁；聚合、浏览器、真实进程与文档提升只在实现稳定后各执行一次。