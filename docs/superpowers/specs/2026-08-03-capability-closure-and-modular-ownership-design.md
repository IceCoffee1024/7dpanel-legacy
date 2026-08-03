---
state: Current
document_role: Change Record
last_updated: "2026-08-03"
---

# 能力收口与模块化所有权设计规格

> 本规格描述下一阶段已经批准的目标，不代表代码、真实环境验证或候选发布已经完成。产品合同以 [PRD](../../PRD.md) 为准，当前交互事实以 [产品设计](../../design.md) 为准，当前实现以 [系统架构](../../architecture.md) 为准，验证证据与发布判定以 [测试策略](../../test.md) 为准。Target 蓝图和本规格都不能替代当前实现或测试证据。

## 目标与驱动因素

当前 Domain、Application、Adapters、Hosting 与 Bootstrap 的项目级分层仍然适合进程内 7DTD Mod，不需要通过微服务或“一能力一项目”降低表面文件数。真正的问题是能力增长速度已经超过真实验证、发布证据和团队局部理解能力：

- Admin 当前有 27 个 Feature；上一阶段已经将其收敛到六个一级任务域，但 Feature、API 与状态仍然很多。
- 后端当前有八个产品项目和 577 个生产 `.cs` 文件，其中业务变化经常横跨 Application、多个 Adapter、Bootstrap、OpenAPI、Admin 与多层测试。
- 单一后端测试项目当前包含 196 个测试源文件；项目边界简单，但按能力检索和聚焦执行的成本正在上升。
- `PanelServiceProviderFactory.cs` 已达到 1385 行，单一组合根正确，但注册清单缺少能力级导航。
- 后端目标蓝图要求 Hosting 保持技术中立，当前 Hosting 项目却实际引用 Application，并由平台采样和重启脚本类使用该依赖；目标与当前实现尚未一致。
- `docs/test.md` 已记录大量自动化证据，同时仍保留真实 7DTD、浏览器、恢复、Linux、性能和外部服务缺口。代码存在、自动化通过和候选发布可用不是同一状态。

本阶段从“继续增加能力面”切换为“能力收口与发布成熟度提升”，目标是：

1. 用单一能力成熟度台账区分 `Implemented`、`Verified` 和 `Release-ready`，让完成状态可审计、可降级而且不依赖口头判断。
2. 为 Platform 与六个纵向 Capability 建立明确所有权和依赖规则，使开发者优先沿一个能力阅读和交付。
3. 一次集中关闭服务器运维、玩家管理、备份恢复三个核心旅程，以及首版 P0 发布所需的共享真实环境门禁。
4. 保持模块化单体、单一进程、单一 Bootstrap 组合根和现有项目数量，只收口已经有证据的横向职责。
5. 把复杂性预算转为可自动检查的依赖、状态、导航、持久化、生命周期、证据和文档门禁。
6. 以一个候选版本完成本阶段，但通过多个可独立验证、可回滚的纵向波次交付，不形成一个长期巨型分支或不可拆分提交。

## 已批准的设计决定

- 继续采用模块化单体，不创建微服务、插件总线、通用工作流引擎、第二套前端状态框架或仓库级共享前端包。
- 保持 Domain、Application、Hosting、四个 Adapter 和 Bootstrap 八个后端产品项目；不按 Feature 新建项目。
- 保持 Bootstrap 为唯一组合根。能力注册文件只拆分注册清单，不创建子容器、Service Locator、自启动 Feature 或隐藏全局状态。
- 将 Platform 与 Capability 先作为所有权、依赖和测试标签，而不是一次性物理迁移全部目录和命名空间。
- 在本阶段冻结净新增能力面。只允许修复核心旅程、发布门禁、恢复性、安全性和兼容性所必需的行为；新增页面或 API 需要独立批准。
- 上一阶段的六个一级任务域和规范路由继续作为用户信息架构，不重复进行第二次导航重构。
- 一个能力只能有一个主要所有者；用户导航归属、代码所有权和外部技术边界可以不同，但必须显式记录。
- 自动化不替代真实边界，历史证据不自动覆盖当前候选 artifact、当前认证方式或当前游戏版本。

## 非目标

