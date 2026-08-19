---
state: Complete
document_role: Outcome-Driven Convergence Objective and Verification Record
last_updated: "2026-08-19"
owner: Coordinator
---

# 2026-08-19 三目标结果驱动收敛

## 判断原则

架构指标只服务三个目标：

1. 用户更容易完成真实任务；
2. 开发者更安全、更快速地修改系统；
3. 运行失败时能够判断发生了什么并恢复。

任何设计如果不能以生产路径和验证证据明显改善至少一个目标，默认怀疑它只提高了架构纯度，不进入实施。删除文件、减少接口和增加抽象都不是独立目标。

本轮只在本地 `main` 执行，不创建或操作远程分支，不推送 `main`。真实 7DTD、OWIN、Linux Mono、恢复 drill、候选 artifact 和外部服务证据不能由本地自动化替代。

## 范围与基线样本

| Stream | 目标 | 样本 | 首要问题 | 状态 |
|---|---|---|---|---|
| User Journey | 用户任务 | 日常运维、玩家发现与处置、备份恢复核对 | 是否存在重复入口、模糊状态或缺少下一步 | completed |
| Developer Path | 开发者修改 | 简单查询、普通配置、危险异步操作 | 是否有同边界重复、事实源同步或难找测试 | completed |
| Failure Recovery | 失败判断与恢复 | 认证/超时、后台 Job、Restore unknown/rollback | 状态、原因、identity、下一步是否关联 | completed |
| Governance | 持续约束 | Review fields、复杂度和文档门禁 | 新设计能否证明改善至少一个目标 | completed |

## 候选准入模板

每个候选必须记录：

- 改善目标：User / Developer / Recovery，可多选；
- 当前生产痛点和完整调用链；
- 当前与目标概念数量或人工同步点；
- 生产消费者和权威事实源；
- 权限、状态、线程、事务、失败、恢复不变量；
- 修改前 characterization 和聚焦测试；
- 可观察成功指标和可执行回滚；
- 最终结论：`completed`、`retained`、`blocked` 或 `rejected`。

## 默认拒绝

- 只为项目、类或方法对称进行的拆分/合并；
- 只为减少接口数量而跨越 Web、SQLite、SevenDays、文件系统、进程或游戏线程边界；
- 用跨层 helper 合并不同信任边界的规范化；
- 新增无生产消费者的 Service、Manager、Registry、事件总线或未来扩展点；
- 通过放宽预算、例外、断言、snapshot 或生成漂移检查制造绿色；
- 把 `202 Accepted`、stale、partial、unknown 或 rollback 状态展示成成功；
- 无法说明改善三个目标中任何一项的重构。

## 不可退化不变量

- Owner/Admin/Viewer 权限、强确认、审计和 actor/correlation identity 不变；
- 游戏线程、持久化、外部服务和浏览器信任边界不被绕过；
- Job 状态、row-version CAS、marker、receipt、safety copy、rollback、`ResultUnknown` 和 startup reconciliation 不变；
- loading、stale、offline、accepted、running、failed、unknown 不被错误折叠；
- OpenAPI 是传输事实源，生成 SDK 和严格 parser 继续承担机械传输与浏览器边界；
- 不修改 migration、API/OpenAPI、generated SDK、公共接口或复杂度阈值，除非候选单独证明必要且获得新的明确批准。

## 并行写集

| Stream | 允许写入 | 禁止写入 | 迭代 | 结果 | 审查 |
|---|---|---|---:|---|---|
| User Journey | 单一 Players Feature/页面及其局部测试 | Job/Restore、OpenAPI、generated SDK、共享导航事实源（未经协调） | 1 | 30 个 User/Recovery UI 测试通过，typecheck/lint 通过 | PASS；本地筛选和 operation receipt |
| Developer Path | 单一调用切片及其聚焦测试 | User/Recovery 已占用文件、物理项目边界 | 1 | server-configuration `2` files/`6` tests、typecheck/lint 通过 | PASS；generated transport |
| Failure Recovery | 单一 operation/job 错误投影及测试 | Restore 安全机制、migration、API 契约（未经协调） | 1 | operation id/receipt wiring；未改状态机 | PASS；低风险 UI 投影 |
| Governance | 本文件、贡献/检查脚本及其 self-test | 生产行为、预算阈值、成熟度台账 | 1 | `CONTRIBUTING.md` outcome evidence checklist 合并 | PASS |

代理先只读调查并返回最多两个候选；协调者选择互斥写集后才授权修改。每条流最多两轮修正，必须经过独立只读审查。若需要扩大为架构设计、迁移、API 变更、公共接口或真实环境，立即标记 `blocked`。

## 成功指标

### User

- 至少一条真实用户路径减少重复入口、理解步骤或模糊反馈；
- 不减少必要授权和危险确认；
- 失败状态显示当前阶段、原因和可执行下一步。

### Developer

- 至少一条真实修改路径减少同边界重复、人工同步点或定位成本；
- 对应入口、边界和测试可直接追踪；
- 不新增无生产消费者的抽象。

### Recovery

- 至少一个失败场景更容易关联状态、error code、job/correlation identity、审计和下一步；
- 不谎报成功，不削弱 retry、rollback 或 unknown 语义。

### Governance

- 非平凡结构变更必须声明至少一个目标及其生产证据；
- 新后台任务和持久状态仍声明 lifecycle、migration、rollback 和 restore；
- 聚焦与聚合门禁通过，真实边界 blocker 保持诚实。

