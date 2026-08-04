# 能力收口与模块化所有权实施计划

> **执行要求：** 在独立执行会话中使用 `executing-plans`，按任务逐项实现、验证和更新 checkbox。Wave 2 的四个通道只可在文件所有权、7DTD 实例、世界、端口、SQLite、外部服务账号和证据目录均独立时并发。危险玩家动作、真实恢复、世界副作用和回滚在执行前仍需满足各自显式确认参数与隔离前置条件。未经用户明确授权，不执行 commit、push、merge 或其他 Git 历史操作。

**Goal:** 在一个稳定化项目和一个候选版本内完成 M1 至 M11，关闭服务器运维、玩家管理、备份恢复和其余 P0 的真实环境缺口；同时尽可能完成 O1、O2、O4，但不把增强目标改写为新的 P0 发布门槛。

**Architecture:** 保持 Domain -> Application -> Adapters -> Bootstrap 的八项目模块化单体。Platform 拥有 Hosting、Authentication、Persistence 和 Game Integration 技术边界；Operations、Players、Community、Economy、Automation、Administration 六个 Capability 拥有业务变化。`docs/test.md` 唯一拥有成熟度与发布证据；Bootstrap 保持单一根 Provider，只把当前 1433 行注册清单拆为能力级显式注册文件。主设计为 [能力收口与模块化所有权设计规格](../specs/2026-08-03-capability-closure-and-modular-ownership-design.md)。

**Tech Stack:** C# 11 / `net48`、xUnit.net v3、Microsoft DI、SQLite/DbUp/Dapper、OWIN/Web API、Vue 3 Composition API、TypeScript、Pinia Colada、Vitest、Playwright、PowerShell 5.1/7、Windows/Linux 7DTD Dedicated Server `v3.0.1-b4`。

## 当前起点与范围锁

- 后端有八个产品项目、583 个生产 `.cs` 文件；不改变项目数量。
- Admin 有 27 个 Feature，六个一级任务域和规范路由已经完成；不再次重构导航。
- 后端测试保持一个 `.csproj`；允许增加 class-level trait 和聚焦执行入口，不拆项目、不批量移动测试文件。
- `PanelServiceProviderFactory.cs` 当前 1433 行；Hosting -> Application 已由 Task 10 清除，后续只防止该依赖回归。
- 默认 `7dtd-reference/v3.0.1-b4` 当前缺少 `0Harmony.dll`、`Assembly-CSharp.dll` 和 `Newtonsoft.Json.dll`，后端聚合与真实候选工作在补齐只读引用或提供显式 `SevenDaysReferenceRoot` 前保持 Blocked。
- Playwright 锁定的 Chromium headless shell 已安装并可执行；本阶段只把 Chromium 作为发布浏览器基线，不安装未纳入产品支持范围的 Firefox/WebKit。真实 OWIN 项目仍需要受控 URL 和凭据，不能由 mock 项目替代。
- 仓库当前没有 CI workflow；O4 先复用现有门禁形成 provider-neutral 候选编排，只在私有 CI 提供者、Runner 和 Secret 模型明确后增加最小接入。
- 当前真实缺口以 `docs/test.md#已知缺口` 为准。执行时先审计实际代码和证据，不能从本计划勾选状态推断实现已完成。
- 本阶段冻结净新增产品能力。只实现核心旅程、P0 发布门禁、安全、恢复、兼容和复杂性治理所必需的完整切片。
- `.pnpm-store/` 等既有未跟踪内容不属于本计划，执行时保持不动。
- Task 1 至 3、5 至 8、10 已形成实现基线；Task 13 的本地 mock 浏览器步骤已完成；Task 4 Step 3、Task 9、Bootstrap、候选 artifact 和真实环境门禁仍未完成。既有 checkbox 保留历史状态，后续不得仅因本次重排改为完成。

## 执行图

```text
已完成基线：Task 1-3、5-8、10

Task 11 环境与自动化
  -> Task 4 Step 3
  -> Task 9 J3 生产闭环
  -> Task 12 Bootstrap
  -> Task 13 用户任务体验
  -> Task 14 冻结候选 artifact
       |-> Task 15 P0/性能/恢复/回滚 -----|
       |-> Task 16 O1/O2 扩大验收 --------|-> Task 17 O4 私有 Runner
                                               -> Task 18 最终判定
```

- Task 11 必须先解除引用与 Chromium 阻塞，再完成 Task 4 Step 3；没有可信自动化基线时不开始结构改动。
- Task 9、12、13 按顺序完成，防止恢复代码、Bootstrap 和共享 Admin 状态互相覆盖；其中真实破坏性恢复仍留到 Task 15。
- Task 15 与 Task 16 可以在独立资源上并行；共享 OpenAPI、i18n、`docs/test.md`、候选 artifact 和证据总目录由主执行者串行整合。
- Task 14 后任何产品代码、依赖或发布内容变化都会产生新 artifact hash，并使受影响真实证据失效。

## 批次范围映射

| 已批准范围 | 计划任务 |
|---|---|
| M1、M2 | Task 11，以及 Task 4 Step 3 |
| M3 | Task 12 |
| M4 | Task 6 至 9、Task 15 |
| M5 | Task 9、Task 15 |
| M6 | Task 13 |
| M7 | Task 14 |
| M8 | Task 15 |
| M9 | Task 11、15 |
| M10 | Task 15 |
| M11 | Task 18 |
| O1、O2 | Task 16 |
| O4 | Task 17 |

## Task 1：建立能力成熟度台账与所有权基线

**Files:**

- Modify: `docs/test.md`
- Modify: `docs/architecture/backend-target-blueprint.md`
- Modify: `CONTRIBUTING.md`
- Create: `tests/docs/Test-CapabilityMaturity.ps1`
- Create: `tests/docs/tests/Test-CapabilityMaturity.Tests.ps1`
- Modify: `tests/README.md`

