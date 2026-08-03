---
state: Current
document_role: Change Record
last_updated: "2026-08-03"
---

# Admin 信息架构重构设计规格

> 本规格描述 Admin 信息架构、路由与导航所有权的一次性目标变更，不代表功能已经实现。产品合同以 [PRD](../../PRD.md) 为准，当前页面与交互事实以 [产品设计](../../design.md) 为准，当前技术实现以 [系统架构](../../architecture.md) 为准，验证策略以 [测试策略](../../test.md) 为准。

## 目标与驱动因素

当前 Admin 已有概览、玩家、聊天、控制台、备份、经济、社区、集成、服务器治理和世界工具等大量页面。现有 `AppShell.vue` 同时维护侧栏分组、角色裁剪、Dashboard Search 和快捷键，部分页面仍作为一级入口出现，同一页面的导航知识存在重复。功能继续增加会同时提高服主寻找入口和开发者维护导航一致性的成本。

本变更落实 `CAP-01` 至 `CAP-12` 的既有能力入口和 `NFR-02`、`NFR-03`、`NFR-04`、`NFR-06`，目标是：

1. 将全部已交付页面收敛到六个稳定的一级任务域。
2. 让高频工作从相关对象和状态直接进入，减少跨页面重新搜索。
3. 让侧栏、移动导航、Dashboard Search、面包屑、快捷键和角色裁剪共享明确的导航事实。
4. 在一次 Admin 发布中切换到新信息架构，同时保持旧 URL、权限、查询状态和浏览器历史连续。
5. 保持现有后端 API、SQLite、领域模型和运行时部署不变，使回滚只涉及 Admin 静态产物。

## 已批准决定

- 认证后的一级入口固定为“概览”“服务器运维”“玩家”“社区”“经济与奖励”和“系统管理”。
- 除概览外，一级入口只表达任务域；具体功能作为二级入口、页签、详情或上下文动作出现。
- 当前已实现能力、服务端授权、Feature 状态机、API 合同和持久数据语义保持不变。
- 新地址是规范 URL；旧地址使用显式 redirect，不使用 alias 或重复页面组件。
- 实施可以分任务和检查点完成，但用户可见切换只在一个版本发生，不提供长期双导航或运行时 Feature Flag。
- 前端 Feature 继续按业务能力拥有状态和 UI，不按侧栏分组批量移动目录或合并状态。
- 本变更不要求真实 7DTD 副作用验证；真实 OWIN 中的认证、静态托管、深链接和浏览器路由必须验证。

## 非目标

- 不新增、删除或重新定义服务器管理能力。
- 不重命名 `/api/v1` 路由，不修改 OpenAPI schema，不增加数据库 migration。
- 不批量移动 C# 项目、命名空间或 Application 能力以匹配前端菜单。
- 不建立通用插件系统、动态导航注册表、后端 BFF、第二套状态管理或仓库级前端共享包。
- 不重新设计每个 Feature 的表单、列表、状态机或 API parser。
- 不取消服务端授权，不把前端隐藏或 redirect 当作安全边界。
- 不在本变更中解决尚未完成的真实游戏、Discord、GeoIP、备份恢复或危险世界副作用证据。

## 目标信息架构

| 一级任务域 | 二级入口 | 页签、详情或上下文入口 | 主要角色 |
|---|---|---|---|
| 概览 | 综合概览 | 异常、玩家、任务和备份的关联入口 | 所有已认证角色，字段与动作按服务端裁剪 |
| 服务器运维 | 服务器控制、备份与恢复、计划与自动化、服务器配置、扩展、世界工具、控制台 | 扩展包含 Mods/功能模块；计划与自动化包含 Schedules/Automation | 子入口按既有 `Owner`、`Admin`、`Viewer` 合同裁剪 |
| 玩家 | 在线玩家、玩家记录、地图、访问名单 | 玩家 Profile、历史详情、游戏资源和玩家审计从对象详情、搜索或相关选择器进入 | 在线玩家、名单和资源按既有合同；历史、地图和写动作保持既有角色限制 |
| 社区 | 游戏聊天、传送、投票、城市 | 聊天包含实时、历史、禁言、设置和彩色聊天 | `Owner` |
| 经济与奖励 | 账户、交易、奖励、商业 | 奖励包含奖励包、每日奖励、补偿、成就/在线奖励；商业包含商店/兑换码 | `Owner` |
| 系统管理 | 用户与权限、API Keys、集成、审计与事件 | 用户与权限包含面板用户、游戏管理员、命令权限；集成包含 Discord/GeoIP | API Keys 对所有已认证角色；其他入口保持既有角色限制 |