## 基线与代理台账

基线（本地 `main`、目标提交 `3a665ee`）：后端 Release build 通过，`0` errors、`115` warnings；后端聚合测试 `1931/1931`。Admin lint、typecheck、Vitest `145` 文件/`1008/1008`、production build 和 `api:check` 通过。Complexity budget、backend taxonomy（`207` files）、frontend dependency（`409` feature files）和 simplification docs checker 通过；`productionHandwrittenFilesOver500Count=42` 保持既有非阻塞 advisory。真实 7DTD/OWIN/Linux Mono/恢复 drill/候选 artifact/外部服务未执行。

候选选择：

- User：在线玩家本地筛选，减少按姓名、entity id、platform identity 手工扫描列表的成本；不增加请求或改变 kick 权限/新鲜度。
- Developer：服务器配置 transport 改用已生成 SDK，删除 feature 对 URL、Authorization、body 和编码事实的重复维护；保留 runtime parser。
- Recovery：第一次勘察因上游服务过载失败，使用更窄范围重新调查；未取得证据前不修改 Restore/Job。
- Governance：只扩展现有贡献 checklist，要求结构变更声明 User/Developer/Recovery 目标及生产证据或 Blocked；不扩展成熟度台账或增加纯度分数。

## 最终验证与结果

- 后端 Release build：`0` errors、`115` warnings；后端聚合测试：`1931/1931`。
- Admin lint、typecheck、Vitest `145` 文件/`1011/1011`、production build、`api:check` 全部通过。
- Complexity budget、backend taxonomy（`207` files）、frontend dependency（`409` feature files）和简化文档 checker 通过；`productionHandwrittenFilesOver500Count=42` 保持既有非阻塞 advisory。
- User 聚焦测试和 operation receipt 页面测试：`30/30`；server-configuration generated transport：`2` files/`6` tests；最终全量 Admin 已覆盖。
- User 实际改善：在线玩家现在可以在客户端按名称、entity ID、platform ID 筛选，桌面/移动列表共享同一 derived collection，不增加网络请求、不改变排序、kick 权限或刷新机制。
- Recovery 实际改善：首页 restart/shutdown dialog 现在接收已有 operationId，accepted/running 阶段可以显示现有 receipt/追踪标识；未改变 polling、状态机、后端或 OpenAPI。
- Developer 实际改善：server-configuration 不再手写 endpoint、method、Authorization、body JSON 和 path encoding；生成 SDK 成为唯一 transport contract，local parser 和 useServerConfiguration 状态契约保留。
- Governance 实际改善：`CONTRIBUTING.md` 要求非平凡结构变更声明 `User`/`Developer`/`Recovery` 目标、before/after concern 和 CAP/J production evidence 或 `Blocked environment`；没有增加成熟度来源或纯度分数。
- Recovery 状态机本身没有继续改动：Restore marker/receipt/safety/rollback/CAS 保护不变；恢复勘察首次因上游服务过载失败，窄范围 operation receipt 候选已安全完成，剩余深层 recovery 候选继续单独处理。
- 未执行真实 7DTD、OWIN、Linux Mono、候选 artifact、恢复 drill 或外部服务验证；这些 blocker 仍保持诚实记录。
- `git diff --check` 通过；所有提交只在本地 `main`，没有远程 Git 操作。

| ID | Stream | 假设 | 证据 | 决策 | 回滚/复核触发 |
|---|---|---|---|---|---|
| OUT-001 | User Journey | 在线玩家列表需要人工扫描已知玩家，且操作结果缺少同屏追踪标识 | 本地 name/entityId/platform ID filter 不增加请求；首页 dialog 复用已有 operationId/receipt | completed | 后续需要服务器端搜索或导航改造时重新设计 |
| OUT-002 | Developer Path | Server configuration feature 手写 endpoint/header/body/path，和 generated SDK 形成双 transport | wrapper 改调用 `serverConfigurationGet/Put`，保留 parser 与模型状态；`2` files/`6` tests 通过 | completed | OpenAPI/generated contract 改变时复核 |
| OUT-003 | Failure Recovery | 首页 accepted/running 操作已有 receipt UI，但 operationId 未从 composable 传入 | restart/shutdown dialog 现在显示已有 operationId/receipt；未改状态机、polling、backend 或 OpenAPI | completed | receipt/status contract 改变时复核 |
| OUT-004 | Governance | 结构变更容易只说明架构纯度，未声明 User/Developer/Recovery 结果 | `CONTRIBUTING.md` 新增 structural improvement 和 production evidence/Blocked 字段，保留原有 maturity 事实源 | completed | 贡献流程或成熟度台账规则改变时复核 |

## 完成定义

- 四条流均有明确 completed/retained/blocked/rejected 结论；
- 至少一个用户任务、一个开发者修改路径和一个失败判断/恢复场景获得可验证改善，或诚实记录为什么没有安全改动；
- 每项代码变更都有 characterization、聚焦验证和独立审查；
- 后端、Admin、OpenAPI、复杂度、taxonomy、Feature dependency 和文档门禁通过；
- 没有通过扩大架构纯度、放宽门禁或隐藏 blocker 制造完成状态；
- 工作树干净，所有提交只存在于本地 `main`，没有远程 Git 操作。