- [x] **Step 1：为成熟度文档检查器写失败夹具**

  用临时 Markdown 夹具覆盖缺少 `CAP-01` 至 `CAP-12`、重复 Journey ID、未知所有者、未知成熟度、`Verified` 无 evidence、`Release-ready` 有 blocker、无 artifact hash、Target/spec/plan 被放入 implementation evidence、断链和 `skipped` 被计为通过。测试只使用临时目录，不改真实文档。

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/docs/tests/Test-CapabilityMaturity.Tests.ps1
  ```

  Expected: RED，报告尚不存在检查器或缺少真实文档区段。

- [x] **Step 2：在 `docs/test.md` 增加唯一成熟度台账**

  用 `CAPABILITY_MATURITY_START/END` 注释限定可机读表格，包含 `CAP-01` 至 `CAP-12` 和 `J1` 至 `J3`。每行固定字段：ID、Owner、Contract、Current implementation anchor、Required boundaries、Evidence、Maturity、Gate、Blockers/expiry。`Maturity` 只允许 `Planned`、`Implemented`、`Verified`、`Release-ready`；`Gate` 只允许 `Open`、`Blocked`、`Passed`。

  逐行从 `docs/architecture.md` 和当前证据审计填写，状态只能等于现有证据支持的最低等级。历史 smoke、不同认证 artifact、Target 文档和本计划不得提升状态。

- [x] **Step 3：记录逻辑所有权目标和评审清单**

  在 backend target blueprint 增加 Platform/Capability 所有权、特殊归属和禁止跨能力 Store/表/UI 状态访问；保持 `state: Draft` 和 `document_role: Target`。在 `CONTRIBUTING.md` 以英文增加变更评审字段：primary capability、production reason for new interface、background lifecycle、migration/recovery、navigation task、real-boundary evidence、rollback；不创建 GitHub 专属模板。

- [x] **Step 4：实现检查器并接入跨系统测试说明**

  检查器解析真实表格并验证枚举、唯一 ID、必要链接、证据范围、artifact SHA-256 形状、blocker 一致性和本地链接。脚本兼容 Windows PowerShell 5.1 与 PowerShell 7，不依赖额外模块，不修改文档。

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/docs/tests/Test-CapabilityMaturity.Tests.ps1
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/docs/Test-CapabilityMaturity.ps1
  ```

  Expected: GREEN；真实台账包含 15 个唯一 ID，未闭环项保持明确 blocker。

## Task 2：建立可重复复杂性基线与预算门禁

**Files:**

- Create: `tests/complexity/Measure-Complexity.ps1`
- Create: `tests/complexity/Test-ComplexityBudget.ps1`
- Create: `tests/complexity/tests/Test-ComplexityScripts.Tests.ps1`
- Modify: `tests/README.md`
- Modify: `README.md`
- Modify: `docs/test.md`

- [x] **Step 1：先写合成仓库夹具测试**

  覆盖生产文件数、产品项目数、Admin Feature 数、测试文件数、Bootstrap 注册文件行数、项目引用、根 Provider 创建点、成熟度差距和存量例外计数；路径缺失、解析失败或未知字段必须 fail closed。

- [x] **Step 2：实现只读复杂性测量器**

  `Measure-Complexity.ps1` 输出稳定 JSON 到 stdout，不写仓库。固定包含 `measuredAtUtc` 之外的可比较字段，并支持 `-RepositoryRoot` 供夹具测试。文件统计排除 `bin/`、`obj/`、`node_modules/`、`.pnpm-store/` 和 `7dtd-reference/`。

- [x] **Step 3：实现预算门禁**

  首个基线锁定八个产品项目、一个后端测试项目、一个 Bootstrap 根 Provider、六个一级导航任务域和当前已知跨层例外。文件总数和 Feature 总数作为趋势指标，不因单纯增长直接失败；项目数、根 Provider、未声明项目引用、未知 Capability、未说明新公共组件/接口和存量例外增加必须失败。

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/tests/Test-ComplexityScripts.Tests.ps1
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/Test-ComplexityBudget.ps1
  ```

- [x] **Step 4：同步聚合入口和测试策略**

  `README.md` 只增加一个稳定仓库级治理检查入口；`tests/README.md` 拥有精确脚本参数；`docs/test.md` 记录指标含义、趋势和发布适用性，不复制命令块。

## Task 3：统一候选 artifact 身份与真实证据 manifest

**Files:**

- Create: `backend/scripts/Get-ReleaseArtifactIdentity.ps1`
- Create: `backend/scripts/New-EvidenceManifest.ps1`
- Modify: `backend/scripts/Test-ReleaseArtifact.ps1`
- Modify: `backend/scripts/Test-ReleaseSmoke.ps1`
- Create: `backend/scripts/tests/Test-EvidenceManifest.Tests.ps1`
- Modify: `backend/scripts/tests/Test-ReleaseArtifact.Tests.ps1`
- Modify: `backend/scripts/tests/Test-ReleaseSmoke.Tests.ps1`
- Modify: `backend/scripts/README.md`
- Modify: `docs/test.md`

- [x] **Step 1：写 artifact identity 和 manifest 的失败测试**

  合成目录覆盖排序无关、内容/路径/大小变化、reparse point、根路径拒绝、缺失 commit、dirty tree、未知 evidence kind、无 artifact、非法 SHA-256、`skipped`、失败步骤、凭据脱敏和 UTF-8 无 BOM。测试不得访问真实服务器。

- [x] **Step 2：实现确定性 artifact identity**

  对 release validator 已批准的文件集合计算每文件 SHA-256，再按规范相对路径、长度和 hash 计算整体 SHA-256。输出只包含相对路径，不包含本机绝对路径；拒绝根目录、reparse point 和清单外文件。

- [x] **Step 3：实现统一 `manifest.json`**

  固定字段包含 schema version、evidence kind、UTC、Git commit、dirty 状态、artifact SHA-256、产品/游戏/OS/浏览器版本、环境 ID、执行范围和子证据相对路径。默认只记录环境标识的非敏感摘要；候选发布检查拒绝 dirty、缺 artifact 或缺版本字段，开发 smoke 允许记录但不能提升成熟度。

- [x] **Step 4：接入现有发布 smoke**

  保留 `summary.json` 的步骤状态；在指定 `-EvidenceDirectory` 时同时生成 manifest，并在每一步完成后保持失败可审计。`Test-ReleaseArtifact.ps1` 输出或返回同一个 artifact identity，不能出现两套 hash 算法。

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File backend/scripts/tests/Test-EvidenceManifest.Tests.ps1
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File backend/scripts/tests/Test-ReleaseArtifact.Tests.ps1
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File backend/scripts/tests/Test-ReleaseSmoke.Tests.ps1
  ```

- [x] **Step 5：更新脚本文档和测试证据合同**

  明确 manifest 与 summary 的区别、候选冻结规则、敏感信息禁止项、失败/跳过语义和 Windows PowerShell 5.1/PowerShell 7 使用方式。

## Task 4：为单一后端测试项目增加能力与边界检索

**Files:**

- Modify: `backend/tests/LSTY.SevenDPanel.Tests/**/*.cs`
- Create: `tests/complexity/Test-BackendTestTaxonomy.ps1`
- Create: `tests/complexity/tests/Test-BackendTestTaxonomy.Tests.ps1`
- Modify: `backend/README.md`
- Modify: `docs/test.md`

