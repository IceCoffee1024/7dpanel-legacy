# 7DPanel 核心功能模块

本文是项目功能导航清单，用于快速了解 7DPanel 的核心模块及主要代码入口，不单独定义产品需求或表示所有功能均已完成。

- 产品目标、范围和验收合同以 [PRD](PRD.md) 的 `CAP-##`、`NFR-##` 为准。
- 当前实现边界以 [架构文档](architecture.md) 为准。
- 验证状态和发布缺口以 [测试策略](test.md) 为准。

## 进度口径

本文的“代码迁移进度”用于比较旧版功能在新项目仓库中的落地程度，不是发布完成度或真实环境验收率。百分比按 5% 粒度维护：`0%` 表示未开始，`25%` 表示已有合同或入口，`50%` 表示主要存储/API 已形成，`75%` 表示后端与前端主流程可用，`90%` 表示代码主干已对齐但仍有真实环境或边界缺口，`100%` 表示适用代码和验证缺口均已闭合。当前综合代码迁移进度约为 **90%**。

## 新项目核心功能模块

| 编号 | 模块 | 主要功能点 | 产品合同 | 代码迁移进度 | 主要代码入口 |
|---|---|---|---|---:|---|
| `N-01` | 服务器接入与状态仪表盘 | Mod 启动与生命周期；游戏就绪状态；服务器概览；运行指标；实时事件流；健康检查 | `CAP-01` | 95% | `OverviewController`、`HealthController`、`ServerEventsController`；Admin `server-status` |
| `N-02` | 实时玩家管理与网页控制台 | 在线玩家列表与详情；踢出等类型化玩家动作；控制台日志；命令目录；受控控制台命令执行；操作结果查询 | `CAP-02` | 95% | `PlayersController`、`PlayerActionsController`、`ConsoleLogsController`、`ConsoleCommandsController`；Admin `players`、`console-logs`、`server-operations` |
| `N-03` | 计划、备份与恢复 | 持久作业；备份策略；手动和计划备份；备份记录；恢复流程；失败与结果未知状态 | `CAP-03` | 90% | `JobsController`、`BackupPoliciesController`、`BackupsController`、`SchedulesController`；Admin `backups`、`schedules` |
| `N-04` | 公告与基础自动化 | 即时公告；进服欢迎；周期提醒；血月提醒；自动化规则；触发、执行和去重记录 | `CAP-04` | 90% | `AnnouncementsController`、`AutomationsController`；Admin `automation` |
| `N-05` | 管理员权限与统一审计 | Owner/Admin/Viewer 面板角色；面板用户管理；API Key；7DTD 管理员和命令权限；访问名单；统一审计查询 | `CAP-05` | 95% | `PanelUsersController`、`ApiKeysController`、`GamePermissionsController`、`AccessListsController`、`AuditController`；Admin `auth`、`permissions`、`api-keys`、`access-lists`、`audit` |
| `N-06` | 服务器配置与模组治理 | 官方服务器配置读取与修改；类型化字段；版本冲突；敏感字段保护；Mod 列表、状态、启停和安全目录治理 | `CAP-06` | 95% | `ServerConfigurationController`、`ModsController`；Admin `server-configuration`、`mods` |
| `N-07` | 游戏聊天管理 | 实时与历史聊天；全局和私聊发送；禁言；彩色聊天与玩家 Profile；动态命令名称/别名；无前缀解析；命令清单、热更新和审计 | `CAP-07` | 95% | `ChatController`、`SevenDaysChatMessageCoordinator`、`GameChatCommandCatalog`；Admin `game-chat` |
| `N-08` | 玩家资料、追踪与物品治理 | 玩家身份与历史资料；会话和活动；位置与地图；背包、技能和统计；游戏物品/方块/图标资源目录；类型化物品及重置动作 | `CAP-08` | 90% | `PlayerEvidenceController`、`GameResourcesController`、`MapController`、`MapJobsController`；Admin `player-profile`、`player-map`、`game-resources` |
| `N-09` | 经济、商店与奖励 | 玩家账户与余额；双重记账交易；转账和排行榜；商品与库存；购买；奖励包；兑换码；成就、在线和每日奖励；补偿与退款 | `CAP-09` | 90% | `CommerceController`、`RewardsController`、`AchievementsController`、`OnlineRewardsController`；Admin `economy`、`commerce`、`rewards` |
| `N-10` | 传送与社区投票 | 私人家；城市；好友请求与传送；返回点；管理员传送；冷却、费用和血月规则；踢人/重启投票及结算 | `CAP-10` | 90% | `CommunityController`、`CommunityGameCommandRouter`；Admin `community` |
| `N-11` | 外部集成与访问策略 | Discord Webhook/Bot；游戏聊天桥；Discord Gateway 与 Interaction；Slash 命令；玩家绑定；远程执行 allow-list；GeoIP 国家/IP 策略 | `CAP-11` | 85% | `DiscordIntegrationController`、`GeoIpAccessPoliciesController`；Admin `discord`、`geoip` |
| `N-12` | 世界工具与功能模块 | 世界和地图信息；领地、车辆、无人机等世界资源；类型化世界操作；长作业；功能模块启停、依赖和状态 | `CAP-12` | 85% | `WorldController`、`WorldOperationsController`、`ModulesController`；Admin `world-tools`、`modules` |

