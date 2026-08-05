---
state: Current
document_role: Change Record
last_updated: "2026-08-04"
---

# 持续复杂度收敛设计规格

> 本规格描述一次大规模但可分波次回滚的内部复杂度治理，不代表目标代码、导航或门禁已经实现。产品合同以 [PRD](../../PRD.md) 为准，当前用户体验以 [产品设计](../../design.md) 为准，当前实现以 [系统架构](../../architecture.md) 为准，验证与发布事实以 [测试策略](../../test.md) 为准。本规格细化[能力收口与模块化所有权设计规格](2026-08-03-capability-closure-and-modular-ownership-design.md)中的复杂度治理，不重复定义产品能力或发布成熟度。

## 目标与驱动因素

当前模块化单体的八个后端产品项目、单一 Bootstrap 组合根、六个 Admin 一级任务域和 Feature 所有权方向仍然成立，不需要通过微服务、按能力新增程序集或全仓库目录迁移追求表面简洁。需要治理的是既有边界内部持续增长的阅读成本、修改半径和验证成本。

2026-08-04 的只读基线为：

| 指标 | 当前值 | 风险解释 |
|---|---:|---|
| 后端产品项目 | 8 | 项目级边界稳定，不应继续增加 |
| 后端生产 `.cs` 文件 | 583 | 文件数量只作趋势，不单独证明复杂 |
| 后端测试 `.cs` 文件 | 197 | 单项目保持简单，但聚焦检索成本提高 |
| `PanelServiceProviderFactory.cs` | 1433 行 | 唯一组合根正确，注册清单和构造细节过度集中 |
| 后端超过 1000 行的生产文件 | 10 | Store、OpenAPI、组合根和模型热点难以局部理解 |
| 后端超过 800 行的生产文件 | 21 | 高修改冲突和高评审负担区域 |
| 后端超过 500 行的生产文件 | 49 | 需要趋势控制，不能机械拆文件 |
| Admin Feature | 27 | 业务能力已经充足，继续扩面会放大维护成本 |
| Admin 源文件 | 470 | 包含 Vue、TypeScript 和测试，需按职责判断 |
| Admin 超过 600 行的手写源文件 | 3 | API 与复杂 composable 热点 |
| Admin 超过 400 行的手写源文件 | 22 | 需要按合同或状态所有权拆分 |
| Admin 页面 | 39 | 页面存在合理，但固定导航入口过密 |
| 一级任务域 | 6 | 已收敛并保持不变 |
| 固定二级导航入口 | 32 | Operations 9 项、Economy 8 项，扫描成本偏高 |
| `docs/` Markdown | 81 个、约 26506 行 | Current 与 Change Record 角色清楚，但入口和活动状态不够集中 |

现有 `tests/complexity/Test-ComplexityBudget.ps1` 已限制项目数、根 Provider、一级任务域、未知 Capability 和未说明的新公共接口，但 `bootstrapRegistrationLineCount` 只要求大于零；生产文件数和 Feature 数也只测量、不收敛。这允许复杂度从新增项目转移到既有大文件、既有 Feature 和既有文档入口。

本变更落实 `NFR-02`、`NFR-05`、`NFR-06` 以及现有架构的模块化所有权，目标是：

1. 保持八项目模块化单体、唯一 Provider、现有 API、schema、状态和副作用语义不变。
2. 把组合根、持久化、Web/OpenAPI 和 Admin 热点拆为可沿一个能力阅读的内部模块。
3. 用依赖、写集、文件热点和导航密度的可重复预算阻止复杂度转移。
4. 让日常用户只扫描稳定任务入口，把低频设置和同域子能力放入局部页签或上下文入口。
5. 让新成员从四份 Current 文档和一个活动变更索引进入，不要求先阅读全部历史 spec/plan。
6. 通过 16 个职责明确、写集互斥的子代理分波次执行，同时保留主协调者的集成和停止权。

## 与现有规格和计划的关系

