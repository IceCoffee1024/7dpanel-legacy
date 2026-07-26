---
state: Current
document_role: Change Record
last_updated: "2026-07-26"
---

# 玩家坐标地图实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 分三个可验收阶段交付 Owner-only OpenLayers 游戏地图、在线与历史轨迹、认证瓦片和业务图层，以及有界区域调查。

**Architecture:** 复用现有玩家在线投影和历史 SQLite，不增加位置采集链路；所有游戏数据先复制成不可变投影，Web 只做有界读取。前端在 Players Feature 内拥有 OpenLayers 实例和各图层局部状态，公开轨迹只返回不带缺口详情的 `segments`。

**Tech Stack:** .NET Framework 4.8、C#、Katana Web API、Dapper、SQLite、Vue 3 Composition API、TypeScript、OpenLayers、Nuxt UI、Vitest、Playwright。

> 主设计规格：[玩家坐标地图设计规格](../specs/2026-07-26-player-coordinate-map-design.md)

**实施状态（2026-07-26）：** 第 1 至第 3 阶段的只读合同、SQLite 查询、Owner-only API、OpenLayers 页面、认证瓦片客户端、业务图层控制器和区域调查已落地。当前没有可信生产瓦片根或目标游戏业务对象字段复制证据，对应服务端投影按规格返回 unavailable；真实浏览器交互、320 CSS 像素布局和真实 7DTD 字段留待人工验收。地图管理操作仍由独立规格和计划拥有，不属于本计划实现结果。

---

## 文件结构

- 后端地图合同放在 `Application/Maps/`，避免把世界图层塞入玩家历史类型。
- `MapController.cs` 统一拥有只读地图 HTTP 路由；玩家历史 Store 只增加历史位置与空间查询。
- SevenDays Adapter 的 `Maps/` 只负责游戏线程复制和不可变投影，Persistence Adapter 负责历史空间查询与瓦片 I/O。
- 前端 `features/player-map/` 拥有 OpenLayers 组件、图层 composable、API parser 和页面状态；复用 players 公开 DTO，不导入 players 内部 composable。
- 每个阶段完成后运行该阶段定向检查；只在第三阶段稳定后运行一次聚合检查。

## 第一阶段：地图基础、在线玩家与单玩家历史轨迹

### Task 1: 锁定地图元数据、游戏时间与历史分段合同

**Files:**

- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Maps/MapMetadata.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Maps/MapGameTime.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Maps/PlayerTrackSegment.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Maps/GetPlayerTrackUseCase.cs`
- Modify: `backend/src/Core/LSTY.SevenDPanel.Application/Players/History/IPlayerHistoryStore.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/PlayerMapUseCaseTests.cs`

- [ ] **Step 1: 写失败测试**，覆盖游戏时间的日/时/分与独立 `observedAtUtc` 合同，以及空跨平台身份、非 UTC/逆序/超过 30 天、超过 5000 条、同毫秒 `snapshotId` 稳定排序和内部缺失边界产生两个 segment。Task 1 只定义 Application 合同；游戏线程复制与 Web 投影在 Task 3 实现。
- [ ] **Step 2: 运行测试确认失败。**

```powershell
dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --filter FullyQualifiedName~PlayerMapUseCaseTests
```

Expected: FAIL，地图合同或用例尚不存在。

- [ ] **Step 3: 实现最小合同。** 公开输出只包含 `Segments`，缺口原因和数量不得进入 Web DTO。

```csharp
public sealed class PlayerTrackSegment
{
    public PlayerTrackSegment(IReadOnlyList<PlayerTrackPoint> points) => Points = points;
    public IReadOnlyList<PlayerTrackPoint> Points { get; }
}
```

- [ ] **Step 4: 运行同一测试确认通过。**

### Task 2: 实现 SQLite 历史位置范围读取

**Files:**

- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqlitePlayerHistoryStore.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/SqlitePlayerHistoryStoreTests.cs`

- [ ] **Step 1: 写失败测试**，覆盖闭合时间边界、稳定顺序、与范围相交的内部缺口、完整 X/Y/Z、未知玩家和上限拒绝。
- [ ] **Step 2: 运行 SQLite 历史定向测试并确认失败。**
- [ ] **Step 3: 使用现有 `ix_player_history_snapshots_player_time` 读取最小位置投影和内部缺口。** 不改 migration、Channel 或保留策略。

