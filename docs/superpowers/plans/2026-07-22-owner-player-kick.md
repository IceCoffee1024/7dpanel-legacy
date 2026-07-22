---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-22-owner-player-kick-design.md
last_updated: "2026-07-22"
---

# Owner 踢出在线玩家与持久审计实施计划

> **面向智能体执行者：** 实施时必须使用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans`，并逐任务进行规格符合性与代码质量审查。以下步骤使用复选框跟踪。

**对应规格：** [Owner 踢出在线玩家与持久审计设计规格](../specs/2026-07-22-owner-player-kick-design.md)

**目标：** 让当前 `Owner` 从 Admin 在线玩家页面明确确认并填写原因后，通过类型化游戏端口踢出在线玩家，同时把动作意图和可信终态持久化到 SQLite。

**架构：** `PlayersController` 只处理 HTTP、认证主体和 Problem Details；`KickPlayerUseCase` 拥有校验、踢出专属 single-flight、审计顺序和结果协调；SevenDays Adapter 在 `GameThreadDispatcher` 内重新校验 `entityId + platformIdentity` 并调用 `GameUtils.KickPlayerForClientInfo`；SQLite Adapter 通过 DbUp migration 和短连接实现审计生命周期。Admin 使用局部 composable 和 Props Down / Events Up 组合确认流程，不新增 Pinia 状态。

**技术栈：** .NET Framework 4.8、C# 11、xUnit、Dapper、DbUp、Microsoft.Data.Sqlite、ASP.NET Web API 2、Katana OWIN、Vue 3.5、TypeScript 6、Nuxt UI 4、Vitest 4、Vue Test Utils、pnpm 11。

## 全局约束

- 只实现 Owner 踢出在线玩家与持久审计；不实现 Ban、Unban、Mute、Teleport、审计查询页面、动作轮询 API 或自动重试。
- 不通过 `IRestrictedConsoleGateway` 或任意控制台字符串执行踢出；不得调用只负责队伍成员的 `Party.KickPlayer`。
- 不创建 Domain 项目、通用命令总线、玩家动作注册表或所有动作共享的容量框架。
- `reason` 在后端 trim 后必须为 1 至 200 个字符，允许 Unicode；`confirmed` 必须精确为 `true`。
- Controller 只从 `ClaimTypes.NameIdentifier` 读取操作者 subject，不接受客户端提交操作者身份。
- 动作专属 single-flight 由 `KickPlayerUseCase` 在插入 `Pending` 之前获取；busy 请求不产生审计记录。
- 审计意图持久化失败时不得调用游戏动作；终态写入失败时不得把结果伪造为 `Failed`。
- 合法审计转换只有 `Pending -> Succeeded | Failed | Unknown`；上次进程遗留的 `Pending` 在接受动作前恢复为 `Unknown`。
- `Succeeded` 只表示 `GameUtils.KickPlayerForClientInfo` 已返回并安排断开，不表示 HTTP 请求内已经观察到玩家离线。
- 游戏主线程开始前允许取消或启动超时；开始后必须等待真实动作结果并尝试完成审计。
- Admin Token 只出现在 `Authorization` Header；URL、请求 body、前端持久化和错误文案不得包含 Token。
- Admin 使用 Vue 3 Composition API、`<script setup lang="ts">`、Nuxt UI v4 和现有语义色；不新增前端依赖。
- 真实 Windows `v3.0.1-b4` 踢出不是本计划完成门；缺失证据必须保留，不能用模拟测试替代。
- 每个生产行为严格执行 RED、验证 RED、GREEN、验证 GREEN；未观察到预期失败前不得写对应生产实现。
- 本计划不授权 `git commit`、`git push`、`git reset` 或 `git revert`。每个任务完成后只报告 diff 与验证结果，等待独立授权后再执行 Git 操作。

---

### 任务 1：定义 Application 踢出用例与审计生命周期

**文件：**

- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Players/KickPlayerRequest.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Players/KickPlayerResult.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Players/IPlayerActions.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Audit/PlayerActionAudit.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Audit/IPlayerActionAuditTrail.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/Players/KickPlayerUseCase.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/KickPlayerUseCaseTests.cs`

**接口：**

- 消费：现有 `PlayerPlatformIdentity`，以及调用方提供的 `actorSubject`、`entityId`、`expectedPlatformIdentity`、`reason`、`confirmed` 和 `CancellationToken`。
- 产出：

```csharp
public sealed class KickPlayerRequest
{
    public KickPlayerRequest(
        string actorSubject,
        int entityId,
        PlayerPlatformIdentity expectedPlatformIdentity,
        string reason,
        bool confirmed);

    public string ActorSubject { get; }
    public int EntityId { get; }
    public PlayerPlatformIdentity ExpectedPlatformIdentity { get; }
    public string Reason { get; }
    public bool Confirmed { get; }
}

public interface IPlayerActions
{
    Task<KickPlayerActionResult> KickAsync(
        KickPlayerCommand command,
        CancellationToken cancellationToken);
}

public interface IPlayerActionAuditTrail
{
    void CreatePending(PlayerActionAuditIntent intent);
    bool TryComplete(PlayerActionAuditCompletion completion);
    int MarkPendingUnknown(DateTimeOffset completedAtUtc);
}

public sealed class KickPlayerUseCase
{
    public Task<KickPlayerResult> ExecuteAsync(
        KickPlayerRequest request,
        CancellationToken cancellationToken);
}
```

