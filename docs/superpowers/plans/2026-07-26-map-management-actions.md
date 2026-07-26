---
state: Current
document_role: Change Record
last_updated: "2026-07-26"
---

# 地图管理操作实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在只读地图稳定后，为 Owner 交付可审计的领地删除、在线玩家传送和有界服务端瓦片资源刷新/渲染作业。

**Architecture:** 三类操作使用独立类型化用例；领地删除和传送经现有有界游戏线程调度器，地图作业经独立有界后台队列和持久 operation 状态。浏览器只提交产品级目标，不提交命令或路径，HTTP 接受不代表最终成功。

**Tech Stack:** .NET Framework 4.8、C#、Katana Web API、System.Threading.Channels、Dapper、SQLite、Vue 3、TypeScript、Nuxt UI、Vitest、Playwright。

> 主设计规格：[地图管理操作设计规格](../specs/2026-07-26-map-management-actions-design.md)

---

## 文件结构

- `Application/Maps/Actions/` 分别拥有领地删除、玩家传送和地图作业用例；不创建字符串动作注册表。
- SevenDays Adapter 只实现领地和传送的游戏线程操作；Persistence/Hosting 侧拥有地图作业和状态存储。
- Web Controller 使用不同 request DTO 和路由，避免一个 `{ action, payload }` 万能端点。
- 前端 `features/map-management/` 只拥有 mutation、确认和作业状态；只读 `player-map` 通过稳定目标值发起事件，不直接执行 mutation。

### Task 1: 建立持久地图操作状态

**Files:**

- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Maps/Actions/MapOperation.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Maps/Actions/IMapOperationStore.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Migrations/007_MapOperations.sql`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqliteMapOperationStore.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/SqliteMapOperationStoreTests.cs`

- [ ] **Step 1: 写失败测试**，覆盖 queued/running/succeeded/failed/cancelled/interrupted/result-unknown、合法迁移、终态不可回退、重启读取和错误脱敏。
- [ ] **Step 2: 实现最小 operation 与 Store。** operation 保存 actor、类型、规范目标、时间和终态，不保存 Token、命令或服务器路径。
- [ ] **Step 3: 运行 SQLite 定向测试确认通过。**

### Task 2: 实现固定目标的领地删除

**Files:**

- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Maps/Actions/DeleteLandClaimUseCase.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Maps/SevenDaysLandClaimActions.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/DeleteLandClaimUseCaseTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/SevenDaysLandClaimActionsTests.cs`

- [ ] **Step 1: 写失败测试**，覆盖 Owner、固定 claim ID/所有者/坐标、执行前再次验证、目标消失或替换、dispatcher 拒绝、成功、失败和 result-unknown。
- [ ] **Step 2: 实现类型化端口和用例。**

```csharp
public sealed class DeleteLandClaimRequest
{
    public string ClaimId { get; init; }
    public string OwnerCrossplatformId { get; init; }
    public PlayerPosition Position { get; init; }
}
```

- [ ] **Step 3: Adapter 经现有有界主线程 dispatcher 查找并删除完全匹配目标；不执行控制台命令。**
- [ ] **Step 4: 运行用例和 Adapter 测试确认通过。**

### Task 3: 实现在线玩家传送

**Files:**

- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Maps/Actions/TeleportPlayerUseCase.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Maps/SevenDaysPlayerTeleport.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/TeleportPlayerUseCaseTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/SevenDaysPlayerTeleportTests.cs`

- [ ] **Step 1: 写失败测试**，覆盖有限目标坐标、世界边界、当前 entityId 与 `crossplatformIdentity.combinedId` 双重匹配、玩家离线、身份复用、game not ready、dispatcher 拒绝和真实结果。
- [ ] **Step 2: 实现类型化传送端口。** 已开始执行后取消不能把真实结果替换为 cancelled；无法确认时记录 result-unknown。
- [ ] **Step 3: 运行传送定向测试确认通过。**

### Task 4: 实现有界服务端瓦片资源刷新与渲染作业

**Files:**

- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Maps/Actions/QueueMapRenderUseCase.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/MapTiles/MapRenderJobService.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/MapRenderJobServiceTests.cs`

- [ ] **Step 1: 写失败测试**，覆盖容量满、同世界互斥、服务端资源刷新/已探索/完整三种固定模式、取消、异常隔离、关服排空、重启 interrupted、临时输出校验、原子发布和失败保留旧版本；浏览器 tile source 重载不进入该服务。
- [ ] **Step 2: 实现单消费者有界 Channel。** 作业只接受 world ID、固定模式和受控范围，不接受路径、命令或脚本。
- [ ] **Step 3: 将输出写入服务端决定的临时目录，校验后原子提升地图资源版本；失败清理临时资源并保留旧版本。**
- [ ] **Step 4: 运行作业服务测试确认通过。**

### Task 5: 增加 Owner-only 操作 API

**Files:**

- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/MapActionsController.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/MapActionHttpModels.cs`
- Modify: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/MapActionsWebContractTests.cs`

- [ ] **Step 1: 写失败测试**，覆盖三个独立 POST、operation GET、Owner/非 Owner、确认值、重复请求、容量满、非法坐标/范围、未知目标，以及 payload 拒绝 command/path 字段。
- [ ] **Step 2: 实现类型化路由。** POST 返回 202 与 operation ID；仅同步且已证实完成的游戏动作可返回终态，不能把排队映射为 succeeded。
- [ ] **Step 3: 更新 OpenAPI 快照并运行 Web/DI 定向测试。**

### Task 6: 实现前端固定目标确认和作业恢复

**Files:**

- Create: `frontend/apps/admin/src/features/map-management/api/mapActions.ts`
- Create: `frontend/apps/admin/src/features/map-management/model/useMapOperation.ts`
- Create: `frontend/apps/admin/src/features/map-management/ui/DeleteLandClaimDialog.vue`
- Create: `frontend/apps/admin/src/features/map-management/ui/TeleportPlayerDialog.vue`
- Create: `frontend/apps/admin/src/features/map-management/ui/MapRenderDialog.vue`
- Create: `frontend/apps/admin/src/features/map-management/ui/MapOperationStatus.vue`
- Modify: `frontend/apps/admin/src/features/player-map/ui/PlayerMapView.vue`
- Test: corresponding `*.test.ts`

- [ ] **Step 1: 写失败测试**，覆盖 Owner-only 入口、只读 popup 不直接执行、固定目标、Stale/Offline 禁止传送、完整渲染强确认、202 显示 queued、刷新后按 operation ID 恢复和 result-unknown 文案。
- [ ] **Step 2: 实现 API parser、mutation controller 和四个独立 UI。** 不把 mutation 放入只读图层 composable，不接受命令或路径输入。
- [ ] **Step 3: 成功后只失效相关图层或地图资源版本；失败保留地图与筛选。**
- [ ] **Step 4: 运行定向 Vitest、typecheck、locale check、lint 和 build。**

### Task 7: 聚合验证与事实提升

**Files:**

- Modify only after implementation evidence: `docs/architecture.md`
- Modify only after implementation evidence: `docs/test.md`

- [ ] **Step 1: 运行后端聚合 build/test 和 Admin 聚合检查。**
- [ ] **Step 2: 运行地图管理 Playwright，覆盖 403、确认、排队非成功、恢复和窄屏；真实领地/传送/渲染兼容性必须由受控 7DTD smoke 单独证明。**
- [ ] **Step 3: 检查无万能 action payload、控制台命令、浏览器路径、HTTP 游戏对象访问、无界队列或排队即成功。**
- [ ] **Step 4: 仅在适用自动化和真实边界证据完成后提升 Current 文档；未执行 smoke 明确保留为风险。**

## 完成条件

- 三类操作各有类型化请求、权限、固定目标复验、权威状态和脱敏审计。
- 游戏变更只经有界主线程 dispatcher，地图渲染只经有界后台作业且失败保留旧版本。
- 浏览器不能提交命令、脚本或服务器路径，HTTP 接受不显示为最终成功。
- 只读地图查询、图层刷新和可访问列表在操作失败时仍可使用。