- [能力收口与模块化所有权设计规格](2026-08-03-capability-closure-and-modular-ownership-design.md)继续拥有 Platform/Capability 模型、成熟度、核心旅程和候选发布目标。
- 本规格只拥有持续复杂度收敛目标，包括组合根拆分、大文件热点、依赖门禁、Admin 固定入口密度和文档入口。
- 该规格获批并创建实施计划后，现有[能力收口与模块化所有权计划](../plans/2026-08-03-capability-closure-and-modular-ownership.md)中的 Task 12 和 Task 13 不再作为同一文件范围的并行执行入口；旧计划必须链接新的唯一实施计划并标明移交，避免双重所有权。
- 当前 `docs/architecture.md`、`docs/design.md` 和 `docs/test.md` 在目标实现和证据完成前保持不变。实现完成后只把验证过的持久事实提升到对应 Current 文档。
- 本规格获批前不创建实施计划；获批后的计划必须只链接本规格作为唯一 primary design，并可把现有规格列为相关背景。

## 已批准边界的延续

- 保持 Domain、Application、Hosting、四个 Adapter 和 Bootstrap 八个产品项目。
- 保持一个 `BuildServiceProvider` 和一个根 `ServiceProvider`；不创建子容器、Service Locator、运行时插件扫描或自启动 Feature。
- 不以测试替换为理由新增接口，不为未来能力创建注册表、生命周期框架或通用 Repository。
- 不改变现有 `/api/v1`、OpenAPI schema、SQLite migration 历史、认证授权和危险操作状态语义。
- 不批量移动 583 个生产文件，不进行全仓库命名空间或 Feature 目录改写。
- 不把文件行数作为单一质量结论；拆分必须对应能力、事务、状态、外部边界或变化原因。
- 不删除历史 spec/plan。文档治理只收敛入口、状态和重复事实，让 Git 和 Change Record 保留历史。
- 不新增一级导航任务域，不删除规范路由，不用隐藏入口替代服务端授权。

## 目标结构

### 唯一组合根与能力注册

`PanelServiceProviderFactory` 只保留以下职责：

1. 校验启动输入并创建单一 composition context。
2. 按确定顺序调用 Platform 与 Capability 的内部注册模块。
3. 调用一次 `BuildServiceProvider`，执行现有 Validate，并返回原 `ServiceProviderRuntime`。

目标注册结构位于同一 Bootstrap 项目，不新增公共接口：

```text
DependencyInjection/
  PanelServiceProviderFactory.cs
  PanelCompositionContext.cs
  RegisterPlatformServices.cs
  RegisterOperationsServices.cs
  RegisterPlayersServices.cs
  RegisterCommunityServices.cs
  RegisterEconomyServices.cs
  RegisterAutomationServices.cs
  RegisterAdministrationServices.cs
```

注册模块可以按现有命名进一步拆成私有 partial 文件，但每个服务只在一个能力注册模块中拥有主要注册位置。跨能力只读合同由消费方引用 Application 合同，不通过注册模块互相调用或取得另一个能力的具体 Store。

注册模块不得：

- 调用 `BuildServiceProvider`、创建第二容器或缓存静态 Provider。
- 通过 `GetService` 提前实例化并启动 runtime。
- 启动线程、`Task.Run`、Timer、网络连接或文件 watcher。
- 读取浏览器、HTTP request scope 或 7DTD 活对象。
- 隐藏启动、停止、排空、失败和恢复顺序。

### 持久化与事务热点

优先治理 `SqliteCommerceStore.cs`、`SqliteCommunityStore.cs`、`SqliteDiscordIntegrationStore.cs`、`SqliteAutomationStore.cs`、`SqliteAuthenticationStore.cs`、`SqliteEconomyLedgerStore.cs`、`SqliteRewardStore.cs` 和玩家证据/动作 Store。

拆分顺序为：

1. 先用 characterization 测试固定公开行为、事务、并发、幂等、恢复和失败语义。
2. 按独立聚合、读写合同或稳定事务边界拆分；共享事务只能由一个内部 transaction coordinator 拥有。
3. SQL、row mapping、业务状态转换和恢复合并逻辑分别拥有明确位置，不能仅把同一巨型类变成多个无边界 partial 文件。
4. Dapper、SQLite connection 和表名不进入 Application 或 Domain。
5. 不在本变更中修改 migration 或 schema。若拆分证明 schema 必须改变，停止该热点并创建独立设计。

大型 models 文件只按同一变化原因拆分。为减少行数而创建一类型一文件不属于完成条件。

### Web 与 OpenAPI 热点

`PanelOpenApiOperationProcessor.cs`、大型 Controller 和 HTTP model 文件按稳定 API 资源与策略拆分，保持现有 route、operation ID、Problem Details、权限和生成 snapshot 不变。