- `KickPlayerActionStatus` 只包含 `Succeeded`、`PlayerNotOnline` 和 `PlayerIdentityChanged`。预期业务结果通过类型返回；主线程启动超时继续使用 `TimeoutException`，取消继续使用 `OperationCanceledException`，未知游戏异常由用例映射为 `PlayerKickFailedException`。
- `KickPlayerResult` 只表示成功，包含 `OperationId`、`Status="succeeded"`、执行时重新解析的 `Target`、`RequestedAtUtc` 和 `CompletedAtUtc`。失败通过稳定 Application 异常交给 Web 映射。
- `PlayerActionAuditIntent.TargetName` 和对应数据库字段允许为空；成功与身份变化可以用主线程结果补全名称，离线失败保持空。

- [ ] **步骤 1：写请求与结果模型的失败测试**

  在 `KickPlayerUseCaseTests.cs` 先覆盖：空 actor、负 `entityId`、空身份、未确认、空白原因、201 字符原因均在审计和动作之前拒绝；合法原因被 trim；Unicode 原因保持原值。

  ```csharp
  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  public async Task Empty_reason_is_rejected_before_audit_and_action(string reason)
  {
      var fixture = new KickFixture();

      await Assert.ThrowsAsync<InvalidPlayerKickReasonException>(() =>
          fixture.UseCase.ExecuteAsync(
              fixture.Request(reason: reason),
              CancellationToken.None));

      Assert.Empty(fixture.Audit.Intents);
      Assert.Equal(0, fixture.Actions.CallCount);
  }
  ```

- [ ] **步骤 2：运行模型测试并验证 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter FullyQualifiedName~KickPlayerUseCaseTests
  ```

  预期：编译失败，指出 `KickPlayerUseCase`、`KickPlayerRequest` 或相关契约尚不存在；失败原因不得来自测试语法或夹具错误。

- [ ] **步骤 3：实现最小不可变契约与参数校验**

  创建上述模型、枚举、异常和端口。所有字符串使用 `string.IsNullOrWhiteSpace` 校验；`Reason` 保存 `reason.Trim()`；不可变目标结果只复制 `entityId`、名称和主平台身份，不携带游戏对象。

- [ ] **步骤 4：运行模型测试并验证 GREEN**

  运行步骤 2 的同一命令。预期：模型与前置校验测试通过，动作和审计调用次数保持 0。

- [ ] **步骤 5：写审计顺序、single-flight 与结果映射失败测试**

  使用记录型 fake 覆盖：

  ```csharp
  [Fact]
  public async Task Pending_audit_is_persisted_before_the_game_action()
  {
      var order = new List<string>();
      var fixture = new KickFixture(order: order);

      var result = await fixture.UseCase.ExecuteAsync(
          fixture.Request(),
          CancellationToken.None);

      Assert.Equal(new[] { "audit:pending", "action:kick", "audit:succeeded" }, order);
      Assert.Equal("succeeded", result.Status);
  }
  ```

  另外覆盖：第二个并发请求立即抛 `PlayerActionBusyException` 且不写第二条审计；成功、异常、取消和超时后 gate 释放；`PlayerNotOnline`、`PlayerIdentityChanged`、`TimeoutException`、排队取消和未知动作异常分别写入稳定 `Failed` 终态；成功写 `Succeeded`；`TryComplete=false` 或完成写异常统一抛 `AuditCompletionUnavailableException` 并且不得再写另一终态；`CreatePending` 失败抛 `AuditUnavailableException` 且不调用动作。

- [ ] **步骤 6：运行用例测试并验证 RED**

  运行步骤 2 的命令。预期：测试因 `ExecuteAsync` 尚未协调审计、single-flight 或结果映射而失败，且至少一个失败明确显示动作先于审计或未写终态。

- [ ] **步骤 7：实现最小 `KickPlayerUseCase`**

  使用实例字段 `int inFlight` 和 `Interlocked.CompareExchange` 获取 gate；在 `finally` 中释放。公开构造函数使用 `Guid.NewGuid().ToString("N")` 和 `DateTimeOffset.UtcNow`，internal 构造函数注入 `Func<string>` 与 `Func<DateTimeOffset>` 供测试稳定断言。流程固定为：校验、获取 gate、创建 `Pending`、调用动作、完成终态、返回或抛稳定异常。

- [ ] **步骤 8：运行用例测试并验证 GREEN**

  运行步骤 2 的命令。预期：`KickPlayerUseCaseTests` 全部通过；没有测试依赖 `Thread.Sleep`、真实 SQLite 或游戏程序集。

- [ ] **步骤 9：运行 Application 邻近回归并记录审查点**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~KickPlayerUseCaseTests|FullyQualifiedName~OnlinePlayerQueryTests|FullyQualifiedName~ConsoleCommandTests"
  ```

  预期：相关 Application 测试全部通过。检查 diff 只包含本任务文件；不执行 Git 提交。

### 任务 2：实现 SQLite 玩家动作审计与启动恢复

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Migrations/002_PlayerActionAudit.sql`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqlitePlayerActionAuditTrail.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/SqlitePlayerActionAuditTrailTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/SqliteAuthenticationStoreTests.cs`

