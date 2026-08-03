# Admin 信息架构一次性重构实施计划

> **执行要求：** 在独立执行会话中使用 `executing-plans`，按任务逐项实现、验证和更新 checkbox。未经用户明确授权，不执行 commit、push 或其他 Git 历史操作。

**Goal:** 在一个 Admin 发布中把现有页面收敛为“概览”“服务器运维”“玩家”“社区”“经济与奖励”和“系统管理”六个一级任务域，建立唯一导航目录和完整旧 URL 兼容，同时保持既有 Feature、API、权限和持久化语义不变。

**Architecture:** 保留 Vue Router 自动生成的 typed routes 和薄 route page；新增显式 `navigationCatalog`、共享 `routeAccess` 与 readonly `useNavigation` 投影，供侧栏、移动导航、Dashboard Search、面包屑和快捷键共同消费。规范 route page 继续组合现有 Feature View，旧 URL 由独立 redirect records 转到规范地址。主设计为 [Admin 信息架构重构设计规格](../specs/2026-08-03-admin-information-architecture-refactor-design.md)。

**Tech Stack:** Vue `3.5` Composition API、TypeScript `6.0`、Vue Router typed routes、Pinia `3`、Nuxt UI `4`、Vue I18n、Vite `8`、Vitest、Vue Test Utils、Playwright、pnpm `11`。

**产品与当前事实：** 产品合同见 [PRD](../../PRD.md)，当前页面规则见 [产品设计](../../design.md)，当前前端边界见 [系统架构](../../architecture.md)，验证与发布证据见 [测试策略](../../test.md)。Target 或本计划条目不作为实现证据。

**范围边界:** 不修改后端源代码、`/api/v1`、OpenAPI schema、生成客户端、SQLite、7DTD Adapter 或发布脚本。若实现中出现这些 diff，立即停止并重新确认范围。

**验证边界:** 迭代阶段只运行当前任务列出的聚焦 Vitest。最终运行 Admin lint、typecheck、全量 Vitest、生产构建、`api:check`、Playwright mock 矩阵和受控真实 OWIN browser smoke。不运行 publish、真实 7DTD、Discord/MaxMind sandbox、备份恢复或危险世界副作用 smoke。

**切换与回滚:** 实施可以分检查点完成，但合流前不能发布半套导航。最终版本同时包含规范路由、六组导航和旧 URL redirects。没有数据库或 API migration，回滚恢复上一版 Admin 静态产物或完整 Mod artifact。

**当前执行状态（2026-08-03）：** Task 1 至 Task 5 已在共享工作树完成；Admin 单元测试 `137` 个文件、`937/937` 项通过，typecheck、`api:check` 和生产构建通过。触及文件的显式 ESLint 通过，但全量 lint 仍有 6 个既有格式错误。Task 6 的 Playwright 未取得有效证据，原因是本机缺少 `chrome-headless-shell` 且未提供受控 OWIN 环境变量；Task 7 的聚合门禁因此保留未完成，不得标记为发布就绪。

---

## 目标文件结构

```text
frontend/apps/admin/src/app/
  AppShell.vue
  router.ts
  navigation/
    navigationCatalog.ts
    navigationCatalog.test.ts
    navigationRedirects.ts
    navigationRedirects.test.ts
    navigationTypes.ts
    routeAccess.ts
    routeAccess.test.ts
    useNavigation.ts
    useNavigation.test.ts
frontend/apps/admin/src/components/navigation/
  PrimaryNavigation.vue
  PrimaryNavigation.test.ts
  SecondaryNavigation.vue
  SecondaryNavigation.test.ts
  AppBreadcrumbs.vue
  AppBreadcrumbs.test.ts
  SectionTabs.vue
  SectionTabs.test.ts
frontend/apps/admin/src/pages/
  operations/**
  players/**
  community/**
  economy/**
  system/**
```

`src/features/*` 继续按业务能力保持扁平。只在上下文入口或复用现有 View 所需时修改 Feature，不为匹配侧栏目录批量移动 Feature 文件。

## Task 1：锁定导航目录、权限投影和完整性合同

**Files:**

