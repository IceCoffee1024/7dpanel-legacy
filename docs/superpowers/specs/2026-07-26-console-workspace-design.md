---
state: Current
last_updated: "2026-07-26"
---

# 网页控制台工作台设计

## 文档角色与依据

本文档是 `CAP-02` 网页控制台工作台的已批准变更设计，不是当前实现证据。产品合同由[产品需求文档](../../PRD.md)定义，交互规则由[产品设计](../../design.md)定义，当前实现边界由[系统架构](../../architecture.md)定义，目标 Admin 边界由[Admin 前端目标架构蓝图](../../architecture/admin-frontend-target-blueprint.md)定义，验证分级由[测试策略](../../test.md)定义。

## 目标

- 为 `Owner` 和 `Admin` 提供一个适合日常值守的全高网页控制台工作台。
- 进入页面时显示服务端内存中的最近原始日志，并无遗漏、无重复地衔接现有认证 SSE 实时流。
- 保持日志区只包含服务端原始文本，不加入浏览器命令行、独立请求输出、错误、缺口或连接消息。
- 从当前 7DTD 进程动态读取命令名称、别名、说明、帮助和有效权限等级，为第一个命令词提供键盘补全；目录不是执行白名单。
- 复用现有日志窗口、统一事件序列、应用级 SSE、动态命令执行、OpenAPI 生成链和 Pinia Colada，不增加运行时日志管道或前端依赖。

## 非目标

- 不持久化控制台日志，不提供跨服务端进程重启查询。
- 不提供关键字、级别、时间范围筛选或浏览器内搜索界面。
- 不解析玩家名、IP、坐标、命令或审计关联，不提供日志行跳转或钻取。
- 不把独立命令响应输出渲染到日志区，不把命令提交状态升级为实时事件。
- 不维护命令白名单、风险级别、参数 schema 或命令确认规则。
- 不安装终端模拟器、虚拟列表或第二套 SSE 客户端。

## 当前基线

- `ConsoleLogService` 已通过容量 1024 的有界 Channel 消费 `Log.LogCallbacksExtended`，并把日志写入容量 5000 的 `ServerEventLiveWindow`。
- 日志、`game-ready` 和 `server-stopping` 共用单调递增的进程内 `sequence`；`ServerEventHub` 提供 replay、live、gap 和慢订阅者隔离。
- `GET /api/v1/events/stream` 已提供认证 Welcome、replay、live、heartbeat、`Last-Event-ID` 和 gap；Admin `serverEvents` 已建立单一应用级连接，但当前忽略 `console-log`。
- `POST /api/v1/console/commands` 已允许 `Owner`/`Admin` 将任意非空原文命令送入容量 32 的 FIFO，在游戏主线程串行执行并返回独立输出。
- 当前没有最近日志 REST、动态命令目录、控制台路由或控制台 Feature。

## 权限边界

| 能力 | Owner | Admin | Viewer | 未认证 |
|---|---|---|---|---|
| 控制台页面与导航 | 允许 | 允许 | 拒绝 | 登录 |
| 最近日志 REST | 允许 | 允许 | 403 | 401 |
| 动态命令目录 | 允许 | 允许 | 403 | 401 |
| `console-log` SSE | 允许 | 允许 | 服务端过滤 | 401 |
| 获准生命周期 SSE | 允许 | 允许 | 保持现有只读授权 | 401 |
| 提交动态命令 | 允许 | 允许 | 403 | 401 |

前端隐藏导航不是授权边界。共享 SSE 在每次建立连接及周期身份复验后使用当前角色决定是否发送 `console-log`；角色降为 `Viewer` 后，最迟在下一次既有复验边界停止发送控制台日志，而不影响仍获准的生命周期事件。

## 后端设计

### 最近日志读取

新增受保护接口：

```http
GET /api/v1/console/logs/recent?limit=1000
```

`limit` 缺省为 `1000`，合法范围为 `1..5000`。读取在 `ServerEventLiveWindow` 的同一锁内取得一致快照，从统一事件窗口中过滤 `console-log`，返回最新 N 条并保持升序。该读取不创建第二份窗口，不等待游戏就绪，不访问 SQLite。

成功响应：

