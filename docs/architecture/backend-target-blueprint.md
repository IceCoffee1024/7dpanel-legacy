---
state: Draft
last_updated: "2026-07-19"
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
- Hosting 负责进程生命周期协调，但不包含业务规则。
- 输入端口接口是可选的；当接口不提供替换价值时，用例类本身可以作为稳定的输入边界。
- Aggregate Roots、通用 repositories、通用 Event Bus、Mediator 和可编程规则引擎都不是默认选择。
- 新项目、目录和抽象必须对应真实的职责、依赖、部署、不变量或复用边界。

### 分层

| 层 | 所有职责 | 可以依赖 | 不得依赖 |
|---|---|---|---|
| Bootstrap / Composition | Mod 入口、配置 I/O、对象图、部署内容 | 所有具体构造信息 | 业务规则 |
| Hosting | 长生命周期组件的启动、排空、停止和释放 | BCL 与 Hosting 契约 | Application、Domain、具体 adapters |
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

[系统架构中的依赖兼容矩阵](../architecture.md#依赖兼容矩阵)是已经选定的后端运行基线及精确版本的权威来源。
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
| 本地 SQLite 与原生运行时 | Local Adapter / Persistence | `Microsoft.Data.Sqlite`、`SQLitePCLRaw.lib.e_sqlite3` | 已批准 | 首个身份、审计或作业持久化切片 | Windows/Linux x64 原生资产、WAL、并发写入、发布边界和正常关服 |
| 数据库迁移 | Local Adapter / Persistence | `dbup-core`、`dbup-sqlite` | 已批准 | 首个可演进 SQLite schema | 嵌入脚本顺序、事务失败、重复运行、升级和恢复路径 |
| 有界后台队列 | Local Adapter / Runtime | `System.Threading.Channels` | 已批准 | 首个后台 consumer 或持久作业 | 容量、背压、公平性、异常传播、排空和 Mono 兼容性 |
| 组合根依赖注入 | Bootstrap / Composition | `Microsoft.Extensions.DependencyInjection` | 已批准 | 手工对象图无法清晰维护首个完整纵向切片 | 生命周期、释放顺序、反射或动态代码需求、发布体积和关服行为 |
| 运行日志 | Local Adapter / Logging | `NLog` | 已批准 | 首个结构化日志与滚动文件需求 | 异步目标排空、文件占用、容量上限、异常隔离，并确认其不能替代审计 |
| 密码摘要 | Local Adapter / Identity | BCL PBKDF2-HMAC-SHA256 | 已批准 | 首个 Owner 身份切片 | 游戏 Mono 支持的 API、参数版本化、随机盐、耗时上限和升级策略 |
| 备份压缩与校验 | Local Adapter / Backups | 优先使用 BCL；第三方库待证据驱动选择 | 预留 | BCL 无法满足流式处理、格式、性能或恢复兼容要求 | 内存峰值、大文件、损坏检测、路径穿越、许可证、维护状态和跨平台行为 |
| 定时任务 | Local Adapter / Scheduling | 内部 hosted scheduler，不引入通用调度框架 | 默认不采用 | 出现持久日历、时区、错过触发补偿或分布式调度等真实需求 | 与持久作业状态的职责边界、关服排空、恢复语义和依赖成本 |
| 用例分派与对象映射 | Application / Adapters | 显式 dispatcher 和手工映射，不引入 Mediator 或自动映射库 | 默认不采用 | 重复代码已形成稳定模式，且显式实现的维护成本有测试证据 | 隐式控制流、反射、调试成本、性能、AOT/Mono 限制和边界泄漏 |

当某个纵向切片准备实现时，必须重新检查候选库的维护状态、许可证、安全公告、传递依赖、原生资产、
发布体积以及目标 `net48`/Unity Mono 兼容性。若当时存在更合适且证据更充分的库，实施者应先说明替代方案及权衡；
涉及架构方向变化时先更新本蓝图或对应变更设计，再修改项目清单。实现和真实进程验证完成后，只把持久结论提升到
[系统架构](../architecture.md)，不把候选状态继续当作当前事实。

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
  -> ModHost.Start
       -> start panel-owned infrastructure
       -> start OWIN
       -> health reports panel HTTP liveness

GameStartDone
  -> Lifecycle Adapter
  -> mark the game runtime ready
  -> start only components that require live 7DTD state

WorldShuttingDown / GameShutdown
  -> Lifecycle Adapter
  -> ModHost.Stop, idempotently
       -> reject new work
       -> stop hosted components in reverse order
       -> drain within deadlines
       -> dispose OWIN and remaining resources
```

`ModHost` 接收命令型生命周期契约。它不负责构造连接、调度器、store、计划器或重试策略。面板 HTTP 存活与游戏运行时就绪是两个独立状态：`InitMod` 可以提供静态页面和不依赖游戏对象的 API，依赖 Unity/7DTD 活对象的组件和用例只能在 `GameStartDone` 后进入可用状态；此前对应 API 返回 `503` 和稳定错误码。

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
- 高频审计和日志写入使用有界串行协调器，并分为不可丢弃和尽力而为两条通道。
- 高风险游戏动作只有在审计意图得到持久确认后，才能进入主线程队列。
- 尽力而为的日志在过载时可以被拒绝，但必须记录丢弃数量，日志流量不得饿死审计。
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
  -> return the one-time plaintext session token in a secure cookie
```

并发初始化请求最多只有一个成功。认证 middleware 将会话映射为操作者；每个用例仍然必须执行权限检查。

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
  -> authentication and CSRF
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

控制台日志和审计搜索返回基于游标分页的读模型。Controllers 不拼接 SQL，也不暴露内部路径和异常堆栈。

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
|   |   |-- Composition/
|   |   |   `-- PanelCompositionRoot.cs          # 唯一具体对象图
|   |   `-- Properties/PublishProfiles/
|   |       `-- FolderProfile.pubxml             # Mod 目录发布
|   |
|   |-- Runtime/LSTY.SevenDPanel.Hosting/
|   |   |-- LSTY.SevenDPanel.Hosting.csproj      # 技术中立的生命周期库
|   |   |-- ModHost.cs                           # 有序启动、排空、反向停止
|   |   |-- ModHostState.cs                      # 从 Created 到 Faulted 的状态
|   |   |-- IModRuntime.cs                       # 生命周期 adapter 命令契约
|   |   |-- IHostedComponent.cs                  # 长生命周期组件契约
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
|   |   |   |-- PanelDependencyResolver.cs       # 用例到 controllers 的解析
|   |   |   |-- ApiResultMapper.cs               # 结果到 HTTP 的映射
|   |   |   |-- Middleware/
|   |   |   |   |-- CorrelationMiddleware.cs    # 请求和审计 id
|   |   |   |   |-- SessionAuthenticationMiddleware.cs
|   |   |   |   |-- CsrfProtectionMiddleware.cs
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
- 世界保存完成信号和归档一致性窗口；
- 在目标世界文件打开前执行待恢复操作；
- Linux x64 上的 SQLite 原生资源和完整依赖矩阵。

这些缺口归入[系统架构](../architecture.md)和[测试策略](../test.md)中的当前风险与验证记录；
本蓝图不会把它们标记为已验证。
