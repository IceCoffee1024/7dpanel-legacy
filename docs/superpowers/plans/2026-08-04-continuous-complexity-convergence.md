---
state: Current
document_role: Change Record
last_updated: "2026-08-04"
---

# 持续复杂度收敛实施计划

> **执行要求：** 在独立执行会话中同时使用 `executing-plans`、`subagent-driven-development` 和 16 个子代理，按 Wave 屏障推进并持续更新 checkbox。未经用户明确授权，不执行 commit、push、发布、真实 7DTD 危险副作用或其他 Git 历史操作。

**Goal:** 在不改变八项目模块化单体、唯一 Provider、API、SQLite schema、权限和危险状态语义的前提下，系统收敛后端组合根与 Adapter 热点、Admin 大文件和固定入口密度，并建立持续防回归的复杂度棘轮。

**Architecture:** 保持 Domain、Application、Hosting、四个 Adapter 和 Bootstrap 的现有依赖方向。Bootstrap 仍是唯一组合根，但注册清单按 Platform/Capability 拆为同项目内部模块；Store 按真实聚合、读写合同和事务边界拆分；Admin 保持 `page -> feature -> shared` 与 Feature 状态所有权；复杂度脚本同时检查宏观边界、热点趋势和依赖例外。主设计为[持续复杂度收敛设计规格](../specs/2026-08-04-continuous-complexity-convergence-design.md)。

**Current facts:** 当前产品合同见 [PRD](../../PRD.md)，当前交互见 [产品设计](../../design.md)，当前技术边界见 [系统架构](../../architecture.md)，验证与成熟度见 [测试策略](../../test.md)。本计划 checkbox、子代理声明和目标阈值都不是实现或发布证据。

**Scope boundaries:** 不新增后端项目、测试项目、数据库 migration、API route、OpenAPI operation、全局前端 Store、一级导航任务域或公共生命周期框架。不批量移动目录和命名空间。若热点必须改变 schema、外部合同或用户可见状态才能继续，立即停止该热点并提交独立设计。

**Verification boundary:** 迭代阶段运行任务列出的聚焦测试。每个 Wave 稳定后运行对应聚合；最终只运行一次完整后端与 Admin 聚合。导航变更运行 Playwright mock 桌面和 `390x844`；只有提供受控 OWIN 环境时才计入真实 OWIN 证据。纯内部拆分不运行 publish、真实 7DTD、Discord/MaxMind、备份恢复或世界危险 smoke。

**Rollback:** 每个 Task 必须保持可单独恢复到前一实现：组合根不改对象图，Store 不改 schema，Web 不改 OpenAPI snapshot，Admin 不改服务端合同，导航不删除规范路由，文档不删除历史 Change Record。

## 16 个代理与协调规则

主协调者一次创建 16 个代理并保留其编号。代理在 Wave 之间复用上下文，不另行创建第 17 个代理。已完成代理在不再需要时可以关闭，但不得用新代理绕过编号、写集或屏障。

| 代理 | 工作范围 | 独占写集 |
|---:|---|---|
| 1 | 指标与趋势 | `Measure-Complexity.ps1`、预算 JSON、测量器夹具 |
| 2 | 预算与依赖门禁 | `Test-ComplexityBudget.ps1`、依赖检查器及其夹具 |
| 3 | Bootstrap 集成 | 根 factory、composition context、Bootstrap characterization |
| 4 | Platform 注册 | Platform registration 文件及聚焦测试 |
| 5 | Operations 注册 | Operations registration 文件及聚焦测试 |
| 6 | Players 注册 | Players registration 文件及聚焦测试 |
| 7 | Community 注册 | Community registration 文件及聚焦测试 |
| 8 | Economy 注册 | Economy registration 文件及聚焦测试 |
| 9 | Automation/Administration 注册 | 两个 registration 文件及聚焦测试 |
| 10 | Commerce/Economy Persistence | 对应 Store 与测试，不修改 migration |
| 11 | Community/Rewards Persistence | 对应 Store 与测试，不修改 migration |
| 12 | Web/OpenAPI | processor、选定 Controller/model 与测试 |
| 13 | Community/World Tools Admin | 两个 Feature 的 API/model 与测试 |
| 14 | Player Map/Server Status Admin | 两个 Feature 的 model/ui 与测试 |
| 15 | 导航和文档入口 | navigation、SectionTabs、Change Record 索引、最终 Current 文档 |
| 16 | 独立验证 | 默认只读，只写经主协调者批准的验证报告区段 |