```json
{
  "entries": [
    {
      "sequence": 42,
      "formattedMessage": "2026-07-26T12:00:00 42 INF example",
      "message": "example",
      "trace": null,
      "logType": "log",
      "timestamp": "2026-07-26T12:00:00Z",
      "uptimeMilliseconds": 120000
    }
  ]
}
```

字段语义：

- `entries` 只包含控制台日志；每项保留统一事件 `sequence` 和 SSE `console-log` 的六个原始源字段，不重新格式化。
- 前端以每项 `sequence` 与先到达的 live 事件合并去重；响应只保留 `entries`。
- 无日志或窗口为空返回 200 和空数组。无效 limit 返回 400 稳定 Problem Details；日志服务不可用返回 503。接口不因游戏尚未 ready 返回 503。

### 动态命令目录

新增受保护接口：

```http
GET /api/v1/console/commands/catalog
```

成功响应：

```json
{
  "capturedAtUtc": "2026-07-26T12:00:00Z",
  "commands": [
    {
      "name": "version",
      "aliases": ["ver"],
      "description": "Show the game version.",
      "help": "Displays the current game version.",
      "permissionLevel": 0
    }
  ]
}
```

Application 定义最小只读命令目录端口；SevenDays Adapter 在游戏主线程从当前 `SdtdConsole` 已注册命令提取数据。实现借鉴旧项目已经验证的字段来源，但不复制其 Controller 直连游戏对象的代码结构：使用 `GetCommands()` 取得名称集合、`GetDescription()` 取得说明、`GetHelp()` 取得完整帮助，并通过 `GameManager.Instance.adminTools.Commands.GetCommandPermissionLevel(commands)` 取得有效权限等级。实现优先使用 `v3.0.1-b4` 参考材料中已有的公开 API，不使用前端白名单或程序集反射扫描替代注册表。

- `name` 优先使用有效 `PrimaryCommand`；该值无效或不在 `GetCommands()` 中时回退到第一个有效名称。
- `aliases` 使用其余有效名称，去除空白、规范名称和重复项，采用不区分大小写的稳定比较。
- `description`、`help` 无法由命令提供时为 `null`；帮助文本保持原样，不派生额外字段。
- `permissionLevel` 是当前服务器配置计算出的有效命令权限等级；单项读取失败时为 `null`，不能阻断其他命令。
- 无法取得任何可用名称的异常第三方项不能形成建议项，可以跳过并写入脱敏服务端告警；原始命令提交仍不受目录限制。
- 返回按 `name` 不区分大小写排序，再以原字符串稳定决胜。
- 游戏未就绪、主线程开始超时或目录服务停止使用现有稳定 503 边界；未知异常由统一 Problem Details 脱敏。

### 共享 SSE 过滤

不新增 SSE 路由或订阅。`ServerEventSseSession` 在写出每个 `ServerEvent` 前根据最近一次认证复验所得角色应用事件授权：

- `Owner`/`Admin`：现有事件保持不变。
- `Viewer`：跳过 `console-log`，保留获准的 Welcome、`game-ready`、`server-stopping`、gap 和 heartbeat。
- 被过滤事件仍推进该连接的内部 `lastSentSequence`，避免 Viewer 重连时反复 replay 同一批不可见日志；SSE 不发送空占位事件。

### OpenAPI

两个新增 REST 接口进入运行时 OpenAPI，固定 Bearer 安全、200 schema、400/401/403/500/503 Problem Details 和稳定 operationId。Admin 受控 OpenAPI 快照与 Hey API 类型、SDK 和 Pinia Colada definitions 由现有命令重新生成，生成目录不手改。

## 前端设计

### 页面结构

```text
ConsoleLogsPage
  -> ConsoleWorkspace
       -> ConsoleLogViewport
       -> ConsoleCommandBar
```

- `ConsoleLogsPage` 只处理路由授权和 Feature 组合。
- `ConsoleWorkspace` 组合页面状态并渲染顶部连接、缺口、缓冲数量和清空操作，不拥有 API 或 DOM 滚动细节。
- `ConsoleLogViewport` 渲染原始文本，并独立拥有是否位于底部、未读计数和回到底部行为。
- `ConsoleCommandBar` 管理输入、建议、帮助、提交锁和历史导航。

