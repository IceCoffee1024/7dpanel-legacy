---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-26-console-workspace-design.md
last_updated: "2026-07-26"
---

# 网页控制台工作台实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 `Owner` 和 `Admin` 交付只呈现服务端原始日志、支持动态命令建议和直接命令执行的全高网页控制台工作台。

**Architecture:** 后端复用现有 5000 条统一事件窗口提供最近日志读取，在游戏主线程动态提取命令目录，并在现有 SSE 会话中按角色过滤 `console-log`。Admin 扩展唯一应用级 SSE，使用页面局部 composable 按 `sequence` 合并 REST 快照和实时事件，并通过现有 Hey API、Pinia Colada、Nuxt UI 与 i18n 组成页面。

**Tech Stack:** .NET Framework 4.8、C#、OWIN/Web API、7DTD `v3.0.1-b4` API、Vue 3、TypeScript、Vite、Nuxt UI、Pinia Colada、Hey API、Valibot、Vitest、Playwright。

---

## 主规格

本计划只实现已批准的[网页控制台工作台设计](../specs/2026-07-26-console-workspace-design.md)，追踪 `CAP-02`、`NFR-02`、`NFR-03` 和 `NFR-04`。实施中出现新的产品或架构决策时，必须先回到权威文档确认。

## 实施约束

- 不安装新包，不引入第二套日志窗口、SSE 客户端、终端模拟器、虚拟列表或页面状态机。
- 日志区只渲染服务端原始文本；本地命令、独立请求输出、错误、连接状态和 gap 均不得成为日志行。
- Viewer 不得读取最近日志、动态命令目录或 `console-log` SSE；隐藏导航不能替代服务端授权。
- 浏览器初始请求 1000 条、最多保留 2000 条；后端沿用现有 5000 条窗口。
- Admin Query 固定使用 `staleTime: 0` 和 `refetchOnWindowFocus: false`。
- 动态目录只提供发现和补全，不能成为命令执行白名单；任意非空命令仍可直接提交。
- 受控 OpenAPI 快照和 `frontend/apps/admin/src/shared/api/generated/` 只能通过 `pnpm api:gen` 更新，禁止手改生成目录。
- 每项任务只运行与改动边界直接相关的聚焦检查；聚合检查在功能稳定后只运行一次。除最终人工边界检查外，不重复运行真实 7DTD 或浏览器流程。
- Git 提交、合并和推送仅在用户明确授权后执行。

## 任务 1：提供最近控制台日志 REST

**文件：**

- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Runtime/ConsoleLogs/ServerEventLiveWindow.cs`
- 新增：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ConsoleLogsController.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/ConsoleLogLiveWindowTests.cs`
- 新增：`backend/tests/LSTY.SevenDPanel.Tests/ConsoleLogsHttpTests.cs`

- [x] 在 `ServerEventLiveWindow` 的现有锁内实现 `ReadRecentConsoleLogs(int limit)`，只复制最新 N 条 `console-log` 并按 `sequence` 升序返回，不创建第二份缓存或额外快照元数据类型。
- [x] 实现 `GET /api/v1/console/logs/recent`，默认 `limit=1000`，接受 `1..5000`；响应只包含 `entries` 及每项 `sequence`、`message`、`formattedMessage`、`trace`、`logType`、`timestamp`、`uptimeMilliseconds`。
- [x] Controller 只负责 Owner/Admin 授权、参数校验、映射和既有 Problem Details 转换；Viewer 返回 403，服务不可用返回 503。
- [x] 增加聚焦测试，覆盖空窗口、最新 N 条、升序、参数边界、匿名 401、Viewer 403、Owner/Admin 200 和 503；运行对应测试文件一次。

## 任务 2：提取并发布动态命令目录

**文件：**

- 新增：`backend/src/Core/LSTY.SevenDPanel.Application/ConsoleCommands/IConsoleCommandCatalogQuery.cs`
- 新增：`backend/src/Core/LSTY.SevenDPanel.Application/ConsoleCommands/ConsoleCommandCatalogEntry.cs`
- 新增：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/ConsoleCommands/SevenDaysConsoleCommandCatalogQuery.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ConsoleCommandsController.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- 新增：`backend/tests/LSTY.SevenDPanel.Tests/ConsoleCommandCatalogTests.cs`
- 新增：`backend/tests/LSTY.SevenDPanel.Tests/ConsoleCommandCatalogHttpTests.cs`