- 不通过减少项目数量、合并层级或移动全部文件来追求表面简洁。
- 不把 577 个生产文件一次性重排为新的物理目录树，不进行全仓库命名空间改写。
- 不拆分当前后端测试 `.csproj`；只有测试反馈时间、独立依赖或团队所有权出现量化证据后再评估。
- 不重写已经稳定的 API、SQLite schema、Feature 状态机或生成客户端。
- 不把 P1 能力的外部服务和危险副作用全部提升为首版阻塞项；只有 PRD 和 `docs/test.md` 当前发布门槛要求的 P0/NFR 边界阻塞候选发布。
- 不用统一状态词汇抹掉 `Interrupted`、`ResultUnknown` 或 `RollbackFailed` 等安全关键信息。
- 不把本规格、Target 蓝图、测试替身、旧 artifact 或手工说明记录为 `Verified`。

## 模块化所有权模型

目标逻辑结构如下：

```text
7DTD Mod Process
├─ Platform
│  ├─ Hosting
│  ├─ Authentication
│  ├─ Persistence
│  └─ Game Integration
└─ Capabilities
   ├─ Operations
   ├─ Players
   ├─ Community
   ├─ Economy
   ├─ Automation
   └─ Administration
```

该结构是所有权视图，不是新增程序集。现有物理项目继续表达依赖层和外部技术边界；Capability 表达业务变化的主要阅读路径。

### Platform 所有权

| Platform 模块 | 拥有 | 不拥有 |
|---|---|---|
| Hosting | Mod 启停、资源生命周期、运行时就绪、Web Host 生命周期和技术中立状态合同 | 业务用例、业务 DTO、能力授权和具体 Adapter 实现 |
| Authentication | 身份建立、Access Token/API Key 生命周期、面板角色主体和认证安全边界 | 各 Capability 的业务授权决定和游戏原生权限映射 |
| Persistence | SQLite bootstrap、migration 执行、连接/事务约束、文件原子性与受控存储根 | 通用 Repository、跨能力表直读和业务状态机 |
| Game Integration | 游戏事件、主线程调度、不可变快照、7DTD/Unity 类型隔离和版本兼容 | HTTP DTO、浏览器状态和跨能力业务编排 |

Platform 提供受限技术能力，不成为放置无归属代码的 `Common`。Authentication、Persistence 和 Game Integration 的业务消费者仍由对应 Capability 拥有。

### Capability 所有权

| Capability | 主要业务职责 | 现有代码的主要归属示例 | Admin 主要任务域 |
|---|---|---|---|
| Operations | 状态与控制、备份/恢复、配置、Mod/模块、控制台、世界运维 | `Overview`、`ServerOperations`、`Backups`、`Jobs`、`ServerConfiguration`、`Mods`、`Modules`、`World`、`WorldOperations`、`ConsoleCommands` | 概览、服务器运维 |
| Players | 在线/历史玩家、资料、证据、地图、名单和玩家处置 | `Players`、`Maps`、`AccessLists` 及玩家动作/证据端口 | 玩家 |
| Community | 游戏聊天、公告、传送、投票、城市和玩家社交关系 | `Chat`、`Announcements`、`Community` | 社区 |
| Economy | 经济账本、奖励、商店、兑换与补偿 | `Economy`、`Rewards`、`Commerce` | 经济与奖励 |
| Automation | 计划、规则、触发、执行、恢复和能力动作编排 | `Schedules`、`Automations` 及其运行时 | 服务器运维中的“计划与自动化” |
| Administration | 面板用户、角色与游戏权限、API Key 管理、审计查询、Discord/GeoIP 管理 | 面板用户、`GamePermissions`、`Audit`、`Discord`、`GeoIp` | 系统管理 |

特殊归属规则：

- `Overview` 是 Operations 拥有的组合读模型，只通过只读查询合同汇总其他能力，不取得其他能力的写权限或内部 Store。
- 游戏资源目录由 Platform/Game Integration 采集和版本化，Players、Economy、Automation 等能力通过公开只读合同消费；它在 Admin “玩家”任务域出现不改变代码所有权。
- Discord 配置、密钥、连接健康和访问策略由 Administration 拥有；游戏聊天桥接行为由 Community 通过明确端口消费，不直接访问 Discord Store 或 transport。
- 通用作业生命周期只保留已经存在的真实消费者；具体 payload、授权、确认、恢复和结果解释由发起 Capability 拥有。
- 游戏原生权限、面板角色和 Capability 授权是三个不同概念，不建立隐式数值转换或共享万能权限模型。