协调约束：

- 主协调者拥有任务分派、Wave 屏障、中央 diff 审查和停止决定，不重复代理已完成的实现。
- 同一文件只能有一个代理写。发现重叠时，后启动任务立即停止，由主协调者重新分配。
- 代理 4 至 9 不编辑 `PanelServiceProviderFactory.cs`；代理 3 在其模块稳定后独占根文件。
- 代理 16 不接受生产修复任务，避免验证者同时证明自己的实现。
- 每个代理返回改动文件、行为不变量、聚焦命令和结果；“看起来正确”不计入证据。
- 任何子代理结果只有经过主协调者 review、合并后聚焦复验和最终聚合门禁才进入完成状态。

## Wave 0：基线与防回归合同

### Task 1：扩展复杂度测量和预算棘轮（代理 1、2、16）

**Files:**

- Modify: `tests/complexity/Measure-Complexity.ps1`
- Modify: `tests/complexity/Test-ComplexityBudget.ps1`
- Create: `tests/complexity/complexity-budget.json`
- Create/Modify: `tests/complexity/tests/Test-ComplexityScripts.Tests.ps1`
- Create: `tests/complexity/Test-FrontendFeatureDependencies.ps1`
- Create: `tests/complexity/tests/Test-FrontendFeatureDependencies.Tests.ps1`

- [x] **Step 1：代理 2 写 fail-closed RED 夹具**

  夹具固定以下失败：第二 Provider、项目依赖环、Hosting -> Application、未知 Capability、未说明公共接口、注册模块调用 `BuildServiceProvider`/启动后台工作、跨 Feature 内部导入、每域超过七个固定入口、预算配置缺字段或路径不存在。

  Run:

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/tests/Test-ComplexityScripts.Tests.ps1
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/tests/Test-FrontendFeatureDependencies.Tests.ps1
  ```

  Expected: RED，现有测量器尚不输出全部指标，新检查器尚不存在。

- [x] **Step 2：代理 1 实现结构化测量结果**

  在现有 schema 上增加：手写生产文件的 `>500`、`>800`、`>1000` 数量，Admin 的 `>400`、`>600` 数量，组合根行数，registration 文件数，公共接口总量，Feature 内部跨域导入，固定导航入口和文档活动记录。排除 generated、migration、snapshot、`7dtd-reference` 和构建产物。

  `complexity-budget.json` 保存批准基线、目标和例外；脚本使用 JSON parser，不用字符串替换。每个例外包含路径、原因、所有者和复审条件。

- [x] **Step 3：代理 2 实现 hard invariant 与 ratchet**

  hard invariant 失败立即退出；热点指标采用棘轮：当前值不得恶化，完成 Wave 后达到规格目标。实现阶段允许通过显式 `phase` 切换当前 Wave 目标，但不能用提高最终阈值规避失败。

- [x] **Step 4：代理 16 独立复算基线**

  用只读 PowerShell 和文件清单独立复算关键数量，与脚本 JSON 对比。计数不一致时停止，不让后续拆分建立在错误基线上。

- [x] **Step 5：转 GREEN**

  Run:

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/tests/Test-ComplexityScripts.Tests.ps1
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/tests/Test-FrontendFeatureDependencies.Tests.ps1
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/Test-ComplexityBudget.ps1
  ```

  Expected: PASS；当前存量由基线显式记录，新回归和未知配置 fail closed。

### Task 2：锁定 Bootstrap 对象图和生命周期（代理 3、16）

**Files:**

- Modify: `backend/tests/LSTY.SevenDPanel.Tests/DependencyInjectionTests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/DependencyRulesTests.cs`
- Modify as needed: `backend/tests/LSTY.SevenDPanel.Tests/Bootstrap/**`
- Modify as needed: `backend/tests/LSTY.SevenDPanel.Tests/PlayerMapDependencyInjectionTests.cs`