- OpenAPI processor 保留一个入口，但把认证、错误响应、operation metadata 和 schema 规则拆为内部纯规则。
- Controller 只负责 HTTP mapping、授权入口和用例调用，不吸收业务状态机。
- HTTP model 与 Application model 的映射保持显式；不通过动态反射或通用 object mapper 降低表面代码量。
- 最终 `api:check` 必须证明 OpenAPI snapshot 和生成客户端无语义漂移。

### Admin Feature 热点

优先治理 `features/community/api/community.ts`、`features/world-tools/api/worldTools.ts`、玩家地图大型 composable、`server-status/model/overview.ts` 和超过预算的 View。

- API 文件按后端资源合同拆分，例如 query、command、parser 和 transport；禁止按任意行数切片。
- composable 按源状态所有权、派生投影和副作用生命周期拆分；一个页面仍只有一个明确 composition surface。
- Feature 不能导入另一个 Feature 的内部 `api`、`model` 或 `ui`。允许的跨能力消费必须经过对方 `index.ts` 明确公开的稳定只读合同，并进入短小 allowlist。
- `shared` 只接受至少两个语义和变化原因一致的生产消费者，不接收单个 Feature 为减少目录层级而上移的代码。
- 严格运行时 parser、Pinia Colada cache ownership、SSE 单一连接、URL 状态和危险确认语义保持不变。

### 用户固定入口收敛

六个一级任务域和全部规范路由保持不变。固定二级入口按任务而不是实现模块收敛：

| 任务域 | 固定入口目标 | 局部页签或上下文入口 |
|---|---|---|
| 概览 | 综合概览 | 异常和对象上下文入口 |
| 服务器运维 | 服务器、备份、计划与自动化、配置、扩展、世界、控制台 | Schedules/Rules；Mods/Modules |
| 玩家 | 在线玩家、记录、地图、访问名单、资源 | Profile、历史详情和玩家动作 |
| 社区 | 游戏聊天、传送、投票、城市 | 聊天历史、禁言、设置、外观 |
| 经济与奖励 | 账户、交易、奖励、商业 | 奖励包/每日/补偿/成就；商店/兑换码 |
| 系统管理 | 访问控制、API Keys、集成、审计 | Discord/GeoIP；统一审计/游戏事件 |

每个一级任务域最多显示七个固定二级入口。现有子路由继续可刷新、可分享、可搜索和可通过面包屑到达；收敛只改变固定扫描列表，不合并 Feature 状态，不创建空白任务域落地页。角色裁剪继续由 route meta 和服务端授权决定。

该交互调整必须更新 `docs/design.md` 并运行导航、键盘、双语、桌面和 `390x844` 浏览器验证；在实现前仍以当前设计文档为事实。

### 文档入口与活动状态

新增 `docs/superpowers/README.md` 作为 Change Record 索引，只维护：

- 当前活动 spec 与唯一 primary plan。
- 已完成、被取代或保留证据的记录状态和链接。
- Current、Target、Change Record 与 Evidence 的阅读顺序。

根 `README.md` 只保留四份 Current 权威文档、Target 蓝图、活动 Change Record 索引和拥有可运行命令的最近 README；不逐项列举历史 spec/plan。历史记录不删除，且不能从 Change Record 反向成为当前实现证据。

## 持续复杂度预算

预算分为不可回归约束、收敛棘轮和观察指标。

### 不可回归约束

- 后端产品项目保持 8，后端测试项目保持 1，根 Provider 保持 1。
- 项目依赖图和 Capability 依赖图无环，Hosting 不依赖 Application。
- 未声明的跨 Capability Store、SQLite 表或前端内部 Feature 导入为 0。
- 新公共接口必须在评审记录中提供生产原因；未说明新增为 0。
- 一级任务域保持 6，每个任务域固定二级入口不超过 7。
- 注册模块不得创建容器、启动后台工作或提前解析 runtime。

### 收敛棘轮

| 指标 | 基线 | 本阶段目标 | 后续规则 |
|---|---:|---:|---|
| `PanelServiceProviderFactory.cs` 行数 | 1433 | 不超过 250 | 不得增长；新增注册进入所属模块 |
| 后端超过 1000 行文件 | 10 | 0，或每个保留项有显式事务/生成例外 | 例外不得新增 |
| 后端超过 800 行文件 | 21 | 不超过 8 | 只减不增 |
| 后端超过 500 行文件 | 49 | 至少下降 30% | 作为趋势，不机械阻塞局部修复 |
| Admin 超过 600 行手写文件 | 3 | 0 | 不得新增 |
| Admin 超过 400 行手写文件 | 22 | 不超过 12 | 只减不增 |
| 固定二级导航入口 | 32 | 不超过 25 | 新入口必须替代或归入现有任务 |