**接口：**

- 消费：任务 1 的 `IPlayerActionAuditTrail`、`PlayerActionAuditIntent`、`PlayerActionAuditCompletion` 和 `PlayerActionAuditStatus`。
- 产出：`SqlitePlayerActionAuditTrail : IPlayerActionAuditTrail`，每次方法调用通过现有 `SqliteConnectionFactory.Open()` 使用独立短连接；不向 Application 暴露 Dapper 或 SQLite 类型。

- [ ] **步骤 1：写 migration 与重复升级失败测试**

  新测试创建临时数据库并连续调用两次 `Upgrade()`，然后断言：

  ```csharp
  Assert.Equal(
      1,
      connection.ExecuteScalar<int>(
          "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'player_action_audit';"));
  Assert.Equal(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM SchemaVersions;"));
  ```

  同时把 `SqliteAuthenticationStoreTests.Upgrade_creates_schema_and_can_be_repeated` 的表、索引和 schema version 精确计数更新为新 schema 的批准值。

- [ ] **步骤 2：运行 SQLite 测试并验证 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~SqlitePlayerActionAuditTrailTests|FullyQualifiedName~SqliteAuthenticationStoreTests.Upgrade_creates_schema"
  ```

  预期：测试因 `002_PlayerActionAudit.sql` 和 `player_action_audit` 表不存在而失败。

- [ ] **步骤 3：实现精确 migration**

  migration 创建下列结构，不添加清理任务或未使用查询索引：

  ```sql
  CREATE TABLE player_action_audit (
      operation_id TEXT NOT NULL PRIMARY KEY,
      action_type TEXT NOT NULL CHECK (action_type = 'kick'),
      actor_subject TEXT NOT NULL,
      target_entity_id INTEGER NOT NULL CHECK (target_entity_id >= 0),
      target_name TEXT NULL,
      target_platform_id TEXT NOT NULL,
      target_platform TEXT NOT NULL,
      reason TEXT NOT NULL CHECK (length(reason) BETWEEN 1 AND 200),
      requested_utc INTEGER NOT NULL,
      completed_utc INTEGER NULL,
      status TEXT NOT NULL CHECK (status IN ('Pending', 'Succeeded', 'Failed', 'Unknown')),
      failure_code TEXT NULL,
      CONSTRAINT ck_player_action_audit_completion
          CHECK ((status = 'Pending' AND completed_utc IS NULL) OR
                 (status <> 'Pending' AND completed_utc IS NOT NULL))
  );
  ```

- [ ] **步骤 4：运行 migration 测试并验证 GREEN**

  运行步骤 2 的同一命令。预期：首次和重复升级通过，`SchemaVersions` 恰好包含两条记录。

- [ ] **步骤 5：写存储生命周期失败测试**

  覆盖：`CreatePending` 保存全部不可变字段且 `target_name/completed_utc/failure_code` 可空；`TryComplete` 只从 `Pending` 更新一次并可补全 `target_name`；重复完成返回 false 且不覆盖原终态；`MarkPendingUnknown` 只更新遗留 `Pending`，保留 `Succeeded/Failed/Unknown`；两个 store/connection factory 实例重开同一路径后仍能恢复；数据库约束拒绝非法状态与超过 200 字符原因。

- [ ] **步骤 6：运行存储测试并验证 RED**

  运行步骤 2 的命令。预期：编译失败或行为失败，指出 `SqlitePlayerActionAuditTrail` 尚不存在或生命周期方法未实现。

- [ ] **步骤 7：实现短连接审计存储**

  `CreatePending` 执行参数化 `INSERT`；`TryComplete` 使用：

  ```sql
  UPDATE player_action_audit
  SET target_name = COALESCE(@TargetName, target_name),
      completed_utc = @CompletedUtc,
      status = @Status,
      failure_code = @FailureCode
  WHERE operation_id = @OperationId
    AND status = 'Pending';
  ```

  返回受影响行数是否为 1。`MarkPendingUnknown` 在一个写事务中把全部 `Pending` 更新为 `Unknown`、设置统一完成时间和 `failure_code='process_interrupted'`，返回更新行数。

- [ ] **步骤 8：运行 SQLite 测试并验证 GREEN**

  运行步骤 2 的命令。预期：migration、CRUD、重复完成、重开和恢复测试全部通过，临时目录在测试结束后可删除。

- [ ] **步骤 9：运行 SQLite 回归并记录审查点**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~SqliteAuthenticationStoreTests|FullyQualifiedName~SqlitePlayerActionAuditTrailTests"
  ```

  预期：认证与动作审计 SQLite 测试全部通过；不执行 Git 提交。