一级任务域本身不引入空白落地页。桌面选择任务域时展开可达二级入口；移动端先进入该任务域的子入口列表。需要地址的父路径重定向到当前角色的稳定默认子页面，不能根据瞬时数据选择目标。

## 规范路由与兼容映射

目标规范路由如下：

```text
/
operations/
  server
  backups
  automation/schedules
  automation/rules
  configuration
  extensions/mods
  extensions/modules
  world
  console
players/
  history
  history/:crossplatformId
  profile/:crossplatformId
  map
  access-lists
  resources
community/
  chat/live
  chat/history
  chat/mutes
  chat/settings
  chat/appearance
  teleport
  votes
  cities
economy/
  accounts
  transactions
  rewards/packages
  rewards/daily
  rewards/operations
  rewards/achievements
  commerce/shop
  commerce/redeem-codes
system/
  access
  api-keys
  integrations/discord
  integrations/geoip
  audit
```

`/players` 继续作为在线玩家规范地址；`/community`、`/economy`、`/operations` 和 `/system` 只作为任务域父路径，进入稳定默认子页面或子入口选择，不形成无业务内容的页面。

| 现有地址 | 规范地址 |
|---|---|
| `/backups` | `/operations/backups` |
| `/schedules` | `/operations/automation/schedules` |
| `/automation` | `/operations/automation/rules` |
| `/server-configuration` | `/operations/configuration` |
| `/mods` | `/operations/extensions/mods` |
| `/modules` | `/operations/extensions/modules` |
| `/world-tools` | `/operations/world` |
| `/console-logs` | `/operations/console` |
| `/game-resources` | `/players/resources` |
| `/access-lists` | `/players/access-lists` |
| `/game-chat/live` | `/community/chat/live` |
| `/game-chat/history` | `/community/chat/history` |
| `/game-chat/mutes` | `/community/chat/mutes` |
| `/game-chat/settings` | `/community/chat/settings` |
| `/game-chat/colored` | `/community/chat/appearance` |
| `/economy/reward-packages` | `/economy/rewards/packages` |
| `/economy/daily-reward` | `/economy/rewards/daily` |
| `/economy/reward-operations` | `/economy/rewards/operations` |
| `/economy/achievement-online-rewards` | `/economy/rewards/achievements` |
| `/economy/shop` | `/economy/commerce/shop` |
| `/economy/redeem-codes` | `/economy/commerce/redeem-codes` |
| `/permissions` | `/system/access` |
| `/api-keys` | `/system/api-keys` |
| `/integrations/discord` | `/system/integrations/discord` |
| `/integrations/geoip` | `/system/integrations/geoip` |
| `/audit` | `/system/audit` |

`/players/history`、玩家详情、`/players/map`、`/community/teleport|votes|cities`、`/economy/accounts|transactions` 和 `/` 已符合目标语义，保持原地址。

每个 redirect 必须：

- 保留原始 `query` 和 `hash`，包括筛选、页签、分页、operation ID 和安全返回目标。
- 只重定向到规范地址，不形成 redirect 链或循环。
- 让最终目标路由执行认证与角色守卫；源路由不承担授权。
- 在刷新、浏览器前进/后退、登录返回和会话失效后保持目标业务能力。
- 作为低成本兼容合同至少保留一个完整发布周期，删除需要独立批准和链接盘点。

## 导航所有权与数据流

导航由一个显式、类型化的静态目录拥有。它只登记当前存在的 route name、任务域、父入口、i18n key、图标、顺序、搜索和快捷键属性，不扫描程序集、Feature 目录或运行时 API。

```text
generated routes + typed RouteMeta
                  |
navigationCatalog + current role
                  |
       useNavigation projection
        /        |          \
sidebar       search      breadcrumbs/shortcuts
```