## 依赖与交付规则

每次业务变化必须形成最小完整纵向闭环：

```text
产品行为合同
  -> Application 用例与授权
  -> 外部 Adapter 与持久化
  -> HTTP/OpenAPI
  -> Admin 任务流程
  -> 自动化与所需真实边界证据
```

具体规则如下：

1. Capability 之间只通过明确的 Application 公共合同或只读查询合同交互，不访问其他能力的 SQLite 表、具体 Store、内部运行时或前端内部状态。
2. Domain 只保留跨调用仍成立的业务不变量、值对象和状态转换；CRUD DTO、HTTP 模型、SQLite 行模型、运行时状态和 UI 状态不得进入 Domain。
3. 新接口必须对应外部边界、项目依赖方向、第二个生产实现/消费者、稳定重复或明确批准的近期消费者。测试替换不能单独证明接口合理。
4. 共享代码只有在至少两个语义和变化原因一致的生产消费者存在时才抽取；UI 公共组件同样遵守该规则。
5. 新后台任务必须有启动、停止、排空、失败、恢复、容量和观测语义，并由 Bootstrap 统一组装和停止。
6. 新持久状态必须有 migration、原子/幂等边界、并发策略、备份/恢复行为和启动失败语义。
7. 新页面必须属于既有用户任务和导航目录；新增 API 不自动产生新增导航入口。
8. 一个 PR 默认只改变一个 Capability 或一个 Platform 边界。跨能力 PR 必须说明不可拆分的不变量或发布原因。

## 能力成熟度台账

### 单一权威位置

`docs/test.md` 新增并唯一拥有“能力成熟度台账”，因为 `Verified` 和 `Release-ready` 是验证与发布事实。台账不复制产品要求、交互设计或实现说明，而是链接：

- `CAP-##`/`NFR-##` 产品合同；
- `docs/design.md` 的用户旅程；
- `docs/architecture.md` 的当前实现事实；
- 当前 commit、候选 artifact、环境、版本和证据包；
- 未满足门禁与失效触发条件。

Target 蓝图和 dated spec/plan 不进入“实现证据”列。`docs/architecture.md` 没有对应当前事实时，能力不能标记为 `Implemented`；证据不能关联当前候选 artifact 时，能力不能标记为 `Verified`。

### 状态定义

| 状态 | 必须同时满足 | 不足以证明 |
|---|---|---|
| `Implemented` | 生产运行时有完整纵向切片；适用的 Domain/Application/Adapter/API/Admin 自动化通过；当前事实已提升到 `docs/architecture.md` | 只有代码骨架、Target 设计、测试专用实现或未连接组合根 |
| `Verified` | `Implemented`；所有无法由替身证明的指定边界在受控环境通过；证据绑定当前 commit、artifact hash、游戏/OS/浏览器或外部服务版本 | 旧提交 smoke、不同认证版本、人工口述、仅 Katana/Happy DOM 或本地发布布局 |
| `Release-ready` | `Verified`；适用聚合门禁、P0/NFR 依赖、安全、性能、恢复、回滚、文档和候选 artifact 验证全部通过；没有未批准阻塞项 | 单个旅程成功、只在 Windows 成功或通过重跑隐藏不稳定失败 |

台账还允许 `Planned` 或 `Blocked` 作为辅助状态，但不能把它们解释为更低等级的完成。`Verified` 和 `Release-ready` 必须注明范围，例如 Windows/Linux、Chromium、Discord sandbox；不能用无范围的绿色标记隐藏未验证平台。

证据在以下情况自动失效或降级：相关产品合同、游戏版本、认证方式、依赖闭包、migration、运行时生命周期、目标平台或候选 artifact 发生变化；安全/恢复回归；对应测试变为不稳定；证据文件丢失或无法关联 commit/hash。失效后保留历史记录，但当前状态回到仍有证据支持的级别。

### 台账最小字段

| 字段 | 约束 |
|---|---|
| Capability / Journey ID | 稳定标识，一个主要所有者 |
| Contract | `CAP-##`、`NFR-##` 和必要设计链接 |
| Implementation | `docs/architecture.md` 当前事实链接 |
| Required boundaries | 自动化、Windows/Linux 7DTD、浏览器、恢复、外部服务等适用边界 |
| Evidence | commit、artifact SHA-256、环境版本、时间和证据包相对路径 |
| Current status | 受控枚举和明确验证范围 |
| Blockers / expiry | 未满足门禁和证据失效条件 |