### 任务 3：实现 SevenDays 类型化踢出动作

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Players/SevenDaysPlayerActions.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/SevenDaysPlayerActionsTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/GameThreadDispatcherTests.cs`

**接口：**

- 消费：任务 1 的 `IPlayerActions.KickAsync(KickPlayerCommand, CancellationToken)` 和现有 `GameThreadDispatcher.Enqueue`。
- 产出：`SevenDaysPlayerActions : IPlayerActions`。公开构造函数连接真实 dispatcher 和游戏对象；internal 构造函数注入 dispatcher 与 `Func<KickPlayerCommand, KickPlayerActionResult>`，测试不得构造 `ClientInfo` 或依赖运行中的游戏。

- [ ] **步骤 1：写 dispatcher 边界与类型化结果失败测试**

  覆盖 capture 只在 dispatcher 委托中调用、operation name 精确为 `7DPanel.Players.Kick`、启动截止时间为 5 秒、命令原样转发，以及 `Succeeded`、`PlayerNotOnline`、`PlayerIdentityChanged` 返回不被转换为控制台文本或异常。

  ```csharp
  [Fact]
  public async Task Kick_capture_runs_only_inside_dispatcher_boundary()
  {
      var dispatched = false;
      var actions = new SevenDaysPlayerActions(
          dispatcher: (name, action, timeout, token) =>
          {
              Assert.Equal("7DPanel.Players.Kick", name);
              Assert.Equal(TimeSpan.FromSeconds(5), timeout);
              dispatched = true;
              return Task.FromResult(action());
          },
          kick: command =>
          {
              Assert.True(dispatched);
              return KickPlayerActionResult.Succeeded(command.EntityId, "Alice", command.ExpectedPlatformIdentity);
          });

      var result = await actions.KickAsync(Command(), CancellationToken.None);

      Assert.Equal(KickPlayerActionStatus.Succeeded, result.Status);
  }
  ```

- [ ] **步骤 2：运行 Adapter 测试并验证 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~SevenDaysPlayerActionsTests|FullyQualifiedName~GameThreadDispatcherTests"
  ```

  预期：编译失败，指出 `SevenDaysPlayerActions` 尚不存在。

- [ ] **步骤 3：实现可测试 dispatcher 壳**

  按现有 `SevenDaysOnlinePlayerQuery` 的构造 seam 实现最小 Adapter；不在该类增加 single-flight。运行步骤 2，预期新 dispatcher 边界测试通过。

- [ ] **步骤 4：写主线程身份校验与原生调用失败测试**

  把纯比较和调用参数构造收敛为 internal helper，测试精确覆盖：找不到 `entityId` 返回 `PlayerNotOnline`；当前 `CombinedString` 或 `PlatformIdentifierString` 任一变化返回 `PlayerIdentityChanged`；匹配时使用当前玩家名、`ManualKick`、api response 0、默认 ban time 和 trim 后原因；不得调用 `Party.KickPlayer` 或 `SdtdConsole.ExecuteSync`。

- [ ] **步骤 5：运行身份与调用测试并验证 RED**

  运行步骤 2 的命令。预期：测试因真实 capture/helper 尚未实现或未调用 kick delegate 而失败。

- [ ] **步骤 6：实现真实 `CaptureAndKick`**

  在 dispatcher 委托内读取 `ConnectionManager.Instance.Clients.List`，按 `entityId` 定位唯一 `ClientInfo`；比较 `client.PlatformId.CombinedString` 与 `PlatformIdentifierString`；匹配后调用：

  ```csharp
  GameUtils.KickPlayerForClientInfo(
      client,
      new GameUtils.KickPlayerData(
          GameUtils.EKickReason.ManualKick,
          0,
          default(DateTime),
          command.Reason));
  ```

  返回只包含快照值的 `KickPlayerActionResult`，委托外不保存 `ClientInfo`。

- [ ] **步骤 7：补强开始前取消、超时与开始后取消测试**

  在 `GameThreadDispatcherTests` 复用现有状态机 seam，明确验证排队请求超时/取消后执行委托无副作用；当状态已经进入 Running 时取消 token 不会让返回 Task 提前取消，而是等待 action 的真实结果。

- [ ] **步骤 8：运行 SevenDays 定向测试并验证 GREEN**

  运行步骤 2 的命令。预期：新动作测试和 dispatcher 回归全部通过；测试不等待真实 0.5 秒协程。

- [ ] **步骤 9：运行玩家 Adapter 回归并记录审查点**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~SevenDaysPlayerActionsTests|FullyQualifiedName~SevenDaysOnlinePlayerQueryTests|FullyQualifiedName~GameThreadDispatcherTests"
  ```

  预期：在线查询与踢出动作共享 dispatcher 语义但没有共享容量状态；不执行 Git 提交。

### 任务 4：接入 Web API、认证主体、SQLite 恢复和 DI

**文件：**

- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/PlayersController.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/KickPlayerHttpModels.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyInjectionTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyRulesTests.cs`

**接口：**

- 消费：任务 1 至 3 的 `KickPlayerUseCase`、`IPlayerActions`、`IPlayerActionAuditTrail`、`SqlitePlayerActionAuditTrail` 和 `SevenDaysPlayerActions`。
- 产出：`POST /api/v1/players/{entityId}/kick`；成功 DTO 精确包含 `operationId`、`status`、`target`、`requestedAtUtc`、`completedAtUtc`；现有 `GET /api/v1/players/online` 保持不变。

- [ ] **步骤 1：写 Controller 请求校验与授权边界失败测试**

  在 Katana 集成测试中覆盖匿名 401、缺失/false `confirmed`、空白/超长原因、空身份和负路径 id 的稳定 400；断言所有拒绝路径的 fake audit/action 调用数为 0。用反射或 Controller descriptor 断言 action 继续受 `[Authorize(Roles = "Owner")]` 保护；当前持久身份只创建 `Owner`，不构造虚假的非 Owner 认证链路来宣称真实 403 已覆盖。认证主体通过 `PanelClaimsIdentityFactory` 产生，Controller 从：

  ```csharp
  var identity = User?.Identity as ClaimsIdentity;
  var actorSubject = identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
  ```

  读取 subject；缺失 subject 使用稳定 401，不回退到用户名。

