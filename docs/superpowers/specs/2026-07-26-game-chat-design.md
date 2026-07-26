---
state: Current
last_updated: "2026-07-26"
---

# 游戏聊天完整功能设计

## 文档角色与依据

本文档是 `CAP-06` 游戏聊天能力的已批准变更设计，不是当前实现证据。产品合同由[产品需求文档](../../PRD.md)定义，页面与交互由[产品设计](../../design.md)定义，当前实现边界由[系统架构](../../architecture.md)定义，目标后端和 Admin 边界分别由[后端目标架构蓝图](../../architecture/backend-target-blueprint.md)与[Admin 前端目标架构蓝图](../../architecture/admin-frontend-target-blueprint.md)定义，验证分级由[测试策略](../../test.md)定义。

本设计参考旧项目 `GameChat` 的用户能力和经验证的 7DTD 字段语义，但不复制其源码、Controller 直连游戏对象、FeatureManager 框架或 Element Plus 页面结构。

## 目标

- 一次性交付实时聊天、历史聊天、聊天设置和彩色聊天四项完整能力。
- 让 `Owner` 在网页中查看游戏聊天、发送全局消息和在线玩家私聊，并按稳定条件查询历史记录。
- 在不阻塞游戏主线程的前提下，把聊天事件实时推送并尽力持久化到本机 SQLite。
- 允许 `Owner` 配置聊天记录策略、服务端发送名称、频道默认颜色和玩家专属彩色 Profile。
- 彩色聊天失败时放行原消息，成功替换时只广播一次，不能因扩展功能故障阻断基础聊天。
- 复用现有认证、单一 SSE、OpenAPI、SQLite、游戏主线程调度、Hey API 和 Pinia Colada，不安装新包。

## 非目标

- 不实现禁言、封禁、举报、敏感词过滤、自动处罚或聊天内容审核工作流。
- 不实现 Discord 双向聊天、跨服聊天、玩家网页登录聊天或聊天机器人。
- 不提供历史导出、全文搜索引擎、跨数据库归档或跨服务器聚合。
- 不把命令前缀扩展为通用聊天命令框架；首版只用于历史排除和彩色重写绕过。
- 不解析或执行聊天正文中的 HTML、Markdown、链接或浏览器脚本。
- 不保证与其他同样拦截并重写 `ModEvents.ChatMessage` 的第三方 Mod 自动兼容；冲突必须通过真实 7DTD 人工验收确认。

## 当前基线

- 后端已经拥有认证的 `GET /api/v1/events/stream`、进程内统一 sequence、replay、gap、heartbeat、角色复验和慢订阅者隔离。
- Admin 已使用生成的 Fetch SSE 客户端维护唯一应用级连接，并在身份变化时取消连接、清理游标和服务端缓存。
- SQLite、Dapper、DbUp migration、短连接、WAL 和 Store 组合模式已经存在。
- SevenDays Adapter 已有游戏生命周期、在线玩家身份来源、主线程调度和控制台命令执行边界。
- Admin 已采用 Vue 3、Nuxt UI、Valibot、Vue I18n、Hey API、Pinia Colada 和 Feature 局部 Composition API 状态。
- 当前没有聊天事件适配器、聊天持久化、聊天 REST、`chat-message` SSE、聊天路由或彩色消息重写。

## 权限边界

聊天记录可能包含私聊、玩家身份和运营信息，因此完整聊天模块首版只允许 `Owner`。

| 能力 | Owner | Admin | Viewer | 未认证 |
|---|---|---|---|---|
| 聊天导航和页面 | 允许 | 拒绝 | 拒绝 | 登录 |
| 实时聊天与历史记录 | 允许 | 403 | 403 | 401 |
| 发送全局消息或私聊 | 允许 | 403 | 403 | 401 |
| 修改聊天设置 | 允许 | 403 | 403 | 401 |
| 彩色设置与 Profile CRUD | 允许 | 403 | 403 | 401 |
| `chat-message` SSE | 允许 | 服务端过滤 | 服务端过滤 | 401 |

前端隐藏导航只改善体验，REST、SSE replay 和 live 必须分别执行服务端授权。被过滤的事件仍推进该连接内部游标，不能因角色过滤造成重复 replay 或伪造 gap。

## 信息架构

全局导航新增“游戏聊天”，展开后包含：

1. `/game-chat/live`：实时聊天。
2. `/game-chat/history`：历史聊天。
3. `/game-chat/settings`：聊天设置。
4. `/game-chat/colored`：彩色聊天，页面内包含“玩家 Profile”和“默认设置”两个标签。