当前源码审计与夹具已经存在并通过，但真实 `dotnet test --filter` 受默认游戏引用缺失阻塞，因此 Step 3 保持未完成。

- [x] **Step 1：定义有限 trait 词汇并写失败审计**

  每个包含 `[Fact]`/`[Theory]` 的测试类必须恰好有一个 class-level `[Trait("Capability", "...")]`，值只能是 `Platform`、`Operations`、`Players`、`Community`、`Economy`、`Automation`、`Administration`；至少有一个 `[Trait("Boundary", "...")]`，值只能是 `Domain`、`Application`、`Persistence`、`Local`、`SevenDays`、`Web`、`Bootstrap`、`CrossSystem`。多边界测试可以有多个 Boundary，但仍只有一个主要 Capability 所有者。

- [x] **Step 2：机械标记全部现有测试类，不移动文件**

  按主要变化原因分配 Capability；`DependencyRulesTests`、组合根、Hosting、migration bootstrap 和发布布局归 Platform。混合测试不能用 `Shared`/`Common` 逃避所有权，必要时拆分类但不重写行为。

- [ ] **Step 3：实现源码审计并验证实际过滤**

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/tests/Test-BackendTestTaxonomy.Tests.ps1
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/Test-BackendTestTaxonomy.ps1
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --no-restore --filter "Capability=Players&Boundary=Application"
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --no-restore --filter "Capability=Operations"
  ```

  Expected: 审计通过；两个过滤命令都发现并执行非零测试，过滤不能静默变成全量或零测试。

- [x] **Step 4：记录聚焦测试使用规则**

  `backend/README.md` 拥有 trait/filter 命令；`docs/test.md` 只定义何时跑 Capability、Boundary、聚合和真实门禁。新测试无 trait 必须被治理检查拒绝。

## Task 5：统一用户可见状态语言而不合并领域状态机

**Files:**

- Create: `frontend/apps/admin/src/shared/model/operationStatus.ts`
- Create: `frontend/apps/admin/src/shared/model/operationStatus.test.ts`
- Modify: `frontend/apps/admin/src/features/backups/**`
- Modify: `frontend/apps/admin/src/features/automation/**`
- Modify: `frontend/apps/admin/src/features/world-tools/**`
- Modify: `frontend/apps/admin/src/features/player-profile/**`
- Modify: `frontend/apps/admin/src/features/server-operations/**`
- Modify: `frontend/apps/admin/src/app/i18n/locales/zh-CN.json`
- Modify: `frontend/apps/admin/src/app/i18n/locales/en.json`
- Modify: `docs/design.md`

- [x] **Step 1：写基础状态和安全扩展映射测试**

  覆盖 `queued/running/succeeded/failed/cancelled`，以及不得折叠的 `interrupted/result-unknown/rollback-failed/unavailable`。未知后端状态必须显示明确未知并记录协议错误，不能默认为成功或失败。

- [x] **Step 2：实现纯展示投影**

  `operationStatus.ts` 只返回稳定语义、i18n key、tone 和 terminal/safe-to-retry 元数据，不导入 Feature、不保存状态、不统一后端 enum。至少由 backups、automation、world-tools、player-profile、server-operations 五个真实消费者使用。

- [x] **Step 3：替换重复用户文案和 badge 映射**

  保留每个 Feature 的内部状态和错误码；查询状态 `loading/empty/fresh/stale/partial/failed/forbidden` 不与长操作状态混用。危险状态不使用成功色或自动重试。

  ```powershell
  Set-Location frontend/apps/admin
  pnpm exec vitest run src/shared/model/operationStatus.test.ts src/features/backups src/features/automation src/features/world-tools src/features/player-profile src/features/server-operations src/app/i18n/messages.test.ts
  pnpm typecheck
  ```

- [x] **Step 4：提升当前交互规则**

  `docs/design.md` 记录已经实现的统一词汇、查询/操作状态区别和危险扩展语义；不把尚未完成的 J1/J3 流程写成 Current。

## Task 6：完成 J1 服务器日常运维与可恢复重启

**Files:**

- Create: `backend/src/Core/LSTY.SevenDPanel.Application/ServerOperations/ServerOperationSnapshot.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/ServerOperations/IServerOperationStore.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/ServerOperations/GetServerOperationUseCase.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/ServerOperations/ReconcileServerOperationsUseCase.cs`
- Modify: `backend/src/Core/LSTY.SevenDPanel.Application/ServerOperations/RestartServerUseCase.cs`
- Modify: `backend/src/Core/LSTY.SevenDPanel.Application/ServerOperations/ShutdownServerUseCase.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Migrations/019_ServerOperationLifecycle.sql`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqliteServerOperationStore.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ServerOperationsController.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ServerOperationHttpModels.cs`
- Modify: `backend/src/Bootstrap/LSTY.SevenDPanel/Runtime/**`
- Modify: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/ServerOperationsTests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/ServerOperationsHttpTests.cs`
- Create: `backend/tests/LSTY.SevenDPanel.Tests/Persistence/SqliteServerOperationStoreTests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/ModHostTests.cs`
- Modify: `frontend/apps/admin/src/features/server-operations/**`
- Modify: `frontend/apps/admin/e2e/admin-routes.spec.ts`

- [x] **Step 1：先固定持久 operation 状态机**

  测试 `queued -> running -> succeeded|failed|cancelled|result-unknown` 的合法转换、CAS、防止终态覆盖、UTC、actor、operation kind、origin process instance、completion deadline、failure code 和重启恢复。HTTP 202 只表示已接受；脚本启动只进入 `running`。

- [x] **Step 2：增加 migration 和原子 Store**

  migration 019 新建专用 lifecycle 表，不修改既有 audit 表的历史语义；从空库和 018 基线执行，重复 migration 幂等由 DbUp journal 保证。Store 在同一业务调用中先持久 intent/lifecycle，再执行副作用；audit 写入失败继续按现有 fail-closed 合同处理。

- [x] **Step 3：实现当前进程身份与启动协调**

  Bootstrap 每次进程启动生成不可预测但非敏感的 instance ID。新进程只有在 origin 不同、处于 completion window 且 `GameReady` 后才把 restart/shutdown 标为 `succeeded`；同一进程、超时、新进程未 ready 或证据不足保持 `running`/转 `result-unknown`。不得仅因 health HTTP 可达推断游戏就绪。

- [x] **Step 4：增加查询合同和 OpenAPI**

  `GET /api/v1/server-operations/{operationId}` 只允许已认证角色读取其获准字段，返回稳定状态、时间、kind、failure code 和 audit status，不返回脚本路径、参数、进程 ID 或主机路径。未知 ID 404，状态源不可用 503。

- [x] **Step 5：让 Admin 跨刷新恢复 operation**

  202 后把稳定 operation ID 写入受控 query，轮询到终态；刷新、登录返回和短暂断线继续恢复。显示“脚本已启动/服务器正在重启”而不是成功；只有查询得到 `succeeded` 才显示完成。退出当前页面或 AbortController 取消轮询不取消服务器动作。

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --no-restore --filter "Capability=Operations&Boundary!=CrossSystem"
  Set-Location frontend/apps/admin
  pnpm api:schema
  pnpm api:gen
  pnpm exec vitest run src/features/server-operations
  pnpm typecheck
  ```

- [x] **Step 6：补 mock 浏览器 J1**

  覆盖确认、202、刷新恢复、running、succeeded、failed、result-unknown、401/403、SSE 断开和 `390x844`。本任务不运行真实重启，真实边界由 Task 15 使用冻结 artifact 执行。

## Task 7：完成 J2 玩家发现、处置与审计

**Files:**

- Modify: `backend/tests/LSTY.SevenDPanel.Tests/OnlinePlayerProjectionRuntimeTests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/PlayerHistoryRuntimeTests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/PlayerHistoryWriteServiceTests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/KickPlayerUseCaseTests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/PlayerActionsWebContractTests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/PlayerActionRecoveryTests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/SevenDaysPlayerActionsTests.cs`
- Modify: `frontend/apps/admin/src/features/players/**`
- Modify: `frontend/apps/admin/src/features/player-profile/**`
- Modify: `frontend/apps/admin/src/features/player-map/**`
- Create: `frontend/apps/admin/e2e/admin-player-journey.spec.ts`
- Create: `tests/journeys/Test-PlayerJourney.ps1`
- Create: `tests/journeys/tests/Test-PlayerJourney.Tests.ps1`
- Modify: `tests/README.md`

- [x] **Step 1：审计 J2 合同并只补缺失自动化**

  固定 Join/Save/Disconnect、31 字段同次不可变复制、逐玩家 observation、有效 EOS 身份、历史 gap/排空、固定 kick target、single-flight、游戏主线程复验、HTTP Problem Details、SQLite audit 终态。已有层级证明的行为不在别层重复测试。

- [x] **Step 2：实现必要修复但不扩大玩家能力面**

  只修复审计发现的字段来源、终态、恢复、角色裁剪或浏览器状态缺口；不新增玩家动作、导出、身份合并或新页面。任何生产改动必须沿 Application -> SevenDays/SQLite -> Web -> Admin 完成闭环。

- [x] **Step 3：增加完整 mock 浏览器旅程**

  `Owner` 覆盖在线列表、详情、历史、地图定位、固定目标确认、踢出 accepted/terminal 和审计跳转；Admin/Viewer/未认证覆盖入口、字段和动作边界；桌面与 `390x844` 无遮挡或根级溢出。

- [x] **Step 4：实现受控真实玩家检查器**

  脚本只接受显式 `-ExpectedCrossplatformId`、`-EnvironmentId`、`-EvidenceDirectory` 和 `-ConfirmKickTestPlayer`；先查询在线列表并精确匹配稳定身份，歧义/不匹配立即停止，不使用名称或 entity ID 猜测。脚本调用真实 API、轮询断开和审计，输出脱敏步骤证据；没有已连接受控测试玩家时报告 `skipped`/Blocked，不能改选其他玩家。

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/journeys/tests/Test-PlayerJourney.Tests.ps1
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --no-restore --filter "Capability=Players"
  Set-Location frontend/apps/admin
  pnpm exec vitest run src/features/players src/features/player-profile src/features/player-map
  pnpm typecheck
  ```

- [x] **Step 5：保留真实执行到 Task 15**

  本任务只用 stub 验证脚本安全门和证据格式，不对任何真实玩家执行踢出。

## Task 8：关闭 `CAP-04` 至 `CAP-07` 的 P0 自动化与浏览器缺口

**Files:**

- Modify: `backend/tests/LSTY.SevenDPanel.Tests/**/*Automation*Tests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/**/*Authentication*Tests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/**/*ServerConfiguration*Tests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/**/*Mod*Tests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/**/*Chat*Tests.cs`
- Modify: `frontend/apps/admin/e2e/admin-owner-waves.spec.ts`
- Modify: `frontend/apps/admin/e2e/admin-auth.spec.ts`
- Modify: `frontend/apps/admin/e2e/chat-mutes.spec.ts`
- Modify: `frontend/apps/admin/src/features/automation/**`
- Modify: `frontend/apps/admin/src/features/auth/**`
- Modify: `frontend/apps/admin/src/features/server-configuration/**`
- Modify: `frontend/apps/admin/src/features/mods/**`
- Modify: `frontend/apps/admin/src/features/game-chat/**`

- [x] **Step 1：从已知缺口生成最小 P0 矩阵**

  `CAP-04` 固定公告、计划、触发、恢复与审计；`CAP-05` 固定当前 Owner/Admin/Viewer、API Key、Token 失效、审计和 Header-only；`CAP-06` 固定配置冲突、敏感字段、Mod 保护/下次启动；`CAP-07` 固定聊天字段、命令绕过、别名热更新、私发/广播、第三方顺序、审计和双语。

- [x] **Step 2：补缺失测试和必要修复**

  不扩张 Discord/GeoIP、经济、世界工具或 P1 动作。跨 Capability 修复拆为独立提交候选并由主代理串行整合；共享 OpenAPI 只刷新一次。

- [x] **Step 3：补 Playwright mock/真实 OWIN 可复用场景**

  Owner/Admin/Viewer、Token 到期、旧/新深链接、配置/Mod 重启提示、聊天实时/历史/设置、中文/英文和 `390x844`。真实 7DTD 副作用通过环境前置条件单独选择，缺少变量时必须 skip 且阻止对应 maturity 提升。

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --no-restore --filter "(Capability=Automation)|(Capability=Administration)|(Capability=Community)"
  Set-Location frontend/apps/admin
  pnpm exec vitest run src/features/automation src/features/auth src/features/server-configuration src/features/mods src/features/game-chat
  pnpm typecheck
  ```

## Task 9：自动化 J3 备份、恢复、重启与核对

**Files:**

- Modify: `backend/src/Core/LSTY.SevenDPanel.Application/Backups/**`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Backups/**`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Restore/**`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Backups/**`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/Application/*Backup*Tests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/Application/StageRestoreTests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/Local/*Backup*Tests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/Local/*Restore*Tests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/Persistence/*Backup*Tests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/Persistence/SqliteRestoreResultMergeStoreTests.cs`
- Modify: `frontend/apps/admin/src/features/backups/**`
- Create: `tests/journeys/Test-RestoreDrill.ps1`
- Create: `tests/journeys/tests/Test-RestoreDrill.Tests.ps1`
- Modify: `tests/README.md`

当前 `Test-RestoreDrill.ps1` 已有隔离路径、确认参数和合成安全 preflight；生产恢复一致性、Admin 完整流程、真实文件时机与破坏性演练尚未闭环，因此以下 Step 保持未完成。

- [ ] **Step 1：固定一致性、时机和恢复状态 RED**

  覆盖世界保存请求与实际归档顺序、数据库/配置独立类型、manifest/checksum、持续写入、pending 持久化、世界打开前 timing gate、真实文件占用、进程中断、再次启动续作、安全副本、回滚失败和 receipt merge。`result-unknown`、`interrupted`、`rollback-failed` 不能转为成功。

- [ ] **Step 2：补齐最小生产修复**

  如果当前代码不能证明在线保存一致性，优先实现明确维护窗口或平台快照策略，不引入通用快照框架。所有文件路径来自批准存储根和 backup ID；HTTP/浏览器不能提交路径。

- [ ] **Step 3：强化 Admin 恢复流程**

  恢复对话框显示类型、校验、目标、停机与下一次启动语义；强确认后恢复稳定 job/operation ID，刷新继续查询。202、关服、下次启动、恢复应用、游戏就绪和结果核对分别显示，不用 Toast 代替终态。

- [ ] **Step 4：实现破坏性演练安全编排**

  `Test-RestoreDrill.ps1` 必须要求显式隔离 server root、预期 world name、environment ID、证据目录、backup ID 或创建策略，以及 `-ConfirmDestructiveRestoreDrill`。解析后的 server/data/artifact 目录必须位于明确测试根，拒绝盘符根、仓库根、用户目录、生产标记、reparse point 和不匹配世界。执行前生成 preflight，不满足备份、回滚目标、空间、凭据或健康状态时停止。

  演练顺序固定为：创建/选择备份 -> 校验 -> 写入可复验世界/文件 hash 清单 -> stage restore -> 观察安全关服 -> 新进程 world-open 前 apply -> health/game-ready -> hash/世界哨兵/数据库/配置/audit 核对 -> 回滚测试。每步写入统一 manifest 子证据。

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/journeys/tests/Test-RestoreDrill.Tests.ps1
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --no-restore --filter "(Capability=Operations&Boundary=Application)|(Capability=Operations&Boundary=Local)|(Capability=Operations&Boundary=Persistence)"
  Set-Location frontend/apps/admin
  pnpm exec vitest run src/features/backups
  pnpm typecheck
  ```

- [ ] **Step 5：真实恢复只在 Task 15 执行**

  本任务只跑临时文件和 stub 进程；没有隔离世界和回滚目标时不得测试真实恢复。

## Task 10：移除 Hosting 对 Application 的依赖

**Files:**

- Move: `backend/src/Runtime/LSTY.SevenDPanel.Hosting/Platform/*.cs` -> `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/Platform/*.cs`
- Move: `backend/src/Runtime/LSTY.SevenDPanel.Hosting/ServerOperations/RestartScriptLauncher.cs` -> `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/ServerOperations/RestartScriptLauncher.cs`
- Modify: `backend/src/Runtime/LSTY.SevenDPanel.Hosting/LSTY.SevenDPanel.Hosting.csproj`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Local/LSTY.SevenDPanel.Adapters.Local.csproj`
- Modify: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/HostOverviewQueryTests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/ServerOperationsTests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/DependencyInjectionTests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/DependencyRulesTests.cs`

- [x] **Step 1：先把目标依赖写成失败测试**

  `DependencyRulesTests` 断言 Hosting `.csproj` 无产品 ProjectReference、Hosting 源码不引用 Application/Adapters、Local 可以引用 Application/Domain/最小 Hosting 契约、Bootstrap 仍是唯一具体组装者。测试先因当前 Hosting -> Application 失败。

- [x] **Step 2：移动完整平台实现簇**

  将 Host CPU/Memory/Storage、Windows/Linux platform adapter、device identity、public address resolver 和 `HostOverviewQuery` 一起移动到 Local/Platform，避免把一个内聚实现拆在两项目。更新 namespace、using、测试和 DI，不改变返回 DTO、采样时间、失败语义或配置。

- [x] **Step 3：移动重启脚本外部边界**

  `RestartScriptOptions` 可以保留为 Hosting 配置合同；具体进程启动移到 Local/ServerOperations。Local 增加对 Hosting 的最小 ProjectReference；不把 `ProcessStartInfo`、路径或脚本参数暴露给 Application/Web。

- [x] **Step 4：删除 Hosting -> Application 并运行边界验证**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --no-restore --filter "(Capability=Platform&Boundary=Application)|(Capability=Platform&Boundary=Local)|(Capability=Platform&Boundary=Bootstrap)"
  dotnet build backend/7DPanel.sln --configuration Release --no-restore
  ```

  Expected: Hosting 无产品引用；八个产品项目不变；发布物仍含同名八个产品 DLL。

## Task 11：补齐验证环境并关闭自动化基线（M1、M2、M9）

**Files:**

- Inspect only: `7dtd-reference/v3.0.1-b4/**`
- Inspect: `backend/Directory.Build.props`
- Modify only if the stable environment contract changes: `backend/README.md`
- Modify only if the stable browser contract changes: `frontend/apps/admin/README.md`
- Modify after evidence exists: `docs/test.md`

- [ ] **Step 1：完成环境 preflight**

  提供完整的只读 `v3.0.1-b4` 引用或显式 `SevenDaysReferenceRoot`，验证全部必需程序集；安装与锁文件匹配的 Playwright Chromium；盘点 Windows/Linux 隔离实例、端口、世界、数据目录、测试账号、受控玩家、证据根和回滚目标。不得修改或提交私有引用、浏览器缓存、凭据和机器路径。

- [ ] **Step 2：关闭 Task 4 Step 3**

  运行 taxonomy 夹具和真实源码审计，再执行 Players/Application 与 Operations 两个实际 filter；必须分别发现非零且非全量测试。通过后同步勾选 Task 4 Step 3。

- [ ] **Step 3：运行后端聚合基线**

  按根 README 的聚合入口完成 restore、Release build 和 test。失败先按 Capability/Boundary 定位，只修复当前发布阻塞；稳定后重新运行一次完整聚合并记录准确测试数、版本和环境。

- [ ] **Step 4：复验 Admin、治理和脚本基线**

  运行 OpenAPI drift、lint、typecheck、unit、build、复杂性、成熟度、artifact/manifest 夹具和 J2/J3 安全脚本夹具。Playwright discovery 或 skip 不能替代 Chromium 执行结果。

- [ ] **Step 5：记录自动化基线结论**

  只把实际运行结果写入 `docs/test.md`；缺环境或失败保持 `Blocked`/`Implemented`。这一阶段不生成候选 artifact，也不提升 `Verified`。

## Task 12：把单一 Bootstrap 注册清单按能力拆分（M3）

**Files:**

- Modify: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- Create: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/Registration/PanelCompositionContext.cs`
- Create: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/Registration/PlatformServiceRegistration.cs`
- Create: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/Registration/OperationsServiceRegistration.cs`
- Create: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/Registration/PlayersServiceRegistration.cs`
- Create: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/Registration/CommunityServiceRegistration.cs`
- Create: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/Registration/EconomyServiceRegistration.cs`
- Create: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/Registration/AutomationServiceRegistration.cs`
- Create: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/Registration/AdministrationServiceRegistration.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/DependencyInjectionTests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/DependencyRulesTests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/Bootstrap/**`

- [ ] **Step 1：锁定对象图和生命周期 RED/characterization**

  测试所有生产 service 可解析、singleton/scope 身份、Web request scope、所有 runtime 数量、启动顺序、失败逆序回滚、停止排空、Provider 最后释放、根 Provider 唯一和不允许注册阶段解析服务。

- [ ] **Step 2：建立无接口的 composition context**

  `PanelCompositionContext` 只保存 options、已解析的 mod/data/admin 路径和显式外部输入；不持有 Provider、不提供 Service Locator、不启动资源。它由唯一 factory 创建并传给七个 registration 文件。

- [ ] **Step 3：机械迁移注册，保持行为**

  按 Platform 与六 Capability 移动 `AddSingleton/AddScoped` 和显式 factory；跨能力 bridge 由消费方 registration 拥有并只依赖公开端口。每迁移一组运行对应 Capability + Bootstrap 测试，避免一次移动 1433 行后才定位错误。

- [ ] **Step 4：收紧组合根门禁**

  `PanelServiceProviderFactory` 只负责 context、顺序调用 registration、`BuildServiceProvider`、Validate 和返回 runtime。注册文件不得调用 `BuildServiceProvider`、`GetService` 触发启动、`Task.Run`、线程、Timer 或静态容器。

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --no-restore --filter "Boundary=Bootstrap"
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/Test-ComplexityBudget.ps1
  ```

## Task 13：收口三个核心旅程的用户任务体验（M6）

**Files:**

- Modify: `frontend/apps/admin/src/app/navigation/**`
- Modify: `frontend/apps/admin/src/app/router/**`
- Modify: `frontend/apps/admin/src/features/server-operations/**`
- Modify: `frontend/apps/admin/src/features/players/**`
- Modify: `frontend/apps/admin/src/features/backups/**`
- Modify: `frontend/apps/admin/e2e/**`
- Modify after implementation: `docs/design.md`

- [x] **Step 1：审计渐进披露与入口裁剪**

  以 J1、J2、J3 为边界，检查高频/高级/危险操作、角色与模块状态、导航和上下文入口；先写失败测试，不新增一级导航、页面或 API。

- [x] **Step 2：完成最小任务连续性修复**

  只修复角色或模块无关入口、技术页面跳转、高风险确认、刷新/断线恢复和三套权限概念混淆；保持六域导航和现有 Feature 所有权。

- [x] **Step 3：执行桌面与窄屏浏览器验证**

  2026-08-04 使用单一 Playwright 进程和单 worker 运行 Chromium mock desktop 与 `390x844`，共 `174` 个场景，`172` 个通过、`2` 个按项目条件跳过。覆盖 Owner/Admin/Viewer、模块启停、桌面与窄屏、上下文入口、根级溢出和危险操作不自动重放；跳过项仅为桌面项目中的移动专属场景，不计作浏览器失败。真实 OWIN、7DTD 和危险恢复仍由后续候选任务证明。

- [x] **Step 4：提升当前交互事实**

  已将本地 mock 浏览器证据和当前交互边界更新至 `docs/design.md`；真实游戏结果仍由后续候选任务证明。

## Task 14：稳定自动化聚合门禁并生成候选 artifact（M7）

**Files:**

- Modify only as failures prove necessary: `backend/**`, `frontend/apps/admin/**`, `backend/scripts/**`, `tests/**`
- Modify: `docs/test.md`

已勾选的 Step 1、3 是 `0c45c0a` 开发基线，不是未来候选 artifact 的最终证据。Task 9、12、13 改变代码后必须按 Step 4 重新运行当前聚合。

- [x] **Step 1：运行治理和脚本门禁**

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/docs/Test-CapabilityMaturity.ps1
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/Test-ComplexityBudget.ps1
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/Test-BackendTestTaxonomy.ps1
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File backend/scripts/tests/Test-EvidenceManifest.Tests.ps1
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File backend/scripts/tests/Test-ReleaseArtifact.Tests.ps1
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File backend/scripts/tests/Test-ReleaseSmoke.Tests.ps1
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/journeys/tests/Test-PlayerJourney.Tests.ps1
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/journeys/tests/Test-RestoreDrill.Tests.ps1
  ```

- [ ] **Step 2：运行后端聚合一次**

  ```powershell
  dotnet restore backend/7DPanel.sln
  dotnet build backend/7DPanel.sln --configuration Release --no-restore
  dotnet test backend/7DPanel.sln --configuration Release --no-build --no-restore
  ```

  任何失败先定位并修复根因；不通过重复运行隐藏不稳定。记录总数、耗时和 warning。

- [x] **Step 3：运行 Admin 聚合一次并清零既有发布阻塞**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm lint
  pnpm typecheck
  pnpm test
  pnpm build
  pnpm api:check
  ```

  保持已经清零的 lint 与 Vitest worker/外部资源噪声不回归；不能通过新增豁免宣称 Release-ready。API check 后 tracked OpenAPI/SDK 必须无漂移。

  2026-08-03 实际结果：`pnpm lint`、`pnpm typecheck`、`pnpm test`（`138/138` 文件、`956/956` 项）、`pnpm build` 和 `pnpm api:check` 均通过。后端聚合仍因只读 `7dtd-reference` 缺少 `0_TFP_Harmony/0Harmony.dll` 与游戏 `Newtonsoft.Json.dll` 阻塞。

  2026-08-04 复验结果：`pnpm lint`、`pnpm typecheck`、`pnpm test`（`141/141` 文件、`963/963` 项）、串行 `pnpm build` 和 `pnpm api:check` 均通过；本次 Task 13 变更的定向 Vitest 为 `6` 文件、`20` 项。首次并行启动 build/typecheck 时 API 生成目录正在重建，出现的 generated import 失败不计入结果；生成完成后串行复验通过。

- [ ] **Step 4：运行冻结前当前聚合**

  Admin、OpenAPI、治理、脚本和安全聚合已复验通过；后端聚合仍在缺失只读 7DTD 引用处阻塞，Task 9、12 的真实边界也未执行，因此本步骤保持未完成。失败必须修复根因并重新运行受影响聚焦测试及一次最终聚合。

- [ ] **Step 5：生成并验证候选 artifact**

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File backend/scripts/Publish-Mod.ps1 -PublishDirectory <CandidateArtifactDirectory>
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File backend/scripts/Test-ReleaseArtifact.ps1 -ArtifactPath <CandidateArtifactDirectory>
  ```

  从 clean commit 记录整体 SHA-256，确认八项目、双 RID native、Admin、配置示例、禁止项和脱敏 preflight。此时不提升 `Verified`，只把冻结 artifact 作为 Task 15、16 输入。

## Task 15：冻结候选 artifact 并执行 P0 真实环境闭环（M4、M5、M8、M9、M10）

**Files:**

- Evidence only: ignored external evidence root
- Modify after evidence review: `docs/test.md`
- Modify only for proven defects: owning Capability files, followed by a new artifact/hash and complete affected rerun

- [ ] **Step 1：执行候选 preflight**

  确认 clean commit、冻结 artifact SHA-256、Windows/Linux 隔离实例、不同端口/世界/数据目录、Owner/Admin/Viewer、受控玩家、浏览器、磁盘空间、备份和回滚目标。没有任一前置条件时对应 lane 为 Blocked，不借用生产实例。

- [ ] **Step 2：并行执行三个只读或受控 lane**

  - Lane A / Windows Operations：`Test-ReleaseSmoke.ps1 -EvidenceDirectory`，当前认证/Swagger、启停、重复启动、J1 operation 查询、日志和程序集扫描。
  - Lane B / Players + Browser：连接受控玩家，执行真实 OWIN Playwright 桌面/`390x844`、J2 查询/历史/地图/踢出/审计；踢出只由显式安全脚本执行一次。
  - Lane C / P0 shared：公告/自动化最小真实触发、配置/Mod 重启生效、聊天/命令/别名/第三方顺序、Owner/Admin/Viewer、中文/英文和 Token 失效。

  每个 lane 使用独立实例或串行使用共享实例；不能让并发动作污染 observation、日志、审计或世界。

- [ ] **Step 3：执行 Linux 候选 smoke**

  在区分大小写文件系统的官方 Linux `v3.0.1-b4` Mono 进程中运行相同 artifact validator、Mod 加载、SQLite native、认证/Swagger、启停、重启脚本、配置/Mod 文件语义、聊天最小矩阵和日志归档。环境启动方式记录在 evidence，不写入产品配置，也不让浏览器提交命令。

- [ ] **Step 4：建立 Windows/Linux 性能阈值**

  空载和典型负载记录主线程任务耗时、帧预算、队列深度、拒绝/丢弃、日志突发与并发管理请求。先测量再在 `docs/test.md` 写入阈值；任何超标先修复或明确阻塞，不能用未定义阈值放行。

- [ ] **Step 5：串行执行 J3 恢复和回滚**

  在 Windows 和 Linux 各运行一次 `Test-RestoreDrill.ps1`。Windows/Linux 可以并行，但各自实例必须独立；同一平台的正常恢复、文件占用、磁盘失败、中断续作和回滚失败按顺序执行。成功证据包含备份 hash、恢复文件 hash、世界哨兵、数据库/配置、审计和新进程 game-ready。

- [ ] **Step 6：执行候选回滚演练**

  恢复上一份已验证 artifact 和匹配数据备份，验证 health、认证、game-ready、schema/version 和 Admin；再恢复候选 artifact 验证前向可恢复。任何不可逆 migration 或数据损坏立即停止发布。

- [ ] **Step 7：审查所有 manifest**

  每份证据必须绑定同一候选 hash 或明确说明 lane artifact；dirty、missing、failed、skipped、版本不符、敏感信息、无退出码或断链均不能计为通过。代码修复后旧证据失效，从 Task 14 Step 5 重新生成 artifact 并只重跑受影响边界和最终候选聚合。

## Task 16：扩大 P1 与外部服务验收（O1、O2）

**Files:**

- Evidence only: ignored external evidence root
- Modify after evidence review: `docs/test.md`
- Modify only for proven shared defects: owning Capability files and affected candidate rerun

- [ ] **Step 1：固定扩大验收矩阵**

  从 `CAP-08` 至 `CAP-12` 的现有合同和 `docs/test.md` blocker 选择真实边界；不新增功能、页面、API 或新的 P0 门槛。

- [ ] **Step 2：执行玩家资源与经济 lane**

  验证真实游戏字段、资源目录、玩家档案、物品/奖励/经济幂等发放和补偿；所有副作用只使用受控账号与可销毁数据。

- [ ] **Step 3：执行社区高级能力与世界工具 lane**

  验证传送、投票、城市和世界工具的适用真实副作用、备份、取消、回滚及结果未知；危险动作必须使用独立实例和显式确认。

- [ ] **Step 4：执行 Discord 与 GeoIP lane**

  在 sandbox 或受控服务验证连接、交互、Secret 轮换、限流、代理、MaxMind 数据、失败策略和恢复；凭据不进入仓库或证据。

- [ ] **Step 5：形成增强目标结论**

  分别输出 O1、O2 的 Passed、Blocked 或 Failed。除非发现共享安全、数据损坏、恢复或运行时风险，否则增强目标不改变 P0 发布结论。

## Task 17：接入私有 CI 与受控 Runner（O4）

**Files:**

- Create: `backend/scripts/Invoke-CandidateValidation.ps1`
- Create: `backend/scripts/tests/Test-CandidateValidation.Tests.ps1`
- Modify: `backend/scripts/README.md`
- Modify if a provider is configured: provider-specific private workflow
- Modify after execution evidence: `docs/test.md`

- [x] **Step 1：确认 CI 提供者与 Runner 边界**

  已审计当前仓库和环境：没有已配置 CI 提供者、受控 Runner、Secret 模型、并发锁或 evidence retention；O4 保持 `Blocked`，并继续完成 provider-neutral 编排。

- [x] **Step 2：实现单一候选验证编排**

  `Invoke-CandidateValidation.ps1` 只调用现有自动化、publish、artifact、真实 lane 和 manifest 入口，不复制判断逻辑；支持本地人工运行和私有 Runner，默认拒绝缺引用、缺版本、dirty 或共享实例。夹具已在 Windows PowerShell 5.1 与 PowerShell 7 通过。

- [ ] **Step 3：增加最小私有 workflow**

  仅在提供者和 Secret 模型明确时创建手动或受保护分支触发的最小 workflow，使用受控 Runner、并发锁、脱敏日志和短期证据保留；不把私有程序集或凭据上传为公开 artifact。

- [ ] **Step 4：执行一次 Runner 候选验证**

  证明 Runner 与人工入口产生相同 artifact identity、退出码和 manifest 语义。环境不可用时保留 Blocked 结论，不影响已有 P0 手工证据。

## Task 18：提升 Current 文档并完成发布判定（M11）

**Files:**

- Modify: `docs/design.md`
- Modify: `docs/architecture.md`
- Modify: `docs/architecture/backend-target-blueprint.md`
- Modify: `docs/architecture/admin-frontend-target-blueprint.md` only if target remains
- Modify: `docs/test.md`
- Modify: `README.md` only if aggregate commands changed
- Modify: `backend/README.md` and `backend/scripts/README.md`
- Modify: `frontend/apps/admin/README.md` only if commands/environment changed
- Modify: `CHANGELOG.md` only after actual release
- Update: this plan's checkboxes and current execution status

- [ ] **Step 1：按权威文档提升事实**

  `docs/design.md` 只写已实现旅程和状态；`docs/architecture.md` 写实际 Capability 所有权、Hosting/Bootstrap、migration/API/运行时；`docs/test.md` 写准确命令结果、性能阈值、证据 manifest、maturity 和 blockers。Target blueprint 删除或缩减已经提升的目标，不复制 Current。

- [ ] **Step 2：重新判定所有成熟度行**

  `CAP-01` 至 `CAP-07` 和 J1/J2/J3 只有满足当前 candidate、Windows/Linux、真实 7DTD、Chromium、恢复、性能、安全和回滚适用边界时才标 `Release-ready`。`CAP-08` 至 `CAP-12` 保留真实范围和 blocker；O1、O2、O4 分别判定，不因被纳入本批次或 P0 发布自动升级。

- [ ] **Step 3：运行最终文档与工作树审计**

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/docs/Test-CapabilityMaturity.ps1
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/Test-ComplexityBudget.ps1
  git diff --check
  git status --short
  ```

  检查所有本地链接、无模板占位词或未解释 placeholder、docs 全部简体中文、README/CONTRIBUTING 保持英文、Current/Target/Evidence 无混淆、没有把 plan checkbox 当证据、没有意外修改 `7dtd-reference/` 或 `.pnpm-store/`。

- [ ] **Step 4：形成发布结论**

  输出 `Release-ready`、`Blocked` 或 `Failed` 之一，不允许“基本通过”，并单列 O1、O2、O4 的 Passed、Blocked 或 Failed。Blocked 必须列出责任边界、已有证据、缺失前置条件和下一次可执行动作；Failed 必须保留失败 artifact 和证据。只有实际发布后更新 `CHANGELOG.md`。

## 已完成开发基线

| 基线 | 当前证据 | 解释 |
|---|---|---|
| 治理与脚本 | 成熟度、复杂性、taxonomy、artifact/manifest、J2/J3 安全夹具已通过 | 仍需在最终候选前复验 |
| Admin 聚合 | 138 个测试文件、956/956，API drift、lint、typecheck、build 通过 | 不替代 Chromium 真实执行 |
| Hosting 收口 | Hosting -> Application 已移除，Hosting 单项目构建通过 | 后端聚合仍受私有引用阻塞 |
| 后端聚合 | 未取得有效结果 | 缺默认 `v3.0.1-b4` 关键程序集 |

## 最终验证矩阵

| 目标 | 自动化 | 真实边界 | 发布证据 |
|---|---|---|---|
| M1/M2 环境与基线 | taxonomy、后端/Admin/治理聚合 | 引用、Chromium、双平台实例 | 环境 preflight 与准确测试结果 |
| M3 模块化所有权 | DependencyRules、Bootstrap characterization | 候选组合与关服 | Hosting 无 Application、单一根 Provider |
| M4/M6 J1/J2/J3 | Application/Adapter/Web/Admin/mock | 真实玩家、双平台、Chromium、恢复 | 任务连续性、终态和审计一致 |
| M5/M9 恢复可靠性 | 故障注入、安全与生命周期 | 跨重启恢复、异常终止、回滚 | hash、世界哨兵、审计、停止条件 |
| M7/M8 Candidate | publish、validator、manifest | Windows/Linux/7DTD/Chromium | clean commit、单一冻结 SHA-256 |
| M10 性能 | 采样与阈值检查 | 双平台空载和典型负载 | 可执行阈值与原始测量 |
| O1/O2 扩大验收 | 既有 `CAP-08` 至 `CAP-12` 自动化 | 游戏副作用、Discord/GeoIP | 分项 Passed/Blocked/Failed |
| O4 私有 Runner | 编排夹具 | 私有 Runner 候选执行 | 同一 hash、退出码和 manifest |
| M11 文档与判定 | docs checker、链接、复杂性 | 人工证据审查 | 15 行台账与唯一发布结论 |

## 阶段完成条件

- [ ] M1 至 M11 均完成，或最终结论明确为 `Blocked`/`Failed` 且保留可执行下一步。
- [ ] 15 个台账条目都有唯一所有者、证据范围、maturity 和 blocker/expiry。
- [ ] `CAP-01` 至 `CAP-07`、`NFR-01` 至 `NFR-04`、`NFR-06` 满足候选发布门槛。
- [ ] J1、J2、J3 在同一冻结 artifact 上完成适用 Windows/Linux、真实 7DTD、Chromium、恢复和审计闭环。
- [ ] Hosting 不引用 Application，八个产品项目和单一 Bootstrap 根 Provider 保持不变。
- [ ] Bootstrap 注册可以按 Platform/Capability 定位，启动、失败回滚和停止顺序未回归。
- [ ] 复杂性、测试分类、成熟度和证据 manifest 门禁可重复运行。
- [ ] 后端、Admin、OpenAPI、脚本、publish 和 artifact validator 稳定通过，无靠重跑隐藏的失败。
- [ ] 性能阈值来自 Windows/Linux 官方进程测量并写回权威文档。
- [ ] 候选回滚演练通过，数据和凭据没有不可恢复或泄漏问题。
- [ ] O1、O2、O4 各自有明确结论；未完成增强目标不被误写成 P0 失败，也不隐藏共享风险。
- [ ] Current、Target、Change Record 与 Evidence 没有混淆，最终台账和发布结论可追溯。
