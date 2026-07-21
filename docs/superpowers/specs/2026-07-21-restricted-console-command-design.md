---
state: Current
document_role: Design Spec
last_updated: "2026-07-21"
---

# 受限控制台命令纵向切片设计规格

> 追溯说明：本文在提交 `4e07302 feat(backend): add restricted console command slice` 完成后补录，只还原该切片的设计边界、实施结果和前置替代关系，不证明规格曾在实施前获得批准。当前实现和验证结论以[系统架构](../../architecture.md)与[测试策略](../../test.md)为准。

## 上游与范围

本规格落实[产品需求](../../PRD.md)中的 `CAP-02`、`NFR-01` 和 `NFR-02`，并实现[后端目标架构蓝图](../../architecture/backend-target-blueprint.md)中的 Controller -> Application Use Case -> typed game port -> `GameThreadDispatcher` -> 7DTD 最小纵向路径。

本切片只向 `Owner` 和 `Admin` 开放只读 `version` 命令，用一个真实 HTTP 消费者证明 Application 边界、授权、游戏就绪判断、类型化 Adapter 和主线程往返。它不把任意控制台文本执行包装为通用 API，也不把首个低频只读消费者的调度策略推广到玩家动作、公告、备份或其他生产者。

## 前置历史与替代关系

提交 `171cae5 feat(backend): add runtime lifecycle foundations` 在本切片之前建立了三项基础：

- `GameStartDone` 驱动的独立 game-readiness 边界；
- 三个 7DTD 生命周期事件的隔离订阅、幂等释放和失败回滚；
- 一个通用程度更高的有界 request-reply 主线程 scheduler。

本切片保留前两项作为当前生命周期基础，但删除第三项的 `MainThreadRequest`、`MainThreadSchedulingAbstractions`、`SevenDaysMainThreadScheduler`、`ThreadManagerMainThreadDispatcher` 及其测试，改用不拥有队列和生命周期的轻量 `GameThreadDispatcher`。因此不为 `171cae5` 单独补录完整 scheduler 规格；本文只保存其与当前命令路径有关的前置事实和替代决策。

## 目标

- 创建首个 Application 项目和一个真实用例，证明 Web 与 SevenDays Adapter 不直接互相引用。
- 只允许大小写不敏感、去除首尾空白后的精确 `version`，其他输入在接触游戏 Adapter 前拒绝。
- 提供认证的 `POST /api/v1/console/commands`，只允许 `Owner` 和 `Admin`。
- 在游戏进入 `GameReadinessState.Ready` 前拒绝命令，不把 HTTP Host 存活误当作游戏可执行。
- 通过只暴露 `ExecuteVersionAsync` 的类型化 Gateway 隔离 `SdtdConsole.ExecuteSync` 和共享输出列表。
- 通过轻量 Dispatcher 把非主线程请求投递到 `ThreadManager.AddSingleTaskMainThread`，并精确定义排队取消、启动超时和执行开始后的结果语义。
- 用 single-flight 拒绝并发版本命令，避免当前低频消费者无界增长 7DTD 主线程队列。

## 非目标

- 不执行任意控制台命令，不接受命令参数、命令链、脚本或批处理。
- 不提供改变游戏状态的控制台命令、玩家动作、公告、备份或服务器控制。
- 不建立通用命令注册表、Mediator、Event Bus、反射扫描或字符串到任意 Handler 的映射。
- 不让 Application、Controller 或 Web Adapter 接收 `SdtdConsole`、`ThreadManager`、Unity 或活动 7DTD 对象。
- 不创建通用主线程任务队列、逐帧 pump、独立 Dispatcher 生命周期或当前切片无法验证的统一背压策略。
- 不宣称 Linux 主线程兼容、多个生产 Gateway、状态变更动作审计或关服 draining 已完成。

## 公开 HTTP 契约

| 方法与路径 | 认证 | 成功语义 | 失败语义 |
|---|---|---|---|
| `POST /api/v1/console/commands` | Bearer 或 Basic；角色为 `Owner`/`Admin` | 接受 `{ "command": "version" }`，返回 `{ command, output }` | 使用统一 Problem Details 返回认证、输入、就绪、白名单、忙和主线程启动超时错误 |

稳定错误分类：

- 请求为空或 command 为空：400 `console_command_required`；
- 未列入白名单：400 `console_command_not_supported`；
- 游戏尚未 Ready：503 `game_not_ready`；
- single-flight 已占用：503 `console_command_busy`；
- 排队后 5 秒内未开始：503 `game_thread_timeout`；
- 未认证或角色不足：沿用统一 401/403 Problem Details 和 challenge。

拒绝路径不得调用 Application Gateway，更不得向游戏主线程投递委托。成功输出是执行期间复制的不可变字符串列表，不把 7DTD 共享可变集合暴露到 HTTP 序列化阶段。

## Application 边界

```text
ConsoleCommandsController
  -> ExecuteConsoleCommandUseCase
  -> IRestrictedConsoleGateway.ExecuteVersionAsync
  -> SevenDaysRestrictedConsoleGateway
  -> GameThreadDispatcher
  -> SdtdConsole.ExecuteSync("version")
```