## 旧项目核心功能模块

下表保留旧项目自身的菜单和 Feature 边界，而不是先按新项目合并。它覆盖旧后端全部 16 个稳定 `FeatureKeys`，并补充不属于 Feature 系统但实际存在的管理页面；“迁入进度”使用上文同一代码口径。

| 编号 | 旧项目模块 | 旧项目边界与主要功能 | 稳定 Feature Key | 新项目承接 | 迁入进度 |
|---|---|---|---|---|---:|
| `O-01` | `Dashboard` | 服务器状态、在线统计和基础运行指标 | - | `N-01` | 95% |
| `O-02` | `PlayerList`、`PlayerProfile` | 在线/历史玩家、玩家详情、背包、技能和处置 | - | `N-02`、`N-08` | 90% |
| `O-03` | `GPSMap` | 玩家位置、轨迹和地图展示 | - | `N-08`、`N-12` | 85% |
| `O-04` | `FeatureModules` | Feature 启停、依赖、命令和能力清单 | - | `N-12` | 85% |
| `O-05` | `GameChat`、`ColoredChat` | 实时/历史聊天、发送、设置和彩色玩家格式 | `chat`、`colored-chat` | `N-07` | 95% |
| `O-06` | `Economy` | 账户、交易、转账、排行、商店、奖励包和兑换码 | `economy` | `N-09` | 90% |
| `O-07` | `GameItems` | 物品、方块、图标和本地化目录 | - | `N-08` | 95% |
| `O-08` | `ServerConfig` | 服务器配置读取与编辑 | - | `N-06` | 95% |
| `O-09` | `Teleport` | 家、城市、好友、返回点、限制和日志 | `teleport` | `N-10` | 90% |
| `O-10` | `GameNotice` | 欢迎、轮播和血月公告 | `game-notice` | `N-04` | 90% |
| `O-11` | `Vote` | 投票踢人和投票重启 | `vote-kick`、`vote-restart` | `N-10` | 90% |
| `O-12` | `Achievement` | 成就规则、进度和奖励 | `achievement` | `N-09` | 90% |
| `O-13` | `OnlineReward` | 在线时长规则和奖励发放 | `online-reward` | `N-09` | 90% |
| `O-14` | `EventAutomation` | 加入、离开、聊天和 Cron 事件规则 | `event-automation` | `N-04` | 90% |
| `O-15` | `DiscordIntegration` | Webhook/Bot、聊天桥、Slash、绑定和命令 relay | `discord-integration` | `N-11` | 85% |
| `O-16` | `GeoIP` | 国家/IP 策略、缓存和加入决策 | `geoip-access-control` | `N-11` | 85% |
| `O-17` | `PlayerTracking` | 会话、活动、位置、背包快照和趋势 | `player-tracking` | `N-08` | 90% |
| `O-18` | `BanWhitelist` | 黑名单、白名单和加入控制 | - | `N-05` | 95% |
| `O-19` | `Permission` | 游戏管理员和命令权限 | - | `N-05` | 95% |
| `O-20` | `ModManagement` | Mod 列表、状态和启停 | - | `N-06` | 95% |
| `O-21` | `Console` | 控制台日志和命令执行 | - | `N-02` | 95% |
| `O-22` | `Restart` | 关服、重启配置和执行记录 | `restart` | `N-02`、`N-03` | 90% |
| `O-23` | `ScheduledCommand` | Cron 命令、计划任务和运行记录 | `scheduled-command` | `N-03`、`N-04` | 90% |
| `O-24` | `Backup` | 手动/计划备份、设置、任务和历史 | `backup` | `N-03` | 90% |
| `O-25` | `AuditLogs`、`GameEventLogs` | 管理审计和游戏事件查询 | - | `N-01`、`N-05` | 95% |
| `O-26` | `AppSettings`、`Swagger`、登录 | 应用设置、接口入口和后台身份认证 | - | `N-05`、各模块 API | 95% |

旧项目的 `restart` 由新项目服务器操作、计划和持久作业共同承接。旧项目独立 Feature 的启停、命令重注册、依赖、能力清单和安全策略，则由新项目 Application 用例、功能模块管理、动态命令目录和统一审计分别承接，不再维持一对一类结构。

## 新旧项目功能对齐矩阵

本表以新项目编号为主轴汇总迁移结果。百分比只表示仓库内代码进度，不等于真实 7DTD、浏览器、Discord、GeoIP、备份恢复或候选发布已经验收；详细证据仍以[测试策略](test.md)为准。