移动端使用同一信息层级，不为四项能力创建四套独立侧栏。无权限时导航不显示，深链接显示权限拒绝状态。

## 统一聊天模型

后端从一次 7DTD 原始聊天事件提取并立即复制以下不可变语义：

| 字段 | 语义 |
|---|---|
| `sequence` | 当前进程统一事件序列，用于 SSE 排序、补取和快照/live 去重 |
| `occurredAtUtc` | Mod 观察到消息的 UTC 时间，不由浏览器生成 |
| `entityId` | 发送者本次运行期实体 ID；系统消息使用游戏提供的特殊值 |
| `crossplatformId` | `ClientInfo.CrossplatformId.CombinedString`；系统来源或不可用时为 `null` |
| `senderName` | 本次事件中的发送者显示名称；空值按系统来源处理，不伪造玩家名 |
| `chatType` | 规范化的 `Global`、`Friends`、`Party`、`Whisper` 或 `Unknown` |
| `sourceKind` | `Player`、`Administrator` 或 `System`，由原始事件上下文判定 |
| `message` | 去除彩色重写所注入表现标记前的原始语义正文，以纯文本存储和返回 |

系统消息参考旧项目的可靠判定：`SenderEntityId == -1`、`ClientInfo == null` 或命令执行代理；管理员消息通过当前游戏权限 API 在受控游戏线程判定。无法可靠判定时使用 `Player` 或 `System` 的保守回退并记录诊断，不伪造管理员身份。

首版不把私聊目标加入观察模型，因为旧项目没有证明所有 `EChatType.Whisper` 事件都能稳定提供接收者。面板主动发送私聊时，目标跨平台 ID只进入该次请求和审计，不反推到其他观察记录。

## 后端运行链路

### 原始事件协调

SevenDays Adapter 直接注册 `ModEvents.ChatMessage`，在同一个窄协调器中完成必须同步发生的工作：

```text
SChatMessageData
  -> copy immutable context
  -> classify command / source / channel
  -> optionally resolve colored settings and player profile
  -> no rewrite: publish canonical chat event and Continue
  -> rewrite: broadcast one rendered replacement, publish canonical chat event,
              return StopHandlersAndVanilla
  -> any rewrite failure: log safe diagnostic, publish original canonical event,
                          return Continue
```

协调器不能访问 SQLite、等待网络、创建每消息 `Task.Run` 或从 Web 层取服务。Profile 与设置读取来自运行时内存快照；持久化成功后原子替换对应快照。

若替换消息所用游戏 API 会再次触发 `ModEvents.ChatMessage`，SevenDays Adapter 使用仅覆盖本次主线程调用的重入抑制标记，让替换消息直接发送且不再次着色、记录或广播。抑制只保护本次内部重发，不能按消息文本做长期去重。

### 实时推送与持久化

协调器为每条 canonical chat 分配统一 `sequence`，先非阻塞发布 `chat-message` 到现有 `ServerEventHub`，再 `TryWrite` 到专用有界持久化队列。队列使用单消费者写入 SQLite，保持接受顺序；满载、Store 失败和关服排空超时形成聊天历史 gap 诊断，但不得撤回已经发送的游戏聊天或阻塞游戏线程。

聊天历史保存上述统一字段。`sequence` 在当前进程内唯一，数据库行 ID只用于内部定位；公开历史分页使用稳定的时间加行 ID keyset cursor，不能依赖跨进程 sequence 连续。

保留策略在启动后和每日低频执行一次：`historyRetentionDays > 0` 时删除早于 UTC 截止时间的记录；`0` 表示不自动删除。清理不在聊天事件回调中执行。

### 管理员发送

两个类型化用例分别处理全局消息和在线玩家私聊：

- `POST /api/v1/chat/messages/global`
- `POST /api/v1/chat/messages/private`

请求正文只接受 `message`，私聊额外接受 `targetCrossplatformId`。正文 trim 后必须为 1 至 500 个字符；目标必须与当前在线玩家的稳定跨平台身份精确匹配。请求经 `Owner` 授权、审计意图和现有 `GameThreadDispatcher` 后调用 SevenDays 类型化发送端口，不通过浏览器提交控制台命令。并发写入使用独立的小型有界 FIFO；队满、游戏未就绪、目标已离线、开始前取消和结果未知返回稳定 Problem Details。开始执行后不能因 HTTP 取消伪造失败。