- Create: `frontend/apps/admin/src/app/navigation/navigationTypes.ts`
- Create: `frontend/apps/admin/src/app/navigation/routeAccess.ts`
- Create: `frontend/apps/admin/src/app/navigation/routeAccess.test.ts`
- Create: `frontend/apps/admin/src/app/navigation/navigationCatalog.ts`
- Create: `frontend/apps/admin/src/app/navigation/navigationCatalog.test.ts`
- Create: `frontend/apps/admin/src/app/navigation/useNavigation.ts`
- Create: `frontend/apps/admin/src/app/navigation/useNavigation.test.ts`
- Modify: `frontend/apps/admin/src/app/router.ts`
- Modify: `frontend/apps/admin/src/app/router.test.ts`

- [x] **Step 1：写权限和目录 RED**

测试固定以下合同：

- 一级任务域按 `overview`、`operations`、`players`、`community`、`economy`、`system` 唯一排序。
- 每个可导航规范 route name 只有一个主要归属；登录、Forbidden、动态详情和 redirect 源不进入侧栏。
- 目录只引用生成 route map 中存在的 route name，不登记字符串组件或 Feature 路径。
- `Owner`、`Admin`、`Viewer` 的可达性来自同一个 `canAccessRoute(meta, role, isAuthenticated)`。
- 当前角色无任何可达 child 时隐藏任务域；任务域不能复制 child 的角色数组。
- 导航、搜索、面包屑和快捷键投影使用同一目录，输出 readonly 数据。

Run:

```powershell
pnpm exec vitest run src/app/navigation src/app/router.test.ts
```

Workdir: `frontend/apps/admin`

Expected: FAIL，因为导航模块尚不存在，现有 Router 仍内联角色判断。

- [x] **Step 2：实现最小 typed navigation core**

`navigationTypes.ts` 定义稳定的 `NavigationGroupId`、目录项、父链和投影类型；`navigationCatalog.ts` 是纯静态数据，不读取 auth、router、i18n 或浏览器全局；label 只保存 i18n key。

`routeAccess.ts` 接受 typed `RouteMeta` 和当前认证状态，返回 allow/deny；`router.ts` 的 `beforeEach` 与 `useNavigation.ts` 都调用该函数。保留现有登录安全返回和 Forbidden 语义，不把权限拒绝改成默认页跳转。

- [x] **Step 3：实现投影并转 GREEN**

`useNavigation.ts` 通过 `computed` 派生：

- 六个可见任务域和当前激活域。
- 当前域可达的二级入口。
- Dashboard Search 扁平项目。
- 当前 route 的 breadcrumb chain。
- 现有快捷键对应的规范 route name。

Run:

```powershell
pnpm exec vitest run src/app/navigation src/app/router.test.ts
pnpm typecheck
```

Workdir: `frontend/apps/admin`

Expected: PASS；没有新增 Store、watcher 持久状态或第二份页面目录。

## Task 2：建立规范路由和旧 URL redirects

**Files:**

- Create: `frontend/apps/admin/src/app/navigation/navigationRedirects.ts`
- Create: `frontend/apps/admin/src/app/navigation/navigationRedirects.test.ts`
- Modify: `frontend/apps/admin/src/app/router.ts`
- Modify: `frontend/apps/admin/src/app/router.test.ts`
- Move/Create route wrappers under `frontend/apps/admin/src/pages/operations/`
- Move/Create route wrappers under `frontend/apps/admin/src/pages/players/`
- Move/Create route wrappers under `frontend/apps/admin/src/pages/community/`
- Move/Create route wrappers under `frontend/apps/admin/src/pages/economy/`
- Move/Create route wrappers under `frontend/apps/admin/src/pages/system/`

- [x] **Step 1：写完整 redirect matrix RED**

使用 table-driven Router 测试覆盖设计规格中的全部旧地址与规范地址，至少包含：

- 普通静态地址。
- `?page=2&search=steel`、页签、operation ID 和 `#fragment` 保留。
- 匿名访问旧地址时，登录返回目标最终是规范地址。
- Owner/Admin/Viewer 到达目标后执行目标 route meta 权限。
- 不存在 redirect chain、循环、alias 或同一业务能力的第二个 component record。
- `/players/history/:crossplatformId` 和 `/players/profile/:crossplatformId` 动态参数保持不变。