```sql
SELECT snapshot_id, crossplatform_id, observed_utc, name,
       position_x, position_y, position_z
FROM player_history_snapshots
WHERE crossplatform_id = @CrossplatformId
  AND observed_utc >= @FromUtc
  AND observed_utc <= @ToUtc
ORDER BY observed_utc ASC, snapshot_id ASC;
```

- [ ] **Step 4: 运行 `SqlitePlayerHistoryStoreTests` 确认通过。**

### Task 3: 增加 Owner-only 地图元数据和轨迹 API

**Files:**

- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/MapController.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/MapHttpModels.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Maps/SevenDaysMapMetadataProjection.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Maps/SevenDaysMapGameTimeProjection.cs`
- Modify: `backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/PlayerMapWebContractTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/SevenDaysMapMetadataProjectionTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/SevenDaysMapGameTimeProjectionTests.cs`

- [ ] **Step 1: 写失败测试**，覆盖 Owner 200、匿名 401、非 Owner 403、无效范围 400、未知玩家 404、脱敏 500、游戏日时与独立 `observedAtUtc`，以及响应 JSON 不包含 `gap`、`reason`、`droppedCount`。
- [ ] **Step 2: 运行 Web 合同测试确认失败。**
- [ ] **Step 3: 在游戏就绪/世界切换边界复制不可变世界元数据和游戏时间，并实现 `GET /api/v1/map/metadata`、`GET /api/v1/map/game-time` 与 `GET /api/v1/map/players/{crossplatformId}/track`。** HTTP 和计时器不读取 `World`；历史路由不检查 game ready，元数据或游戏时间不可用分别表达稳定状态。
- [ ] **Step 4: 更新受控 OpenAPI 快照并运行 Web/DI 定向测试。**

### Task 4: 引入 OpenLayers 并实现 Feature 边界

**Files:**

- Modify: `frontend/apps/admin/package.json`
- Modify: `frontend/apps/admin/pnpm-lock.yaml`
- Create: `frontend/apps/admin/src/features/player-map/api/playerMap.ts`
- Create: `frontend/apps/admin/src/features/player-map/model/mapProjection.ts`
- Create: `frontend/apps/admin/src/features/player-map/model/usePlayerMap.ts`
- Test: corresponding `*.test.ts`

- [ ] **Step 1: 添加 OpenLayers 直接依赖并锁定版本。**

```powershell
pnpm --dir frontend/apps/admin add ol
```

Expected: `package.json` 和 lockfile 只新增 OpenLayers 所需依赖。

- [ ] **Step 2: 写失败测试**，覆盖严格元数据/segment parser、自定义 extent 与轴方向、URL 筛选恢复、旧请求取消和公开响应拒绝缺口字段。
- [ ] **Step 3: 实现纯 projection helper 和页面局部 controller。** OpenLayers 类型不进入 API DTO；不使用 Pinia 或全局 map registry。
- [ ] **Step 4: 运行 player-map API/model Vitest 确认通过。**

### Task 5: 实现 `/players/map` 基础页面

**Files:**

- Create: `frontend/apps/admin/src/pages/players/map.vue`
- Create: `frontend/apps/admin/src/features/player-map/ui/PlayerMapView.vue`
- Create: `frontend/apps/admin/src/features/player-map/ui/OpenLayersGameMap.vue`
- Create: `frontend/apps/admin/src/features/player-map/ui/PlayerTrackObservations.vue`
- Create: `frontend/apps/admin/src/assets/images/map-background.webp`
- Modify: `frontend/apps/admin/src/features/players/ui/PlayersSectionNavigation.vue`
- Modify: `frontend/apps/admin/src/app/i18n/locales/zh-CN.json`
- Modify: `frontend/apps/admin/src/app/i18n/locales/en.json`
- Test: corresponding route and component tests

- [ ] **Step 1: 写失败组件测试**，覆盖路由、七种状态、游戏时间 30 秒刷新/Stale、在线与历史独立失败、单玩家筛选、segment 不跨段连线、返回观察数、起终点、首次 fit 后不抢回手动视口、统一观察说明、时间/列表同步和无危险操作。
- [ ] **Step 2: 创建当前项目自有的固定本地背景图并记录素材来源；实现固定朝北的 OpenLayers Map/View 生命周期。** `onMounted` 创建，不注册旋转控件，失活暂停，卸载时 `setTarget(undefined)` 并释放 listeners/sources；不得复制旧项目图片。
- [ ] **Step 3: 实现背景、网格、在线 VectorLayer、历史 segment VectorLayer、起终点、一次性自动 fit、游戏时间、选择详情和同步文本列表。** 不实现小地图或 `minDistance`。
- [ ] **Step 4: 运行定向 Vitest、typecheck 和 locale check；用 320 CSS 像素组件布局断言覆盖窄屏。**

## 第二阶段：认证瓦片与低频业务图层

### Task 6: 实现地图瓦片安全读取

**Files:**

- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Maps/GetMapTileUseCase.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/MapTiles/LocalMapTileStore.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/MapController.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/MapTileWebContractTests.cs`