发送成功表示游戏发送调用已被接受，不表示每个客户端已显示。操作审计保存操作者、频道、目标、时间、结果和消息长度，不保存完整消息正文。

## REST 与 OpenAPI

所有接口位于 `/api/v1/chat`，使用现有 Bearer、Problem Details、Correlation ID、OpenAPI 快照和生成链。

| 方法与路径 | 用途 |
|---|---|
| `GET /api/v1/chat/messages/recent?limit=200` | 实时页初始上下文；从现有当前进程事件窗口过滤聊天事件，`limit` 范围 `1..500`，按 sequence 升序返回，不混入上一进程 SQLite 记录 |
| `GET /api/v1/chat/messages` | 历史游标分页；支持 `cursor`、`limit`、`crossplatformId`、`senderName`、`chatType`、`sourceKind`、`startUtc`、`endUtc` |
| `POST /api/v1/chat/messages/global` | 发送全局消息 |
| `POST /api/v1/chat/messages/private` | 向当前在线玩家发送私聊 |
| `GET /api/v1/chat/settings` | 读取聊天设置 |
| `PUT /api/v1/chat/settings` | 保存聊天设置并立即替换运行时快照 |
| `DELETE /api/v1/chat/settings` | 恢复内置默认值 |
| `GET /api/v1/chat/colored/settings` | 读取彩色聊天设置 |
| `PUT /api/v1/chat/colored/settings` | 保存设置并立即生效 |
| `DELETE /api/v1/chat/colored/settings` | 恢复内置默认值 |
| `GET /api/v1/chat/colored/profiles` | Profile 过滤、排序和游标分页 |
| `POST /api/v1/chat/colored/profiles` | 创建玩家 Profile |
| `PUT /api/v1/chat/colored/profiles/{crossplatformId}` | 按稳定业务键更新 Profile |
| `DELETE /api/v1/chat/colored/profiles/{crossplatformId}` | 删除 Profile 并清除运行时缓存 |

查询字符串和响应中的时间统一使用 UTC ISO 8601。历史默认按 `occurredAtUtc`、内部行 ID 倒序；`startUtc > endUtc`、无效 cursor、未知枚举和超出 limit 返回 400。空结果返回 200 和空集合，不使用 404。

## 聊天设置

聊天设置只保留首版有真实运行时消费者的字段：

| 字段 | 规则 |
|---|---|
| `isEnabled` | 控制新的聊天捕获和面板主动发送；关闭后已有历史仍可查询 |
| `globalServerName` | 面板发送全局消息时的可选服务端显示名称 |
| `whisperServerName` | 面板发送私聊时的可选服务端显示名称 |
| `commandPrefixes` | 去重后的单字符前缀集合，默认包含 `/`；用于识别命令 |
| `excludeCommandsFromHistory` | 命令消息是否不写入历史；实时页仍可看到原始游戏事件 |
| `historyRetentionDays` | `0..3650`；`0` 不自动删除 |

首版不加入旧项目的 `AllowNoPrefix`、命令参数分隔符、隐藏已注册命令广播和禁言通知模板，因为当前项目没有对应聊天命令或禁言运行时消费者。

设置保存使用 SQLite 单行配置和明确 schema；Web 只映射合同，Application 校验并提交 Store，运行时在事务成功后替换不可变快照。保存失败保留旧值和旧运行时行为。重置返回实际恢复后的完整设置。

## 彩色聊天

### 默认设置

彩色设置包含：

- `isEnabled`。
- `globalDefaultColor`、`whisperDefaultColor`、`friendsDefaultColor`、`partyDefaultColor`。
- `adminDefaultColor`、`systemDefaultColor`。
- `playerColorTagPermission`：`None`、`AdminOnly` 或 `All`。

颜色统一存储为不带 `#` 的六位 RGB 十六进制大写字符串；空值表示无覆盖。前后端都校验，但服务端为权威。玩家消息中的颜色标签默认作为普通文本或被转义；只有策略允许的玩家可使用经过白名单解析的颜色标签，其他 BBCode/富文本标签不得原样注入游戏渲染字符串。

### 玩家 Profile

每个 Profile 以 `crossplatformId` 为唯一业务键，包含：

- `customName`：可空名称模板。
- `nameColor`：可空名称颜色。
- `textColor`：可空正文颜色。
- `description`：只在面板显示的可空运营备注。
- `createdAtUtc`、`updatedAtUtc`：服务端时间。