- [x] **Step 1：补 characterization 测试**

  固定所有生产 service 可解析、singleton/scoped 身份、Web request scope、runtime 集合与顺序、启动失败逆序回滚、停止排空、Provider 最后释放、唯一 `BuildServiceProvider` 和注册阶段不解析服务。测试只观察现有行为，不先改变生产实现。

- [x] **Step 2：证明 characterization 有辨别力**

  在测试工作副本中临时制造一个重复注册、错误 lifetime 或第二 Provider，确认至少一个测试失败后撤销该测试性扰动；不得把故意错误保留在工作树。

- [x] **Step 3：运行 Bootstrap 基线**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-restore --filter "Boundary=Bootstrap|FullyQualifiedName~DependencyInjectionTests|FullyQualifiedName~DependencyRulesTests|FullyQualifiedName~PlayerMapDependencyInjectionTests"
  ```

  Expected: PASS，记录准确测试数；Wave 1 不得改变该结果和对象图。

## Wave 1：唯一组合根收敛

### Task 3：创建七组无副作用注册模块（代理 4 至 9）

**Files:**

- Create: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/Registration/PlatformServiceRegistration.cs`
- Create: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/Registration/OperationsServiceRegistration.cs`
- Create: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/Registration/PlayersServiceRegistration.cs`
- Create: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/Registration/CommunityServiceRegistration.cs`
- Create: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/Registration/EconomyServiceRegistration.cs`
- Create: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/Registration/AutomationServiceRegistration.cs`
- Create: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/Registration/AdministrationServiceRegistration.cs`
- Create focused tests under: `backend/tests/LSTY.SevenDPanel.Tests/Bootstrap/Registration/`

- [x] **Step 1：主协调者发布注册所有权表**

  从当前 factory 逐项列出 service、lifetime、factory side effect、主要 Capability 和跨能力端口。每个注册只有一个代理所有者；Overview、资源目录、Discord bridge、作业 bridge 等特殊归属按批准规格处理。

- [x] **Step 2：代理 4 至 9 并行创建内部模块**

  每个模块只接收 `IServiceCollection` 和 composition context，不创建接口、子容器或静态状态。此步骤先复制并整理所属注册，不删除根 factory 中的原注册；模块不被调用时不得产生副作用。

- [x] **Step 3：每个代理运行编译和所属聚焦测试**

  ```powershell
  dotnet build backend/7DPanel.sln --configuration Release --no-restore
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-restore --filter "Boundary=Bootstrap"
  ```

  Expected: PASS；新增模块可以编译但尚未改变运行时对象图。

- [x] **Step 4：主协调者检查互斥写集**

  确认代理 4 至 9 没有编辑 root factory，没有重复拥有同一 service，没有测试专用注册入口。存在争议时在集成前解决，不让代理自行创建共享注册表。

### Task 4：集成 composition context 和根 factory（代理 3、16）

**Files:**

- Create: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/Registration/PanelCompositionContext.cs`
- Modify: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/DependencyInjectionTests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/DependencyRulesTests.cs`
- Modify as needed: `backend/tests/LSTY.SevenDPanel.Tests/Bootstrap/**`

- [x] **Step 1：实现无接口 composition context**

  Context 只保存已验证 options、路径和外部输入，不持有 Provider，不提供任意解析方法，不启动资源。敏感值不进入 `ToString`、日志或异常摘要。

- [x] **Step 2：按稳定顺序切换到 registration 模块**

  代理 3 每次迁移一个模块，删除 root 中对应重复注册，运行该 Capability 与 Bootstrap 测试后再迁移下一组。保持 singleton/scoped lifetime、显式 factory、runtime 装饰顺序和 dispose 顺序。

- [x] **Step 3：收敛根 factory**

  根文件只保留输入校验、context 创建、七组注册调用、一次 `BuildServiceProvider`、Validate 和 `ServiceProviderRuntime` 返回，目标不超过 250 行。辅助路径解析进入 context factory 或现有明确 helper，不创建通用 bootstrap framework。

- [x] **Step 4：代理 16 执行对象图差异审计**

  对比迁移前后的注册 service type、implementation/factory、lifetime 和 runtime 顺序。任何缺失、重复或 lifetime 变化都阻塞 Wave。

