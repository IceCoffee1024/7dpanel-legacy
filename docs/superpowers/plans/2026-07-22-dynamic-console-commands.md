---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-22-dynamic-console-commands-design.md
last_updated: "2026-07-22"
---

# 动态控制台命令与全局审计实施计划

> **面向智能体执行者：** 实施时必须使用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans`，并逐任务进行规格符合性与代码质量审查。以下步骤使用复选框跟踪。

**对应规格：** [动态控制台命令设计规格](../specs/2026-07-22-dynamic-console-commands-design.md)

**目标：** 让 `Owner` 和 `Admin` 通过有界 FIFO 执行全部 7DTD 已注册命令，并在不改变任何标准 `SdtdConsole` 调用结果的前提下异步、尽力地持久化全局命令审计。

**架构：** Application 将现有 `version` 白名单端口替换为携带操作者和原始命令的动态端口；SevenDays Adapter 用容量 32 的单消费者 Channel 为 HTTP 请求提供 FIFO 和主线程执行；独立的容量 256 审计 Channel 接收 Harmony 对 `SdtdConsole.executeCommand` 的 Prefix/Postfix/Finalizer 快照，并通过 Application 审计端口写入 SQLite。7DTD 原生 `ExecuteAsync` 队列、现有控制台日志服务和 SSE 均不改变。

**技术栈：** .NET Framework 4.8、C# 11、xUnit v3、System.Threading.Channels、Harmony 2（游戏提供的 `0_TFP_Harmony`）、ASP.NET Web API 2、Katana OWIN、Dapper、DbUp、Microsoft.Data.Sqlite、7DTD Dedicated Server `v3.0.1-b4`。

## 全局约束

- 只执行 7DTD 当前已注册命令；不建立 7DPanel 白名单、命令注册表、脚本引擎或命令结果业务解释器。
- `Owner` 和 `Admin` 可调用 HTTP 命令入口；Controller 从 `ClaimTypes.NameIdentifier` 读取操作者 subject，客户端不能提交或覆盖操作者身份。
- HTTP 命令队列容量固定为 32，严格 FIFO、单消费者、每请求独立，使用 `TryWrite` 在容量满时立即拒绝；不按命令文本合并。
- Application 不 trim 或标准化非空命令原文；只用 `string.IsNullOrWhiteSpace` 拒绝空输入，SevenDays Adapter 将原字符串交给 `SdtdConsole.ExecuteSync`。
- 排队时取消可以阻止执行；工作项进入 Running 后忽略 HTTP 取消并等待真实同步结果或异常。主线程仍沿用 5 秒启动截止时间。
- `SdtdConsole.ExecuteSync`、`ExecuteAsync`、`executeCommand` 和 `Output` 继续按 7DPanel 主线程串行边界处理；不得用 Harmony 替换 7DTD 原生队列或 `Update()`。
- Harmony Patch 目标精确为 `SdtdConsole.executeCommand(string, CommandSenderInfo)`；Prefix 只捕获不可变输入，Postfix 在返回边界复制共享输出，Finalizer 隔离观察失败并保留原异常。
- 命令审计队列容量固定为 256，单消费者、非阻塞 `TryWrite`、fail-open；它不与 `ConsoleLogService`、`ServerEventLiveWindow` 或 `ServerEventHub` 共享容量。
- SQLite 保存完整 `raw_command`、解析后的命令名与逐项参数、逐行输出、来源、操作者、开始/完成时间和 `Completed`/`Threw` 观察结果；不把任意文本输出解释为业务成功。
- 完整命令和参数按敏感数据处理，但本切片不脱敏、不截断，也不增加应用级请求长度、参数长度、输出行数、单行长度或总输出大小限制。
- 审计数据库、队列或消费者失败不得改变命令结果；首次缺口立即写警告，恢复后尽力持久化缺口起止时间、原因和数量，停止摘要保留未恢复缺口计数。
- 不新增结构化命令 SSE、同步 Gateway C# 事件、统一事件服务或前端命令页面；现有 `console-log` SSE 行为保持不变。
- 不修改 `7dtd-reference/`；它仅用于签名和兼容性证据。
- 每个生产行为严格执行 RED、验证 RED、GREEN、验证 GREEN；未观察到预期失败前不得写对应生产实现。
- 本计划不授权 `git commit`、`git push`、`git reset` 或 `git revert`。每个任务完成后只报告 diff 与验证结果，等待独立授权后再执行 Git 操作。

---

### 任务 1：替换 Application 与 HTTP 动态命令合同

**文件：**

- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/ConsoleCommands/ConsoleCommandRequest.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/ConsoleCommands/IConsoleCommandGateway.cs`
- 修改：`backend/src/Core/LSTY.SevenDPanel.Application/ConsoleCommands/ExecuteConsoleCommandUseCase.cs`
- 删除：`backend/src/Core/LSTY.SevenDPanel.Application/ConsoleCommands/IRestrictedConsoleGateway.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ConsoleCommandsController.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/ConsoleCommandTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs`