- [x] 定义最小只读端口，目录项只包含 `name`、`aliases`、`description`、`help` 和 `permissionLevel`；不增加仅透传的 Use Case、快照包装或前端建议规则。
- [x] 在 SevenDays Adapter 游戏主线程读取当前 `SdtdConsole` 注册表：借鉴旧项目已验证的 `GetCommands()`、`GetDescription()`、`GetHelp()` 和有效权限等级来源，但按现有 Adapter 边界重新实现。
- [x] 规范名称优先使用有效 `PrimaryCommand`，否则回退第一个有效名称；清理别名并稳定排序。单项可选元数据失败只降级该项，无有效名称的异常项跳过并记录脱敏告警。
- [x] 实现 `GET /api/v1/console/commands/catalog`；Owner/Admin 可读，Viewer 返回 403，未就绪、主线程超时或服务停止返回稳定 503。保留现有任意非空命令 POST，不用目录拦截提交。
- [x] 增加聚焦测试，覆盖字段提取、第三方异常隔离、主线程边界、角色授权和 503；运行对应测试文件一次。

## 任务 3：收紧共享 SSE 的 Viewer 过滤

**文件：**

- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ServerEventSseSession.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/ServerEventSseSessionTests.cs`

- [x] 在 replay 与 live 共用写出判定中按最近认证角色过滤：Owner/Admin 保持现有事件，Viewer 跳过 `console-log` 并保留获准的生命周期、gap、welcome 和 heartbeat。
- [x] 被过滤事件仍推进连接内部 `lastSentSequence`，不发送占位事件、不改变统一窗口 sequence、不新增 SSE 路由。
- [x] 增加聚焦测试，覆盖 Viewer replay/live 均不可见、允许事件仍可见、角色复验后生效和过滤事件不重复 replay；运行该测试文件一次。

## 任务 4：同步 OpenAPI 和生成客户端

**文件：**

- 修改：`backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostOpenApiSnapshotTests.cs`
- 修改（生成）：`frontend/apps/admin/openapi/7dpanel.v1.json`
- 修改（生成）：`frontend/apps/admin/src/shared/api/generated/`

- [x] 扩展运行时 OpenAPI 断言，覆盖两个 GET 的稳定 operationId、Bearer 安全、200 schema 和适用的 Problem Details。
- [x] 在 `frontend/apps/admin` 只运行 `pnpm api:gen`，通过现有配置同步受控 OpenAPI 快照、类型、SDK 和 Pinia Colada definitions；不得手改生成目录。
- [x] 运行一次现有 API 漂移检查，确认重复生成无差异。

## 任务 5：实现单一 SSE 日志合并和页面日志状态

**文件：**

- 修改：`frontend/apps/admin/src/app/serverEvents.ts`
- 修改：`frontend/apps/admin/src/app/serverEvents.test.ts`
- 新增：`frontend/apps/admin/src/features/console-logs/api/consoleLogs.ts`
- 新增：`frontend/apps/admin/src/features/console-logs/model/consoleLog.ts`
- 新增：`frontend/apps/admin/src/features/console-logs/model/useConsoleLogs.ts`
- 新增：`frontend/apps/admin/src/features/console-logs/model/useConsoleLogs.test.ts`

- [x] 扩展现有应用级 `serverEvents`，分发 `console-log` 和连接状态；保持 `Last-Event-ID`、取消、3 秒重连和登出清游标，不建立第二条连接。
- [x] 用 Valibot 解析最近响应和 SSE payload。页面先订阅并缓冲 live，再读取最近 1000 条，最后按数值 `sequence` 合并、升序和去重；相同 sequence 保留先到达的有效项。
- [x] `useConsoleLogs` 只拥有 `snapshotLoading`、`connectionStatus`、`hasGap`、`entries` 和 `unreadCount`。最多保留 2000 条，超出时从顶部淘汰。
- [x] 清空只清除当前页面的 `entries` 和未读数，不记录截止游标、不重置应用 SSE；重新进入页面重新读取最近日志。
- [x] 增加聚焦测试，覆盖订阅/快照竞态、去重、gap、快照失败仍保留 live、容量和当前页面清空；运行相关测试文件一次。

## 任务 6：实现命令行为和三组件页面

**文件：**

- 新增：`frontend/apps/admin/src/features/console-logs/api/consoleCommands.ts`
- 新增：`frontend/apps/admin/src/features/console-logs/model/useConsoleCommands.ts`
- 新增：`frontend/apps/admin/src/features/console-logs/model/useConsoleCommands.test.ts`
- 新增：`frontend/apps/admin/src/features/console-logs/ui/ConsoleWorkspace.vue`
- 新增：`frontend/apps/admin/src/features/console-logs/ui/ConsoleLogViewport.vue`
- 新增：`frontend/apps/admin/src/features/console-logs/ui/ConsoleCommandBar.vue`
- 新增：`frontend/apps/admin/src/features/console-logs/ui/ConsoleWorkspace.test.ts`
- 新增：`frontend/apps/admin/src/features/console-logs/index.ts`
- 新增：`frontend/apps/admin/src/pages/console-logs.vue`
- 修改：`frontend/apps/admin/src/app/AppShell.vue`
- 修改：`frontend/apps/admin/src/app/router.ts`
- 修改：`frontend/apps/admin/src/app/i18n/locales/zh-CN.json`
- 修改：`frontend/apps/admin/src/app/i18n/locales/en.json`

- [x] 命令目录 Query 固定 `queryOptions: { staleTime: 0, refetchOnWindowFocus: false }`；进入页面读取一次，`game-ready` 后精确失效。目录失败只禁用建议，自由输入继续可用。
- [x] 在 `useConsoleCommands` 内实现名称/别名不区分大小写的前缀匹配并保持目录顺序；方向键选择、Tab 替换首词并保留参数、Esc 关闭、Enter 直接提交完整原文。
- [x] 页面会话内保留最多 50 条成功提交历史，只抑制连续重复；历史导航保留未提交草稿。提交中拒绝重复提交，失败或结果未知时保留输入。
- [x] 使用生成 Mutation 执行命令；解析但不显示独立 `output`。成功后清空输入，失败使用本地化短暂通知，任何反馈均不写入日志。
- [x] 只实现 `ConsoleWorkspace`、`ConsoleLogViewport`、`ConsoleCommandBar` 三个 Feature 组件。日志通过普通文本节点和 `white-space: pre-wrap` 原样显示，`logType` 只提供语义色，不隐藏任何日志。
- [x] 实现智能跟随、未读数量、回到最新和当前页面清空；补齐 Owner/Admin 路由和导航，Viewer 深链接进入权限拒绝页，窄屏控件不得遮挡。
- [x] 增加聚焦 composable、组件、路由和权限测试；运行相关测试文件一次。

## 任务 7：完成最小交付验证和文档事实提升

**文件：**

- 新增：`frontend/apps/admin/tests/e2e/admin-console-workspace.spec.ts`
- 修改：`docs/architecture.md`
- 修改：`docs/architecture/admin-frontend-target-blueprint.md`
- 修改：`docs/test.md`
- 复核：`docs/PRD.md`
- 复核：`docs/design.md`

- [ ] 在功能稳定后各运行一次后端聚合构建/测试，以及 Admin 的 typecheck、lint、unit test 和 build；不重复已通过的聚焦检查。
- [ ] 只保留一条 Owner 浏览器主路径和一条 Viewer 拒绝路径，覆盖快照/live 去重、智能跟随、直接提交、日志不受反馈污染和服务端权限；选择一个桌面与 `390x844` 窄屏视口检查布局。
- [ ] 只对真实游戏 API 边界进行一次人工检查：内置和第三方命令元数据可提取、任意命令可提交、独立响应不进入日志、原生日志自然回显。
- [x] 将已经实现且有验证证据的边界提升到 `docs/architecture.md` 和 `docs/test.md`；目标蓝图只保留尚未完成的目标，PRD 与 design 不重复实现细节。
- [x] 运行一次文档链接、OpenAPI 漂移和差异格式检查；确认无新增依赖、无手改生成文件、无机器路径和无未授权 Git 操作。

## 完成标准

- Owner/Admin 能读取最近 1000 条及后续实时原始日志；Viewer 在 REST、SSE 和路由三处均不能读取控制台数据。
- 快照与 live 按 `sequence` 合并、排序和去重；缺口仅在日志外提示，浏览器最多保留 2000 条，清空只影响当前页面会话。
- 动态目录来自当前 7DTD 注册命令，包含名称、别名、说明、帮助和有效权限等级；目录不限制任意非空命令直接提交。
- 建议、补全、50 条历史、草稿恢复、智能跟随和未读数量符合设计；独立命令响应、错误和状态不污染日志。
- 页面仅使用三个 Feature UI 组件，复用唯一应用级 SSE 和现有依赖；聚焦检查、一次聚合检查和最小人工边界检查通过。