- [x] **Step 5：Wave 1 门禁**

  ```powershell
  dotnet build backend/7DPanel.sln --configuration Release --no-restore
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-restore --filter "Boundary=Bootstrap|FullyQualifiedName~DependencyInjectionTests|FullyQualifiedName~DependencyRulesTests"
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/Test-ComplexityBudget.ps1
  ```

  Expected: PASS；唯一 Provider 和对象图不变，root 行数满足本 Wave 棘轮。

验证记录：[Task 4 review package](../../../.superpowers/sdd/continuous-complexity-convergence/task-4-review-package.md)、[Agent 16 review](../../../.superpowers/sdd/continuous-complexity-convergence/task-4-agent-16-review.md)。

## Wave 2：后端热点收敛

### Task 5：拆分 Commerce/Economy 持久化热点（代理 10）

**Files:**

- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Commerce/SqliteCommerceStore.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Economy/SqliteEconomyLedgerStore.cs`
- Create internal collaborators beside the owning Store as characterization proves appropriate
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/SqliteCommerceConcurrencyTests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/SqliteEconomyLedgerStoreTests.cs`
- Modify as needed: `backend/tests/LSTY.SevenDPanel.Tests/CommerceRewardUseCaseTests.cs`
- Modify as needed: `backend/tests/LSTY.SevenDPanel.Tests/CommerceRewardHttpTests.cs`

- [x] **Step 1：固定事务、并发和幂等行为**

  增加 characterization，覆盖产品保存、上下架、兑换码、每日领取、余额/账本原子性、并发冲突、row version、重复请求和失败回滚。测试从当前接口调用，不暴露内部 SQL helper。

- [x] **Step 2：按真实合同拆分**

  优先分离 catalog query、command persistence、claim/redemption 和 row mapping；同一事务的写入仍由一个内部 coordinator 负责。保留现有 public Store 或接口适配面，除非已有多个生产接口可直接由具体内部实现承担。

- [x] **Step 3：保持 schema 和 SQL 语义**

  不改 migration、表、索引、默认值、排序、分页和时间格式。若拆分需要跨 connection 模拟事务，停止并重新设计。

- [x] **Step 4：聚焦验证**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~SqliteCommerceConcurrencyTests|FullyQualifiedName~SqliteEconomyLedgerStoreTests|FullyQualifiedName~CommerceReward"
  ```

  Expected: PASS；热点满足行数棘轮或记录明确事务例外。

### Task 6：拆分 Community/Rewards 持久化热点（代理 11）

**Files:**

- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Community/SqliteCommunityStore.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Rewards/SqliteRewardStore.cs`
- Create internal collaborators beside the owning Store as characterization proves appropriate
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/CommunityGameCommandPersistenceContractTests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/SqliteRewardStoreTests.cs`
- Modify as needed: `backend/tests/LSTY.SevenDPanel.Tests/CommunityDailyClaimTests.cs`
- Modify as needed: `backend/tests/LSTY.SevenDPanel.Tests/EconomyCommunityMigrationTests.cs`

- [x] **Step 1：固定 Community 与 Reward persistence 合同**

  覆盖动态命令配置、传送/投票/城市、daily claim、奖励包、delivery journal、并发更新、重试和恢复读取。明确哪些写操作共享事务，哪些只是同一 facade 的独立变化原因。

- [x] **Step 2：拆出内部职责而不泄漏技术类型**

  按配置、关系/投票、领取、奖励包和 journal 边界建立内部类；Application/Domain 不引用 Dapper、connection、表名或 row model。不创建通用 Repository 基类。

- [x] **Step 3：聚焦验证**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~CommunityGameCommandPersistenceContractTests|FullyQualifiedName~SqliteRewardStoreTests|FullyQualifiedName~CommunityDailyClaimTests|FullyQualifiedName~EconomyCommunityMigrationTests"
  ```

  Expected: PASS；migration snapshot 和现有数据语义不变。

### Task 7：拆分 Web/OpenAPI 热点（代理 12）

**Files:**

- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OpenApi/PanelOpenApiOperationProcessor.cs`
- Create internal OpenAPI rule files in the same directory
- Modify selected large files under: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostOpenApiSnapshotTests.cs`
- Modify relevant HTTP tests selected from the changed Controller boundary

