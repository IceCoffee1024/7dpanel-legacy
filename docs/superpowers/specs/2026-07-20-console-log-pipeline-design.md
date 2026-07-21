---
state: Current
document_role: Design Spec
last_updated: "2026-07-20"
---

# 7DTD 控制台日志服务设计规格

## 上游与范围

本规格落实 [系统架构](../../architecture.md#7dtd-控制台日志采集边界) 中的当前进程控制台日志边界，并为 `CAP-02` 和 `NFR-02` 提供默认关闭的开发期 SSE 观测入口。本规格是在首轮管线实现后的设计收缩：可靠性目标不变，删除只有一个实现和一个消费者的一对一抽象，并让 `ConsoleLogHub` 与首个真实 HTTP 消费者在同一切片出现。

本阶段包含：

- 订阅 `Log.LogCallbacksExtended`；
- 同步回调内复制稳定字段并非阻塞入队；
- 单 consumer 保序写入固定容量当前进程窗口；
- 进程内 sequence、有限补取和 gap 判断；
- 与 `ModHost` 的显式启停组合；
- 过载、停止和 consumer failure 的内部计数；
- 每客户端 bounded mailbox、窗口 replay、gap、heartbeat 和 SSE 编码；
- 由 `config.json` 显式开启的开发期未认证日志流；
- Windows `v3.0.1-b4` 发布与真实进程兼容验证。

本阶段不包含生产日志 API、认证授权、Admin 日志页面、WebSocket、SQLite 原始日志持久化或跨进程游标。开发入口默认关闭，不属于首版产品承诺。

## 设计原则

- 只为不同的运行时职责保留不同容器：Channel 是待消费 inbox，live window 是已消费历史。
- 一个生产实现不需要 source、callback、sink、options、state 和 statistics 接口层。
- 测试替换点优先使用内部构造委托，不把测试 seam 提升为生产接口。
- 回调线程的工作量必须有上界；不得等待、执行 I/O、逐条 `Task.Run`、调用 `Log.*` 或遍历未来 SSE 客户端。
- 当前没有生产日志契约；开发入口只固定维持连接和隔离慢客户端所需的 heartbeat、客户端上限、mailbox 容量与断开释放，不提前设计生产角色权限、审计、连接配额、错误契约和重连退避。
- 开发 SSE 是完整的最小纵向切片；Hub 不作为无消费者的预留基础设施单独存在。

## 最终生产类型

SevenDays 日志运行时保留 6 个文件、7 个顶层生产类型；幂等订阅 token 等私有实现细节留在所属类型内部：

| 类型 | 职责 |
|---|---|
| `ConsoleLogService` | 游戏 delegate、bounded Channel、单 consumer、窗口接线、启停和内部计数 |
| `ConsoleLogRuntime` | 先启动日志服务再启动 `ModHost`，停止时反向执行并聚合失败 |
| `ConsoleLogEntry` | 回调与窗口共用的稳定日志模型；窗口 append 后获得 sequence |
| `ConsoleLogType` | 隔离 Unity `LogType` |
| `ConsoleLogLiveWindow` | 固定容量当前进程历史与 sequence 分配 |
| `ConsoleLogWindowReadResult` | 有界补取结果及 oldest/latest/gap |
| `ConsoleLogHub` | 窗口读取、每客户端 bounded mailbox、广播、溢出隔离和完成 |

Web 与 SevenDays Adapter 不能互相引用，因此 Hosting 增加最小跨项目契约 `IConsoleLogStream`、`IConsoleLogSubscription` 和不可变 `ConsoleLogStreamEvent`。这是实际依赖方向边界，不是测试 seam。Web Adapter 增加首个生产消费者 `ConsoleLogsController`。

删除以下首轮类型：

- `ISevenDaysLogCallbacks`、`SevenDaysLogCallbacks`；
- `ISevenDaysLogSource`、`SevenDaysLogSource`；
- `ConsoleLogSnapshot`；
- `IConsoleLogSink`；
- `ConsoleLogPipeline`、`ConsoleLogPipelineOptions`、`ConsoleLogPipelineState`、`ConsoleLogPipelineStatistics`；
- `IHostedComponent`。

## 数据流

```text
Log.LogCallbacksExtended
  -> ConsoleLogService callback
  -> immutable ConsoleLogEntry without sequence
  -> bounded Channel.TryWrite
  -> one tracked consumer
  -> ConsoleLogLiveWindow.Append
  -> retained ConsoleLogEntry with process-local sequence
  -> ConsoleLogHub.Publish
       -> bounded subscriber mailboxes
       -> ConsoleLogsController
```

Channel 和窗口不能合并：Channel 中的条目读取后消失，用于隔离同步回调和 consumer；窗口保留最近记录，用于未来查询和 SSE 重连补取。若未来取消日志查询与 SSE，窗口可以独立删除。

## 游戏事件与队列边界

`ConsoleLogService` 直接创建并保存精确的 `Log.LogCallbackExtendedDelegate`。嵌套的幂等 `IDisposable` token 使用同一个 delegate 注销。订阅失败时 best-effort 注销并保留原异常。

回调把游戏提供的六个字段转换为 `ConsoleLogEntry`，把 Unity `LogType` 数值映射为 `ConsoleLogType`，然后只调用一次 `TryPublish`。字符串不可变，不做字符级复制。

Channel 使用：

```csharp
new BoundedChannelOptions(capacity)
{
    FullMode = BoundedChannelFullMode.Wait,
    SingleReader = true,
    SingleWriter = false,
    AllowSynchronousContinuations = false
}
```

生产回调只调用 `TryWrite`。容量不足时立即返回 false 并增加 dropped-full；不得调用 `WriteAsync`、`WaitToWriteAsync` 或同步等待。默认 queue capacity 为 `1024`，不是服主配置。

## Consumer 与窗口

服务只创建一个 tracked consumer Task。consumer 按 Channel 顺序调用窗口 append；单条 append 失败增加 consumer-failure 并继续处理后续条目。

窗口默认 capacity 为 `5000`。只有成功 append 的条目获得从 1 开始的 `long sequence`，被拒绝或处理失败的条目不占 sequence。读取返回有界批次；仅当 `afterSequence < OldestSequence - 1` 时报告 `HasGap`。

窗口只覆盖当前 7DTD 进程，不写 SQLite。服务端重启后窗口和 sequence 重新开始；`output_log_dedi__*.txt` 继续承担跨重启原始证据。

## 生命周期

`ConsoleLogRuntime` 是 `IModRuntime` 装饰器，不是通用组件注册表：

```text
Start: ConsoleLogService.Start -> ModHost.Start
Ready: ModHost.MarkGameReady
Stop:  ConsoleLogService.Stop -> ModHost.Stop
```

日志服务启动时先确认 consumer 已进入读取循环，再允许接收并订阅游戏 delegate。停止时先禁止接收、注销 delegate、完成 Channel writer，再在 `5s` 内排空；超时取消 consumer 并报告聚合异常。注销完成后才允许写一次停止摘要，避免摘要重新进入自身。

`ConsoleLogRuntime.Stop` 保持幂等；日志服务停止失败不能阻止 `ModHost.Stop`，反之亦然。出现多个失败时统一以 `AggregateException` 报告。

当前不引入通用 `IHostedComponent`。只有当第二种独立长生命周期组件出现，且显式装饰器或组合根代码已产生可证明的重复与协调成本时，才重新评估通用组件生命周期。

## 开发期 SSE 边界

SSE 不直接订阅同步 .NET event，也不直接读取采集 Channel。`ConsoleLogHub` 位于窗口之后：

```text
ConsoleLogLiveWindow
  -> ConsoleLogHub
       -> bounded mailbox A
       -> bounded mailbox B
```

开发入口固定为 `GET /api/v1/dev/console-logs/stream`，只有 `enableUnauthenticatedDevelopmentConsoleLogStream` 为 true 时可用；默认 false 时返回 404。启用时 Bootstrap 必须输出明确警告。该入口不启用 CORS，不作为生产 API，也不得在不受信任网络开启。

Controller 先订阅 live mailbox，再从窗口按 `Last-Event-ID` replay，并按 sequence 去重，避免 replay 与 publish 竞态造成遗漏或重复。窗口已淘汰请求游标时先发送 `gap` event；正常日志使用 `id` 和 `console-log` event；空闲连接定期发送 SSE comment heartbeat。每个客户端 mailbox 固定容量，慢客户端溢出时只结束该订阅并发送 gap，不阻塞主 consumer。Hub 总订阅数也必须有固定上限。

生产认证 SSE 仍需独立设计角色权限、审计、连接配额、稳定错误契约和 `/api/v1` 正式路由；不得把本开发入口直接升级为生产能力。

## 验证标准

- 六个字段无损进入窗口，已接受条目保序且只消费一次；
- append 不在游戏回调线程执行；
- 队满时 `TryWrite` 立即失败，容量和 high-water 不越界；
- 单条 append 失败不终止 consumer；
- 订阅失败保留原异常并停止接收；
- 停止先禁止接收并注销，再排空已接受条目；停止摘要不会递归；
- `ConsoleLogRuntime` 的日志先启动、先停止和 readiness 转发有自动化覆盖；
- 默认配置和未显式提供 stream 时开发 SSE 返回 404；
- 多订阅者相互隔离，单个 mailbox 满不会阻塞采集或影响其他客户端；
- Katana 集成测试覆盖 `text/event-stream`、replay、`Last-Event-ID`、gap 格式和客户端断开释放；15 秒 heartbeat 的间隔与 comment 格式通过源码复核，不增加等待 15 秒的集成测试；
- Release Rebuild 零警告，完整后端测试和依赖规则通过；
- 发布物包含 Channels 所需依赖，不复制游戏提供的 Unsafe、LogLibrary 或 Unity 程序集；
- Windows `v3.0.1-b4` 真实进程验证 delegate、Channels 加载、日志采集、停止排空和端口释放。

## 文档影响

获批目标结构同步到[后端目标架构蓝图](../../architecture/backend-target-blueprint.md)。实现和验证事实写入[系统架构](../../architecture.md)，风险分级验证和新增证据写入[测试策略](../../test.md)，AI 工作约束写入根 `AGENTS.md`。开发入口默认关闭且没有产品 UI，因此不修改 `PRD.md`、`design.md` 或 `CHANGELOG.md`。