**接口：**

- 消费：HTTP body 的原始 `command`、`ClaimTypes.NameIdentifier` 和请求 `CancellationToken`。
- 产出：

```csharp
public sealed class ConsoleCommandRequest
{
    public ConsoleCommandRequest(string actorSubject, string command);
    public string ActorSubject { get; }
    public string Command { get; }
}

public interface IConsoleCommandGateway
{
    Task<ConsoleCommandResult> ExecuteAsync(
        ConsoleCommandRequest request,
        CancellationToken cancellationToken);
}

public sealed class ExecuteConsoleCommandUseCase
{
    public Task<ConsoleCommandResult> ExecuteAsync(
        ConsoleCommandRequest request,
        CancellationToken cancellationToken);
}

public sealed class ConsoleCommandQueueFullException : Exception { }
public sealed class ConsoleCommandUnavailableException : Exception { }
```

- `ConsoleCommandResult.Command` 保留调用方提交的精确原文；`Output` 继续在构造时复制为不可变快照。
- 删除 `VersionCommand`、`ConsoleCommandNotSupportedException` 和 `ConsoleCommandBusyException`。队满映射为 503 `console_command_queue_full`，服务停止接收映射为 503 `console_command_unavailable`；空命令、游戏未就绪和主线程启动超时保持现有稳定错误。

- [x] **步骤 1：写动态命令用例失败测试**

  把 `ConsoleCommandTests` 改为记录 `ConsoleCommandRequest` 的 Gateway，并先覆盖空 actor、空白命令和精确原文透传：

  ```csharp
  [Fact]
  public async Task Arbitrary_command_is_forwarded_without_normalization()
  {
      var gateway = new RecordingConsoleGateway();
      var useCase = new ExecuteConsoleCommandUseCase(gateway);
      const string rawCommand = "  say \"Hello  world\"  ";

      var result = await useCase.ExecuteAsync(
          new ConsoleCommandRequest("owner", rawCommand),
          CancellationToken.None);

      Assert.Equal(rawCommand, gateway.Requests.Single().Command);
      Assert.Equal("owner", gateway.Requests.Single().ActorSubject);
      Assert.Equal(rawCommand, result.Command);
  }
  ```

  空 actor 或空白命令必须在 Gateway 前抛 `ArgumentException`；`version`、状态变更命令和第三方命令使用同一路径，不再存在 unsupported 测试。

- [x] **步骤 2：运行 Application 测试并验证 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter FullyQualifiedName~ConsoleCommandTests
  ```

  预期：编译失败，指出 `ConsoleCommandRequest` 或 `IConsoleCommandGateway` 不存在；失败不得来自测试语法。

- [x] **步骤 3：实现最小 Application 动态端口**

  创建上述请求与 Gateway，删除白名单分支。`ExecuteConsoleCommandUseCase.ExecuteAsync` 只执行参数校验后原样转发：

  ```csharp
  if (request == null) throw new ArgumentNullException(nameof(request));
  if (string.IsNullOrWhiteSpace(request.ActorSubject))
      throw new ArgumentException("A console command actor is required.", nameof(request));
  if (string.IsNullOrWhiteSpace(request.Command))
      throw new ArgumentException("A console command is required.", nameof(request));
  return gateway.ExecuteAsync(request, cancellationToken);
  ```

- [x] **步骤 4：运行 Application 测试并验证 GREEN**

  运行步骤 2 的同一命令。预期：动态命令、空输入和精确原文测试全部通过。

- [x] **步骤 5：写 HTTP 合同失败测试**

  在 `OwinWebHostTests` 用记录型动态 Gateway 覆盖：Owner 与 Admin 提交第三方样例命令成功；操作者来自 `NameIdentifier`；匿名 401；空命令 400；未 Ready 503；队满 503；服务停止接收 503；5 秒主线程启动超时 503。删除 `console_command_not_supported` 和 `console_command_busy` 断言。

  ```csharp
  Assert.Equal("console_command_queue_full", problem.Code);
  Assert.Equal(0, gateway.CallCount); // only for auth/input/readiness rejection paths
  Assert.Equal("owner", gateway.Requests.Single().ActorSubject);
  Assert.Equal("thirdparty.sample  alpha", gateway.Requests.Single().Command);
  ```

- [x] **步骤 6：运行 Katana 命令测试并验证 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~Console_command|FullyQualifiedName~console_command"
  ```

  预期：测试因 Controller 仍构造字符串用例请求、仍映射白名单/busy 或未读取 subject 而失败。

