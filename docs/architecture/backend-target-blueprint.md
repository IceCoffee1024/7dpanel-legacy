---
state: Draft
last_updated: "2026-07-21"
---

# 7DPanel 后端目标架构蓝图

> 本文是 7DPanel 后端的批准目标设计，不是当前实现证据。当前系统事实以
> [系统架构](../architecture.md)为准，产品行为以 [PRD](../PRD.md) 为准，验证要求以
> [测试策略](../test.md) 为准。

## 用途与生命周期

本蓝图把首版后端能力映射到目标运行链路、项目边界和生产文件职责。它回答两个问题：

1. 请求或事件应如何经过端口、线程、状态和副作用；
2. 每项后端目标职责应该放在哪里，以及原因是什么。

目录布局是设计地图，不是脚手架。只有某个纵向切片真正需要某项职责时，才创建对应目标文件。
当目标决策已经实现并验证后，将持久的当前事实提升到 `docs/architecture.md`；不得从本文推断实现证据。
当本文不再包含超出当前架构的有意义目标设计时，应删除本蓝图。

## 架构定位

后端使用 Explicit Architecture 作为组织方法。它结合 Ports and Adapters 的调用方向、Clean Architecture 的依赖规则，
并仅在领域确实需要时选择性采用 DDD 和 CQRS 模式。目标是让用例、外部边界、运行时所有权和技术依赖清晰可见，
而不是把任何示例目录模板当作强制规定。

项目遵循以下规则：

- Application 以及负责维护真实不变量的 Domain 规则共同组成应用核心。
- Inbound adapters 将 HTTP、7DTD 回调、控制台命令和计划任务转换为应用调用。
- Outbound adapters 为 7DTD、SQLite、文件、密码学和进程能力实现端口。
- Bootstrap 是唯一同时了解接口和具体实现的组合根。
- Hosting 提供技术中立的运行时命令、状态和 Web Host 生命周期契约；Bootstrap 与具体 adapter 显式组合生产资源。
- 输入端口接口是可选的；当接口不提供替换价值时，用例类本身可以作为稳定的输入边界。
- Aggregate Roots、通用 repositories、通用 Event Bus、Mediator 和可编程规则引擎都不是默认选择。
- 新项目、目录和抽象必须对应真实的职责、依赖、部署、不变量或复用边界。
- 横向基础设施应与首个非测试运行时消费者在同一个获批纵向切片中出现。只有真实外部边界、跨项目依赖方向、第二个运行时实现或消费者、已经形成的重复，或明确批准的近期消费者才能例外；测试替换本身不是生产抽象的理由。

### 分层

| 层 | 所有职责 | 可以依赖 | 不得依赖 |
|---|---|---|---|
| Bootstrap / Composition | Mod 入口、配置 I/O、对象图、部署内容 | 所有具体构造信息 | 业务规则 |
| Hosting | 技术中立的运行时命令、状态和 Web Host 生命周期 | BCL 与 Hosting 契约 | Application、Domain、具体 adapters |
| Application | 用例、授权协调、结果、读模型、输出端口 | Domain 与 Application 类型 | OWIN、SQLite、文件、Unity、7DTD 类型 |
| Domain | 必要的策略、不变量和状态转换 | BCL 与 Domain 类型 | Application、Hosting、adapters |
| Inbound Adapters | 将协议和回调转换为用例调用 | Application 公共类型和最小 Hosting 契约 | Outbound 实现 |
| Outbound Adapters | 面向外部技术实现核心端口 | Application 端口、最小 Hosting 契约、外部 API | Controllers 和用例编排 |

依赖指向 Application 和 Domain。Bootstrap 是唯一可以构造并连接两侧的地方。

### 项目依赖

```text
LSTY.SevenDPanel.Domain                         no product references
            ^
            |
LSTY.SevenDPanel.Application                    Domain only
       ^             ^              ^
       |             |              |
Adapters.Web   Adapters.SevenDays   Adapters.Local
       ^             ^              ^
       +-------------+--------------+
                     |
             LSTY.SevenDPanel                    Bootstrap

LSTY.SevenDPanel.Hosting                         independent runtime contracts
       ^                    ^                    ^
       |                    |                    |
Adapters.Web       Adapters.SevenDays      Adapters.Local
       ^                    ^                    ^
       +--------------------+--------------------+
                            |
                    LSTY.SevenDPanel
```

Adapter 项目按外部边界命名：`Web`、`SevenDays` 和 `Local`。项目内第一层使用 `Inbound` 或 `Outbound`，
再按 `Http`、`Lifecycle`、`Players` 或 `Persistence` 等能力分组。Adapter 根目录不得包含混合调用方向的 `Common` 目录。

`Application/Common` 同样不是工具杂物抽屉。只有当类型具有稳定的跨能力语义、至少两个真实消费者、
没有更明确的 Feature 所有者且不依赖外部技术时，才允许放入其中。

### 领域建模

首版不引入 Aggregate Root 基础设施或通用 repository。默认模型如下：