Run:

```powershell
pnpm exec vitest run src/app/navigation/navigationRedirects.test.ts src/app/router.test.ts
```

Workdir: `frontend/apps/admin`

Expected: FAIL，因为规范 route page 与 redirect records 尚不存在。

- [x] **Step 2：移动薄 route page 到规范地址**

按下列映射移动页面 wrapper，并只调整 Feature import 相对路径与 route meta：

```text
backups.vue                         -> operations/backups.vue
schedules.vue                       -> operations/automation/schedules.vue
automation/index.vue                -> operations/automation/rules.vue
server-configuration.vue            -> operations/configuration.vue
mods.vue                            -> operations/extensions/mods.vue
modules.vue                         -> operations/extensions/modules.vue
world-tools.vue                     -> operations/world.vue
console-logs.vue                    -> operations/console.vue
game-resources.vue                  -> players/resources.vue
access-lists.vue                    -> players/access-lists.vue
game-chat/*.vue                     -> community/chat/*.vue
economy/reward-packages.vue         -> economy/rewards/packages.vue
economy/daily-reward.vue            -> economy/rewards/daily.vue
economy/reward-operations.vue       -> economy/rewards/operations.vue
economy/achievement-online-rewards.vue -> economy/rewards/achievements.vue
economy/shop.vue                    -> economy/commerce/shop.vue
economy/redeem-codes.vue            -> economy/commerce/redeem-codes.vue
permissions.vue                     -> system/access.vue
api-keys.vue                        -> system/api-keys.vue
integrations/discord.vue            -> system/integrations/discord.vue
integrations/geoip.vue              -> system/integrations/geoip.vue
audit.vue                           -> system/audit.vue
```

`game-chat/colored.vue` 的新文件名固定为 `community/chat/appearance.vue`。保留 `/`、`/players`、玩家详情、`/players/history`、`/players/map`、`/community/teleport|votes|cities` 和 `/economy/accounts|transactions`。

- [x] **Step 3：实现兼容 records 和父路由默认行为**

`navigationRedirects.ts` 返回显式 `RouteRecordRaw[]`，redirect function 保留 `to.query` 和 `to.hash`。`router.ts` 将 records 与 generated routes 组合；父路径只转到规格批准的稳定默认子页，不根据请求结果或瞬时角色数据选择页面。

Vue Router 对 redirect 源不运行 source guard，因此测试必须证明最终目标 route guard 生效。

- [x] **Step 4：转 GREEN 并检查生成 route map**

Run:

```powershell
pnpm exec vitest run src/app/navigation/navigationRedirects.test.ts src/app/router.test.ts
pnpm typecheck
pnpm build
```

Workdir: `frontend/apps/admin`

Expected: PASS；Vite history fallback 可以直接构建全部新深链接，旧页面组件文件不再生成第二套路由。

## Task 3：拆分 AppShell 并统一侧栏、搜索、面包屑和快捷键

**Files:**

- Create: `frontend/apps/admin/src/components/navigation/PrimaryNavigation.vue`
- Create: `frontend/apps/admin/src/components/navigation/PrimaryNavigation.test.ts`
- Create: `frontend/apps/admin/src/components/navigation/SecondaryNavigation.vue`
- Create: `frontend/apps/admin/src/components/navigation/SecondaryNavigation.test.ts`
- Create: `frontend/apps/admin/src/components/navigation/AppBreadcrumbs.vue`
- Create: `frontend/apps/admin/src/components/navigation/AppBreadcrumbs.test.ts`
- Modify: `frontend/apps/admin/src/app/AppShell.vue`
- Modify: `frontend/apps/admin/src/app/AppShell.test.ts`
- Modify: `frontend/apps/admin/src/app/i18n/locales/zh-CN.json`
- Modify: `frontend/apps/admin/src/app/i18n/locales/en.json`

- [x] **Step 1：写六任务域和响应式交互 RED**

覆盖：