- [x] **步骤 7：实现 HTTP 映射并验证 GREEN**

  Controller 使用 `User.FindFirst(ClaimTypes.NameIdentifier)?.Value` 构造 Application 请求；缺失 subject 沿现有认证失败边界拒绝，不接受 body 中的 actor 字段。捕获新队列异常并返回上述稳定 Problem Details。运行步骤 6 命令，预期全部通过且现有 Basic/Bearer challenge 不变。

- [x] **步骤 8：运行邻近回归**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~ConsoleCommandTests|FullyQualifiedName~OwinWebHostTests|FullyQualifiedName~AuthenticationTests"
  ```

  预期：相关用例、OWIN 和认证测试通过；不执行 Git 操作。

### 任务 2：实现容量 32 的 HTTP FIFO 与主线程执行

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/ConsoleCommands/ConsoleCommandWorkItem.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/ConsoleCommands/SevenDaysConsoleCommandService.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/ConsoleCommands/ConsoleCommandSourceContext.cs`
- 删除：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/ConsoleCommands/SevenDaysRestrictedConsoleGateway.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/SevenDaysConsoleCommandServiceTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/GameThreadDispatcherTests.cs`

**接口：**

- 消费：任务 1 的 `IConsoleCommandGateway` 和 `ConsoleCommandRequest`，现有 `GameThreadDispatcher.Enqueue<T>`。
- 产出：`SevenDaysConsoleCommandService : IConsoleCommandGateway, IModRuntime, IDisposable`，生产构造函数使用容量 32、5 秒主线程启动截止时间和真实执行 delegate；internal 构造函数注入容量、Dispatcher delegate 与日志。

```csharp
internal delegate Task<ConsoleCommandResult> DispatchConsoleCommand(
    ConsoleCommandRequest request,
    TimeSpan startTimeout,
    CancellationToken cancellationToken);

public sealed class SevenDaysConsoleCommandService :
    IConsoleCommandGateway,
    IModRuntime,
    IDisposable
{
    public const int DefaultQueueCapacity = 32;
    public Task<ConsoleCommandResult> ExecuteAsync(
        ConsoleCommandRequest request,
        CancellationToken cancellationToken);
    public void Start();
    public void MarkGameReady();
    public void Stop();
    public void Dispose();
}
```

- `ConsoleCommandWorkItem` 使用原子 `Pending -> Running -> Completed`。排队取消只有在 Pending 时完成 Task 并使 consumer 跳过；进入 Running 后释放 cancellation registration，并以 `CancellationToken.None` 等待 `GameThreadDispatcher` 的真实结果。
- Channel 使用 `BoundedChannelFullMode.Wait`、`SingleReader=true`、`SingleWriter=false`、`AllowSynchronousContinuations=false`；生产者只调用 `TryWrite`，失败抛 `ConsoleCommandQueueFullException`。

- [x] **步骤 1：写 FIFO、饱和和独立结果失败测试**

  用可控 Dispatcher 阻塞第一个工作项，连续提交命令并断言接收顺序、容量和输出不串线：

  ```csharp
  Assert.Equal(new[] { "first", "second", "third" }, dispatcher.StartedCommands);
  Assert.Equal(new[] { "first-output" }, firstResult.Output);
  Assert.Equal(new[] { "second-output" }, secondResult.Output);
  await Assert.ThrowsAsync<ConsoleCommandQueueFullException>(() => overflow);
  ```

  容量测试使用 internal 构造函数的 `queueCapacity: 2`，明确容量只计算等待 Channel 中的项，正在执行项不占等待槽。

- [x] **步骤 2：运行服务测试并验证 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter FullyQualifiedName~SevenDaysConsoleCommandServiceTests
  ```

  预期：编译失败，指出新服务不存在。