- 在线玩家、服务器状态和日志使用不可变快照；
- 用例负责协调授权、审计和类型化游戏端口；
- 身份、会话、作业、审计、备份和自动化分别使用有明确用途的 store port；
- Domain 仅承载权限映射、备份与恢复转换、自动化去重或其他真实不变量所需的策略；
- 对必须在一次 SQLite 事务中变化的记录，使用能力范围内的原子 store 方法。

只有当多个对象必须在同一个并发边界内保持一致，并且需要统一的版本控制时，才重新考虑 Aggregate Root。

## 依赖策略与候选库

[系统架构中的依赖兼容矩阵](../architecture.md#当前依赖兼容矩阵)是已经选定的后端运行基线及精确版本的权威来源。
本节只保存目标能力与候选技术之间的决策线索，不复制版本号，也不构成安装指令。实际安装结果和精确版本分别以
`.csproj`、`package.json` 及其锁文件为准；安装命令和包管理操作属于对应实施计划或工程说明。

依赖状态采用以下含义：

- **已采用**：当前项目清单和实现中已经存在，并有相应验证证据；
- **已批准**：目标方向已经决定，但不得据此声称当前实现存在；
- **候选**：实现时优先评估，不保证最终采用；
- **预留**：只有引入条件，尚不足以选择具体第三方库；
- **默认不采用**：除非出现表中所列证据，否则保持显式代码或 BCL 实现。

| 能力或场景 | 目标边界 | 当前方向或候选 | 状态 | 引入或复审触发条件 | 实现前必须验证 |
|---|---|---|---|---|---|
| Web API 与 OWIN Self Host | Web Adapter | `Microsoft.AspNet.WebApi.*`、`Microsoft.Owin.*` | 已采用 | 升级 7DTD、Mono、HTTP 管线或当前包基线 | `net48` 与游戏 Mono 进程内加载、路由、关闭释放、程序集冲突和安全公告 |
| 过渡认证与生产 SSE | Web Adapter / Bootstrap | `Microsoft.Owin.Security.OAuth` + 自有 Basic middleware + 配置身份 | 已采用；待最终身份替换 | 首个 SQLite/Header Bearer 身份切片或认证公开面变化 | 不透明 Token、到期/容量、限流、统一 Problem Details、Header-only Bearer、连接配额、命名事件和真实 Mono 加载；产品不采用 Cookie 认证，当前实现边界见[系统架构](../architecture.md#owinweb-api-与静态资源) |
| 本地 SQLite 与原生运行时 | Local Adapter / Persistence | `Microsoft.Data.Sqlite`、`SQLitePCLRaw.lib.e_sqlite3` | 已批准 | 首个身份、审计或作业持久化切片 | Windows/Linux x64 原生资产、WAL、并发写入、发布边界和正常关服 |
| 数据库迁移 | Local Adapter / Persistence | `dbup-core`、`dbup-sqlite` | 已批准 | 首个可演进 SQLite schema | 嵌入脚本顺序、事务失败、重复运行、升级和恢复路径 |
| 有界后台队列 | Local Adapter / Runtime | `System.Threading.Channels` | 已批准 | 首个后台 consumer 或持久作业 | 容量、背压、公平性、异常传播、排空和 Mono 兼容性 |
| 组合根依赖注入 | Bootstrap / Composition | `Microsoft.Extensions.DependencyInjection 6.0.2`、Abstractions `6.0.0` | 已采用 | 目标游戏 Mono、宿主 Bcl AsyncInterfaces 或对象生命周期边界变化 | Bootstrap 唯一根 Provider、显式注册、`ValidateOnBuild`/`ValidateScopes`、OWIN 单请求 scope、Web API non-owning bridge、先停运行时再释放 Provider、发布体积和关服行为；当前实现证据见[系统架构](../architecture.md#owinweb-api-与静态资源) |
| Mod 运行日志 | Bootstrap / SevenDays Adapter | 7DTD 提供的 `LogLibrary`（`Log.Out`、`Log.Warning`、`Log.Error`、`Log.Exception`） | 已采用 | 7DTD 日志 API 或目标版本发生变化 | 目标程序集加载、输出行为、异常记录和关服生命周期 |
| 控制台日志采集 | SevenDays Adapter / ConsoleLogs | `LogLibrary.LogCallbacksExtended` + `System.Threading.Channels` 有界队列 | 已采用 | 7DTD 日志 API、游戏版本或当前容量基线变化 | 回调耗时、顺序、订阅与取消订阅、过载丢弃计数、Mono 兼容性和版本差异；当前实现证据见[系统架构](../architecture.md#7dtd-控制台日志采集边界) |
| 密码摘要 | Local Adapter / Identity | BCL PBKDF2-HMAC-SHA256 | 已批准 | 首个 Owner 身份切片 | 游戏 Mono 支持的 API、参数版本化、随机盐、耗时上限和升级策略 |
| 备份压缩与校验 | Local Adapter / Backups | 优先使用 BCL；第三方库待证据驱动选择 | 预留 | BCL 无法满足流式处理、格式、性能或恢复兼容要求 | 内存峰值、大文件、损坏检测、路径穿越、许可证、维护状态和跨平台行为 |
| 定时任务 | Local Adapter / Scheduling | 内部 hosted scheduler，不引入通用调度框架 | 默认不采用 | 出现持久日历、时区、错过触发补偿或分布式调度等真实需求 | 与持久作业状态的职责边界、关服排空、恢复语义和依赖成本 |
| 用例分派与对象映射 | Application / Adapters | 显式 dispatcher 和手工映射为默认；`Mapster` 作为稳定边界映射重复出现后的候选 | 候选 | 首个真实 DTO/Domain/View 映射切片形成多个稳定映射对，且重复代码维护成本有测试证据 | 优先评估代码生成或显式配置；映射配置启动期校验；验证隐式控制流、反射、调试成本、性能、AOT/Mono 限制、发布体积和边界泄漏 |
| 映射表达式编译优化 | Application / Adapters / Bootstrap | `FastExpressionCompiler` 仅作为 `Mapster` 运行时表达式编译的可选优化，不单独引入 | 预留 | 已采用 `Mapster` 且代表性基准证明 `Expression.Compile` 成为实际瓶颈 | 验证 `CompileFast` 等价性、目标 Unity Mono、动态代码/AOT 限制、启动与分配成本、失败回退和游戏主线程首次编译行为 |

当某个纵向切片准备实现时，必须重新检查候选库的维护状态、许可证、安全公告、传递依赖、原生资产、
发布体积以及目标 `net48`/Unity Mono 兼容性。若当时存在更合适且证据更充分的库，实施者应先说明替代方案及权衡；
涉及架构方向变化时先更新本蓝图或对应变更设计，再修改项目清单。实现和真实进程验证完成后，只把持久结论提升到
[系统架构](../architecture.md)，不把候选状态继续当作当前事实。

`Mapster` 进入候选并不表示当前安装或默认采用。只有在首个真实映射切片中出现多个稳定映射对，
并且测试证据表明手工实现的维护成本已经成为问题时，才应将其加入具体项目。
`FastExpressionCompiler` 不是对象映射库；除非采用 `Mapster` 后的代表性基准证明运行时表达式编译需要优化，
否则保持不安装。

前端依赖不由本后端蓝图定义。Admin 当前与目标依赖分别以实际 `package.json`、锁文件和
[Admin 前端目标蓝图](admin-frontend-target-blueprint.md)为准；`frontend/apps/marketing/` 尚未初始化框架工程。
未来前端应用形成明确框架和功能边界时，应在该应用的 Target 蓝图或变更设计中记录候选，实际版本仍以
对应 `package.json` 和锁文件为权威来源。

## 运行时执行模型

### Mod 生命周期

```text
7DTD Mod Loader
  -> ModMain.InitMod
  -> load configuration and build the object graph
  -> register shutdown lifecycle handlers
  -> ConsoleLogRuntime.Start
       -> ConsoleLogService.Start
       -> ModHost.Start -> start OWIN
       -> health reports panel HTTP liveness

GameStartDone
  -> Lifecycle Adapter
  -> mark the game runtime ready
  -> start only components that require live 7DTD state

WorldShuttingDown / GameShutdown
  -> Lifecycle Adapter
  -> ConsoleLogRuntime.Stop, idempotently
       -> unsubscribe and drain ConsoleLogService
       -> ModHost.Stop -> dispose OWIN
```

`IModRuntime` 是命令型生命周期契约。当前由薄的 `ConsoleLogRuntime` 显式组合日志服务与 `ModHost`，不建立通用组件注册表。未来出现数据库、后台 consumer 或 scheduler 时，先在组合根中明确所有权和停止顺序；只有重复协调已经形成可证明成本时才提取通用生命周期。面板 HTTP 存活与游戏运行时就绪是两个独立状态：`InitMod` 可以提供静态页面和不依赖游戏对象的 API，依赖 Unity/7DTD 活对象的组件和用例只能在 `GameStartDone` 后进入可用状态；此前对应 API 返回 `503` 和稳定错误码。

### 请求与游戏动作链路

```text
Browser
  -> OWIN / Web API
  -> authentication and controller
  -> Application use case
  -> typed Application port
  -> outbound adapter
  -> bounded main-thread scheduler
  -> 7DTD API
```

Application 代码永远不会接收活动的 Unity 或 7DTD 对象。游戏 adapter 在游戏主线程上将这些对象映射为不可变快照后再返回。

### 执行通道

```text
HTTP worker
  Controller -> Use Case -> SQLite / File / Queue Port
                         -> Main Thread Scheduler -> 7DTD

7DTD main thread
  Game Event Adapter -> immutable event snapshot -> background queue

Background worker
  schedules / compression / verification / persistence
  -> return briefly to the main thread only through a typed port
```

HTTP 和后台 worker 不得直接访问活动的游戏对象。游戏线程不得执行数据库查询、压缩、网络等待或无界文件复制。

控制台日志使用独立的尽力而为通道，不进入通用后台工作 dispatcher：

```text
Log.LogCallbacksExtended
  -> immutable ConsoleLogEntry
  -> bounded Channel.TryWrite
  -> one tracked consumer
  -> bounded current-process live window
```

日志回调只复制游戏提供的字段并尝试非阻塞入队。它不得执行文件、SQLite、网络或同步事件广播，也不得为每条日志创建 `Task.Run`。队满时允许拒绝普通日志，但必须准确计数；consumer 就绪后才能订阅，关闭时先注销生产者，再完成写入侧并限时排空。详细设计见[控制台日志服务设计规格](../superpowers/specs/2026-07-20-console-log-pipeline-design.md)。

### 后台工作

```text
Game Event / Scheduler / Use Case
  -> immutable BackgroundWorkItem
  -> IBackgroundWorkQueue
  -> BoundedBackgroundWorkQueue
  -> BackgroundWorkConsumer
  -> BackgroundWorkDispatcher
  -> exactly one Application Use Case
```

- 工作项只包含不可变标识符和值，不包含活动对象、连接或捕获的委托。
- dispatcher 将已知工作项类型明确映射到一个用例。不通过广播、程序集扫描或反射注册处理器。
- 一个 hosted consumer 组件拥有读取循环和有界执行槽位；长时间备份不得阻塞所有短事件。
- 一个 hosted scheduler 负责周期检查。自动化和备份触发器是无状态策略，不拥有独立生命周期。
- 持久作业在入队前先保存，重启后从 `queued` 或 `running` 状态幂等恢复。
- 短暂的游戏通知不会跨进程故障持久化，但拒绝和过载必须可观测。
- 关闭时停止生产者、完成写入侧，并在截止时间内排空 consumer。

### 持久化与事务

- SQLite 初始化并验证 WAL 模式，并为每个连接应用经过测试的 `busy_timeout`。
- 低频原子操作使用短事务，由能力专属的 store adapter 隐藏事务细节。
- 高频审计写入使用独立的有界串行协调器，不得与可丢弃的控制台日志共享容量。
- 高风险游戏动作只有在审计意图得到持久确认后，才能进入主线程队列。
- 原始控制台日志不复制到 SQLite。7DTD 每次进程启动生成的日志文件承担原始持久证据；7DPanel 只维护当前进程的有界内存窗口。日志过载可以被拒绝，但必须记录丢弃数量，且不得饿死审计。
- SQLite、待恢复文件、归档和游戏动作不属于同一个事务。跨边界流程使用持久状态、幂等步骤和补偿。
- Application 永远不会接收数据库连接、事务对象或 ambient database context。

## 目标业务链路

### 启动与首个 Owner

```text
ModMain
  -> configuration loader
  -> database migration
  -> apply pending restore before world files open
  -> ensure setup credential when no Owner exists
  -> local console prints one-time code and setup URL
  -> register lifecycle callbacks

POST /api/v1/setup
  -> validate setup code
  -> hash password and session token
  -> IIdentityProvisioningStore
       -> atomically consume setup state
       -> create first Owner
       -> create first session
  -> return the one-time plaintext opaque Bearer token in the response body
```

并发初始化请求最多只有一个成功。认证 middleware 将会话映射为操作者；每个用例仍然必须执行权限检查。

当前系统为先建立生产 SSE 的认证、错误和限流边界，暂时从服主 `config.json` 读取配置凭据，并按 `NFR-04` 在框架搭建阶段提供已知默认值 `username` / `password` 和明文 HTTP 例外，再通过 Basic 或 password grant 映射为临时 `Owner`。该身份没有 SQLite 用户、密码摘要、持久 Token、refresh token 或用户管理能力；实现本节首个 Owner 链路时，必须整体替换配置凭据、已知默认值和进程内 Token，迁移到 SQLite 身份与 `Authorization` Header Bearer，而不是并存两套长期身份来源。产品不采用 Cookie 认证；Token 持久化、刷新和浏览器恢复策略由该身份切片设计。替换后恢复加密传输要求，并保持稳定的 401/403 Problem Details、角色 claims 和受保护 Controller 边界，具体当前事实见[系统架构](../architecture.md#本地配置与状态)。

### 在线玩家

```text
GET /api/v1/players/online
  -> authentication
  -> GetOnlinePlayersUseCase
  -> authorize ViewPlayers
  -> IOnlinePlayerQuery
  -> SevenDaysMainThreadScheduler
  -> map live players to immutable PlayerSnapshot values
  -> HTTP response contracts
```

超时返回 `Unavailable` 或 `Unknown`，不得把过期数据伪装成实时数据。

### 玩家管理动作

```text
POST /api/v1/players/{id}/kick
  -> Header authentication and authorization
  -> KickPlayerUseCase
  -> authorize and validate confirmation
  -> persist audit intent
  -> IPlayerActions.KickAsync
  -> main-thread scheduler
  -> typed 7DTD action
  -> complete audit with Succeeded / Failed / Unknown
```

HTTP 超时不能证明游戏动作失败。动作一旦开始，就必须继续到最终的审计状态。

### 公告与自动化

即时公告先完成授权并持久化审计意图，再通过主线程 scheduler 使用类型化公告 gateway。

```text
PlayerJoined callback
  -> snapshot PlayerJoinedNotice on the game thread
  -> enqueue HandlePlayerJoinedWorkItem
  -> return immediately
  -> consumer and dispatcher
  -> HandlePlayerJoinedUseCase
  -> automation policy and deduplication
  -> announcement gateway
  -> execution record and audit
```

周期触发器和血月触发器通过同一管线入队不可变工作。首版不需要通用 Event Bus 或脚本引擎。

### 备份

```text
HTTP request or schedule trigger
  -> StartBackupUseCase
  -> reject conflicting backup or restore
  -> persist queued job and audit intent
  -> enqueue RunBackupJobWorkItem
  -> return Accepted + jobId

RunBackupJobUseCase
  -> request and await a 7DTD save commit
  -> create temporary snapshot off the game thread
  -> manifest, checksum, compress, and verify
  -> atomically publish verified archive and catalog entry
  -> complete job and audit state
```

临时输出失败时，永远不得出现在可恢复备份列表中。

### 恢复

```text
POST /api/v1/backups/{id}/restore
  -> require Owner and explicit confirmation
  -> verify catalog entry and archive
  -> persist PendingRestart job and audit intent
  -> atomically publish pending-restore file marker
  -> request graceful shutdown

Next Mod initialization, before world load
  -> read marker and re-verify archive
  -> create rollback copy
  -> restore archive
  -> complete job and audit, then clear marker
  -> on failure, attempt rollback and preserve evidence
```

恢复不是在线动作，也不是长时间占用的 HTTP 请求。其状态包括
`PendingRestart`、`Applying`、`Succeeded`、`Failed` 和 `RollbackFailed`。

### 日志、审计与关服

控制台日志查询只读取当前进程的有界内存窗口，使用进程内单调 sequence 排序和补取；服务端重启后窗口与 sequence 重新开始。7DTD 日志文件继续承担跨重启原始证据，本目标不要求把日志正文复制进 SQLite。审计搜索仍读取持久化、不可静默丢弃的游标分页读模型。Controllers 不拼接 SQL，也不暴露内部路径和异常堆栈。

当前实现已把旧开发日志流收敛为认证的 `GET /api/v1/events/stream`：受限 `ServerEventHub` 为每个客户端建立独立有界 mailbox，`console-log`、`game-ready` 和 `server-stopping` 共享当前进程 sequence，连接先发送 Welcome，建流前错误使用稳定 Problem Details。它仍不是通用领域 Event Bus，也没有持久日志查询或跨进程游标。目标日志页面继续使用 gap 和窗口补取，慢客户端不能阻塞主 consumer；最终 SQLite/Header Bearer 身份替换临时配置身份时应保持该流契约，并重新验证连接审计、Token 撤销和角色权限。

```text
WorldShuttingDown / GameShutdown
  -> health becomes Draining
  -> reject new HTTP writes and game-thread tasks
  -> stop producers in reverse registration order
  -> complete the background queue
  -> drain audit and accepted work within deadlines
  -> dispose OWIN, SQLite, queues, and logging
  -> Stopped, or Faulted when release fails
```

## 目标生产目录

下列每个文件都有明确的目标职责。本蓝图不声称这些文件当前已经存在；在两项职责尚未表现出不同的变化原因前，
实现可以暂时把它们放在一起。

```text
backend/
|-- 7DPanel.sln
|-- src/
|   |-- Bootstrap/LSTY.SevenDPanel/
|   |   |-- LSTY.SevenDPanel.csproj              # net48 Mod 入口和发布项目
|   |   |-- ModMain.cs                           # 唯一 IModApi 入口
|   |   |-- ModInfo.xml                          # 7DTD Mod 元数据
|   |   |-- config.example.json                  # 可分发的宿主配置示例
|   |   |-- Configuration/
|   |   |   |-- PanelHostConfig.cs               # 序列化配置契约
|   |   |   `-- PanelHostConfigurationLoader.cs # 配置 I/O 和选项映射
|   |   |-- DependencyInjection/
|   |   |   |-- PanelServiceProviderFactory.cs  # 唯一根 Provider 组合根
|   |   |   `-- ServiceProviderRuntime.cs       # 运行时停止后释放根 Provider
|   |   `-- Properties/PublishProfiles/
|   |       `-- FolderProfile.pubxml             # Mod 目录发布
|   |
|   |-- Runtime/LSTY.SevenDPanel.Hosting/
|   |   |-- LSTY.SevenDPanel.Hosting.csproj      # 技术中立的生命周期库
|   |   |-- ModHost.cs                           # OWIN Host 生命周期与状态
|   |   |-- ModHostState.cs                      # 从 Created 到 Faulted 的状态
|   |   |-- IModRuntime.cs                       # 生命周期 adapter 命令契约
|   |   |-- IPanelWebHost.cs                     # Web 宿主生命周期契约
|   |   |-- RuntimeHealth.cs                     # 对外可见的运行时健康状态
|   |   |-- PanelHostOptions.cs                  # 已验证的不可变监听选项
|   |   `-- ProductInfo.cs                       # 稳定的产品标识和版本
|   |
|   |-- Core/LSTY.SevenDPanel.Application/
|   |   |-- LSTY.SevenDPanel.Application.csproj # 产品依赖仅限 Domain
|   |   |-- Common/
|   |   |   |-- OperationOutcome.cs              # 如实表达的操作结果状态
|   |   |   |-- PageResult.cs                    # 有界游标分页
|   |   |   `-- Ports/IClock.cs                  # 可测试的当前时间
|   |   |-- BackgroundWork/
|   |   |   |-- BackgroundWorkItem.cs            # 不可变工作项协议
|   |   |   |-- IBackgroundWorkQueue.cs          # 有界队列生命周期契约
|   |   |   `-- BackgroundWorkDispatcher.cs      # 工作项到用例的映射
|   |   |-- Identity/
|   |   |   |-- EnsureSetupCredentialUseCase.cs  # 不存在 Owner 时的初始化状态
|   |   |   |-- CreateOwnerUseCase.cs            # 首个 Owner 和会话
|   |   |   |-- LoginUseCase.cs                  # 密码和会话流程
|   |   |   |-- LogoutUseCase.cs                 # 撤销当前会话
|   |   |   |-- ListUsersUseCase.cs              # Owner 用户列表
|   |   |   |-- UpdateUserRoleUseCase.cs         # 带审计的角色更新
|   |   |   |-- AuthorizationService.cs          # 操作者和权限协调
|   |   |   |-- Models/AuthenticatedActor.cs     # 不可变操作者快照
|   |   |   `-- Ports/
|   |   |       |-- IIdentityStore.cs            # 用户和密码摘要
|   |   |       |-- ISessionStore.cs             # 会话摘要和过期时间
|   |   |       |-- ISetupStateStore.cs          # 一次性初始化状态
|   |   |       |-- IIdentityProvisioningStore.cs # 首个 Owner 的原子操作
|   |   |       |-- IPasswordHasher.cs           # 版本化密码摘要
|   |   |       |-- ISecureTokenGenerator.cs     # 初始化和会话随机数
|   |   |       `-- ILocalConsole.cs             # 仅本地显示的初始化说明
|   |   |-- ServerStatus/
|   |   |   |-- GetServerStatusUseCase.cs        # 已授权的当前状态
|   |   |   |-- Models/ServerStatusView.cs       # 感知新鲜度的读模型
|   |   |   `-- Ports/IServerStatusQuery.cs      # 服务器状态快照
|   |   |-- Players/
|   |   |   |-- GetOnlinePlayersUseCase.cs       # 在线玩家查询
|   |   |   |-- KickPlayerUseCase.cs             # 已授权且可审计的动作
|   |   |   |-- Models/
|   |   |   |   |-- PlayerSnapshot.cs            # 线程安全的玩家读模型
|   |   |   |   |-- KickPlayerRequest.cs         # 目标、原因、确认信息
|   |   |   |   `-- KickPlayerResult.cs          # 结果和审计 id
|   |   |   `-- Ports/
|   |   |       |-- IOnlinePlayerQuery.cs        # 在线玩家快照
|   |   |       `-- IPlayerActions.cs            # 类型化玩家变更
|   |   |-- ConsoleLogs/
|   |   |   |-- SearchConsoleLogsUseCase.cs      # 有界游标搜索
|   |   |   |-- ExecuteConsoleCommandUseCase.cs  # 受限高级命令
|   |   |   |-- Models/ConsoleLogEntryView.cs    # 安全的日志读模型
|   |   |   `-- Ports/
|   |   |       |-- IConsoleLogQuery.cs          # 控制台日志搜索
|   |   |       `-- IRestrictedConsoleGateway.cs # 白名单控制台动作
|   |   |-- Announcements/
|   |   |   |-- PublishAnnouncementUseCase.cs    # 权限、审计、发布
|   |   |   |-- Models/
|   |   |   |   |-- PublishAnnouncementRequest.cs
|   |   |   |   `-- PublishAnnouncementResult.cs
|   |   |   `-- Ports/IAnnouncementGateway.cs   # 类型化游戏广播
|   |   |-- Automation/
|   |   |   |-- ListAutomationsUseCase.cs        # 配置和最近执行记录
|   |   |   |-- SaveAutomationUseCase.cs         # 固定触发器配置
|   |   |   |-- HandlePlayerJoinedUseCase.cs     # 玩家加入欢迎行为
|   |   |   |-- RunDueAutomationsUseCase.cs      # 周期自动化
|   |   |   |-- Models/
|   |   |   |   |-- AutomationExecutionView.cs
|   |   |   |   `-- HandlePlayerJoinedWorkItem.cs
|   |   |   `-- Ports/IAutomationStore.cs        # 配置和执行记录
|   |   |-- Backups/
|   |   |   |-- StartBackupUseCase.cs            # 持久化并入队备份
|   |   |   |-- RunBackupJobUseCase.cs           # 保存、快照、验证
|   |   |   |-- ListBackupsUseCase.cs            # 恢复候选项
|   |   |   |-- UpdateBackupScheduleUseCase.cs   # 计划配置
|   |   |   |-- RunDueBackupsUseCase.cs          # 到期计划处理
|   |   |   |-- RequestRestoreUseCase.cs         # 标记和关服
|   |   |   |-- ApplyPendingRestoreUseCase.cs    # 启动恢复和回滚
|   |   |   |-- Models/
|   |   |   |   |-- BackupView.cs
|   |   |   |   |-- JobView.cs
|   |   |   |   `-- RunBackupJobWorkItem.cs
|   |   |   `-- Ports/
|   |   |       |-- IWorldSaveGateway.cs         # 保存提交完成状态
|   |   |       |-- IServerShutdown.cs           # 优雅关服
|   |   |       |-- IBackupCatalog.cs            # 已验证元数据
|   |   |       |-- IJobStore.cs                 # 持久作业状态
|   |   |       |-- IBackupArchiveStore.cs       # 归档和恢复
|   |   |       `-- IPendingRestoreStore.cs      # 原子恢复标记
|   |   `-- Audit/
|   |       |-- SearchAuditUseCase.cs            # 有界审计搜索
|   |       |-- Models/AuditEntryView.cs         # 安全的审计读模型
|   |       `-- Ports/IAuditTrail.cs             # 审计生命周期
|   |
|   |-- Core/LSTY.SevenDPanel.Domain/
|   |   |-- LSTY.SevenDPanel.Domain.csproj       # 不引用其他产品项目
|   |   |-- Authorization/
|   |   |   |-- Permission.cs                    # 动作权限
|   |   |   |-- PanelRole.cs                     # Owner, Admin, Viewer
|   |   |   `-- RolePermissionPolicy.cs          # 纯角色映射
|   |   |-- Backups/
|   |   |   |-- BackupJobState.cs                # 备份状态
|   |   |   |-- BackupStateMachine.cs            # 有效状态转换
|   |   |   |-- RestoreState.cs                  # 恢复状态
|   |   |   `-- RestoreStateMachine.cs           # 恢复状态转换
|   |   `-- Automation/
|   |       |-- AutomationTrigger.cs             # 固定触发器类型
|   |       `-- AutomationExecutionPolicy.cs     # 作用域和去重
|   |
|   |-- Adapters/LSTY.SevenDPanel.Adapters.Web/
|   |   |-- LSTY.SevenDPanel.Adapters.Web.csproj
|   |   |-- Inbound/Http/
|   |   |   |-- OwinStartup.cs                   # OWIN 入站管线
|   |   |   |-- HealthController.cs              # 运行时健康端点
|   |   |   |-- ServerEventsController.cs        # 认证命名 SSE 入口
|   |   |   |-- ServerEventSseSession.cs         # 每请求订阅与 SSE 写出
|   |   |   |-- ApiResultMapper.cs               # 结果到 HTTP 的映射
|   |   |   |-- DependencyInjection/
|   |   |   |   |-- MicrosoftDependencyResolver.cs # Web API root/fallback scope
|   |   |   |   |-- ScopedServiceProviderMiddleware.cs # OWIN 请求 scope 所有者
|   |   |   |   `-- OwinScopeBridgingHandler.cs # Web API non-owning bridge
|   |   |   |-- Middleware/
|   |   |   |   |-- CorrelationMiddleware.cs    # 请求和审计 id
|   |   |   |   |-- SessionAuthenticationMiddleware.cs
|   |   |   |   |-- DrainingGateMiddleware.cs
|   |   |   |   `-- ExceptionMappingMiddleware.cs
|   |   |   |-- Identity/
|   |   |   |   |-- SetupController.cs
|   |   |   |   |-- SessionsController.cs
|   |   |   |   |-- UsersController.cs
|   |   |   |   `-- IdentityContracts.cs
|   |   |   |-- ServerStatus/
|   |   |   |   |-- ServerStatusController.cs
|   |   |   |   `-- ServerStatusContracts.cs
|   |   |   |-- Players/
|   |   |   |   |-- PlayersController.cs
|   |   |   |   `-- PlayerContracts.cs
|   |   |   |-- ConsoleLogs/
|   |   |   |   |-- ConsoleLogsController.cs
|   |   |   |   `-- ConsoleContracts.cs
|   |   |   |-- Announcements/
|   |   |   |   |-- AnnouncementsController.cs
|   |   |   |   `-- AnnouncementContracts.cs
|   |   |   |-- Automation/
|   |   |   |   |-- AutomationsController.cs
|   |   |   |   `-- AutomationContracts.cs
|   |   |   |-- Backups/
|   |   |   |   |-- BackupsController.cs
|   |   |   |   `-- BackupContracts.cs
|   |   |   `-- Audit/
|   |   |       |-- AuditController.cs
|   |   |       `-- AuditContracts.cs
|   |   `-- Outbound/Hosting/
|   |       `-- OwinWebHost.cs                    # Katana 宿主
|   |
|   |-- Adapters/LSTY.SevenDPanel.Adapters.SevenDays/
|   |   |-- LSTY.SevenDPanel.Adapters.SevenDays.csproj
|   |   |-- Inbound/
|   |   |   |-- Lifecycle/SevenDaysGameLifecycleAdapter.cs
|   |   |   |-- ConsoleCommands/SevenDaysConsoleCommandAdapter.cs
|   |   |   `-- Events/SevenDaysGameEventAdapter.cs
|   |   |-- Runtime/ConsoleLogs/
|   |   |   |-- ConsoleLogService.cs             # 服务及与 ModHost 的生命周期组合
|   |   |   |-- ConsoleLogEntry.cs
|   |   |   |-- ConsoleLogType.cs
|   |   |   |-- ServerEventLiveWindow.cs
|   |   |   |-- ServerEventWindowReadResult.cs
|   |   |   `-- ServerEventHub.cs                # 生产事件流的每客户端有界 mailbox
|   |   `-- Outbound/
|   |       |-- Console/SevenDaysLocalConsole.cs
|   |       |-- ServerStatus/SevenDaysServerStatusQuery.cs
|   |       |-- Players/
|   |       |   |-- SevenDaysOnlinePlayerQuery.cs
|   |       |   |-- SevenDaysPlayerActions.cs
|   |       |   `-- SevenDaysSnapshotMapper.cs
|   |       |-- Announcements/SevenDaysAnnouncementGateway.cs
|   |       |-- ConsoleLogs/
|   |       |   |-- SevenDaysConsoleLogQuery.cs
|   |       |   `-- SevenDaysRestrictedConsoleGateway.cs
|   |       |-- Backups/
|   |       |   |-- SevenDaysWorldSaveGateway.cs
|   |       |   `-- SevenDaysServerShutdown.cs
|   |       `-- Runtime/SevenDaysMainThreadScheduler.cs
|   |
|   `-- Adapters/LSTY.SevenDPanel.Adapters.Local/
|       |-- LSTY.SevenDPanel.Adapters.Local.csproj
|       |-- Inbound/
|       |   |-- BackgroundWork/BackgroundWorkConsumer.cs
|       |   `-- Scheduling/
|       |       |-- BackgroundScheduler.cs
|       |       |-- AutomationScheduleTrigger.cs
|       |       `-- BackupScheduleTrigger.cs
|       `-- Outbound/
|           |-- Runtime/
|           |   |-- SystemClock.cs
|           |   `-- BoundedBackgroundWorkQueue.cs
|           |-- Identity/
|           |   |-- SqliteIdentityProvisioningStore.cs
|           |   |-- SqliteIdentityStore.cs
|           |   |-- SqliteSessionStore.cs
|           |   |-- SqliteSetupStateStore.cs
|           |   |-- Pbkdf2PasswordHasher.cs
|           |   `-- CryptoTokenGenerator.cs
|           |-- Audit/SqliteAuditTrail.cs
|           |-- Automation/SqliteAutomationStore.cs
|           |-- Backups/
|           |   |-- SqliteBackupCatalog.cs
|           |   |-- FileSystemBackupArchiveStore.cs
|           |   `-- JsonPendingRestoreStore.cs
|           `-- Persistence/
|               |-- SqliteConnectionFactory.cs
|               |-- SqliteWriteCoordinator.cs
|               |-- DatabaseMigrator.cs
|               |-- SqliteJobStore.cs
|               |-- AtomicFileWriter.cs
|               `-- Migrations/
|                   |-- Migration001Initial.cs
|                   `-- Migration002Operations.cs
```

## 架构复审触发条件

不得因为命名偏好或模板对齐而改变项目边界。只有证据表明出现以下情况之一时，才重新评估本蓝图：

- 通过修正职责仍无法消除项目引用环；
- 某个 adapter 需要不同的目标框架、平台资源集或部署单元；
- 某项能力形成独立的数据所有权和一致性边界；
- 已有架构测试但内部依赖违规仍频繁发生；
- 真实纵向切片在现有项目中没有清晰且唯一的所有者。

单独增加文件、接口或目录不构成架构变更。

## 尚需验证的证据缺口

目标设计仍需要以下真实实现和进程证据：

- 用于在线玩家快照和类型化玩家动作的稳定 `v3.0.1-b4` API；
- `ThreadManager.AddSingleTaskMainThread` 的取消、异常和关服行为；
- 控制台日志真实容量饱和与 Linux Unity Mono 行为；Windows `v3.0.1-b4` 的 `Log.LogCallbacksExtended`、`System.Threading.Channels`、正常负载和关服排空已有当前证据；
- SQLite 身份下不透明 Bearer Token 的持久化、到期、撤销、刷新和浏览器恢复策略；产品不采用 Cookie 认证；
- 世界保存完成信号和归档一致性窗口；
- 在目标世界文件打开前执行待恢复操作；
- Linux x64 上的 SQLite 原生资源和完整依赖矩阵。

这些缺口归入[系统架构](../architecture.md)和[测试策略](../test.md)中的当前风险与验证记录；
本蓝图不会把它们标记为已验证。