- [x] **Step 1：固定 OpenAPI 和 HTTP behavior**

  通过 snapshot、operation ID、授权、Problem Details、错误 response、分页和 schema characterization 锁定现有输出。不得先更新 snapshot适配实现。

- [x] **Step 2：拆分纯规则**

  将认证、标准错误、operation metadata 和 schema augmentation 拆为内部纯规则，由一个 processor 顺序调用。规则不读取 DI、HTTP request、数据库或游戏运行时。

- [x] **Step 3：收敛选定 Controller/model**

  只处理超过预算且有明确资源边界的文件。Controller 保留 route 和授权映射，业务状态留在 Application；HTTP model mapping 保持显式。

- [x] **Step 4：聚焦和 drift 验证**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~OwinWebHostOpenApiSnapshotTests|Boundary=Web"
  Set-Location frontend/apps/admin
  pnpm api:check
  ```

  Expected: PASS，OpenAPI snapshot 与生成客户端无 diff。

### Task 8：Wave 2 后端聚合和剩余例外审查（代理 10、11、12、16）

- [x] **Step 1：运行后端 Release build/test**

  ```powershell
  dotnet build backend/7DPanel.sln --configuration Release --no-restore
  dotnet test backend/7DPanel.sln --configuration Release --no-build --no-restore
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/Test-ComplexityBudget.ps1
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/Test-BackendTestTaxonomy.ps1
  ```

- [x] **Step 2：处理仍超过阈值的后端热点**

  代理 10 至 12 只在原独占 Adapter 范围继续第二轮。每个保留的 `>1000` 或 `>800` 文件必须记录事务/生成/兼容原因和复审条件；不能把阈值提高到当前值。

- [x] **Step 3：代理 16 审计 API/schema/对象图**

  确认无 migration、OpenAPI、项目引用、Provider 数量和 runtime 生命周期漂移。Wave 2 失败时只回滚对应热点，不撤销已通过的 Wave 1。

  Wave 2 证据：Release build `0 warning / 0 error`；全量后端测试
  `1882 passed / 2 pre-existing failures / 1884 total`；Task 5 `33/33`、Task 6
  `16/16`、Task 7 `416 passed / 1 pre-existing snapshot drift`；`pnpm api:check`
  通过；复杂度预算 `phase=Wave2` 通过。两个既有失败分别为 runtime OpenAPI
  与 Admin snapshot 漂移，以及未修改的 `SqliteServerOperationStore` 合法状态转换
  测试；未更新 snapshot 或绕过失败。上述数字是 Wave 2 时点证据；Wave 4 的
  最新聚合结果由 Task 13 记录为 `1881 passed / 2 failed / 1883 total`。

## Wave 3：Admin 与用户入口收敛

### Task 9：拆分 Community/World Tools Admin 热点（代理 13）

**Files:**

- Modify: `frontend/apps/admin/src/features/community/api/community.ts`
- Modify: `frontend/apps/admin/src/features/community/api/community.test.ts`
- Modify: `frontend/apps/admin/src/features/community/model/useCommunity.ts`
- Modify: `frontend/apps/admin/src/features/community/model/useCommunity.test.ts`
- Modify: `frontend/apps/admin/src/features/world-tools/api/worldTools.ts`
- Modify: `frontend/apps/admin/src/features/world-tools/worldTools.test.ts`
- Create focused API/parser/composable files inside the same two Feature directories

- [x] **Step 1：固定 parser、transport 和状态合同**

  测试无效 payload、稳定错误码、取消、冲突、`result-unknown`、查询键、刷新和 Mutation invalidation。保持严格 parser，不直接把 generated DTO 交给 UI。

- [x] **Step 2：按资源和副作用拆分**

  Community 按 teleport/vote/city/config resource 拆分；World Tools 按 read/preflight/operation/history/resource 拆分。原入口文件只做明确 re-export 或 composition，不保留第二份实现。

- [x] **Step 3：聚焦验证**

  Workdir: `frontend/apps/admin`

  ```powershell
  pnpm exec vitest run src/features/community src/features/world-tools
  pnpm typecheck
  pnpm lint src/features/community src/features/world-tools
  ```

  Expected: PASS；Feature 状态、URL、危险确认和后端合同不变。

  Task 9 evidence: Community/World Tools focused tests `89/89` passed；typecheck 和
  feature-local lint passed；strict parser、UTC、city/full-city 和 vote/full-round
  projection 修复经 Agent 16 scoped re-review 批准，详见 SDD reports。

### Task 10：拆分 Player Map/Server Status Admin 热点（代理 14）

**Files:**

- Modify: `frontend/apps/admin/src/features/player-map/model/usePlayerMap.ts`
- Modify: `frontend/apps/admin/src/features/player-map/model/usePlayerMap.test.ts`
- Modify selected large player-map model/ui files with focused tests
- Modify: `frontend/apps/admin/src/features/server-status/model/overview.ts`
- Modify: `frontend/apps/admin/src/features/server-status/model/useOverview.ts`
- Modify: `frontend/apps/admin/src/features/server-status/model/useOverview.test.ts`
- Create focused controllers/projections inside the same Feature directories

- [x] **Step 1：固定生命周期和竞态语义**

  Player Map 覆盖四类 AbortController、sequence 防旧响应、visibility、timer、world identity 更换、URL 同步、track fit 和 dispose。Server Status 覆盖 Fresh/Partial/Stale/Offline、最后成功快照、轮询、SSE 触发 refetch 和敏感字段裁剪。

- [x] **Step 2：拆分 source state、异步 controller 和 projection**

  保持一个页面 composition surface；抽出的 controller 必须有明确 start/dispose，不能新增 Store、第二轮询器或第二 SSE。派生 projection 使用 readonly/computed，不复制权威服务器状态。

- [x] **Step 3：聚焦验证**

  Workdir: `frontend/apps/admin`

  ```powershell
  pnpm exec vitest run src/features/player-map src/features/server-status
  pnpm typecheck
  pnpm lint src/features/player-map src/features/server-status
  ```

  Expected: PASS；无 timer、listener、Blob URL 或请求泄漏。

  Task 10 evidence: Player Map/Server Status focused tests `131/131` passed；typecheck
  和 Feature-local lint passed；Agent 16 生命周期和资源审计批准，详见 SDD reports。

### Task 11：收敛固定二级入口和活动文档入口（代理 15）

**Files:**

- Modify: `frontend/apps/admin/src/app/navigation/navigationCatalog.ts`
- Modify: `frontend/apps/admin/src/app/navigation/navigationTypes.ts`
- Modify: `frontend/apps/admin/src/app/navigation/useNavigation.ts`
- Modify: `frontend/apps/admin/src/app/navigation/navigationCatalog.test.ts`
- Modify: `frontend/apps/admin/src/app/navigation/useNavigation.test.ts`
- Modify as needed: `frontend/apps/admin/src/components/navigation/SectionTabs.vue`
- Modify: `frontend/apps/admin/src/components/navigation/SectionTabs.test.ts`
- Modify relevant thin route pages under `frontend/apps/admin/src/pages/`
- Modify: `frontend/apps/admin/src/app/AppShell.test.ts`
- Modify: `frontend/apps/admin/src/app/router.test.ts`
- Modify: `frontend/apps/admin/tests/e2e/admin-navigation.spec.ts`
- Create: `docs/superpowers/README.md`
- Modify: `README.md`

- [x] **Step 1：写固定入口密度 RED**

  测试每域不超过七项，并固定任务入口：Operations 将 Schedules/Rules 归入“计划与自动化”、Mods/Modules 归入“扩展”；Economy 将四类奖励归入“奖励”、Shop/Redeem 归入“商业”；System 将 Discord/GeoIP 归入“集成”。所有现有子 route name、角色守卫、搜索和 breadcrumb 仍可达。

- [x] **Step 2：实现导航投影而不删除路由**

  固定侧栏只显示任务入口；SectionTabs 或现有局部导航展示同域子路由。Dashboard Search 可以继续索引具体子路由，面包屑保留完整父链。不得创建空白 landing page、动态插件目录或第二导航数组。

- [x] **Step 3：建立 Change Record 索引**

  `docs/superpowers/README.md` 只列活动、已完成、被取代记录及阅读顺序。根 README 链接该索引，不逐项列历史 spec/plan；不删除任何历史文件，不复制 Current 事实。

- [x] **Step 4：聚焦验证**

  Workdir: `frontend/apps/admin`

  ```powershell
  pnpm exec vitest run src/app/navigation src/app/AppShell.test.ts src/app/router.test.ts src/components/navigation/SectionTabs.test.ts
  pnpm typecheck
  pnpm lint src/app src/components/navigation
  ```

- [x] **Step 5：浏览器验证**

  ```powershell
  pnpm exec playwright test tests/e2e/admin-navigation.spec.ts
  ```

  Expected: Chromium desktop 与 `390x844` PASS；角色裁剪、键盘、搜索、局部 tabs、Back/Forward、刷新和无水平溢出保持正确。缺浏览器 executable 时明确 Blocked，不把 discovery 计为通过。

  Task 11 evidence: mock desktop navigation `2 passed`；未取得 `390x844` PASS，真实
  OWIN 未执行；Agent 16 审查通过，未将缺失边界写成成功证据。该记录是 Task 11
  时点证据，Task 13 已补齐 mock `390x844` 的 `2 passed`；真实 OWIN 仍因环境变量
  缺失未执行。

### Task 12：Wave 3 Admin 聚合和热点棘轮（代理 13、14、15、16）

- [x] **Step 1：运行 Admin 静态与单元聚合**

  Workdir: `frontend/apps/admin`

  ```powershell
  pnpm lint
  pnpm typecheck
  pnpm test:unit
  pnpm build
  pnpm api:check
  ```

- [x] **Step 2：检查跨 Feature 依赖和大文件目标**

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/Test-FrontendFeatureDependencies.ps1
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/Test-ComplexityBudget.ps1
  ```

  代理 13、14 只在原 Feature 写集处理剩余 `>600` 和选定 `>400` 热点；保留例外必须说明状态所有权或框架兼容原因。