- [x] **步骤 3：实现最小生命周期与 FIFO**

  `Start()` 启动唯一 consumer 并等待 ready；`ExecuteAsync` 只在 accepting 时创建工作项并 `TryWrite`；consumer 顺序调用 `TryStart()`、Dispatcher 和 `TrySetResult/Exception`。真实执行 delegate 固定为：

  ```csharp
  return GameThreadDispatcher.Enqueue(
      "7DPanel.Console." + request.Command,
      () =>
      {
          using (ConsoleCommandSourceContext.Push("7dpanel-http", request.ActorSubject))
          {
              var output = SdtdConsole.Instance.ExecuteSync(request.Command, null);
              return new ConsoleCommandResult(
                  request.Command,
                  output ?? (IEnumerable<string>)Array.Empty<string>());
          }
      },
      MainThreadStartTimeout,
      cancellationToken);
  ```

  本任务创建 `ConsoleCommandSourceContext`，使用 `[ThreadStatic]` 栈式 previous 值和幂等 `IDisposable` 恢复；它只携带 `source` 与可空 `actorSubject`，任务 4 的 Patch 直接读取该稳定 seam，不再改动其合同。

- [x] **步骤 4：运行 FIFO 测试并验证 GREEN**

  运行步骤 2 的命令。预期：FIFO、队满和独立输出测试通过。

- [x] **步骤 5：写取消、异常与停止失败测试**

  覆盖：排队取消后永不调用 Dispatcher；Running 后取消仍返回真实结果；单项异常不终止 consumer；Stop 后拒绝新请求；Stop 完成 writer 并在 5 秒内排空已开始项，未开始项完成 `ConsoleCommandUnavailableException`；重复 Start/Stop/Dispose 幂等；`MarkGameReady()` 无操作。

- [x] **步骤 6：运行测试验证 RED，补齐状态机后验证 GREEN**

  先运行步骤 2 并观察至少一个取消或停止测试失败；随后实现 `ConsoleCommandWorkItem` 原子状态与限时 Stop，再运行同一命令。预期全部通过且测试不使用 `Thread.Sleep`。

- [x] **步骤 7：运行 Dispatcher 与命令服务回归**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~SevenDaysConsoleCommandServiceTests|FullyQualifiedName~GameThreadDispatcherTests|FullyQualifiedName~ConsoleCommandTests"
  ```

  预期：排队取消、启动超时和 Running 后真实结果语义全部通过。

### 任务 3：定义 SQLite 命令审计模型与原子写入

**文件：**

- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/ConsoleCommands/ConsoleCommandAuditEntry.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/ConsoleCommands/ConsoleCommandAuditGap.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/ConsoleCommands/IConsoleCommandAuditStore.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Migrations/003_ConsoleCommandAudit.sql`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqliteConsoleCommandAuditStore.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/SqliteConsoleCommandAuditStoreTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/SqliteAuthenticationStoreTests.cs`

**接口：**

```csharp
public enum ConsoleCommandCompletionKind { Completed, Threw }

public sealed class ConsoleCommandAuditEntry
{
    public ConsoleCommandAuditEntry(
        string auditId,
        string rawCommand,
        IEnumerable<string> tokens,
        IEnumerable<string> output,
        string source,
        string? actorSubject,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        ConsoleCommandCompletionKind completionKind,
        string? exceptionType);
}

public sealed class ConsoleCommandAuditGap
{
    public ConsoleCommandAuditGap(
        string gapId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        long droppedCount,
        string reason);
}