- [ ] **步骤 2：运行 Web/DI 测试并验证 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~OwinWebHostTests|FullyQualifiedName~DependencyInjectionTests|FullyQualifiedName~DependencyRulesTests"
  ```

  预期：新 POST 路由返回 404/405 或 DI 缺少用例，新增测试失败。

- [ ] **步骤 3：实现 HTTP DTO 与成功响应映射**

  `KickPlayerRequestBody` 只包含 `ExpectedPlatformIdentity`、`Reason`、`Confirmed`；复用独立 HTTP identity DTO，不把 Application model 直接作为 request body。新增 `[HttpPost] [Route("{entityId:int}/kick")]` action，先检查 `GameReadinessState.Ready`，再构造 `KickPlayerRequest`。时间使用 `ToString("O", CultureInfo.InvariantCulture)`，全局 camelCase formatter 负责字段名。

- [ ] **步骤 4：实现稳定 Problem Details 映射**

  Controller 只捕获任务 1 的稳定 Application 异常、`TimeoutException` 和请求取消边界，精确映射规格中的：

  ```text
  player_kick_confirmation_required
  invalid_player_kick_reason
  invalid_player_identity
  player_not_online
  player_identity_changed
  player_action_busy
  game_thread_timeout
  audit_unavailable
  audit_completion_unavailable
  player_kick_failed
  ```

  不返回异常消息。取消发生在动作开始前时沿用宿主取消，不生成伪造 500；开始后用例会等待真实结果。

- [ ] **步骤 5：写成功与全部稳定失败的集成测试**

  fake 用例依赖分别产生成功、离线、身份变化、busy、主线程超时、审计意图失败、审计完成失败和未知动作失败。成功测试精确断言 camelCase 字段白名单、`status="succeeded"`、Owner subject、trim 后原因、身份值和 UTC 时间；失败测试断言 `application/problem+json`、状态码和 `code`，且 body 不包含内部异常文本。

- [ ] **步骤 6：注册服务并执行启动恢复**

  在 composition root 中注册同一单例实例：

  ```csharp
  services.AddSingleton<SqlitePlayerActionAuditTrail>();
  services.AddSingleton<IPlayerActionAuditTrail>(provider =>
      provider.GetRequiredService<SqlitePlayerActionAuditTrail>());
  services.AddSingleton<SevenDaysPlayerActions>();
  services.AddSingleton<IPlayerActions>(provider =>
      provider.GetRequiredService<SevenDaysPlayerActions>());
  services.AddSingleton<KickPlayerUseCase>();
  ```

  在 `databaseBootstrapper.Upgrade()` 成功后、创建 OWIN Host 并接受动作前调用 `MarkPendingUnknown(DateTimeOffset.UtcNow)`。恢复失败必须让 Host 启动失败关闭，不能在审计不可用时开放动作 API。

- [ ] **步骤 7：补齐 DI 与依赖规则测试**

  验证接口与具体实现解析为同一单例、`KickPlayerUseCase` 可构造、Provider dispose 后 SQLite factory 不可用；Application 不引用 Adapter；Web 不引用 SevenDays/Persistence；SevenDays 和 Persistence 不互相引用；发布产品项目数量不变。

- [ ] **步骤 8：运行 Web/DI 测试并验证 GREEN**

  运行步骤 2 的命令。预期：POST 契约、认证、全部错误映射、启动恢复、DI 和依赖规则测试通过，既有 GET 玩家查询不回归。

- [ ] **步骤 9：运行后端切片回归并记录审查点**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~KickPlayer|FullyQualifiedName~PlayerActionAudit|FullyQualifiedName~OwinWebHostTests|FullyQualifiedName~DependencyInjectionTests|FullyQualifiedName~DependencyRulesTests"
  ```

  预期：后端踢出纵向链路相关测试全部通过；不执行 Git 提交。

### 任务 5：实现 Admin 踢出 API 与局部提交状态

**文件：**

- 新建：`frontend/apps/admin/src/features/players/api/kickPlayer.ts`
- 新建：`frontend/apps/admin/src/features/players/api/kickPlayer.test.ts`
- 新建：`frontend/apps/admin/src/features/players/model/useKickPlayer.ts`
- 新建：`frontend/apps/admin/src/features/players/model/useKickPlayer.test.ts`
- 修改：`frontend/apps/admin/src/features/players/index.ts`

**接口：**

- 消费：现有 `requestJson`、`HttpError`、`OnlinePlayer`、`PlayerIdentity` 和内存 `authorizationHeader`。
- 产出：

