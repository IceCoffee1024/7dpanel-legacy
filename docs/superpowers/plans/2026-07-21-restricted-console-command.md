---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-21-restricted-console-command-design.md
last_updated: "2026-07-21"
---

# 受限控制台命令纵向切片实施计划

> 追溯说明：本计划在提交 `4e07302 feat(backend): add restricted console command slice` 完成后补录。全部复选框记录该提交已经实施的工作，不是当前待办，也不证明对应规格曾在实施前获得批准。当前事实和验证门禁以[系统架构](../../architecture.md)与[测试策略](../../test.md)为准。

## 主规格

本计划只记录[受限控制台命令纵向切片设计规格](../specs/2026-07-21-restricted-console-command-design.md)所描述的切片，追踪 `CAP-02`、`NFR-01` 和 `NFR-02`。它保留 `171cae5` 建立的 lifecycle/readiness 基础，但以轻量 `GameThreadDispatcher` 替换其中已删除的通用 scheduler。

## 实施约束

- 当前公开命令白名单只有精确 `version`，且只能由 `Owner` 或 `Admin` 调用。
- 未支持命令必须在调用游戏 Adapter 或投递游戏主线程前拒绝。
- Application 只依赖 BCL 和自身类型，不接收 Hosting、Web、SevenDays、SQLite、Unity 或 7DTD 类型。
- 类型化 Gateway 只暴露 `ExecuteVersionAsync`，不建立任意命令字符串端口。
- Dispatcher 不拥有通用队列、逐帧 pump、独立生命周期或无真实消费者的扩展点。
- 本计划只记录历史动作，不授权重新执行真实 7DTD smoke、Git commit 或其他 Git 操作。

## 任务 1：创建首个 Application 用例边界

**文件**

- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/LSTY.SevenDPanel.Application.csproj`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/ConsoleCommands/IRestrictedConsoleGateway.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/ConsoleCommands/ExecuteConsoleCommandUseCase.cs`
- 新建：`backend/src/Core/LSTY.SevenDPanel.Application/ConsoleCommands/ConsoleCommandResult.cs`
- 修改：`backend/7DPanel.sln`

- [x] 创建只依赖 BCL 的 Application 项目，并把它加入解决方案和发布边界。
- [x] 定义只暴露 `ExecuteVersionAsync(CancellationToken)` 的类型化 `IRestrictedConsoleGateway`。
- [x] 实现 `ExecuteConsoleCommandUseCase`，先 trim 输入，再按 ordinal-ignore-case 精确匹配 `version`。
- [x] 让空输入产生稳定参数错误，未支持命令产生 `ConsoleCommandNotSupportedException` 且不调用 Gateway。
- [x] 使用不可变 `ConsoleCommandResult` 返回标准化命令名和输出列表，不泄漏游戏对象。

## 任务 2：实现认证 HTTP 命令入口

**文件**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ConsoleCommandsController.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/LSTY.SevenDPanel.Adapters.Web.csproj`

- [x] 新增 `POST /api/v1/console/commands`，并以 `[Authorize(Roles = "Owner,Admin")]` 限制调用者。
- [x] 接受 `{ "command": "version" }`，成功返回 `{ command, output }`。
- [x] 在调用用例前检查 `GameReadinessState.Ready`，游戏未就绪返回 503 `game_not_ready`。
- [x] 把空输入、未支持命令、single-flight 忙和主线程启动超时映射为稳定 Problem Details。
- [x] 保持匿名 401、角色 403、关联 traceId、Path-only instance 和现有 Basic/Bearer challenge 契约。

## 任务 3：建立轻量主线程 Dispatcher

**文件**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Runtime/GameThreadDispatcher.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/GameThreadDispatcherTests.cs`

- [x] 已在游戏主线程时直接执行委托并返回真实结果或异常。
- [x] 非主线程时通过 `ThreadManager.AddSingleTaskMainThread` 投递，不维护自有任务队列。
- [x] 用每请求原子 `Pending -> Running -> Completed` 状态处理投递、取消、启动超时和实际执行竞争。
- [x] 排队取消或 5 秒启动超时赢得竞争后，使稍后到达游戏线程的委托成为 no-op。
- [x] 委托进入 `Running` 后忽略调用方取消或启动截止时间，等待并传播真实结果或异常。
- [x] 使用 `RunContinuationsAsynchronously`，避免 HTTP continuation 在游戏主线程内联执行。

## 任务 4：实现只读 SevenDays Gateway 与 single-flight