public interface IConsoleCommandAuditStore
{
    void Append(ConsoleCommandAuditEntry entry);
    void AppendGap(ConsoleCommandAuditGap gap);
}
```

- `Tokens[0]` 是解析后的命令名，其余元素按 ordinal 写入参数子表；空或解析失败允许 tokens 为空。输出逐行写入输出子表，不做截断或 JSON/string 拼接。
- `Append` 在一个 immediate transaction 中写主记录、参数和输出；任一步失败整条审计回滚。`AppendGap` 按 `gap_id` 幂等插入。

- [x] **步骤 1：写 migration 与完整原文失败测试**

  使用真实临时 SQLite 和 DbUp，断言 migration 可重复、SchemaVersions 为 3，并写入包含引号、双空格、Unicode、空参数和多行输出的记录：

  ```csharp
  Assert.Equal(rawCommand, row.RawCommand);
  Assert.Equal(new[] { "say", "Hello  world", "密钥=原文" }, arguments);
  Assert.Equal(new[] { "line 1", "line 2" }, output);
  Assert.Equal("7dpanel-http", row.Source);
  Assert.Equal("owner", row.ActorSubject);
  ```

- [x] **步骤 2：运行 SQLite 测试并验证 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~SqliteConsoleCommandAuditStoreTests|FullyQualifiedName~SqliteAuthenticationStoreTests.Upgrade_creates_schema"
  ```

  预期：测试因 migration、模型和 Store 不存在而失败。

- [x] **步骤 3：实现精确 migration**

  创建 `console_command_audit`、`console_command_audit_argument`、`console_command_audit_output` 和 `console_command_audit_gap`。主表至少包含 `audit_id` 主键、`raw_command`、可空 `command_name`、`source`、可空 `actor_subject`、毫秒 UTC 起止时间、`completion_kind CHECK IN ('Completed','Threw')` 和可空 `exception_type`；子表以 `(audit_id, ordinal)` 为主键并通过 `ON DELETE CASCADE` 外键关联。gap 表保存唯一 id、起止毫秒、正数 dropped_count 和 reason。

- [x] **步骤 4：实现 Store 并验证 GREEN**

  Store 每次调用使用 `SqliteConnectionFactory.Open()` 和 `BeginTransaction(deferred: false)`；Dapper 循环插入参数/输出，最后提交。运行步骤 2，预期 migration、完整原文、顺序、事务回滚和 gap 幂等测试全部通过。

- [x] **步骤 5：写并发与锁竞争失败测试**

  覆盖多个 Store 实例并发写不同 audit id 不串记录；重复 audit id 原子失败；外部 immediate transaction 锁住数据库时 `Append` 抛出且释放锁后下一条成功；参数或输出插入失败不留下主记录。

- [x] **步骤 6：运行 SQLite 审计回归**

  先观察新增锁竞争测试 RED，补齐短事务实现后运行：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~SqliteConsoleCommandAuditStoreTests|FullyQualifiedName~SqlitePlayerActionAuditTrailTests|FullyQualifiedName~SqliteAuthenticationStoreTests"
  ```

  预期：身份、玩家动作和命令审计的真实 SQLite 测试全部通过。

### 任务 4：实现 Harmony 最终执行点观察与容量 256 异步审计

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/ConsoleCommands/ConsoleCommandExecutionObservation.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/ConsoleCommands/ConsoleCommandExecutionPatch.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/ConsoleCommands/ConsoleCommandAuditService.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/ConsoleCommandExecutionPatchTests.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/ConsoleCommandAuditServiceTests.cs`

**接口：**

- 消费：任务 3 的 `IConsoleCommandAuditStore`，`SdtdConsole.executeCommand` 的原始参数、返回值和异常，以及任务 2 的 HTTP source scope。
- 产出：

```csharp
internal sealed class ConsoleCommandPatchState
{
    public string AuditId { get; }
    public string RawCommand { get; }
    public IReadOnlyList<string> Tokens { get; }
    public string Source { get; }
    public string? ActorSubject { get; }
    public DateTimeOffset StartedAtUtc { get; }
}

public static class ConsoleCommandExecutionPatch
{
    internal static void Prefix(
        SdtdConsole __instance,
        string _command,
        CommandSenderInfo _senderInfo,
        out ConsoleCommandPatchState __state);
    internal static void Postfix(
        List<string>? __result,
        ConsoleCommandPatchState __state);
    internal static Exception? Finalizer(
        Exception? __exception,
        ConsoleCommandPatchState? __state);
}

public sealed class ConsoleCommandAuditService : IModRuntime, IDisposable
{
    public const int DefaultQueueCapacity = 256;
    internal static IDisposable Subscribe(
        Action<ConsoleCommandExecutionObservation> observer);
    internal bool TryPublish(ConsoleCommandExecutionObservation observation);
    public void Start();
    public void MarkGameReady();
    public void Stop();
}
```