- [x] **Step 3：代理 16 独立审查运行时资源**

  检查 timer、listener、AbortController、Blob URL、SSE subscription、query cache 和 Mutation invalidation 没有重复或泄漏；确认 OpenAPI generated 和 snapshot 无 diff。

  Task 12 evidence: focused `13 files / 25 tests` passed；Admin aggregate evidence was
  recorded before the final fix round as `lint/typecheck/test:unit/build/api:check`
  passing；final dependency gate `75/75` and Wave 3 complexity budget passed；the
  final scoped review is approved. The standalone `src/features/modules` filter has
  no matching test files and is recorded as a non-pass filter result.

## Wave 4：聚合验证与事实提升

### Task 13：全仓库聚合门禁（代理 16、主协调者）

- [x] **Step 1：确认范围和干净输入**

  `git status --short` 中只能出现本计划批准文件。忽略但保留用户既有无关改动；发生写集重叠时停止，不重置或覆盖。

- [x] **Step 2：运行后端聚合一次**

  ```powershell
  dotnet build backend/7DPanel.sln --configuration Release --no-restore
  dotnet test backend/7DPanel.sln --configuration Release --no-build --no-restore
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/Test-ComplexityBudget.ps1
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/Test-BackendTestTaxonomy.ps1
  ```

  2026-08-04 结果：Release build 为 `0` warnings、`0` errors；后端全量测试为 `1881` passed、`2` failed、`1883` total。两个失败是已知未修改问题：`OwinWebHostOpenApiSnapshotTests` 的 runtime OpenAPI snapshot drift，以及 `SqliteServerOperationStore` 的非法状态转换；未更新 snapshot、未绕过测试。`Test-ComplexityBudget.ps1` 已切换 `phase=Wave4` 并通过，`Test-BackendTestTaxonomy.ps1` 以 `205` 个源文件通过。