- Owner 首层恰好显示六个任务域，当前域展开对应二级入口。
- Admin/Viewer 仍显示有可达 child 的任务域，隐藏无可达 child 和无权页面。
- 侧栏、Dashboard Search、breadcrumb 和快捷键的 route name 均来自同一投影。
- 路由变化关闭移动抽屉；搜索选择进入规范地址并关闭搜索。
- 语言切换只改变 label，不改变 route、identity 或 active chain。
- 动态详情显示“玩家 > 玩家记录/在线玩家 > 当前详情”的父链，不进入固定二级菜单。

Run:

```powershell
pnpm exec vitest run src/app/AppShell.test.ts src/components/navigation src/app/navigation
```

Workdir: `frontend/apps/admin`

Expected: FAIL，现有 `AppShell.vue` 仍定义多组页面数组和独立搜索映射。

- [x] **Step 2：实现 focused components**

- `PrimaryNavigation.vue` 只展示任务域并发出选择/关闭事件。
- `SecondaryNavigation.vue` 只展示当前域 children。
- `AppBreadcrumbs.vue` 只展示输入的 typed items。
- `AppShell.vue` 只拥有 `sidebarOpen`、`searchOpen` 和账号/外观/语言菜单装配。
- 熟悉的图标和现有 Nuxt UI shell 保持不变，不引入新的导航组件库。

删除 `gameChatNavigation`、`playerAndWorldNavigation`、`operationsNavigation`、`economyNavigation`、`communityNavigation`、`integrationsNavigation`、手写 `navigation` 和独立 `searchGroups` 页面清单。

- [x] **Step 3：同步双语 key 并转 GREEN**

Run:

```powershell
pnpm exec vitest run src/app/AppShell.test.ts src/components/navigation src/app/navigation
pnpm typecheck
```

Workdir: `frontend/apps/admin`

Expected: PASS；导航组件无直接写入的中英文可见文案，`AppShell.vue` 不再拥有业务页面目录。

## Task 4：交付二级组合页、局部页签和服务器控制页面

**Files:**

- Create: `frontend/apps/admin/src/components/navigation/SectionTabs.vue`
- Create: `frontend/apps/admin/src/components/navigation/SectionTabs.test.ts`
- Create: `frontend/apps/admin/src/features/server-operations/ui/ServerOperationsView.vue`
- Create: `frontend/apps/admin/src/features/server-operations/ui/ServerOperationsView.test.ts`
- Create: `frontend/apps/admin/src/pages/operations/server.vue`
- Modify: `frontend/apps/admin/src/pages/operations/automation/*.vue`
- Modify: `frontend/apps/admin/src/pages/operations/extensions/*.vue`
- Modify: `frontend/apps/admin/src/pages/community/chat/*.vue`
- Modify: `frontend/apps/admin/src/pages/economy/rewards/*.vue`
- Modify: `frontend/apps/admin/src/pages/economy/commerce/*.vue`
- Modify: `frontend/apps/admin/src/pages/system/*.vue`
- Modify: `frontend/apps/admin/src/features/players/ui/PlayersSectionNavigation.vue`
- Modify: `frontend/apps/admin/src/features/players/ui/PlayersSectionNavigation.test.ts`

- [x] **Step 1：写组合责任和 route-driven tabs RED**

测试确认每个页签选择产生独立规范 URL，刷新可恢复，Back/Forward 正确；页签 wrapper 不复制 Feature 请求、Mutation、表单草稿或服务器状态。`SectionTabs` 只接受 typed items 并渲染当前项。

`ServerOperationsView` 只组合现有状态摘要、重启策略、Restart/Shutdown 对话框和 Quick Actions，不接受浏览器脚本路径、命令或参数，不把“脚本已启动”显示为“服务器已重启”。

- [x] **Step 2：实现服务器控制和局部页签**

按规格组合以下二级集合：

- Schedules/Automation。
- Mods/功能模块。
- 实时聊天/历史/禁言/设置/聊天外观。
- 奖励包/每日奖励/补偿/成就与在线奖励。
- 商店/兑换码。
- 面板用户/游戏管理员/命令权限继续由现有 `PermissionsView` 拥有内部状态。
- Discord/GeoIP。
- 统一审计/游戏事件继续由现有 `AuditWorkspace` 拥有内部状态。

不把这些 Feature 合并为新 Store 或大 View；route page 只装配局部导航和现有 Feature View。