- `RouteMeta` 拥有 `requiresAuth` 和允许角色；路由守卫与导航投影调用同一纯权限函数。
- `navigationCatalog` 拥有展示层级，不复制角色列表；目录项引用生成的 typed route name。
- `useNavigation` 只派生可达任务域、子入口、当前激活链、搜索结果和面包屑，不持久化第二份服务器或 Feature 状态。
- Dashboard Search、侧栏、移动抽屉、快捷键和面包屑不得维护自己的路由数组。
- 目录完整性测试拒绝不存在的 route name、重复规范地址、缺失中英文 key、无归属的可导航页面和指向 redirect 源的入口。
- 动态详情、登录、Forbidden 和兼容 redirect 不进入侧栏；详情通过父 route name 生成面包屑。

## Vue 组件边界

| 组件或模块 | 单一责任 | 输入与输出 |
|---|---|---|
| `AppShell.vue` | 安装全局布局并拥有侧栏、搜索的打开状态 | 消费导航投影；不定义页面目录或角色规则 |
| `PrimaryNavigation.vue` | 展示六个一级任务域和当前域 | typed items 输入；发出选择/关闭事件 |
| `SecondaryNavigation.vue` | 展示当前角色在当前域可达的二级入口 | active group 与 children 输入；发出选择/关闭事件 |
| `AppBreadcrumbs.vue` | 展示当前路由的稳定父链 | breadcrumb items 输入；不读取 Feature 状态 |
| `DashboardSearch` adapter | 将同一导航投影转换为搜索项 | accessible searchable routes 输入；选择后关闭搜索 |
| `navigationCatalog.ts` | 拥有任务域与页面展示层级 | 纯静态 typed data |
| `routeAccess.ts` | 判断当前身份能否访问 route meta | role/meta 输入；布尔结果 |
| `useNavigation.ts` | 组合目录、route 和角色并派生 UI | router/auth 输入；readonly computed 输出 |

所有新增 Vue 组件使用 Composition API、`<script setup lang="ts">`、最小源状态和派生 `computed`。route page 继续是 Feature composition surface；表单、请求、缓存、SSE 和危险确认仍由现有 Feature/composable 拥有。

## 页面组合与二级页签

- “服务器控制”组合现有 `server-status` 和 `server-operations` Feature，只移动概览中的运维入口或复用其组件，不新增浏览器命令、脚本参数或后端端点。
- “计划与自动化”“扩展”“游戏聊天”“奖励”“商业”“用户与权限”“集成”“审计与事件”使用局部二级页签或子导航。每个现有 Feature View 保持自己的查询和写入状态，不合并为大 Store 或大 SFC。
- 页签使用独立规范 route 表达可刷新和可分享状态；不以组件内临时索引取代路由。
- 游戏资源不占用玩家主导航的高频位置，但可由 Dashboard Search、玩家物品、奖励、商店和自动化选择器进入。
- 玩家 Profile 与历史详情不作为固定二级入口；它们从在线玩家、玩家记录、地图对象、审计目标和其他关联对象进入。

## 上下文操作

- 玩家详情提供档案、地图定位、禁言、封禁、传送和玩家审计入口；目标使用稳定玩家身份，不以显示名或 entity ID 猜测合并。
- 备份和恢复结果可以进入服务器状态、作业状态和对应审计；离线不能被表示为恢复成功。
- 服务器配置、Mod 或模块变更显示重启要求时，可以进入服务器控制；跳转不能自动执行或确认重启。
- Discord、GeoIP 和自动化失败可以进入带受控筛选的审计页面。
- 奖励、商店、玩家物品和自动化中的游戏资源入口可以带资源 ID 或安全筛选返回资源目录。
- URL 只保存稳定 ID、筛选、页码、页签和 operation ID；密码、Bearer Token、完整 API Key、危险确认、未提交秘密和动作成功状态不得进入 URL。
- 返回路径只接受经现有站内安全重定向规则验证的目标；没有安全目标时回到所属任务域的稳定页面。

## 权限与失败语义

- 一个任务域只在当前角色至少可访问一个子入口时显示。
- 导航隐藏、搜索过滤和控件隐藏只改善体验；直接访问仍执行 route guard，API 仍执行服务端认证与授权。
- 角色在会话中变化、用户禁用、Token 到期或 401 时，导航投影立即重新计算，清除危险确认并按现有认证流程离开受保护页面。
- 403 显示统一 Forbidden，不把用户重定向到看似成功的默认页；网络错误、5xx、SSE 断开和 Feature unavailable 不改变页面归属。
- redirect 目标对当前角色无权时进入 Forbidden；不能静默选择权限更低的相似页面。
- 导航目录缺失或 route name 无效属于构建/测试错误，不在运行时隐藏失败。