- Prefix 使用游戏的 `__instance.tokenizeCommand(_command)` 并立即复制 tokens，避免自写不兼容解析器；原方法随后按原语义再次 tokenize。嵌套调用以 Harmony `__state` 隔离。
- 来源优先读取 `ConsoleCommandSourceContext`；否则按 `CommandSenderInfo` 映射为 `remote-client`、`network` 或 `local-game`。不得把 `RemoteClientInfo.ToString()` 或网络描述当 actor subject 持久化。
- Patch 只向进程内 observer 做一次同步、非阻塞通知；无订阅者、observer 抛错或队满都吞掉观察侧异常并保留 `__result`/`__exception`。

- [x] **步骤 1：写 Patch 纯函数失败测试**

  直接调用 Prefix/Postfix/Finalizer seam，覆盖 HTTP source、三种原生来源、完整 tokens、连续与嵌套 state、null/空命令、共享输出返回后立即复制、原方法异常和 observer 异常：

  ```csharp
  Assert.Same(originalException, returnedException);
  Assert.Equal(rawCommand, observation.RawCommand);
  Assert.Equal(new[] { "say", "Hello  world" }, observation.Tokens);
  Assert.Equal(new[] { "before mutation" }, observation.Output);
  Assert.Equal("7dpanel-http", observation.Source);
  ```

- [x] **步骤 2：运行 Patch 测试并验证 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter FullyQualifiedName~ConsoleCommandExecutionPatchTests
  ```

  预期：编译失败，指出 Patch、state 或 source context 不存在。

- [x] **步骤 3：实现最小 Patch 与 source context**

  Patch 的公开类型只供 Bootstrap 通过 `typeof(ConsoleCommandExecutionPatch)` 建立 Harmony 生命周期；Prefix/Postfix/Finalizer 和时间/id 工厂保持 internal 测试 seam。Postfix 发布 `Completed`；Finalizer 仅在 `__exception != null` 时发布 `Threw`，并返回原异常。用 state 的一次完成 gate 防止 Postfix 与 Finalizer 双发。

- [x] **步骤 4：运行 Patch 测试并验证 GREEN**

  运行步骤 2。预期：所有来源、复制、嵌套和异常透明性测试通过。

- [x] **步骤 5：写审计服务队列与 fail-open 失败测试**

  用容量 2、可阻塞 Store 覆盖 FIFO、单消费者、队满即时拒绝、Store 异常后继续、Start 订阅失败回滚、Stop 注销后拒绝、限时排空、重复停止和指标。首次 drop/Store failure 必须调用注入日志；恢复后下一次可写时先 `AppendGap`，再 `Append` 当前命令。

- [x] **步骤 6：运行审计服务测试验证 RED，补齐实现后验证 GREEN**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter FullyQualifiedName~ConsoleCommandAuditServiceTests
  ```

  先预期因服务不存在或不记录 gap 而失败；实现与 `ConsoleLogService` 同类但独立的 bounded Channel、metrics 和 5 秒 drain 后，预期全部通过。消费者捕获每条 Store 异常，不终止后续消费。

- [x] **步骤 7：验证没有结构化命令 SSE 回归**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~ConsoleCommandExecutionPatchTests|FullyQualifiedName~ConsoleCommandAuditServiceTests|FullyQualifiedName~ConsoleLogServiceTests|FullyQualifiedName~ServerEvent"
  ```

  预期：命令观察与现有 `console-log`/lifecycle replay 测试通过；`ServerEvent` 名称仍只有现有事件，没有 command 事件。

### 任务 5：组合独立 Harmony 与运行时生命周期

**文件：**

- 新建：`backend/src/Bootstrap/LSTY.SevenDPanel/Compatibility/ConsoleCommandHarmonyRuntime.cs`
- 新建：`backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/ConsoleCommandRuntime.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/ModMain.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyInjectionTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyRulesTests.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/ConsoleCommandRuntimeTests.cs`

**接口：**

- `ConsoleCommandHarmonyRuntime` 使用独立 Harmony id `com.lsty.7dpanel.console-command-audit`，只 Patch `ConsoleCommandExecutionPatch`，`Dispose()` 只调用该实例的 `UnpatchSelf()`。
- `ConsoleCommandRuntime` 是具体组合器，不建立通用 runtime registry。Start 顺序固定为 audit service -> HTTP command service -> inner `ConsoleLogRuntime/ModHost`；Stop 顺序固定为 inner（先停 OWIN）-> HTTP command service -> audit service，并聚合所有失败。
- `ModMain` 继续首先应用独立的 Assembly.Location Patch；DI/SQLite runtime 成功构造后才创建 command Harmony runtime，启动失败按 adapter -> runtime -> command Harmony -> assembly Harmony 逆序回滚。

- [x] **步骤 1：写组合顺序与失败回滚测试**

  `ConsoleCommandRuntimeTests` 使用记录型 `IModRuntime` 断言：

  ```csharp
  Assert.Equal(
      new[] { "audit:start", "commands:start", "inner:start" },
      order.Take(3));
  Assert.Equal(
      new[] { "inner:stop", "commands:stop", "audit:stop" },
      order.Skip(3));
  ```

  每个 Start/Stop 位置分别注入异常，证明逆序回滚、后续 Stop 仍执行、异常聚合和幂等 Dispose。

- [x] **步骤 2：运行组合测试并验证 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~ConsoleCommandRuntimeTests|FullyQualifiedName~DependencyInjectionTests|FullyQualifiedName~DependencyRulesTests"
  ```

  预期：测试因新 runtime、动态端口或审计 Store 尚未注册而失败。