```typescript
export interface KickPlayerInput {
  entityId: number
  expectedPlatformIdentity: PlayerIdentity
  reason: string
}

export interface KickPlayerResponse {
  operationId: string
  status: 'succeeded'
  target: Pick<OnlinePlayer, 'entityId' | 'name' | 'platformIdentity'>
  requestedAtUtc: string
  completedAtUtc: string
}

export function kickPlayer(
  authorizationHeader: string,
  input: KickPlayerInput,
  signal?: AbortSignal,
): Promise<KickPlayerResponse>

export interface KickPlayerController {
  isSubmitting: DeepReadonly<ShallowRef<boolean>>
  feedback: DeepReadonly<ShallowRef<KickPlayerFeedback | null>>
  submit: (player: OnlinePlayer, reason: string) => Promise<KickPlayerResponse | null>
  clearFeedback: () => void
  dispose: () => void
}
```

- [ ] **步骤 1：写请求安全与响应验证失败测试**

  覆盖 `POST /api/v1/players/7/kick`、Bearer 只在 Header、body 精确包含 `expectedPlatformIdentity/reason/confirmed:true`、不包含 Token/玩家 IP/跨平台身份；严格解析 32 位小写 hex `operationId`、`succeeded`、目标和 UTC 时间；拒绝未知 status、非法日期、空目标名和额外依赖字段不影响批准白名单。

  ```typescript
  expect(requestJson).toHaveBeenCalledWith('/api/v1/players/7/kick', {
    method: 'POST',
    headers: {
      Authorization: authorizationHeader,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({
      expectedPlatformIdentity: player.platformIdentity,
      reason: '违反服务器规则',
      confirmed: true,
    }),
    signal: controller.signal,
  })
  ```

- [ ] **步骤 2：运行 API 测试并验证 RED**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm test:unit -- src/features/players/api/kickPlayer.test.ts
  ```

  预期：测试因 `kickPlayer.ts` 不存在而失败。

- [ ] **步骤 3：实现 API Client 和严格解析器**

  复用 `onlinePlayers.ts` 的 record/integer/identity/UTC 校验风格，但只复制本响应批准字段；URL 的 `entityId` 必须先验证为非负整数再转成十进制文本。请求不设置自动重试。

- [ ] **步骤 4：运行 API 测试并验证 GREEN**

  运行步骤 2 的同一命令。预期：安全边界和响应解析测试全部通过。

- [ ] **步骤 5：写 `useKickPlayer` 状态与错误分类失败测试**

  使用 composable wrapper 覆盖：无 auth header 映射会话过期且不调用 API；提交期间第二次 submit 返回同一 in-flight promise 或不发第二个请求；成功返回结果；401 失效会话；403 映射 forbidden；`player_not_online`、`player_identity_changed`、`player_action_busy`、`game_not_ready`、`game_thread_timeout`、`audit_unavailable` 映射稳定反馈；网络与 `audit_completion_unavailable` 映射 `unknown`；任何错误都不自动重试；dispose 只取消尚未完成的 HTTP 等待。

- [ ] **步骤 6：运行 composable 测试并验证 RED**

  ```powershell
  pnpm test:unit -- src/features/players/model/useKickPlayer.test.ts
  ```

  预期：测试因 `useKickPlayer` 不存在或未映射错误而失败。

- [ ] **步骤 7：实现最小局部 composable**

  使用 `shallowRef` 保存 `isSubmitting` 和 `feedback`，用一个 `inFlight` 与一个 `AbortController` 管理单次提交；返回 readonly state 和显式 action。错误反馈只保存稳定前端 code 与安全中文文案，不保存 raw exception、Token 或 pending promise；网络/审计完成不可用不得映射为失败。

- [ ] **步骤 8：运行前端 model 回归并记录审查点**

  ```powershell
  pnpm test:unit -- src/features/players/api/kickPlayer.test.ts src/features/players/model/useKickPlayer.test.ts src/features/players/api/onlinePlayers.test.ts src/features/players/model/useOnlinePlayers.test.ts
  ```

  预期：新旧玩家 API 与 composable 测试全部通过；不执行 Git 提交。

### 任务 6：接入 Admin 玩家动作菜单与确认对话框

**文件：**

- 新建：`frontend/apps/admin/src/features/players/ui/KickPlayerDialog.vue`
- 新建：`frontend/apps/admin/src/features/players/ui/KickPlayerDialog.test.ts`
- 修改：`frontend/apps/admin/src/features/players/ui/OnlinePlayersTable.vue`
- 修改：`frontend/apps/admin/src/features/players/ui/OnlinePlayersList.vue`
- 修改：`frontend/apps/admin/src/features/players/ui/OnlinePlayersView.vue`
- 修改：`frontend/apps/admin/src/features/players/ui/OnlinePlayersView.test.ts`

**组件图：**

- `OnlinePlayersTable`：只呈现桌面数据和每行 `UDropdownMenu`，向上发出 `kickPlayer(player)`；不拥有请求状态。
- `OnlinePlayersList`：只呈现移动数据和同一动作菜单，向上发出 `kickPlayer(player)`；不拥有请求状态。
- `KickPlayerDialog`：接收固定 `player`、`open`、`isSubmitting`、`feedback`，本地持有原因输入，通过 `confirm(reason)`、`cancel` 和 `update:open` 向上通信。
- `OnlinePlayersView`：组合在线查询与 `useKickPlayer`，固定 `selectedPlayer`，通过现有 `UApp` 下的 `useToast()` 呈现成功通知，并处理会话过期、冲突刷新和关闭策略。

**接口：**

```typescript
// OnlinePlayersTable.vue / OnlinePlayersList.vue
defineEmits<{
  copyIdentity: [combinedId: string]
  kickPlayer: [player: OnlinePlayer]
}>()