名称模板首版只支持大小写不敏感的 `{playerName}`、`{playerId}`、`{entityId}` 和 `{chatType}`。未知变量保持为普通文本，不执行表达式、反射或脚本。渲染后的名称和正文分别执行长度上限和标签白名单，避免 Profile 产生无限扩张或任意富文本。

Profile 查询支持跨平台 ID、自定义名称、名称颜色、正文颜色和创建时间范围；默认按更新时间倒序。创建冲突返回 409，更新或删除不存在的业务键返回 404。写入事务成功后更新或清除对应内存项；读取可以使用有界惰性缓存，但缓存不得成为第二份持久事实。

### 颜色解析优先级

1. 系统消息使用 `systemDefaultColor`。
2. 管理员玩家消息优先使用 `adminDefaultColor`。
3. 普通玩家存在 Profile 时，名称和正文分别使用 Profile 覆盖。
4. 未覆盖部分回退到当前 `chatType` 的默认颜色。
5. 无有效颜色时保留游戏原始表现。

命令消息始终绕过彩色重写。空消息、未知频道、缺失玩家身份或任何处理异常均 fail-open，继续原版链路。

## Admin 页面设计

### 实时聊天

- 页面先订阅应用级 `chat-message`，缓冲 live，再读取最近 200 条，最后按 `sequence` 合并、升序和去重。
- 顶部提供 `全部`、`全局`、`好友`、`队伍`、`私聊`、`未知`频道筛选；筛选只影响当前内存列表，不建立额外请求缓存。
- 消息行显示频道、发送者、来源类型、时间和纯文本正文；不能使用 `v-html` 或解释游戏 BBCode。
- 右侧或窄屏抽屉显示在线玩家，复用在线玩家 Query；选择具有稳定跨平台 ID 的玩家后输入区切换为私聊，清除目标后恢复全局。
- 输入支持 Enter 发送、Shift+Enter 换行和当前页面最多 50 条成功发送历史；提交失败保留正文和目标，不自动重试。
- 用户位于底部时自动跟随；离开底部后保持阅读位置并显示未读数量。SSE gap 显示在消息区外，并刷新最近上下文，但不宣称完整历史已经补齐。

### 历史聊天

- 筛选包含跨平台 ID、发送者名称、频道、来源和 UTC 时间范围；筛选、排序和游标状态进入 URL。
- 表格显示时间、发送者、跨平台 ID、实体 ID、频道、来源和纯文本正文；窄屏使用摘要列表和详情展开。
- 查询条件变化时取消旧请求并从第一页开始；继续加载使用服务端 cursor，不自行计算 offset。
- 请求失败保留最后成功页并显示 Stale；首次失败显示明确错误；无数据使用空状态。

### 聊天设置

- 按“功能与发送名称”“命令与历史”分组展示批准字段。
- 保存、重置分别使用生成 Mutation；离开脏表单前确认。
- 页面明确说明关闭功能不会删除已有历史，保留天数 `0` 表示不自动清理。

### 彩色聊天

- “玩家 Profile”标签提供过滤、游标分页、新增、编辑、删除确认和纯文本效果预览。
- Profile 编辑时业务键不可修改；需要更换身份时删除后重新创建。
- 名称模板变量通过按钮插入，不提供自由脚本。
- “默认设置”标签展示功能开关、玩家颜色标签权限、六类默认颜色和实时安全预览。
- 颜色控件复用 Nuxt UI 输入和本地预设，不为颜色选择器安装第三方包。

## Admin 状态与缓存

- 所有生成 Query 固定使用 `queryOptions: { staleTime: 0, refetchOnWindowFocus: false }`。
- 实时消息窗口、发送历史、滚动和未读数属于页面局部状态，不进入 Pinia Colada cache 或浏览器 Storage。
- 历史、设置和 Profile 使用生成 Query；Mutation 成功后只精确失效所属查询键，不清空无关服务端状态。
- `chat-message` 只进入实时页监听器或触发精确历史失效；不能任意覆盖历史页当前 cursor 数据。
- 登出、Token 替换或 401 复用现有全局流程，取消 SSE 并清空 Query/Mutation cache；聊天草稿不持久化。

## 数据库与生命周期

新增顺序 migration，至少包含 `chat_messages`、`chat_settings`、`colored_chat_settings` 和 `colored_chat_profiles`。索引围绕历史默认排序、跨平台 ID、发送者、频道、来源和 Profile 唯一业务键建立；不创建全文索引。