行数统计排除 generated、migration、decompiled reference、snapshot 和机器产物。超过阈值不是自动拆分命令；保留例外必须记录单一责任、事务原因、风险和下一次复审条件。

### 观察指标

- 每个变更触及的 Capability、项目和持久化边界数量。
- 生产代码与测试代码变化比、聚焦测试时长和聚合门禁时长。
- 公共接口总量、跨 Feature allowlist、组合根注册项和后台 runtime 数量。
- Current 文档入口数量、活动 spec/plan 数量和断链数量。
- 文件热点、循环依赖、扇入/扇出和重复稳定合同。

观察指标先建立基线和趋势，不能在没有历史分布时设置伪精确硬阈值。

## 16 个子代理执行模型

实施计划必须使用 16 个子代理，并以如下职责作为稳定上限。代理可以跨波次复用上下文，但同一时刻只能有一个代理拥有中央集成文件。

| 代理 | 主要职责 | 独占写集 |
|---:|---|---|
| 1 | 复杂度测量器与趋势输出 | `tests/complexity/Measure-Complexity.ps1` 及其专用夹具 |
| 2 | 预算、依赖和跨 Feature 门禁 | `tests/complexity/Test-ComplexityBudget.ps1`、新增依赖检查及专用夹具 |
| 3 | Bootstrap 根组合与集成 | `PanelServiceProviderFactory.cs`、`PanelCompositionContext.cs` |
| 4 | Platform 注册模块 | `RegisterPlatformServices.cs` |
| 5 | Operations 注册模块 | `RegisterOperationsServices.cs` |
| 6 | Players 注册模块 | `RegisterPlayersServices.cs` |
| 7 | Community 注册模块 | `RegisterCommunityServices.cs` |
| 8 | Economy 注册模块 | `RegisterEconomyServices.cs` |
| 9 | Automation 与 Administration 注册模块 | 对应两个注册文件；不修改根组合文件 |
| 10 | Commerce/Economy 持久化热点 | Commerce 与 Economy Store，保持 migration 不变 |
| 11 | Community/Rewards 持久化热点 | Community 与 Rewards Store，保持 migration 不变 |
| 12 | Web/OpenAPI 热点 | OpenAPI processor、选定 Controller/HTTP model 及其测试 |
| 13 | Community/World Tools 前端热点 | 两个 Feature 的 API/model 及聚焦测试 |
| 14 | Player Map/Server Status 前端热点 | 两个 Feature 的 model/ui 及聚焦测试 |
| 15 | 导航与文档入口 | navigation、局部 tabs、`docs/superpowers/README.md` 和实现后的 Current 文档 |
| 16 | 独立验证与冲突审计 | 默认只读；验证命令、diff、依赖、行为和证据报告 |

执行屏障：

1. **Wave 0：基线与 characterization。** 代理 1、2、16 先固定指标、失败夹具和现有行为；没有可信 RED/GREEN 或 characterization 的热点不得拆分。
2. **Wave 1：组合根。** 代理 4 至 9 创建互斥注册模块；代理 3 在这些文件稳定后独占修改根组合文件。不得六个代理同时编辑 `PanelServiceProviderFactory.cs`。
3. **Wave 2：后端热点。** 代理 10、11、12 并行处理不同 Adapter；每个热点独立通过聚焦测试后才进入集成。
4. **Wave 3：前端与用户入口。** 代理 13、14、15 在互斥 Feature 和 navigation 写集工作；导航变更等待 Feature API 拆分稳定后执行浏览器矩阵。
5. **Wave 4：聚合与文档提升。** 代理 16 独立复验；主协调者解决集成差异。代理 15 只根据已验证事实更新 Current 文档，不复制计划状态。

子代理发现写集重叠、未声明用户行为变化、schema/OpenAPI 漂移或不确定事务边界时必须停止并上报，不能自行扩大范围。主协调者不得把 16 个代理的独立通过声明直接当作聚合通过证据。

## 验证策略

### 每个热点的最小证据