## 响应式、无障碍与双语

- 桌面固定侧栏首层只呈现六个任务域；当前域展开二级入口，其他域保持折叠，不能因动态文案改变固定工具尺寸。
- 移动端抽屉先展示任务域，再展示所选域的二级入口；当前页名称、服务器状态和紧急告警保持可见。
- 页面标题、面包屑、导航、搜索结果和工具提示使用同一 `zh-CN`/`en` key；技术标识与原始日志保持原文。
- 键盘可以打开任务域、移动到二级入口、执行选择并关闭移动抽屉；当前项、展开状态和 Forbidden 使用正确的可访问语义。
- `390x844` 与桌面视口不得出现导航文字遮挡、不可解释截断或页面级水平溢出。
- 语言切换保留当前规范 route、查询筛选和安全输入；不能重新进入旧 redirect 地址或丢失当前任务域。

## 实现边界与文件影响

主要前端影响范围：

```text
frontend/apps/admin/src/app/
  AppShell.vue
  router.ts
  navigation/
    navigationCatalog.ts
    navigationTypes.ts
    routeAccess.ts
    useNavigation.ts
    navigationRedirects.ts
frontend/apps/admin/src/components/navigation/
frontend/apps/admin/src/pages/
frontend/apps/admin/src/app/i18n/locales/
frontend/apps/admin/e2e/
```

既有 `src/features/*` 原则上只增加上下文入口、可复用 composition prop 或局部页签容器所需的小改动。OpenAPI snapshot、生成客户端、后端项目和 SQLite migration 预期无 diff；出现差异时停止并重新评估范围。

## 验证与发布门禁

### 自动化

- 纯导航测试覆盖目录完整性、唯一归属、角色过滤、激活链、搜索、面包屑、快捷键和父路由默认目标。
- Router 测试覆盖全部旧 URL、动态参数、`query`/`hash` 保留、目标守卫、登录返回、Forbidden、刷新和重定向循环。
- AppShell/组件测试覆盖六个一级任务域、当前域展开、移动抽屉关闭、角色变化和语言切换。
- Feature 聚焦测试覆盖新增上下文入口不会改变固定目标、危险确认和现有状态机。
- i18n key audit 覆盖 `zh-CN` 与 `en`；可导航条目不得使用直接写入的可见文案。
- Admin 聚合门禁为 lint、typecheck、全量 Vitest、生产构建和 `api:check`。

### 浏览器

- Playwright mock 项目覆盖桌面与 `390x844` 的六组导航、二级入口、搜索、面包屑、角色矩阵、旧 URL、前进/后退、刷新和上下文入口。
- 受控真实 OWIN 部署覆盖登录返回、直接加载新旧深链接、静态资源 fallback、API/SSE 认证、退出和 Token 失效。
- 本变更不跨游戏 API、SQLite 或外部服务副作用边界，因此不运行 publish、真实 7DTD、Discord/MaxMind sandbox、备份恢复或危险世界 smoke。

### 完成定义

- 一级导航始终不超过六项，每个已交付页面只有一个主要导航归属。
- Owner、Admin、Viewer 的侧栏、搜索和直接访问与既有服务端权限合同一致。
- 所有既有 URL 到达唯一规范地址并保留安全路由状态。
- `AppShell.vue` 不再定义页面数组、角色分组或独立搜索目录。
- 新旧地址、上下文入口、桌面、移动、键盘和双语门禁全部通过。
- OpenAPI、后端和数据库无非预期变更，当前文档只在实现与证据完成后提升事实。

## 发布与回滚

- 该变更在一个 Admin 版本中发布，不提供长期 legacy/new 导航开关。
- 发布前保留上一版完整 Mod artifact 或 Admin 静态产物；因为没有数据 migration 和 API 变更，失败时恢复上一版静态产物即可回滚。
- 旧 URL redirects 与新版本同时发布，避免书签、浏览器历史和外部运维文档立即失效。
- 导航、权限、静态 fallback 或登录返回任一真实 OWIN smoke 失败时停止发布，不通过局部隐藏入口规避。
- 发布完成后把已验证的信息架构、前端所有权和测试证据分别提升到 `docs/design.md`、`docs/architecture.md` 和 `docs/test.md`；只有发布后才更新 `CHANGELOG.md`。