- [x] **Step 3：运行 Admin 聚合一次**

  Workdir: `frontend/apps/admin`

  ```powershell
  pnpm lint
  pnpm typecheck
  pnpm test:unit
  pnpm build
  pnpm api:check
  ```

  2026-08-04 结果：`pnpm lint`、`pnpm typecheck`、`pnpm test:unit`、`pnpm build` 和 `pnpm api:check` 均通过；Admin 为 `145` 个测试文件、`1010/1010` 项，生成客户端和 OpenAPI snapshot 无 tracked drift。

- [x] **Step 4：运行适用浏览器门禁**

  运行 Task 11 的 mock 导航矩阵。受控 OWIN 环境变量齐全时再运行真实 OWIN 深链接、认证、静态 fallback 和角色裁剪；否则保留明确未执行边界。

  2026-08-04 结果：Chromium mock desktop `2` 个场景通过，Chromium mock `390x844` `2` 个场景通过；`SEVENDPANEL_ADMIN_URL`、`PANEL_USERNAME`、`PANEL_PASSWORD` 未提供，真实 OWIN 本轮未执行。

- [x] **Step 5：差异和秘密审查**

  ```powershell
  git diff --check
  git status --short
  ```

  确认没有 migration、OpenAPI、生成客户端、项目数、Provider 数、凭据、机器路径、构建产物或私有 reference diff；根目录 `.pnpm-store/` 仅为本轮 pnpm 生成的未跟踪缓存。已尝试删除，但 `v11/index.db*` 被外部 Node 进程占用，未终止无法归属的进程；该缓存清理作为环境收尾 concern 记录，不属于产品变更。