**文件**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/ConsoleCommands/SevenDaysRestrictedConsoleGateway.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/LSTY.SevenDPanel.Adapters.SevenDays.csproj`

- [x] 让 SevenDays Adapter 实现 Application 的 `IRestrictedConsoleGateway`，但不让 Application 或 Web 引用游戏类型。
- [x] 用原子 single-flight 门禁保证同一时刻最多一个版本命令进入 Dispatcher。
- [x] 并发请求立即返回 `ConsoleCommandBusyException`，不排队等待，也不增加主线程任务。
- [x] 在游戏主线程调用 `SdtdConsole.ExecuteSync("version")`，并在同一线程复制共享输出列表。
- [x] Dispatcher 完成后只向 Application 返回不可变命令结果；异常保持可观察，不伪装为成功输出。

## 任务 5：删除被替代的通用 scheduler

**文件**

- 删除：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Runtime/MainThreadRequest.cs`
- 删除：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Runtime/MainThreadSchedulingAbstractions.cs`
- 删除：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Runtime/SevenDaysMainThreadScheduler.cs`
- 删除：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Runtime/ThreadManagerMainThreadDispatcher.cs`
- 删除：`backend/tests/LSTY.SevenDPanel.Tests/SevenDaysMainThreadSchedulerTests.cs`

- [x] 保留 `171cae5` 建立的 `SevenDaysModEvents`、`SevenDaysGameLifecycleAdapter` 和 `GameReadinessState`。
- [x] 删除没有第二个运行时消费者的 scheduler 接口、请求模型、队列和调度实现。
- [x] 把仍适用于当前消费者的取消、启动超时和执行异常语义收敛到 `GameThreadDispatcherTests`。
- [x] 不为测试替换保留生产抽象，也不创建通用组件注册表或逐帧 pump。
- [x] 在本规格记录替代关系，不为已删除 scheduler 另建一组追溯计划。

## 任务 6：接入组合根与发布边界

**文件**

- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/LSTY.SevenDPanel.csproj`
- 修改：`backend/scripts/Publish-Mod.ps1`
- 修改：`backend/scripts/README.md`

- [x] 在唯一组合根注册 `SevenDaysRestrictedConsoleGateway`、`IRestrictedConsoleGateway` 和 `ExecuteConsoleCommandUseCase`。
- [x] 让 Web Adapter 与 SevenDays Adapter 只通过 Application 类型相连，不产生 Adapter-to-Adapter 引用。
- [x] 把 Application DLL 加入发布脚本和六个产品 DLL 完整性门禁。
- [x] 保持游戏提供程序集和现有 SQLite、DI、OAuth、Channels 依赖的发布排除与完整性规则。
- [x] 在脚本说明中记录 Application DLL 和当前只读命令 smoke 边界，不复制仓库聚合命令。

## 任务 7：覆盖用例、API、DI 与依赖规则

**文件**

- 新建：`backend/tests/LSTY.SevenDPanel.Tests/ConsoleCommandTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyInjectionTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyRulesTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj`

- [x] Application 测试覆盖 `version` 大小写与空白标准化，以及未支持命令不进入 Gateway。
- [x] Katana 集成测试覆盖匿名 401、认证成功、空命令、未支持命令、游戏未 Ready 和 single-flight 忙。
- [x] 精确断言成功 `{ command, output }` 与稳定 Problem Details code，并验证拒绝路径没有 Gateway 调用。
- [x] DI 测试证明用例和类型化 Gateway 可解析，并保持 request scope 与 singleton 所有权正确。
- [x] 依赖测试证明 Application 的引用白名单、Adapter 方向、六个产品 DLL 和唯一入口约束。

## 任务 8：同步当前文档与完成验证

**文件**

- 修改：`docs/PRD.md`
- 修改：`docs/architecture.md`
- 修改：`docs/architecture/backend-target-blueprint.md`
- 修改：`docs/test.md`

- [x] 在产品合同中明确首个命令切片只支持 `Owner`/`Admin` 的只读 `version`，任意命令和状态变更命令不属于当前能力。
- [x] 在当前架构记录 Application 项目、类型化 Gateway、single-flight、Dispatcher 状态竞争和 HTTP 错误契约。
- [x] 在 Target 蓝图保留未来多个类型化游戏端口方向，但不把当前 single-flight 推广为通用动作策略。
- [x] 在测试策略记录用例、Dispatcher、Katana 和 Windows 真实主线程往返证据，并保留 Linux、性能阈值和状态变更动作缺口。
- [x] 完成 Release Rebuild、后端全量测试、发布检查和 Windows `v3.0.1-b4` 认证 `version` 命令 smoke。

## 完成记录

- 提交 `4e07302` 创建首个 Application 项目和认证 HTTP -> Application -> SevenDays Gateway -> 游戏主线程的完整纵向路径。
- 该提交删除 `171cae5` 的通用 scheduler，保留生命周期订阅与 readiness 基础，并用更窄的 `GameThreadDispatcher` 承担当前真实消费者所需语义。
- Windows `v3.0.1-b4` smoke 返回了真实 `version` 输出，随后正常关服并确认端口不可用；精确自动化数量和证据细节由[测试策略](../../test.md)维护。
- 任意命令、状态变更动作、审计、多个主线程生产者、Linux 往返和性能阈值仍未由本切片完成。
- 未通过本追溯计划执行新的构建、发布、真实进程 smoke 或 Git 提交。