文档审计应拒绝未知状态、缺少所有者、无合同链接、`Verified` 无真实证据、`Release-ready` 仍有阻塞项、无 artifact 身份或失效日期/触发条件缺失。

## 统一状态语言

长操作的基础状态统一为：

| 稳定语义 | 中文显示 | 使用条件 |
|---|---|---|
| `queued` | 等待中 | 已持久接受但尚未开始 |
| `running` | 运行中 | 已开始且终态未知 |
| `succeeded` | 已成功 | 权威副作用和结果验证均完成 |
| `failed` | 已失败 | 已知未完成且有稳定失败原因 |
| `cancelled` | 已取消 | 在允许取消的边界终止，且不会继续执行 |

安全扩展状态继续保留：`interrupted` 表示进程中断且可按恢复合同处理，`result-unknown` 表示副作用可能发生但无法确认，`rollback-failed` 表示主操作失败后回滚也失败，`unavailable` 表示权威状态源不可读。它们不得被映射为普通 `failed` 或 `succeeded`。

查询页面的 `loading`、`empty`、`fresh`、`stale`、`partial`、`failed`、`forbidden` 是数据读取状态，与长操作生命周期分开。各 Feature 可以拥有额外内部状态，但用户可见文字和颜色必须映射到上述稳定语义。

## 核心旅程闭环

### 旅程 J1：服务器日常运维与重启

```text
查看状态 -> 固定目标确认 -> 提交重启/关服 -> operation receipt
         -> 等待停止与启动 -> health/game-ready -> 审计与结果核对
```

完成条件：

- 用户从“服务器运维”完成查看状态、重启或关服，不需要自行串联技术模块或脚本参数。
- HTTP 接受、脚本启动、进程退出、监听恢复和游戏就绪是不同阶段；任何阶段都不能提前显示成功。
- 页面刷新、SSE 断开和重新登录后可以通过稳定 operation ID 恢复状态。
- 重复提交、关服竞态、超时、脚本失败、启动失败和结果未知均有稳定状态、审计与恢复建议。
- Windows 和 Linux 候选发布物分别完成正常启停、重复启动、当前 Swagger/认证、日志归档和失败证据保留。

### 旅程 J2：玩家发现、处置与审计

```text
在线列表 -> 玩家详情/历史/地图 -> 固定身份确认 -> 踢出
         -> 游戏断开 -> 在线投影更新 -> SQLite 审计核对
```

完成条件：

- 使用受控测试玩家和稳定跨平台身份，验证 Join/Save/Disconnect、31 字段来源、观察时间、历史写入与缺口语义。
- `Owner` 在桌面和 `390x844` 浏览器完成列表、详情、历史、地图定位、固定目标确认和踢出。
- 踢出真实调用、约定拒绝原因、断开、列表变化和审计终态一致；关闭或取消浏览器不能伪造取消已经开始的动作。
- `Admin`、`Viewer`、未认证用户的页面入口、字段裁剪和服务端授权均按合同执行。
- 队列饱和、SQLite 暂不可用、关服排空和主线程超时不会阻塞游戏线程或制造连续历史。

### 旅程 J3：备份、恢复、重启与核对

```text
创建备份 -> 校验与目录记录 -> 发起恢复/强确认 -> 持久 pending
         -> 安全关服 -> 下次启动且世界加载前恢复 -> 启动
         -> 世界哨兵/数据库/配置/审计核对 -> 成功或回滚结果
```

完成条件：

- 世界、数据库和服务器配置备份保持独立类型、校验、保留和恢复语义。
- 在持续写入与显式保存场景验证备份一致性，不能仅凭压缩成功推断世界语义一致。
- 恢复前固定 backup ID、世界/服务器目标、校验值和强确认；浏览器不能提交文件系统路径。
- 真实 7DTD 证明 pending restore 在世界文件打开前执行，并覆盖真实文件占用、磁盘失败、中途终止、再次启动续作、安全副本和回滚失败。
- Windows 与 Linux 各至少完成一次从已校验备份跨重启恢复；恢复后验证世界哨兵、7DPanel 数据库、配置、审计和 artifact 身份。
- `result-unknown`、`interrupted` 或 `rollback-failed` 必须阻止 `Release-ready`，不能手工改写为成功。