### Task 14：提升 Current 文档并关闭专项（代理 15、16、主协调者）

**Files:**

- Modify after verified implementation: `docs/design.md`
- Modify after verified implementation: `docs/architecture.md`
- Modify after verified implementation: `docs/test.md`
- Modify: `docs/superpowers/README.md`
- Modify: `docs/superpowers/plans/2026-08-03-capability-closure-and-modular-ownership.md`
- Modify this plan to update execution checkboxes and evidence
- Modify only after actual release: `CHANGELOG.md`

- [x] **Step 1：按事实所有权提升**

  `docs/design.md` 只记录已验证固定入口和局部导航；`docs/architecture.md` 记录当前 registration、Store/Web/Admin 边界；`docs/test.md` 记录实际指标、命令、环境、通过、失败和缺口。不得把本计划或代理总结当作实现证据。

- [x] **Step 2：关闭旧计划重叠入口**

  保留旧计划 Task 12 的移交说明和 Task 13 的历史通过证据，链接本计划最终状态。不得把未执行专项任务反向标记为旧计划完成。

- [x] **Step 3：文档语义审计**

  逐一验证本地链接、简体中文政策、Current/Target/Change Record/Evidence 角色、spec/plan 唯一配对、占位标记、机器路径和事实来源。`docs/superpowers/README.md` 只做入口，不复制架构和测试详情。

- [x] **Step 4：记录最终复杂度结果**

  在 `docs/test.md` 记录最终测量 JSON 与基线差异；未达到目标的保留例外必须有路径、原因、风险、所有者和复审条件。指标改善不自动提升 Capability 成熟度。

- [x] **Step 5：最终差异审查**

  ```powershell
  git diff --check
  git status --short
  ```

  Expected: 所有改动属于本计划，文档链接有效，无 Git 历史操作。向用户报告每个 Wave、16 个代理、准确测试结果、浏览器/真实环境缺口和剩余复杂度例外。

## 完成检查表

- [x] 16 个代理均按固定编号和互斥写集工作，没有创建第 17 个代理。
- [x] 八个产品项目、一个测试项目和一个根 Provider 保持不变。
- [x] `PanelServiceProviderFactory.cs` 不超过 250 行，注册模块无启动副作用。
- [x] 后端与 Admin 热点达到最终棘轮或有不增加的显式例外。
- [x] 跨 Capability Store/表和前端内部 Feature 访问为零或在 allowlist/例外中有生产理由。
- [x] 六个一级任务域保持不变，每域固定二级入口不超过七个，全部规范路由可达。
- [x] OpenAPI snapshot、生成客户端和 SQLite migration 无语义变化。
- [ ] 后端 build/test、治理门禁和 Admin lint/typecheck/unit/build/api drift 全部通过；build、治理和 Admin 门禁通过，但全量后端测试保留两个已知未修改失败。
- [x] 适用 Playwright mock 通过；未提供的真实 OWIN 环境如实记录。
- [x] Current 文档只包含已验证事实，Change Record 索引可用且历史记录未删除。
- [x] 没有 commit、push、发布、真实危险副作用或来源不明文件修改。