- [x] **步骤 3：实现 DI 和具体运行时组合**

  注册单例 `SqliteConsoleCommandAuditStore`/`IConsoleCommandAuditStore`、`ConsoleCommandAuditService`、`SevenDaysConsoleCommandService`/`IConsoleCommandGateway`、用例和具体组合 runtime。`SqliteDatabaseBootstrapper.Upgrade()` 仍先于任何接受请求/审计；最终 `IModRuntime` 指向 `ConsoleCommandRuntime`。删除 restricted Gateway 注册。

- [x] **步骤 4：实现独立 Harmony 生命周期**

  `ConsoleCommandHarmonyRuntime` 的生产工厂执行：

  ```csharp
  Harmony.CreateAndPatchAll(
      typeof(ConsoleCommandExecutionPatch),
      "com.lsty.7dpanel.console-command-audit");
  ```

  在 `ModMain.InitMod` 中保持 Assembly.Location Patch 先行，再创建服务 Provider 和命令 Patch；任一步失败只释放 7DPanel 自身资源，不调用全局 `UnpatchAll`，不影响其他 Mod。

- [x] **步骤 5：运行组合测试并验证 GREEN**

  运行步骤 2。预期：DI 可解析动态 Gateway/审计 Store；生命周期顺序和依赖白名单测试通过；只有 Bootstrap 实现 `IModApi`。

- [x] **步骤 6：运行 Release Rebuild**

  ```powershell
  dotnet build backend/7DPanel.sln --configuration Release --no-incremental
  ```

  预期：零错误；不新增 Harmony NuGet 或发布 `0Harmony.dll`，不修改 `7dtd-reference/`。

### 任务 6：完成端到端验证、真实进程证据与文档提升

**文件：**

- 修改：`backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyInjectionTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyRulesTests.cs`
- 修改：`backend/README.md`
- 修改：`docs/architecture.md`
- 修改：`docs/architecture/backend-target-blueprint.md`
- 修改：`docs/test.md`
- 修改：`docs/superpowers/plans/2026-07-22-dynamic-console-commands.md`

**接口：**

- 消费：任务 1-5 的公开 HTTP 合同、FIFO、SQLite migration、Harmony Patch 和运行时生命周期。
- 产出：自动化与 Windows `v3.0.1-b4` 证据；只有已执行并验证的事实才提升到当前架构。

- [x] **步骤 1：补齐完整 Katana 场景并验证 RED**

  在同一真实 OWIN host 内并发发送 `version` 与记录型第三方命令，断言 FIFO、每请求输出、队满 Problem Details、取消不执行和非空原文。通过 SQLite Store 查询 HTTP actor/source 审计；审计 Store 故障时 HTTP 命令仍返回 Gateway 结果并记录 warning。

  ```csharp
  Assert.Equal(HttpStatusCode.OK, commandResponse.StatusCode);
  Assert.Equal("owner", audit.ActorSubject);
  Assert.Equal("7dpanel-http", audit.Source);
  Assert.Equal(rawCommand, audit.RawCommand);
  Assert.DoesNotContain("command", serverEventNames);
  ```