- `ExecuteConsoleCommandUseCase` 是 Application 当前唯一用例，只依赖 BCL 和 `IRestrictedConsoleGateway`。
- 用例先 trim 输入，再按 ordinal-ignore-case 精确匹配 `version`；未支持命令抛出稳定 Application 异常并且不调用 Gateway。
- `IRestrictedConsoleGateway` 只暴露 `ExecuteVersionAsync(CancellationToken)`，避免把任意字符串命令能力提升为核心端口。
- `ConsoleCommandResult` 只包含标准化命令名和不可变输出，不包含游戏对象或 Adapter 异常。
- Bootstrap 负责把唯一 SevenDays Gateway 注册给 Application 用例；Web Controller 只接收用例与技术中立的 runtime status。

## SevenDays Gateway 与 single-flight

- `SevenDaysRestrictedConsoleGateway` 只负责 `version`，在进入 Dispatcher 前用原子门禁保证同一时刻最多一个请求执行。
- 并发请求立即抛出 `ConsoleCommandBusyException`，不排队等待，也不向 `ThreadManager` 增加第二个任务。
- Gateway 在游戏主线程调用 `SdtdConsole.Instance.ExecuteSync("version", null)`，并在主线程内复制共享输出列表。
- Dispatcher 返回后只传播不可变结果；任何游戏 API、共享列表或 Unity 对象都不得跨线程存活。
- single-flight 是当前低频、只读消费者的局部策略。新增玩家动作或其他 Gateway 前必须按真实负载、幂等性、副作用和审计要求重新决定容量与拒绝语义。

## GameThreadDispatcher 语义

- 调用方已在游戏主线程时直接执行委托并返回真实结果或异常。
- 非主线程调用通过 `ThreadManager.AddSingleTaskMainThread` 投递，使用每请求原子 `Pending -> Running -> Completed` 状态，而不是维护自有队列。
- 请求仍为 `Pending` 时，调用取消或 5 秒启动截止时间可以赢得状态竞争；完成 Task 后，稍后到达游戏线程的委托必须成为 no-op。
- 请求一旦进入 `Running`，取消或截止时间不能伪造失败；调用方等待同步游戏操作的真实结果或异常。
- `TaskCompletionSource` 使用 `RunContinuationsAsynchronously`，避免 HTTP continuation 在游戏主线程内联执行。
- 投递 API 同步抛错时完成请求异常；Dispatcher 不吞掉异常，也不把执行失败改写成超时。
- Dispatcher 不拥有 Start/Stop、队列容量、全局统计或逐帧执行预算；这些能力只有出现第二个真实消费者和可验证负载时才重新设计。

## 生命周期与就绪边界

- 前置 `SevenDaysModEvents` 与 `SevenDaysGameLifecycleAdapter` 隔离 `GameStartDone`、`GameShutdown` 和 `WorldShuttingDown`，保持订阅 token 的幂等释放及注册失败回滚。
- `GameStartDone` 只把 runtime readiness 标记为 Ready；OWIN Host 可以更早启动，但命令 Controller 必须独立检查 game readiness。
- 命令切片复用该状态，不让 Web Adapter 直接订阅静态 `ModEvents`。
- 当前 Dispatcher 没有独立生命周期；排队且尚未开始的请求可由 HTTP 取消阻止执行，已开始命令返回真实同步结果。
- 通用关服 draining、状态变更动作终态和跨多个生产 Gateway 的协调仍属于后续设计。

## 验证标准

- Application 测试证明空输入失败、`version` 大小写与空白标准化、未支持命令在 Gateway 前被拒绝。
- API 集成测试覆盖匿名 401、角色授权、空命令 400、未支持命令 400、游戏未 Ready 503、single-flight 忙 503 和成功输出。
- 所有失败使用稳定 Problem Details code、Path-only instance 和关联 traceId；拒绝路径没有 Gateway 调用。
- Dispatcher 确定性测试覆盖主线程直接执行、投递失败、排队取消、启动超时、稍后委托 no-op、执行开始后取消/超时仍返回真实结果，以及委托异常传播。
- 依赖规则证明 Application 不引用 Web、SevenDays、Hosting、SQLite 或游戏程序集，Web 与 SevenDays Adapter 不互相引用。
- Release Rebuild、后端全量测试和 Windows `v3.0.1-b4` 真实进程 smoke 验证认证请求返回真实 `version` 主线程输出，随后正常关服并释放端口。
- Linux 主线程往返、状态变更动作、多个生产者背压和性能阈值继续作为[测试策略](../../test.md#已知缺口)中的未完成证据。

## 文档影响

- `CAP-02` 的首个只读命令合同由[产品需求](../../PRD.md)拥有；任意命令和状态变更动作仍不属于当前切片。
- 当前 Application、主线程 Dispatcher、HTTP 契约和残余风险由[系统架构](../../architecture.md)拥有。
- 自动化、真实进程证据、主线程性能缺口和发布门槛由[测试策略](../../test.md)拥有。
- [后端目标架构蓝图](../../architecture/backend-target-blueprint.md)继续描述未来多个类型化 Gateway 的方向，不得把当前 single-flight 策略复制到所有动作。

## 追溯结论

提交 `4e07302` 建立了认证 HTTP -> Application -> 类型化 SevenDays Gateway -> 游戏主线程的首个完整路径，并用实际消费者把 `171cae5` 的通用 scheduler 收缩为更窄的 `GameThreadDispatcher`。本文作为后补 Change Record 保存该决策，不为已删除 scheduler 建立独立历史计划，也不把追溯补录表述为实施前审批。