启动顺序为 migration、加载设置快照、启动聊天历史 writer、注册聊天事件；关闭顺序相反：先注销事件，完成 writer，在固定截止时间内排空，再释放 Store。彩色 Profile 不需要启动时全量加载，可按玩家身份惰性读取并有界缓存。

## 目录目标

```text
backend/src/Core/LSTY.SevenDPanel.Application/Chat/
  ChatMessage.cs
  ChatSettings.cs
  ChatUseCases.cs
  ChatPorts.cs
backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Inbound/Chat/
  SevenDaysChatRuntime.cs
  SevenDaysChatMessageCoordinator.cs
backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Outbound/Chat/
  SevenDaysChatMessageSender.cs
backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/
  SqliteChatStore.cs
  SqliteColoredChatStore.cs
  Migrations/006_GameChat.sql
backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/
  ChatController.cs
  ChatHttpModels.cs

frontend/apps/admin/src/pages/game-chat/
  live.vue
  history.vue
  settings.vue
  colored.vue
frontend/apps/admin/src/features/game-chat/
  api/
  model/
  ui/
```

文件名是计划阶段的职责地图，不要求为每个类型机械拆文件。只有不同变化原因已经明确时才拆分；不得创建通用 Feature 注册表、第二条 SSE 或没有生产消费者的聊天命令框架。

## 依赖决策

本变更不安装新包。

- 后端复用 .NET Framework 4.8、现有 7DTD 引用、Channels、Microsoft DI、Dapper、DbUp、Microsoft.Data.Sqlite、OWIN/Web API、NSwag 和 Newtonsoft.Json。
- 前端复用 Vue、Nuxt UI、VueUse、Valibot、Vue I18n、Hey API、Pinia Colada、Vitest 和 Playwright。
- 颜色输入使用 Nuxt UI/原生颜色输入和本地预设；消息正文使用纯文本节点。

## 最小验证范围

- 后端聚焦单元：字段复制、来源和频道分类、命令绕过、颜色优先级、标签许可、模板变量、重写成功单次广播和异常 fail-open。
- SQLite 聚焦集成：migration、历史写入和游标分页、保留清理、设置事务、Profile 唯一键与缓存刷新。
- Katana/OpenAPI：全部聊天路由、Owner/非 Owner 权限、输入边界、Problem Details、SSE replay/live 过滤和受控 schema 生成。
- Admin 聚焦单元/组件：SSE 快照合并、gap、频道筛选、发送失败保留、历史 URL 筛选、脏表单、颜色校验和纯文本安全渲染。
- 浏览器只保留一条 Owner 主路径和一条非 Owner 拒绝路径。
- 真实 7DTD 只做一次人工边界验收：玩家消息实时出现、全局/私聊发送、命令绕过、六类默认色、玩家 Profile、无重复广播、关闭彩色功能恢复原版、异常时原消息仍可见。

不重复执行与聊天无关的真实游戏、发布或浏览器流程；功能稳定后只运行一次受影响项目聚合检查。

## 风险与恢复

| 风险 | 处理 |
|---|---|
| 第三方 Mod 同样重写聊天 | fail-open、单次真实游戏验收；不宣称自动兼容 |
| 彩色重发递归或双重广播 | 受控重入抑制；成功替换返回 `StopHandlersAndVanilla` |
| SQLite 变慢或不可用 | 有界队列、游戏线程不等待、记录历史 gap，实时聊天继续 |
| SSE 断开或窗口缺口 | `Last-Event-ID` replay；无法补齐时显示 gap 并刷新最近上下文 |
| Profile 或设置保存失败 | 事务失败保留旧运行时快照，不显示已生效 |
| 玩家标签注入 | 服务端白名单解析，Web 纯文本渲染，默认禁止玩家颜色标签 |
| 私聊内容暴露 | 完整聊天模块和 `chat-message` SSE 首版仅 Owner 可读 |

## 文档影响

- `docs/PRD.md`：新增 `CAP-06` 游戏聊天产品合同。
- `docs/design.md`：新增游戏聊天导航、四页面、状态和响应式规则。
- `docs/architecture.md`：记录已批准但尚未实现的边界和当前证据缺口。
- `docs/test.md`：增加 `CAP-06` 追踪和聊天特有风险验证。
- `docs/architecture/backend-target-blueprint.md`：增加聊天事件、持久化、发送和彩色重写目标链路。
- `docs/architecture/admin-frontend-target-blueprint.md`：增加聊天 Feature、单一 SSE、状态所有权和目录目标。