## P0 候选发布闭环

三个核心旅程重点关闭 `CAP-01` 至 `CAP-03`。候选版本还必须按现有发布门槛集中复验 `CAP-04` 至 `CAP-07` 和 `NFR-01` 至 `NFR-04`、`NFR-06`，但本阶段不扩张其功能面：

| 合同 | 集中闭环 |
|---|---|
| `CAP-04` | 公告、计划和受限自动化在真实游戏触发、执行、恢复和审计中的最小 P0 矩阵 |
| `CAP-05` | 当前认证版本的 Owner/Admin/Viewer、API Key、审计、Token 失效和真实 OWIN/Unity Mono |
| `CAP-06` | 配置与 Mod 变更的重启提示、真实生效、保护规则和 Windows/Linux 文件语义 |
| `CAP-07` | 游戏聊天、动态命令、自定义别名、第三方 Mod 顺序、私发/广播、双语桌面与窄屏真实浏览器 |
| NFR | 离线自托管、安全与状态诚实、中英一致、认证边界、六域导航连续性、性能和恢复门禁 |

`CAP-08` 至 `CAP-12` 按真实证据保持 `Implemented`、`Verified` 或 `Blocked`，不因本阶段候选发布自动升级。只有宣称旧版主要功能完全对齐时，才执行 `docs/test.md` 已规定的全部 P1 门禁。

## Hosting 与 Bootstrap 收口

### Hosting

本阶段审计当前 Hosting 的每个生产类型并分为：生命周期合同、平台能力、Application 用例实现或具体外部适配。目标是移除 Hosting 对 Application 的项目引用，使其重新符合技术中立运行时边界：

- `IModRuntime`、`IPanelWebHost`、运行时状态、就绪与启停合同留在 Hosting。
- 主机内存/存储/公网地址采样等 Application 端口实现移动到 Local Adapter 的平台区域。
- 重启脚本启动属于受控本地进程边界，移动到 Local Adapter；Application 只保留端口和业务编排。
- Bootstrap 更新注册，但不改变公开 API、配置文件或运行时生命周期顺序。
- 如果某个类型不能明确分类，先记录例外、消费者和移除条件，不为搬迁而新增接口。

### Bootstrap

`PanelServiceProviderFactory` 仍是唯一创建根 `IServiceProvider` 的入口，但按 Platform 和六个 Capability 拆分为同一 Bootstrap 项目内的显式注册清单：

```text
PanelServiceProviderFactory
  -> AddPlatform
  -> AddOperations
  -> AddPlayers
  -> AddCommunity
  -> AddEconomy
  -> AddAutomation
  -> AddAdministration
  -> validate one root provider
```

每个清单只注册类型和显式工厂，不启动线程、不访问服务、不保存静态容器。运行时启动/停止顺序继续由单一宿主编排，依赖规则和组合根测试验证所有生产消费者可解析、scope 正确、失败可回滚、停止逆序且资源最终释放。

## 测试组织与证据包

### 测试责任矩阵

| 边界 | 主要证明 | 不重复证明 |
|---|---|---|
| Domain | 不变量、值对象、纯状态转换 | HTTP、SQLite、浏览器布局 |
| Application | 用例、授权、固定目标、幂等与结果语义 | 外部库真实兼容 |
| Adapter | SQLite、文件、HTTP、7DTD 类型隔离和边界合同 | 页面视觉和完整用户旅程 |
| Browser | 路由、角色、交互、响应式、恢复 operation 状态 | 游戏副作用是否真实发生 |
| 真实 7DTD | 游戏版本字段、事件、线程、文件时机和副作用 | 每个纯逻辑组合 |
| 候选发布 | artifact、平台、启停、性能、恢复、回滚和证据完整性 | 重新实现下层全部断言 |

保持单一后端测试项目，但给新增和本阶段触及的测试增加稳定的 Capability 与 Boundary trait；提供按 `Operations`、`Players`、`Community`、`Economy`、`Automation`、`Administration`、`Platform` 和测试边界执行的聚焦入口。既有测试不为追求目录整齐一次性搬迁；未标记存量形成显式基线，新测试不得扩大基线。