| 编号 | 新项目模块 | 对应旧项目编号 | 代码迁移进度 | 结构差异或待闭环项 |
|---|---|---|---:|---|
| `A-01` | `N-01` 服务器接入与状态仪表盘 | `O-01`、`O-25` | 95% | 新增来源、单位、观察时间、warning、统一 SSE 和 gap；真实指标及事件字段仍需真实进程验收。 |
| `A-02` | `N-02` 实时玩家管理与网页控制台 | `O-02`、`O-21`、`O-22` | 95% | 新增同次玩家观察、历史摘要、类型化玩家动作、有界命令队列和结果诚实；真实玩家动作、第三方命令与高负载仍待验收。 |
| `A-03` | `N-03` 计划、备份与恢复 | `O-22`～`O-24` | 90% | 统一为持久作业和恢复状态机；真实存档备份、损坏检测、恢复演练和中断回滚尚未闭合。 |
| `A-04` | `N-04` 公告与基础自动化 | `O-10`、`O-14`、`O-23` | 90% | 保留规则 CRUD、validate、dry-run、执行记录和调度；真实 trigger、公告和动作副作用尚未集中验收。 |
| `A-05` | `N-05` 管理员权限与统一审计 | `O-18`、`O-19`、`O-25`、`O-26` | 95% | 分离面板角色与游戏权限，增加面板用户、API Key 和统一审计；旧 Steam 登录由当前 Header-only Token 合同替代。 |
| `A-06` | `N-06` 服务器配置与模组治理 | `O-08`、`O-20` | 95% | 增加类型化字段、版本冲突、敏感字段保护、安全目录和受保护 Mod；真实原生配置动作与发布目录仍待验收。 |
| `A-07` | `N-07` 游戏聊天管理 | `O-05` | 95% | 增加禁言事务、历史 gap、动态命令名称/别名、无前缀解析和统一命令审计；真实广播、私聊和第三方聊天 Mod 顺序仍待验收。 |
| `A-08` | `N-08` 玩家资料、追踪与物品治理 | `O-02`、`O-03`、`O-07`、`O-17` | 90% | 增加版本化资源目录、背包 diff、技能快照、数据 gap 和类型化物品动作；真实字段、轨迹、图标覆盖和副作用仍待验收。 |
| `A-09` | `N-09` 经济、商店与奖励 | `O-06`、`O-12`、`O-13` | 90% | 改为双重记账、幂等 grant、补偿/退款和结果未知状态，并增加每日奖励；真实发放和补偿恢复仍待验收。 |
| `A-10` | `N-10` 传送与社区投票 | `O-09`、`O-11` | 90% | 统一费用、冷却、血月、原子版本、命令热更新和类型化投票终态；真实传送、扣费、返回点和投票副作用仍待验收。 |
| `A-11` | `N-11` 外部集成与访问策略 | `O-15`、`O-16` | 85% | Gateway、Interaction、Ed25519 和 GeoIP 策略已有代码入口；Discord sandbox 往返、限流及 MaxMind/远程 GeoIP 仍待验证。 |
| `A-12` | `N-12` 世界工具与功能模块 | `O-03`、`O-04`、`O-07` | 85% | 新增领地、车辆、无人机、类型化世界操作和持久长作业，超出旧版完整域；真实世界副作用和危险操作回滚仍待验收。 |

## 跨模块基础能力

| 能力 | 作用 |
|---|---|
| 自托管运行时 | 以 7DTD Mod 进程承载本地 Web API、Admin SPA、SQLite 和游戏适配器，不依赖产品方云服务。 |
| 身份与授权 | 面板角色、API Token/API Key 和游戏玩家稳定身份分别承担管理端与游戏内授权。 |
| SQLite 持久化 | 保存配置、审计、事件、玩家证据、作业、经济、奖励、传送、投票和集成状态。 |
| 类型化游戏适配 | 玩家、物品、传送和世界工具等结构化管理动作通过明确的 Application 用例和 SevenDays Adapter 执行，不接受通用脚本或万能动作载荷。 |
| 实时事件与状态诚实 | SSE 和持久记录暴露实时状态；失败、超时和结果未知不会被伪装为成功。 |
| 统一审计 | 聚合面板操作、聊天命令、玩家动作、经济、传送、投票和作业等专用记录，避免复制敏感正文。 |
| 双语管理界面 | Admin SPA 提供简体中文和英文界面，并按角色控制导航与操作入口。 |

## 仓库结构

| 目录 | 职责 |
|---|---|
| `backend/src/Core/LSTY.SevenDPanel.Domain` | 领域实体、值对象和纯业务不变量。 |
| `backend/src/Core/LSTY.SevenDPanel.Application` | 用例、端口、类型化命令和跨能力协调。 |
| `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays` | 7DTD 游戏事件、读取和主线程写操作适配。 |
| `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite` | SQLite Store、migration 和审计投影。 |
| `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web` | Owner/Admin/Viewer Web API、认证和 Problem Details。 |
| `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local` | 本地文件、网络和外部协议适配。 |
| `backend/src/Bootstrap/LSTY.SevenDPanel` | 依赖注入、运行时组装和 Mod 启停。 |
| `frontend/apps/admin` | Vue 3 + Nuxt UI 管理端 SPA。 |
| `backend/tests/LSTY.SevenDPanel.Tests` | 后端单元、SQLite、适配器和 HTTP 契约测试。 |
