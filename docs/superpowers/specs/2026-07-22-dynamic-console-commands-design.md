---
state: Approved
document_role: Design Spec
last_updated: "2026-07-22"
---

# 动态控制台命令设计规格

> 本规格落实[产品需求](../../PRD.md) `CAP-02` 的已批准目标，并细化[后端目标架构蓝图](../../architecture/backend-target-blueprint.md)与[测试策略](../../test.md)。它描述尚未实现的目标设计，不是当前实现证据；当前行为仍以[系统架构](../../architecture.md)为准。

## 目标与替代关系

本变更把现有只允许 `version`、并发时立即返回 busy 的控制台纵向切片，替换为面向 `Owner` 和 `Admin` 的动态命令能力。7DPanel 不维护命令白名单，允许执行当前 7DTD 进程已经注册的全部命令，包括内置命令和第三方 Mod 注册命令。

本规格替代[受限控制台命令纵向切片设计规格](2026-07-21-restricted-console-command-design.md)中的目标产品合同，但保留旧规格作为已实现历史记录。实施完成并验证前，旧规格描述的 `version` 白名单、类型化 `ExecuteVersionAsync` 和 single-flight 仍是当前事实。

## 范围

- 保留认证的 `POST /api/v1/console/commands`，允许 `Owner` 和 `Admin` 提交非空原始命令文本。
- 让 7DTD 的已注册命令集合负责识别命令名、别名和参数，支持第三方 Mod 命令。
- 为 7DPanel HTTP 请求建立独立有界 FIFO，每个请求独立排队、执行和返回输出。
- 所有 7DPanel HTTP 命令仍通过 `GameThreadDispatcher` 进入游戏主线程，并在离开主线程前复制本次输出。
- 在最终 `SdtdConsole.executeCommand` 执行点用 Harmony 观察正常控制台调用，异步尽力写入 SQLite 审计。
- 保留现有 `Log.LogCallbacksExtended`、`console-log` SSE、`ServerEventLiveWindow` 和 `ServerEventHub`。

## 非目标

- 不替换、包装或重新消费 7DTD 的 `ExecuteAsync` 原生队列和 `Update()` 调度。
- 不治理绕过 `SdtdConsole.executeCommand`、直接调用游戏 API 或直接调用命令实例的第三方 Mod 行为。
- 不保证第三方命令安全、幂等、快速、可取消或能提供可靠的业务成功状态。
- 不新增结构化控制台命令 SSE，不把日志重构为统一事件服务，不向 Gateway 暴露同步 C# 事件。
- 不在首个切片增加应用级请求长度、参数长度、输出行数、单行长度、总输出大小或命令级资源配额。
- 不合并看似相同的请求，不建立命令注册表、脚本引擎或 7DPanel 命令插件模型。

## HTTP 命令执行

```text
ConsoleCommandsController
  -> ExecuteConsoleCommandUseCase
  -> bounded FIFO owned by the SevenDays command gateway
  -> GameThreadDispatcher
  -> SdtdConsole.ExecuteSync(raw command, null)
  -> immutable ConsoleCommandResult
  -> HTTP response
```

Controller 负责认证、角色和协议映射；Application 负责非空输入、游戏就绪判断和技术中立请求/结果；SevenDays Adapter 拥有 7DTD 特定的排队及执行。命令原文除了现有 HTTP JSON 解码和非空判断外不被白名单、参数模型或命令名称标准化改写。

有界 FIFO 只接收 7DPanel HTTP 命令，不接管 Telnet、游戏 Web/GUI、其他 Mod 或 7DTD 原生异步队列。每次请求创建独立工作项，不按命令文本合并。队列满时拒绝新请求；排队中的请求取消后不得执行；工作项一旦开始游戏线程执行，就等待真实同步结果或传播无法确认的终态，HTTP 取消不得伪造命令失败。

队列容量和具体稳定错误码由实施计划结合现有 HTTP Problem Details 约定确定，并写回当前架构与测试；未量化容量不能通过发布性能门槛。停止流程必须停止接收新工作、处理已开始项，并对未开始项给出明确终态。

## Harmony 观察边界