// KickPlayerDialog.vue
const open = defineModel<boolean>('open', { required: true })
const props = defineProps<{
  player: OnlinePlayer | null
  isSubmitting: boolean
  feedback: KickPlayerFeedback | null
}>()
defineEmits<{
  confirm: [reason: string]
  cancel: []
}>()
```

- [ ] **步骤 1：写桌面与移动动作入口失败测试**

  分别 mount `OnlinePlayersTable` 和 `OnlinePlayersList`，断言每名玩家有固定尺寸、aria-label 为“玩家操作：Test Player”的 icon button；菜单项使用 `i-lucide-log-out` 和“踢出玩家”，选择后只发出该行完整 `OnlinePlayer`，不会发出控制台文本。

- [ ] **步骤 2：运行组件入口测试并验证 RED**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm test:unit -- src/features/players/ui/OnlinePlayersView.test.ts
  ```

  预期：测试因桌面/移动组件没有 `kickPlayer` 事件和动作按钮而失败。

- [ ] **步骤 3：实现动作菜单与事件上抛**

  使用 Nuxt UI v4 `UDropdownMenu :items` 和 `DropdownMenuItem`；每个菜单项的 `onSelect` 捕获当前行 player 并 `$emit('kickPlayer', player)`。表格新增窄“操作”列，列表把菜单按钮放在标题行右侧；保持固定按钮尺寸，不能让菜单改变表格或卡片高度。

- [ ] **步骤 4：写确认对话框失败测试**

  覆盖：打开时显示固定玩家名、平台和身份；轮询替换父列表不改变传入目标；trim 后空白和超过 200 字符禁用确认；1/200 字符可提交；提交期间 textarea、取消、关闭和确认全部锁定；反馈使用 `role="status"`；不得渲染 Token、IP、原始异常或未批准玩家字段。

  ```typescript
  await wrapper.get('textarea').setValue('  违反服务器规则  ')
  await wrapper.get('[data-testid="confirm-kick-player"]').trigger('click')
  expect(wrapper.emitted('confirm')).toEqual([['违反服务器规则']])
  ```

- [ ] **步骤 5：运行对话框测试并验证 RED**

  ```powershell
  pnpm test:unit -- src/features/players/ui/KickPlayerDialog.test.ts
  ```

  预期：测试因组件不存在而失败。

- [ ] **步骤 6：实现 Nuxt UI v4 确认对话框**

  使用 `UModal v-model:open`、`title`、`description`、`#body`、`#footer`，`UTextarea v-model`、`rows`、`maxlength="200"` 和明确的 `UButton`。按钮文案为“踢出玩家”，icon 为 `i-lucide-log-out`，危险色使用 Nuxt UI semantic `error`；关闭按钮在提交期间禁用或隐藏。对话框只在新目标打开时清空原因，普通父组件重渲染不清空输入。

- [ ] **步骤 7：写 View 编排失败测试**

  mock `useKickPlayer` 和 `useToast`，覆盖桌面/移动入口都固定 `selectedPlayer`；确认调用 submit；成功关闭、调用 `toast.add({ title: '已踢出 Test Player', color: 'success' })` 并 `await refresh()`；`player_not_online` 与 `player_identity_changed` 触发刷新且不自动重试；busy/ready/timeout/audit unavailable 保留对话框和输入；unknown 显示“结果尚无法确认”；401 沿用 `/login?redirect=/players`；403 禁用或隐藏动作。

- [ ] **步骤 8：实现 View 编排**

  `OnlinePlayersView` 使用 `shallowRef<OnlinePlayer | null>` 固定目标；把同一个 `openKickDialog` 传给桌面和移动事件；确认时调用 `useKickPlayer.submit(selectedPlayer, reason)`。成功后通过 `useToast().add` 显示包含固定玩家名的 `success` 通知、关闭对话框并刷新；冲突刷新但不把刷新结果解释为审计终态；未知反馈不自动重试。继续保持 route view 为组合层，不把 API 逻辑塞入表格、列表或对话框。

- [ ] **步骤 9：运行玩家 UI 回归并验证 GREEN**

  ```powershell
  pnpm test:unit -- src/features/players/ui/KickPlayerDialog.test.ts src/features/players/ui/OnlinePlayersView.test.ts
  pnpm typecheck
  ```

  预期：组件行为测试和 Vue/TypeScript 类型检查通过，没有未经声明的 emit、可变 prop 或 overlay 类型错误。

- [ ] **步骤 10：运行 Admin 聚焦回归并记录审查点**

  ```powershell
  pnpm test:unit -- src/features/players
  pnpm lint
  ```

  预期：全部玩家 feature 测试与 lint 通过；桌面/移动入口复用同一确认流程；不执行 Git 提交。

### 任务 7：同步当前文档并运行聚合门

**文件：**

- 修改：`docs/architecture.md`
- 修改：`docs/design.md`
- 修改：`docs/test.md`
- 按实现后的精确命令或契约需要修改：`backend/README.md`
- 按实现后的精确命令或契约需要修改：`frontend/apps/admin/README.md`
- 保持不变：`docs/PRD.md`
- 保持不变：`docs/architecture/backend-target-blueprint.md`
- 保持不变：`CHANGELOG.md`