- [x] **Step 3：转 GREEN**

Run:

```powershell
pnpm exec vitest run src/components/navigation/SectionTabs.test.ts src/features/server-operations src/features/players/ui/PlayersSectionNavigation.test.ts src/app/router.test.ts
pnpm typecheck
```

Workdir: `frontend/apps/admin`

Expected: PASS；每个现有 Feature 的加载、Stale、Forbidden、Unavailable、冲突和危险确认语义保持原样。

## Task 5：增加上下文入口并保持固定目标安全

**Files:**

- Modify: `frontend/apps/admin/src/features/players/ui/OnlinePlayerDetailsSlideover.vue`
- Modify: `frontend/apps/admin/src/features/players/ui/OnlinePlayerDetailsSlideover.test.ts`
- Modify: `frontend/apps/admin/src/features/players/ui/PlayerSnapshotDetails.vue`
- Modify: `frontend/apps/admin/src/features/players/ui/PlayerSnapshotDetails.test.ts`
- Modify: `frontend/apps/admin/src/features/backups/ui/BackupsView.vue`
- Modify: `frontend/apps/admin/src/features/backups/ui/BackupsView.test.ts`
- Modify: `frontend/apps/admin/src/features/server-configuration/ui/ServerConfigurationView.vue`
- Modify: `frontend/apps/admin/src/features/mods/ui/ModsView.vue`
- Modify: `frontend/apps/admin/src/features/modules/ui/FeatureModulesView.vue`
- Modify: `frontend/apps/admin/src/features/discord/ui/DiscordView.vue`
- Modify: `frontend/apps/admin/src/features/geoip/ui/GeoIpView.vue`
- Modify: `frontend/apps/admin/src/features/game-resources/ui/GameResourcesView.vue`
- Add focused tests beside every changed Feature View

- [x] **Step 1：写上下文链接 RED**

覆盖：

- 玩家目标使用稳定 `crossplatformId`；Profile、地图、禁言、名单、传送和审计链接不以显示名猜测身份。
- 链接可以预填安全筛选，但不能自动打开危险确认或提交动作。
- 配置、Mod、模块的“需要重启”只进入 `/operations/server`。
- 备份/恢复结果进入服务器控制或受控审计，离线不显示为恢复成功。
- Discord/GeoIP 失败只传递批准的筛选值，不把 endpoint、凭据、Token 或原始异常写入 URL。
- 资源目录只接受资源 ID、搜索、类型、可见性和页码；返回目标经现有安全站内路径校验。

- [x] **Step 2：实现命名路由跳转**

使用 typed route name 和结构化 params/query，不在 Feature 中新增硬编码旧 URL。上下文按钮放在现有对象动作区或状态旁，不创建新的卡片层级，不改变现有固定目标快照和提交锁。

- [x] **Step 3：运行 Feature 聚焦测试**

Run:

```powershell
pnpm exec vitest run src/features/players src/features/backups src/features/server-configuration src/features/mods src/features/modules src/features/discord src/features/geoip src/features/game-resources
pnpm typecheck
```

Workdir: `frontend/apps/admin`

Expected: PASS；上下文入口可达，现有写操作测试和结果未知语义无回归。

## Task 6：补齐 i18n、键盘、移动端和浏览器路由矩阵

**Files:**

- Modify: `frontend/apps/admin/src/app/i18n/locales/zh-CN.json`
- Modify: `frontend/apps/admin/src/app/i18n/locales/en.json`
- Modify: `frontend/apps/admin/e2e/admin-routes.spec.ts`
- Modify: `frontend/apps/admin/e2e/admin-owner-waves.spec.ts`
- Modify: `frontend/apps/admin/tests/e2e/admin-navigation.spec.ts`
- Modify as needed: `frontend/apps/admin/e2e/support/admin.ts`
- Modify as needed: `frontend/apps/admin/tests/e2e/support/adminLocale.ts`

- [ ] **Step 1：扩展 mock Playwright matrix**（代码矩阵已更新；浏览器执行被本机缺少 `chrome-headless-shell` 阻塞）