页面使用全高、无卡片的工作台布局。顶部状态栏紧凑，中间日志占满剩余高度，底部输入固定可见；建议列表从输入上方展开。窄屏不得让状态、未读返回、建议或输入互相遮挡。

### 单一实时连接

扩展现有 `serverEvents`：

- 支持并分发 `console-log`，保留事件 `id` 和原始 payload。
- 暴露 `connecting`、`live`、`reconnecting`、`stopped` 的可订阅连接状态；Welcome 后进入 `live`，网络结束或异常进入 `reconnecting`，主动停止进入 `stopped`。
- 保持现有 `Last-Event-ID`、取消、重连退避和登出清游标规则。

页面挂载顺序固定为：

```text
subscribe console-log and connection state
  -> start buffering live events
  -> fetch recent snapshot
  -> merge snapshot and buffered live events by sequence
  -> continue appending live events
```

该顺序比“先请求快照再订阅”多覆盖请求期间产生的日志。合并只接受有效 payload，按 `sequence` 去重和升序排列；相同 sequence 保留先到达的有效项，不建立额外冲突状态。

### 日志状态

`useConsoleLogs` 不建立页面状态机，只拥有最小正交状态：

- `snapshotLoading`：最近日志请求是否仍在进行。
- `connectionStatus`：直接消费应用级 `serverEvents` 的 `connecting`、`live`、`reconnecting` 或 `stopped`。
- `hasGap`：是否收到不能补齐的 SSE gap；只在日志外提示。
- `entries`：已经按 sequence 合并去重的可见日志。
- `unreadCount`：用户离开底部后到达的日志数量。

浏览器最多保留 2000 条。初始快照默认 1000 条；追加超过上限时从顶部淘汰。容量淘汰是页面资源策略，不等同于服务端 gap。

“清空页面”只在初始快照完成后可用，清除当前 entries 并重置未读计数；它不停止订阅或重置应用级 SSE 游标。重新进入页面时重新获取服务端最近日志。

原始文本使用普通文本节点和 `white-space: pre-wrap`：

```text
formattedMessage when nonblank
otherwise message when nonblank
append trace as subsequent original text when nonblank
```

不添加时间、级别、前缀、命令提示符或 HTML 解释。`logType` 只映射到克制的语义色；任何类型均保留原文，不提供隐藏或筛选。

### 智能跟随

`ConsoleLogViewport` 以接近底部的稳定像素阈值判断跟随状态：

- 位于底部时，新日志渲染后滚动到最新。
- 用户滚动离开底部后停止程序化滚动，保持阅读位置并累计新日志条数。
- “回到最新”滚动到底部、清零未读并恢复跟随。
- 清空后保持跟随状态；如果用户原本离开底部，空视图自然回到底部并恢复跟随。
- 组件使用固定容器尺寸，状态标签和未读入口不得改变日志 viewport 尺寸。

### 命令建议与历史

命令目录使用生成 Query，固定项目级实时策略：

```ts
queryOptions: {
  staleTime: 0,
  refetchOnWindowFocus: false,
}
```

进入页面读取一次；收到 `game-ready` 后精确刷新目录。目录失败只关闭建议并在输入区外显示非阻断状态，自由输入保持可用。

建议只匹配 trimStart 后的第一个命令词，对 `name` 和 `aliases` 做不区分大小写的前缀匹配并保持目录顺序。建议展示名称、别名、说明、帮助和有效权限等级，但不依据权限等级阻止提交。

方向键移动建议选择，Tab 只替换第一个词并保留其后参数，Esc 关闭建议，Enter 提交完整原文。没有建议时，上下方向键浏览当前页面会话的命令历史。

历史最多 50 条，只记录成功获得 HTTP 响应的提交；连续重复原文不重复追加，离开历史末端前保留当前草稿，不写入 Pinia、sessionStorage 或 localStorage。提交进行中阻止重复提交；成功后清空输入，失败或结果未知时保留原文。

### 命令反馈

现有命令 Mutation 的独立 `output` 仍需通过生成 schema 和 Feature parser，但 UI 不渲染。成功只清空输入并记录历史，随后等待原生日志自然出现。400、401、403、503、网络失败和结果未知映射为当前语言短暂通知；通知、命令原文和服务端异常文本都不进入日志区。

## 错误与恢复