- 拆分前 characterization 测试通过，并能在有意改变关键语义时失败。
- 拆分后公开类型、API route、operation ID、schema、权限、事务和错误码保持一致。
- 新内部模块有生产调用方；没有测试专用接口或未连接文件。
- 聚焦测试、编译、静态检查和 `git diff --check` 通过。
- diff 只包含批准写集，没有格式化或生成文件噪声。

### 后端聚合

```powershell
dotnet build backend/7DPanel.sln --configuration Release --no-restore
dotnet test backend/7DPanel.sln --configuration Release --no-build --no-restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/Test-ComplexityBudget.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/Test-BackendTestTaxonomy.ps1
```

只有变更跨发布布局、真实 7DTD、文件恢复或外部服务边界时才运行对应高成本门禁。纯内部拆分不以 publish 或真实游戏 smoke 代替聚焦和聚合测试。

### Admin 聚合

在 `frontend/apps/admin` 运行现有 lint、typecheck、全量 Vitest、生产构建和 `api:check`。固定入口调整还必须运行 Playwright mock 的桌面与 `390x844` 导航矩阵；具备受控 OWIN 环境时验证认证深链接、静态 fallback、刷新和角色裁剪。

### 文档审计

- 所有本地链接存在且解析到仓库内文件。
- Draft spec 和 plan 不进入当前实现或验证证据列。
- `docs/` 保持简体中文，代码标识、路径和命令保持原样。
- Current、Target、Change Record 和 Evidence 没有混合所有权。
- 没有未决占位、模板标记、机器路径、凭据或生成秘密。

## 停止与回滚条件

出现以下任一情况立即停止当前热点，不把失败扩散到其他波次：

- 必须修改 API route、OpenAPI schema、SQLite schema 或用户可见状态语义才能继续。
- 无法用现有测试固定事务、恢复、并发或危险副作用语义。
- 新模块需要第二 Provider、Service Locator、静态容器或后台自启动。
- 跨 Capability 访问只能通过具体 Store、SQLite 表或前端内部路径完成。
- 聚焦测试通过但聚合测试、依赖门禁或 API drift 失败。
- 导航收敛导致规范路由、角色访问、查询/hash、浏览器历史或移动端可达性回归。
- 代理写集冲突、来源不明的用户修改或无法解释的大范围生成 diff。

各波次必须保持可单独回滚：

- 组合根拆分不改变配置、数据库或外部合同，失败时恢复上一组合文件和对应注册模块集合。
- Store 拆分不修改 migration，失败时恢复原 Store 实现；不能只回滚一半事务参与者。
- 前端 API/composable 拆分不改变 OpenAPI snapshot，失败时恢复原 Feature 文件。
- 导航只改变固定入口投影，规范路由保持不变；失败时恢复上一导航目录和静态 Admin 产物。
- 文档索引可以独立回滚，不删除任何历史记录。

## 完成定义

- 八个产品项目、唯一 Provider、现有依赖方向、API、schema 和外部行为保持不变。
- 组合根只负责 context、注册顺序、一次 Provider 构建、验证和 runtime 返回，并满足行数棘轮。
- 选定后端与前端热点满足预算或有经过审查的明确例外；例外总量不增加。
- 跨 Capability Store/表/内部 Feature 访问为零，允许项有稳定公共合同和生产原因。
- 六个一级任务域保持不变，每域固定二级入口不超过七个，所有规范子路由仍可达。
- `docs/superpowers/README.md` 提供唯一活动变更入口，根 README 不再逐项列举历史 Change Record。
- 16 个代理的写集、聚焦证据和集成顺序可审计；最终聚合验证由独立代理和主协调者复核。
- 已验证事实分别提升到 `docs/design.md`、`docs/architecture.md` 和 `docs/test.md`；未验证目标仍只留在 spec/plan。
- 未执行、blocked、skipped 和真实环境缺口如实保留，不因复杂度指标改善而升级能力成熟度或发布状态。

## 实施计划入口

本规格已于 2026-08-04 获得批准，由[持续复杂度收敛实施计划](../plans/2026-08-04-continuous-complexity-convergence.md)执行。实施计划必须：

1. 只链接本规格作为唯一 primary design。
2. 把 16 个代理职责转成具体文件、RED/GREEN 步骤、依赖屏障和聚合门禁。
3. 更新旧计划中 Task 12、Task 13 的执行所有权，禁止两个活动计划并行修改同一文件。
4. 不执行 commit、push、发布或真实危险副作用，除非用户另行明确授权。