### 证据包

每次真实环境运行生成不可变证据目录，至少包含：

- `manifest.json`：commit、dirty 状态、artifact SHA-256、产品/游戏/OS/浏览器版本、执行时间和环境 ID；
- 脱敏步骤结果和退出码；
- 7DTD、Mod、发布、浏览器和适用外部服务日志；
- 测试报告、失败截图或 trace；
- 数据库/migration 版本、发布程序集清单和禁止项扫描；
- 恢复演练的备份校验、世界哨兵、回滚副本和最终审计核对；
- 明确的 `passed`、`failed`、`skipped`，其中 `skipped` 不能计作通过。

证据不得包含密码、有效 Token、完整 API Key、Discord Secret、真实玩家身份、生产 IP 或生产世界数据。真实测试只使用隔离实例、临时世界、受控账号和可销毁数据。

## 可执行复杂性预算

### 自动门禁

- 依赖规则：保持 Domain/Application/Adapter/Bootstrap 方向，移除 Hosting -> Application；禁止 Adapter 直接依赖另一具体 Adapter，Bootstrap 除外。
- 组合根：只有 Bootstrap 创建根 Provider；注册清单不启动后台线程，所有 runtime 都有启动、停止、排空和失败测试。
- Capability：新跨能力引用必须经过批准的公共合同；现有例外形成固定基线并只能减少。
- 持久化：新增 migration 需要从空库和上一个支持版本执行、重复运行、失败回滚和恢复测试。
- 导航：每个新页面只有一个任务域归属，角色裁剪与路由守卫共享合同，一级任务域继续不超过六个。
- 共享代码：新增公共组件或 helper 至少有两个语义相同的生产消费者，并在评审中列出。
- 成熟度：`Verified`/`Release-ready` 的台账行必须通过证据字段、链接、状态枚举和 blocker 审计。
- 文档：Current、Target、Change Record 与 Evidence 链接可解析，禁止用 Target 或 plan checkbox 证明实现完成。

### 评审预算

以下指标按波次记录基线和趋势，不设未经测量的武断绝对阈值：

- 一个普通变更触及的 Capability、项目和文件数量；
- 从需求到目标测试通过、到真实验证、到 `Release-ready` 的周期；
- 聚焦测试、后端聚合、Admin 聚合和候选门禁的耗时与不稳定率；
- 跨能力回归数量和未声明依赖数量；
- 用户完成核心旅程的页面跳转、概念切换和失败恢复步骤；
- Bootstrap 注册清单大小、Application/Hosting/Adapter 职责例外数量；
- `Implemented` 但长期未 `Verified` 的能力数量与最长停留时间。

本阶段结束时必须给每项指标留下可重复采集方法。只有基线证明测试项目、运行时或团队所有权已经成为独立瓶颈，才批准进一步物理拆分。

## 实施波次与依赖

本阶段是一个稳定化项目、一个候选版本和多个可回滚波次：

| Wave | 目标 | 主要输出 | 依赖 |
|---|---|---|---|
| 0. 基线与冻结 | 固定范围、状态和证据格式 | 能力台账、所有权矩阵、当前 blocker、复杂性基线、净新增能力冻结 | 无 |
| 1. 守护规则 | 让新复杂性不能继续无声增长 | 依赖/导航/成熟度文档门禁、测试 trait 与聚焦入口、证据 manifest 校验 | Wave 0 |
| 2A. 服务器运维 | 完成 J1 | Windows/Linux 启停、重启、operation 恢复、当前认证/Swagger、审计证据 | Wave 1 |
| 2B. 玩家管理 | 完成 J2 | 真实玩家字段、历史、浏览器、踢出和审计闭环 | Wave 1，可与 2A 并行但使用独立实例 |
| 2C. P0 共享验证 | 收口 `CAP-04` 至 `CAP-07` | 自动化、认证、配置/Mod、聊天和真实浏览器/游戏最小矩阵 | Wave 1，可与 2A/2B 并行 |
| 3. 备份恢复 | 完成 J3 | 一致性、文件时机、故障注入、Windows/Linux 跨重启恢复与回滚证据 | Wave 2A 的可靠启停与证据链 |
| 4. 结构收口 | 降低横向阅读成本 | Hosting 去 Application 依赖、Bootstrap 注册清单按能力拆分、局部测试检索优化 | Wave 1；在核心合同稳定后合并 |
| 5. 候选发布 | 证明首版发布门槛 | P0/NFR 聚合、性能阈值、Windows/Linux、Chromium、恢复、回滚、文档审计和候选 artifact | Waves 2 至 4 |