| 情况 | 页面行为 |
|---|---|
| 最近日志 401 | 清除会话并返回登录 |
| 最近日志 403 | 权限拒绝页，停止控制台 Feature |
| 最近日志 400 | 视为客户端合同错误，不自动重试 |
| 最近日志 503/网络失败 | 停止快照 loading，保留已经收到的 live 日志并在日志外提示最近上下文不可用 |
| SSE 断开 | 保留日志，显示重连状态，应用级自动重连 |
| SSE gap | 状态栏持续标记不连续，不插入日志 |
| 命令目录失败 | 自由输入可用，建议不可用 |
| 命令提交失败 | 保留输入，短暂通知，不插入日志 |
| 服务端停止 | 已有日志保留，状态转离线；不把断开解释为命令成功或失败 |

请求异常不清除最后有效日志或目录。页面卸载时注销 Feature listener 和 DOM 监听器，但应用级 SSE 继续服务其他页面。

## 状态与数据所有权

| 数据 | 所有者 | 生命周期 |
|---|---|---|
| Access Token、角色 | Auth Store | 认证会话 |
| SSE 游标、连接与重连 | 应用级 `serverEvents` | 认证会话 |
| 最近日志、live merge、snapshotLoading、hasGap | `useConsoleLogs` | 页面挂载 |
| 滚动跟随与未读数量 | `ConsoleLogViewport` | 组件挂载 |
| 命令目录 Query | Pinia Colada | staleTime 0；页面消费者存在期间 |
| 当前输入、建议选择、50 条历史 | `useConsoleCommands` | 页面挂载 |
| 命令请求 | 生成 Mutation | 单次提交 |

## 目录目标

```text
backend/src/Core/LSTY.SevenDPanel.Application/ConsoleCommands/
  ConsoleCommandCatalog*.cs
backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/ConsoleCommands/
  SevenDaysConsoleCommandCatalogQuery.cs
backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/
  ConsoleLogsController.cs
  ConsoleCommandsController.cs
frontend/apps/admin/src/pages/
  console-logs.vue
frontend/apps/admin/src/features/console-logs/
  api/
  model/
  ui/
```

具体文件拆分由实施计划锁定；不得为测试便利新增没有生产消费者的注册表或通用事件总线。

## 依赖决策

本变更不安装新包。后端复用 .NET、现有游戏引用、Channels、OWIN/Web API 和当前事件窗口；前端复用 Vue、Nuxt UI、VueUse、Hey API 生成客户端、Pinia Colada、Valibot、vue-i18n 和现有 SSE parser。固定 2000 条浏览器上限先控制 DOM 成本，虚拟列表只能在实现后性能证据证明必要时另行批准。

## 验证范围

- 后端单元：最近 tail、空窗口、sequence、命令目录名称/别名/可空元数据、有效权限等级和第三方异常隔离。
- Katana：两个新 REST 的 schema、limit、Owner/Admin、Viewer/匿名、503 与统一 Problem Details；SSE 角色过滤且 Viewer 生命周期事件保持可用。
- OpenAPI：运行时文档、受控快照和 Hey API 生成产物无漂移。
- 前端单元：SSE payload parser、快照/live 合并去重、容量、当前页面清空、连接/gap 正交状态、前缀建议、补全、历史和草稿恢复。
- 组件：原始文本与语义色安全、智能跟随、未读计数、清空、目录失败自由输入、提交反馈不污染日志。
- 浏览器：一条 Owner 主路径和 Viewer 拒绝路径，覆盖桌面与 `390x844`；不为纯 UI 状态重复运行真实 7DTD。
- 真实 7DTD：只验证当前注册命令元数据可提取、内置/第三方补全数据和提交后的原生日志回显。

不执行日志持久化、跨重启回放、筛选、关联识别、结构化参数补全或危险命令确认测试，因为它们不属于本设计。

## 文档影响

- `docs/PRD.md`：修订 `CAP-02` 产品合同和验收。
- `docs/design.md`：替换旧筛选与独立结果区，定义工作台交互。
- `docs/architecture.md`：保持当前事实，记录批准目标与尚未实现缺口。
- `docs/test.md`：增加权限、游标、合并、键盘和浏览器验证策略。
- `docs/architecture/admin-frontend-target-blueprint.md`：记录单一 SSE、状态所有权、目录目标和无新依赖决策。