在 Chromium desktop/`390x844` 覆盖六个任务域、二级展开、局部页签、Dashboard Search、面包屑、上下文入口、旧 URL、刷新和 Back/Forward；Firefox/WebKit 保持 Owner 页面矩阵。断言页面级无水平溢出、导航文本不遮挡且移动抽屉选择后关闭。

- [ ] **Step 2：扩展真实 OWIN smoke**（未提供 `SEVENDPANEL_ADMIN_URL`、`PANEL_USERNAME`、`PANEL_PASSWORD`）

使用既有环境变量，只在受控部署验证：

- 匿名新旧深链接保留规范登录返回目标。
- Owner/Admin/Viewer 的导航与直接访问。
- 新路径的静态资源 fallback、刷新、退出和 Token 失效。
- API/SSE 仍只使用 Header Bearer；浏览器地址不出现凭据。

没有所需环境变量时真实 OWIN 项目必须明确 skipped，不能作为通过证据。

- [ ] **Step 3：执行浏览器前置静态门禁**（typecheck、Vitest、build、`api:check` 通过；全量 lint 仍有 6 个既有错误）

Run:

```powershell
pnpm lint
pnpm typecheck
pnpm test:unit
pnpm build
```

Workdir: `frontend/apps/admin`

Expected: 全部 PASS 后才进入 Playwright。

- [ ] **Step 4：运行 Playwright**（已尝试；浏览器 executable 缺失，未形成通过证据）

Run: `pnpm test:e2e`

Workdir: `frontend/apps/admin`

Expected: mock desktop/mobile matrix PASS；真实 OWIN 项目只有在环境完整时才计入发布证据。

## Task 7：聚合验证、文档提升和发布回滚检查

**Files:**

- Modify after verified implementation: `docs/design.md`
- Modify after verified implementation: `docs/architecture.md`
- Modify after verified implementation: `docs/test.md`
- Modify if owning commands or app behavior summary changes: `frontend/apps/admin/README.md`
- Modify only after an actual release: `CHANGELOG.md`

- [ ] **Step 1：运行最终 Admin 聚合门禁一次**（lint 未通过；其余门禁通过）

Run separately:

```powershell
pnpm lint
pnpm typecheck
pnpm test:unit
pnpm build
pnpm api:check
git diff --check -- frontend/apps/admin docs
```

Workdir for pnpm commands: `frontend/apps/admin`

Workdir for `git diff --check`: repository root

Expected: 全部 PASS；`api:check` 证明 OpenAPI snapshot 与生成客户端没有漂移。

- [x] **Step 2：检查范围和回滚条件**

确认 diff 不包含：

- `backend/src`、`backend/tests`、migration 或后端项目文件。
- `frontend/apps/admin/openapi/7dpanel.v1.json` 或 `src/shared/api/generated/` 的语义变化。
- 第二套导航开关、旧页面 component 副本、动态插件扫描或新全局 Store。
- 密码、Token、完整 API Key、机器路径、测试凭据或运行时产物。

记录上一版可恢复 Admin 静态产物或完整 Mod artifact；没有可恢复 artifact 时不得执行发布切换。

- [x] **Step 3：提升已验证事实**

- `docs/design.md`：替换当前导航分组、路由、移动导航、搜索、面包屑和上下文流程事实。
- `docs/architecture.md`：记录 navigation catalog、共享 access projection、route redirects 和组件责任；不得把 spec/plan 当成代码证据。
- `docs/test.md`：记录实际运行的 Vitest、lint、typecheck、build、API drift、Playwright mock 和真实 OWIN 结果，保留 skipped 或未执行证据缺口。
- `frontend/apps/admin/README.md`：仅在验证命令或应用级运行说明发生变化时更新。
- `CHANGELOG.md`：只有该版本实际发布后才记录用户可见导航变化。

- [x] **Step 4：执行文档与最终差异审查**（文档链接、占位符、规格/计划配对和 `git diff --check` 已完成；浏览器与全量 lint 门禁仍按上文保留未完成）

Run:

```powershell
git diff --check
git status --short
```

Expected: 无 whitespace 错误；所有变更都属于本规格；没有 Git 历史操作。向用户报告通过项、skipped 项、真实 OWIN 证据、回滚 artifact 和剩余风险，再等待明确的发布或 Git 操作授权。