Wave 2A、2B、2C 只能在独立测试实例和独立证据目录上并行；不得并发操作同一个 7DTD 进程、世界、端口或 SQLite 数据库。Wave 3 的破坏性恢复演练只使用可销毁世界，并在执行前确认备份、回滚目标和停止条件。

## 发布、停止与回滚

- 每个波次形成小而完整的提交和明确证据，不等待最后一次性提交全部代码。
- API、migration 或持久状态变化必须向后兼容当前回滚窗口；无法兼容时在实施前单独批准升级/回滚策略。
- 候选 artifact 一经进入 Wave 5 即冻结；任何代码或依赖变化都会产生新 hash，并使受影响真实证据失效。
- 发现数据损坏、身份绕过、不可恢复世界状态、未脱敏凭据、主线程预算超标或 `result-unknown` 无法收敛时立即停止候选发布。
- 恢复演练、危险玩家动作和真实聊天只在隔离环境执行；没有受控目标时标记 Blocked，不用生产服务器补证。
- 回滚恢复上一份已验证 artifact、匹配的数据库/配置备份和 Admin 静态资源；回滚后重新执行健康、认证、游戏就绪和数据版本检查。

## 文档影响

- `docs/PRD.md`：本阶段不改变产品目标、范围或能力合同，设计批准时无需修改；实施发现需要改变外部行为时，必须先更新 PRD。
- `docs/design.md`：实现核心旅程、统一状态文字或恢复流程后，提升已完成的当前交互事实。
- `docs/architecture.md`：实现后提升 Capability 所有权、Hosting/Bootstrap 当前边界和依赖门禁，不提前声明。
- `docs/architecture/backend-target-blueprint.md`：批准后补充逻辑 Capability 所有权和 Hosting 收口目标；它仍不是当前实现证据。
- `docs/architecture/admin-frontend-target-blueprint.md`：只有 Feature 公共边界或状态所有权目标变化时更新；六域导航已经是当前事实，不重复记录。
- `docs/test.md`：唯一拥有能力成熟度台账、证据范围、真实环境结果、复杂性测量和发布判定。
- `README.md` 与最近 owning README：只有聚合或局部可运行命令变化时更新。
- `CHANGELOG.md`：只在候选版本实际发布后记录用户或运维可见变化。

## 完成定义

本阶段只有同时满足以下条件才算完成：

1. 所有 `CAP-01` 至 `CAP-12` 都有唯一所有者、当前成熟度、证据范围和 blocker；没有“代码存在即完成”的模糊状态。
2. `CAP-01` 至 `CAP-07`、`NFR-01` 至 `NFR-04` 和 `NFR-06` 满足 `docs/test.md` 的候选发布门槛。
3. J1、J2、J3 在当前候选 artifact 上完成规定的 Windows/Linux、真实 7DTD、Chromium、恢复和审计闭环。
4. 性能阈值来自官方 Windows/Linux 进程的空载与典型负载基线，并写回架构/测试权威文档。
5. Hosting 不再引用 Application；Bootstrap 仍只有一个根 Provider，注册清单可按 Platform/Capability 定位，启动和停止语义未改变。
6. 新依赖、跨能力访问、后台任务、持久化、导航、公共组件和成熟度声明都有可执行门禁；存量例外不增加。
7. 后端和 Admin 聚合门禁稳定通过，真实证据绑定冻结的 commit 和 artifact SHA-256，不通过重跑隐藏失败。
8. Current、Target、Change Record 和 Evidence 的文档角色没有混淆，文档审计无未处理 ERROR。
9. 回滚演练成功，上一份已验证 artifact、匹配数据备份和恢复步骤可用。
10. `CAP-08` 至 `CAP-12` 未闭环的真实边界保持诚实 blocker，不因本阶段发布被错误升级。

本规格批准后再创建唯一对应的实施计划；计划必须把每个 Wave 拆为可验证任务、明确并行边界、真实环境前置条件、停止条件和文档提升时机。