- [x] **步骤 2：运行端到端测试并验证 RED，修复装配缺口后验证 GREEN**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~OwinWebHostTests|FullyQualifiedName~DependencyInjectionTests|FullyQualifiedName~DependencyRulesTests"
  ```

  先观察新增场景因装配或 fake 缺口失败；只修复本切片装配后重跑，预期全部通过。

- [x] **步骤 3：运行后端全量测试**

  ```powershell
  dotnet test backend/7DPanel.sln --configuration Release --no-build
  ```

  预期：全部后端测试通过，无跳过、挂起或通过重跑隐藏的失败。记录实际测试数量，不预填数字。

- [x] **步骤 4：执行发布物检查**

  ```powershell
  backend/scripts/Publish-Mod.ps1
  ```

  预期：六个产品 DLL、Admin `wwwroot`、SQLite 双平台 native 和既有依赖齐全；发布物不含 `0Harmony.dll`、游戏程序集、`7dtd-reference/` 或服务器自有 `data/`。

- [ ] **步骤 5：执行 Windows 真实进程 smoke**

  本轮缺少可安全使用的独立服务器配置，未执行；内置/第三方命令、原生异步队列、非 HTTP 来源、SQLite 故障和正常关服 Patch 卸载继续作为明确缺口。

  使用 [后端脚本指南](../../../backend/scripts/README.md) 的现有发布、启动、认证和停止流程，在未修改的 `v3.0.1-b4` 进程内验证：

  1. Owner Bearer 提交 `version` 与一个测试 Mod 注册命令均返回真实独立输出；并发请求按 FIFO 完成。
  2. 7DTD `ExecuteAsync`/`Update()` 的命令仍按原生顺序执行，没有被 7DPanel 队列消费或重排。
  3. HTTP、原生异步和至少一个非 HTTP 标准入口都产生完整原文、参数、输出与来源审计。
  4. 临时锁住 SQLite 时命令仍执行，日志出现审计缺口告警；恢复后 gap 被持久化且后续审计继续。
  5. `console-log` SSE 仍只有普通日志事件，没有结构化命令事件；正常关服卸载自身 Patch、排空服务并释放端口。

  若仓库没有可安全加载的测试命令 Mod，只验证内置命令并把“第三方注册命令真实进程证据”保留为明确缺口；不得修改私有只读 `7dtd-reference/` 来伪造证据。

- [x] **步骤 6：提升当前文档**

  只有步骤 3-5 实际获得的事实才能写入 `backend/README.md`、`docs/architecture.md` 和 `docs/test.md`。从 Target 蓝图移除已完全实现且不再具有目标意义的重复段落；保留未验证的 Linux、第三方 Mod 或性能证据为目标/缺口。`docs/PRD.md` 和 `docs/design.md` 的批准产品合同不重复改写，未发布前不更新 `CHANGELOG.md`。

- [x] **步骤 7：执行最终文档与差异校验**

  ```powershell
  git diff --check
  git status --short
  ```

  预期：无空白错误；变更仅覆盖计划列出的代码、测试和文档，`7dtd-reference/` 无修改。使用编辑器诊断复核所有修改文件，并在本计划勾选真实完成步骤；不执行 Git 提交。

## 计划自审

- **规格覆盖：** 任务 1 覆盖动态权限与原文合同；任务 2 覆盖有界 FIFO、取消和主线程结果；任务 3 覆盖完整敏感原文与 SQLite；任务 4 覆盖最终执行点、标准来源、fail-open 和无命令 SSE；任务 5 覆盖独立 Patch/运行时所有权；任务 6 覆盖第三方兼容、原生队列不变、真实进程和文档提升。
- **明确排除：** 没有结构化命令 SSE、统一事件服务、同步 Gateway 事件、输入/输出长度限制、命令白名单、第三方业务成功解释或 Harmony 队列替换任务。
- **类型一致性：** `ConsoleCommandRequest`、`IConsoleCommandGateway`、`ConsoleCommandAuditEntry`、`IConsoleCommandAuditStore`、`SevenDaysConsoleCommandService` 和 `ConsoleCommandAuditService` 只在定义后被后续任务消费；异常名与 HTTP 映射保持一致。
- **批准状态：** 主规格为 `Approved`；本计划只链接一个 `primary_spec`，不改变已批准产品或架构决策。