**接口：**

- 消费：任务 1 至 6 的已实现代码、自动化输出和工作区 diff。
- 产出：只把已实现并验证的玩家动作、SQLite 审计、Admin 确认流程与证据缺口提升到 Current 文档；不把 dated plan 当作当前事实来源。

- [ ] **步骤 1：先运行后端定向测试**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~KickPlayer|FullyQualifiedName~PlayerActionAudit|FullyQualifiedName~GameThreadDispatcherTests|FullyQualifiedName~DependencyInjectionTests|FullyQualifiedName~DependencyRulesTests|FullyQualifiedName~OwinWebHostTests"
  ```

  预期：所有新增后端切片与受影响边界测试通过。

- [ ] **步骤 2：运行后端 Release 聚合门**

  ```powershell
  dotnet build backend/7DPanel.sln --configuration Release --no-restore --target:Rebuild
  dotnet test backend/7DPanel.sln --configuration Release --no-build --no-restore
  ```

  预期：Release Rebuild 零错误，后端全量测试零失败。若 restore 状态不足，先运行 `dotnet restore backend/7DPanel.sln`，再重新执行同一聚合门并记录该前置动作。

- [ ] **步骤 3：运行 Admin 聚合门**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm lint
  pnpm typecheck
  pnpm test:unit
  pnpm build
  ```

  预期：lint、Vue/TypeScript 类型检查、全部 Vitest 和生产构建零失败。Playwright 真实环境变量未设置时可以 skip，但不得把 skip 记录为真实游戏验证通过。

- [ ] **步骤 4：更新当前架构事实**

  在 `docs/architecture.md` 记录已实现的 `KickPlayerUseCase`、`IPlayerActions`、`IPlayerActionAuditTrail`、SQLite `002_PlayerActionAudit.sql`、SevenDays 原生动作、Owner POST 契约、single-flight、审计恢复和 DI 所有权。明确 `Succeeded` 是“安排断开”，且真实玩家断开仍未验证；不得宣称 Ban/Teleport/通用审计查询存在。

- [ ] **步骤 5：更新当前界面设计与测试策略**

  `docs/design.md` 记录玩家动作菜单、固定目标确认、1 至 200 字符原因、提交锁定、成功刷新、冲突/busy/unknown 状态；`docs/test.md` 记录实际通过的自动化数量和边界，并保留 Windows `v3.0.1-b4` 拒绝原因、延迟断开、列表更新与 SQLite 审计一致性的证据缺口。

- [ ] **步骤 6：只在最近所有者需要时更新 README**

  若新增精确模块测试命令或 API smoke 入口，在最近的 README 记录并从高层链接；不复制仓库聚合命令，不加入机器路径、凭据或真实 Token。若现有 README 已能导航到权威文档且没有新命令，则保持 README 不变并在完成报告说明原因。

- [ ] **步骤 7：执行文档与敏感信息检查**

  ```powershell
  git diff --check
  rg -n "TB[D]|TO[D]O|PLACEHOLDE[R]|access_token=|Authorization: Bearer [^<]" docs backend/README.md frontend/apps/admin/README.md
  git status --short
  git diff --stat
  ```

  预期：`git diff --check` 无输出；占位符和明文 Token 搜索无新增命中；状态只包含本计划范围内文件。`rg` 命中合法的协议说明时逐条人工确认，而不是机械删除。

- [ ] **步骤 8：进行最终语义审查**

  逐项对照主规格确认：请求字段、稳定错误码、审计转换、single-flight 所有权、开始前/开始后取消、Admin 未知结果、真实证据缺口均有对应实现与测试。检查 Application 无 Adapter 引用、Web 无游戏/SQLite引用、SevenDays 与 Persistence 不互相引用、没有控制台字符串路径或新 Domain 项目。

- [ ] **步骤 9：报告完成状态并等待 Git 授权**

  报告新增/修改文件、精确测试数量、构建结果、Playwright skip 和真实游戏证据缺口。不得执行真实服务器踢出，不得更新 `CHANGELOG.md`，不得执行 Git 提交；如用户随后明确授权提交，再按文档、后端和前端的实际 diff 决定是否分批。

## 完成标准

- Owner-only `POST /api/v1/players/{entityId}/kick` 按批准字段和稳定 Problem Details 工作。
- Application 在动作前持久化 `Pending`，准确完成 `Succeeded/Failed/Unknown`，busy 不产生审计。
- SevenDays Adapter 在主线程重新校验实体与平台身份并调用 `GameUtils.KickPlayerForClientInfo(ManualKick, reason)`。
- SQLite migration 可重复执行，审计永久保留，重复完成不覆盖证据，遗留 `Pending` 在启动时恢复为 `Unknown`。
- Admin 桌面与移动入口共享固定目标确认流程，原因边界、提交锁定、刷新和未知结果语义符合规格。
- 后端 Release build、后端全量测试、Admin lint/typecheck/Vitest/build 全部通过。
- 当前架构、界面和测试文档只记录已经实现并验证的事实。
- Windows `v3.0.1-b4` 真实拒绝消息、约 0.5 秒断开、列表更新和 SQLite 审计一致性仍未执行时被明确保留为证据缺口。
- 工作区没有越界重构、敏感信息、自动 Git 操作或未经批准的未来能力。