Patch 目标是 `SdtdConsole.executeCommand` 的最终共享执行点，而不是 `ExecuteAsync` 或 `Update()`。观察范围包括所有正常汇聚至该方法的 HTTP、Telnet、游戏 Web/GUI、内置调用和第三方 Mod 调用；命令的原同步返回、异常和原生排队顺序不得被 Patch 改变。

Patch 在命令执行前后维护单次调用上下文，复制完整原始命令、参数、可识别来源、时间、输出和可判断结果。实现必须处理连续调用、异常和可能的嵌套调用，不能依赖 `SdtdConsole` 的共享可变列表在离开执行边界后保持稳定。

观察代码不得在游戏主线程等待 SQLite、网络、SSE subscriber 或其他消费者。任何观察或投递异常都必须被隔离，原命令继续遵循 7DTD 的结果语义。

## 审计语义

命令执行后，观察快照进入独立的有界异步审计写入路径并保存到 SQLite。记录保留完整原始命令和参数；该决定接受凭据、Token 或其他敏感文本被持久化的风险，后续访问权限、备份和运维必须把命令审计视为敏感数据。

审计为 fail-open、best effort：数据库锁定、队列饱和、消费者停止或写入失败都不阻止或回滚原命令，也不改变 HTTP 命令结果。系统必须产生可见告警，并记录可识别的受影响时间或计数，使操作者知道审计可能不完整；不得把失败静默吞掉或宣称完整审计。

命令审计不与可丢弃的原始日志队列共享容量。现有 `console-log` SSE 继续传输普通日志，是否出现 7DTD 的“Executing command”文本取决于游戏日志配置，不能作为命令审计证据。

## 结果与兼容性

- 已注册命令返回本次执行期间复制的控制台输出；未知命令返回 7DTD 的真实未知命令输出或对应稳定协议结果。
- 命令实现抛错、游戏线程无法开始、队列饱和、游戏未就绪、请求取消和执行结果未知必须保持可区分。
- 7DPanel 不解释任意第三方输出以推导业务成功；审计只记录可观察执行事实和可判断结果。
- Harmony 兼容性必须以仓库支持的真实 7DTD 版本和游戏提供的 `0_TFP_Harmony` 验证；7DPanel 不发布自己的 Harmony 副本。
- Patch 生命周期必须由 Bootstrap 显式拥有，并与现有 `Assembly.Location` 兼容补丁使用不同、稳定的 Harmony 标识或明确的共同所有权，避免误取消其他 Mod 的 Patch。

## 验证标准

- Application/API 测试证明 Owner/Admin 授权、非空输入、游戏就绪、原文透传、未知命令及稳定失败映射。
- 队列确定性测试证明容量有界、FIFO、并发请求独立、队满拒绝、排队取消、开始后真实结果、异常隔离和停止语义。
- Adapter 测试证明所有 HTTP 命令在游戏主线程执行，并在返回前复制独立输出。
- Harmony 测试证明内置命令、测试 Mod 注册命令、HTTP 和非 HTTP 标准入口均被观察，原生 `ExecuteAsync` 顺序和同步结果不变，绕过边界的直接 API 调用不被误报。
- SQLite 集成测试证明完整原文持久化、并发顺序、锁竞争、队列饱和和消费者失败；每种审计失败都保持命令 fail-open 并产生告警/缺口证据。
- 事件测试证明没有新增结构化命令 SSE，现有 `console-log` replay、gap 和慢客户端隔离保持不变。
- Windows `v3.0.1-b4` 真实进程 smoke 证明游戏提供 Harmony 的 Patch 签名兼容、第三方命令发现、原生队列不变、完整命令审计和正常卸载；支持 Linux 前补充对应证据。

## 文档与实施门槛

实施完成前，[系统架构](../../architecture.md)不得声称动态命令、FIFO、Harmony command Patch 或 SQLite 命令审计已经存在。代码稳定并获得上述证据后，再把实现事实提升到当前架构，更新测试基线和最接近的后端 README；未发布前不更新 `CHANGELOG.md`。

下一步必须基于本规格创建一份实施计划，按最小完整纵向切片安排代码、migration、测试、真实进程兼容和文档提升。计划不得重新引入结构化命令 SSE、统一事件服务或未经批准的资源限制。