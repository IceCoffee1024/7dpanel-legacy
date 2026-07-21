---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-20-console-log-pipeline-design.md
last_updated: "2026-07-20"
---

# 7DTD 控制台日志服务实施计划

> 本计划只落实 [7DTD 控制台日志服务设计规格](../specs/2026-07-20-console-log-pipeline-design.md)。它包含默认关闭的开发期未认证 SSE，但不实现生产日志 API、认证、WebSocket、SQLite 原始日志持久化或 Admin 日志页面。

## 实施约束

- 游戏回调只创建一个 `ConsoleLogEntry` 并调用一次非阻塞 `TryWrite`。
- Channel 与窗口分别承担待消费 inbox 和当前进程历史，不能混为一个容器。
- 不保留只有一个生产实现的一对一 source、callback、sink 或配置/状态/统计类型。
- 不引入通用组件注册表、EventBus、Mediator、DI 容器或每日志 `Task.Run`。
- 不执行 Git 提交；每项完成后运行相关测试并复核 diff。
- 开发 SSE 必须与 Hub 同切片交付；默认关闭，未显式开启时保持 404。

## 任务 1：收缩日志模型与窗口

**文件**

- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/ConsoleLogs/ConsoleLogEntry.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/ConsoleLogs/ConsoleLogLiveWindow.cs`
- 保留：`ConsoleLogType.cs`、`ConsoleLogWindowReadResult.cs`
- 删除：`ConsoleLogSnapshot.cs`、`IConsoleLogSink.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/ConsoleLogLiveWindowTests.cs`

- [x] 用一个 `ConsoleLogEntry` 模型承载回调复制字段与窗口记录。
- [x] 窗口 append 时创建带 sequence 的 retained entry。
- [x] 保留固定容量淘汰、批次上限、`ReadAfter` 和 gap 边界测试。

## 任务 2：集中订阅、Channel 与 consumer

**文件**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/ConsoleLogs/ConsoleLogService.cs`
- 删除：`Inbound/ConsoleLogs/` 下四个 source/callback 类型
- 删除：`ConsoleLogPipeline*.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/ConsoleLogServiceTests.cs`
- 删除：`ConsoleLogPipelineTests.cs`、`SevenDaysLogSourceTests.cs`

- [x] `ConsoleLogService` 直接拥有精确游戏 delegate 和幂等注销 token。
- [x] 保留 bounded Channel、单 consumer、默认容量、限时排空和内部计数。
- [x] 通过内部委托构造点测试订阅、回调线程隔离、队满、保序、失败继续和停止顺序。
- [x] 删除为测试替换而提升到生产代码的一对一接口。

## 任务 3：简化组合生命周期

**文件**

- 删除：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/IHostedComponent.cs`
- 恢复：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/ModHost.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/ModMain.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/ModHostTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyRulesTests.cs`

- [x] 增加薄的 `ConsoleLogRuntime`，显式组合 `ConsoleLogService` 与 `ModHost`。
- [x] 启动顺序为日志服务后 OWIN，停止顺序为日志服务后 `ModHost`，两个停止路径都必须尝试。
- [x] 恢复 `ModHost` 的 OWIN 生命周期职责，不保留通用组件列表。
- [x] 更新 Bootstrap candidate 发布顺序规则测试。

## 任务 4：依赖、发布物与自动化验证

**文件**

- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/LSTY.SevenDPanel.Adapters.SevenDays.csproj`
- 修改：`backend/scripts/Publish-Mod.ps1`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyRulesTests.cs`

- [x] 保留 `System.Threading.Channels` 及其发布依赖边界。
- [x] 相关日志、`ModHost` 和依赖规则测试通过。
- [x] Release Rebuild 零警告、68 项完整后端测试和发布检查通过。
- [x] Windows `v3.0.1-b4` 真实进程重新验证当前精简二进制。

## 任务 5：同步文档并复核

**文件**

- 修改：`backend/README.md`
- 修改：`docs/architecture.md`
- 修改：`docs/architecture/backend-target-blueprint.md`
- 修改：`docs/test.md`
- 修改：本规格与计划

- [x] 当前架构只描述集中服务、组合运行时和仍未实现的公开 SSE。
- [x] Target 蓝图删除旧的一对一类型并保留未来每客户端 mailbox 边界。
- [x] 测试策略记录最终自动化数量和真实进程结果，不把收缩前 smoke 当成当前二进制证据。
- [x] 执行 `git diff --check`、本地链接/占位符检查和工作区复核。

## 任务 6：固化最小纵向切片与风险分级验证

**文件**

- 修改：`AGENTS.md`
- 修改：`docs/architecture/backend-target-blueprint.md`
- 修改：`docs/test.md`

- [x] AGENTS 只保留最小完整纵向切片、抽象例外和风险分级验证的强制规则。
- [x] 具体检查矩阵由测试策略拥有，不在 AGENTS 重复命令块。
- [x] Target 蓝图明确测试便利不构成生产抽象理由。

## 任务 7：实现开发日志流边界和 Hub

**文件**

- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/ConsoleLogs/ConsoleLogStreamEvent.cs`
- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/ConsoleLogs/IConsoleLogStream.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/ConsoleLogs/ConsoleLogHub.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/ConsoleLogs/ConsoleLogService.cs`
- 新建：`backend/tests/LSTY.SevenDPanel.Tests/ConsoleLogHubTests.cs`

- [x] 用 Hosting 最小契约保持 Web 与 SevenDays Adapter 无直接引用。
- [x] Hub 为每个订阅者创建 bounded mailbox，并限制总订阅数。
- [x] 窗口 append 成功后才广播带 sequence 的不可变 stream event。
- [x] 慢订阅者溢出只影响自身；停止服务会完成全部订阅。

## 任务 8：实现默认关闭的开发 SSE Controller

**文件**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ConsoleLogsController.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OwinStartup.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/Configuration/PanelHostConfig.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/Configuration/PanelHostConfigurationLoader.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/config.example.json`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/ModMain.cs`
- 修改：对应配置、OWIN 和依赖规则测试

- [x] 默认配置和未注入 stream 时路由返回 404。
- [x] 显式开关启用时输出未认证风险警告并注入同一个 Hub。
- [x] Controller 支持 replay、`Last-Event-ID` 去重、gap、heartbeat 和客户端取消。
- [x] Katana 集成测试验证 SSE 响应和禁用边界；heartbeat 由源码复核。

## 任务 9：同步当前文档并按风险分层验证

- [x] 更新 `backend/README.md`、当前架构、Target 蓝图和测试策略。
- [x] 迭代期只运行 Hub、配置和 OWIN 定向测试，最终定向范围 46 项通过。
- [x] 稳定后执行一次 Release Rebuild 和后端全量测试，结果为零警告且 80 项通过。
- [x] 因变更跨越游戏回调、OWIN 和发布物，执行一次发布与真实 7DTD SSE/关服 smoke；未执行无关前端视觉检查。
- [x] 完成 AI 文档审查、`git diff --check` 和工作区复核。