- [ ] **Step 1: 写失败测试**，覆盖 Header Bearer、合法 tile、非法 world/zoom/x/y、路径穿越、缺失 tile、ETag/地图资源版本及错误脱敏。
- [ ] **Step 2: 实现服务端 tile key 到受控路径的映射。** 浏览器请求不得包含路径；I/O 不进入游戏线程。
- [ ] **Step 3: 运行瓦片 Web 合同测试确认通过。**

### Task 7: 建立只读地图图层投影

**Files:**

- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Maps/MapLayerModels.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Maps/GetMapLayerUseCase.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Maps/GetHistoricalPlayerLastLocationsUseCase.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Maps/SevenDaysMapLayerProjection.cs`
- Modify: `backend/src/Core/LSTY.SevenDPanel.Application/Players/History/IPlayerHistoryStore.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqlitePlayerHistoryStore.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/MapController.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/SevenDaysMapLayerProjectionTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/MapLayerWebContractTests.cs`

- [ ] **Step 1: 逐层写字段复制和查询失败测试**，覆盖历史玩家最后保留位置、与当前在线规范身份去重、在线投影不可用时不输出离线判断，以及商人营业状态、领地、载具和无人机的已加载/未加载、规范所有者、可空字段、有限坐标和共同 `observedAtUtc`。
- [ ] **Step 2: 实现游戏线程内复制和线程安全 latest snapshot。** 不保存 `World`、Manager、Entity、Unity Vector 或枚举容器。
- [ ] **Step 3: 实现历史最后位置及按 extent、zoom 和 limit 的独立只读 API；超限返回稳定错误。** 历史点合同只表达最后保留观察，不命名为当前位置或确定离线。
- [ ] **Step 4: 运行投影和 Web 定向测试确认通过。**

### Task 8: 实现认证瓦片和按需业务图层 UI

**Files:**

- Create: `frontend/apps/admin/src/features/player-map/model/useAuthenticatedTileLayer.ts`
- Create: `frontend/apps/admin/src/features/player-map/model/useMapVectorLayer.ts`
- Create: `frontend/apps/admin/src/features/player-map/ui/MapLayersPanel.vue`
- Create: `frontend/apps/admin/src/features/player-map/ui/MapFeatureDetails.vue`
- Modify: `frontend/apps/admin/src/features/player-map/ui/OpenLayersGameMap.vue`
- Test: corresponding `*.test.ts`

- [ ] **Step 1: 写失败测试**，覆盖 Header Bearer Blob、对象 URL 释放、瓦片署名、客户端重载不调用服务端作业、背景在瓦片关闭/加载/失败时可见、默认关闭、成功后对象数、显示加载、隐藏/页面失活暂停、恢复刷新、独立失败、缩放门槛和聚合选择。
- [ ] **Step 2: 实现 tile loader、只刷新 OpenLayers tile source 的重载按钮、region VectorLayer，以及历史玩家最后位置、商人/领地/载具/无人机独立 controller。**
- [ ] **Step 3: 实现图层计数、商人营业状态、只读 tooltip/popup、规范身份资料跳转和同步对象列表。** 未知字段显示未知，popup 不包含危险操作或玩家/载具背包。
- [ ] **Step 4: 运行定向 Vitest、typecheck、lint 和 build。**

## 第三阶段：区域调查与高波动实体

### Task 9: 增加 SQLite 区域玩家查询

**Files:**

- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Migrations/006_PlayerMapSpatialQueries.sql`
- Modify: `backend/src/Core/LSTY.SevenDPanel.Application/Players/History/IPlayerHistoryStore.cs`
- Create: `backend/src/Core/LSTY.SevenDPanel.Application/Maps/SearchPlayersInAreaUseCase.cs`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqlitePlayerHistoryStore.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/PlayerMapSpatialQueryTests.cs`

- [ ] **Step 1: 写失败测试**，覆盖矩形边界、圆形半径、UTC 范围、首次/最后命中、同玩家分组、结果上限和查询计划使用空间/时间索引。
- [ ] **Step 2: 增加 `(observed_utc, position_x, position_z, crossplatform_id)` 索引并实现数据库有界过滤。** 圆形先包围盒后半径，不读取全量历史。
- [ ] **Step 3: 运行 migration 幂等和空间查询测试确认通过。**

### Task 10: 增加区域 API 和高波动实体投影

**Files:**

- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/MapController.cs`
- Create: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Maps/SevenDaysTransientEntityProjection.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/MapAreaSearchWebContractTests.cs`
- Test: `backend/tests/LSTY.SevenDPanel.Tests/SevenDaysTransientEntityProjectionTests.cs`

- [ ] **Step 1: 写失败测试**，覆盖矩形/圆形参数、Owner-only、有界结果、动物/敌对实体的 extent、zoom、limit、过期和停止清理。
- [ ] **Step 2: 实现区域查询 Web 映射和短生命周期实体投影。** HTTP 不触发世界扫描，实体数据不持久化。
- [ ] **Step 3: 运行 Web 与投影定向测试确认通过。**

### Task 11: 实现区域绘制和高波动图层 UI

**Files:**

- Create: `frontend/apps/admin/src/features/player-map/model/useAreaInvestigation.ts`
- Create: `frontend/apps/admin/src/features/player-map/ui/MapAreaInvestigation.vue`
- Modify: `frontend/apps/admin/src/features/player-map/ui/MapLayersPanel.vue`
- Modify: `frontend/apps/admin/src/features/player-map/ui/OpenLayersGameMap.vue`
- Test: corresponding `*.test.ts`

- [ ] **Step 1: 写失败测试**，覆盖矩形/圆形互斥 Draw interaction、URL 几何、时间范围、取消旧请求、结果列表联动，以及动物/敌对图层缩放与刷新限制。
- [ ] **Step 2: 实现受控 Draw/Modify、区域结果列表和历史详情/轨迹跳转。** 文案明确命中观察不代表持续停留。
- [ ] **Step 3: 运行定向 Vitest 和 Playwright 地图场景；浏览器测试使用受控 API，不替代真实游戏字段验证。**

## 收尾

### Task 12: 聚合验证与事实提升

**Files:**

- Modify only after implementation evidence: `docs/architecture.md`
- Modify only after implementation evidence: `docs/test.md`

- [ ] **Step 1: 运行一次后端聚合 build/test。**
- [ ] **Step 2: 运行 Admin `api:check`、test、typecheck、locale check、lint 和 build。**
- [ ] **Step 3: 运行适用 Playwright；除非实现跨越真实 7DTD 字段/线程边界且测试策略要求，否则不运行发布或真实服务器 smoke。**
- [ ] **Step 4: 检查不存在第二位置表/Channel、HTTP 游戏对象访问、QueryString Token、用户可见缺口详情或只读 popup 危险操作。**
- [ ] **Step 5: 仅把已实现且有证据的阶段提升到 Current 文档；未完成阶段继续留在 Target/spec。**

## 完成条件

- 三个阶段分别可运行、可测试；后续阶段不破坏第一阶段轨迹和可访问列表。
- 公开地图合同和普通地图界面不显示 `gap`、原因或数量，但已知缺失边界不会被连线跨越。
- 认证瓦片、图层和区域查询均有权限、范围、条数、刷新和取消边界。
- 游戏时间、固定本地背景、轨迹起终点/首次自动定位、历史玩家最后位置、对象计数、资料跳转、商人营业状态、瓦片署名和客户端重载均有自动化边界。
- HTTP 不访问游戏活对象，玩家历史采集与保留策略保持不变。
- 地图第 1 至第 3 阶段没有删除、传送或渲染入口。
