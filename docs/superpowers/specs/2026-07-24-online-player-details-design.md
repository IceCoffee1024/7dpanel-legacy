---
state: Current
document_role: Change Record
last_updated: "2026-07-24"
---

# 在线玩家详情设计规格

## 上游与变更范围

本规格落实[产品需求](../../PRD.md)中的 `CAP-02`、`CAP-05` 和 `NFR-02`，遵循[产品设计](../../design.md)的主表与详情抽屉规则、[后端目标架构蓝图](../../architecture/backend-target-blueprint.md)的在线玩家字段来源，以及 [Admin 前端目标架构蓝图](../../architecture/admin-frontend-target-blueprint.md)的 Feature 状态所有权。验证要求由[测试策略](../../test.md)拥有。

本变更扩展现有在线玩家事件投影、Owner-only API 和 Admin `/players` 页面。它不改变[在线玩家事件投影设计规格](2026-07-22-online-player-event-projection-design.md)已经实现的 Join/Save/Disconnect 生命周期、逐玩家 `observedAtUtc`、最终一致语义、无请求时回源和条件删除设计；只扩大一次 `SavePlayerData` observation 的批准字段，并为完整详情增加前端展示入口。

当前实现仍只投影 entity ID、名称、原生/可选跨平台身份、ping、level、health 和逐玩家观察时间。本文是待书面审核的 Change Record，不是实现证据；代码和适用验证完成后，才把持久结果提升到[当前系统架构](../../architecture.md)和测试证据。

## 目标与非目标

### 目标

- 从同一次成功 `SavePlayerData` 回调复制完整 25 字段在线玩家 observation，所有字段共享同一个 `observedAtUtc`。
- 保持 HTTP 查询只读产品自有不可变值，不访问 `EntityPlayer`、`ConnectionManager` 或其他游戏活对象，也不投递主线程任务。
- 让 Owner 在 Admin 主表快速比较玩家，并在详情抽屉查看身份、连接、当前状态和累计统计。
- 保持详情选择与踢出危险操作目标相互独立，刷新不能偷换目标。
- 对缺失可选值、旧 observation、玩家离线和身份变化给出明确且可验证的表现。

### 非目标

- 不增加独立玩家详情 API、离线玩家历史、分页、筛选、排序、地图或位置编辑。
- 不增加 `playerId`、`platformId`、`playerName`、`isAdmin` 等重复或可推导字段。
- 不解析 progression blob，不增加 Twitch、经验、技能点、game stage 或需要在线实体的字段。
- 不监听或 patch `PlayerStats` 网络包，不增加请求时回源、定时扫描、周期对账或第二套投影。
- 不改变 Owner-only 授权、10 秒轮询、90 秒前端行级过期提示或现有踢出审计语义。
- 不引入 OpenLayers、查询缓存库、全局 Store、通用详情框架或共享格式化包。

## 已验证的兼容性证据

只读私有参考子模块 `7dtd-reference/v3.0.1-b4` 提供以下实现证据：

- `ClientInfo` 直接携带 `PlatformId`、`CrossplatformId`、`DiscordUserId`、`device`、`compatibilityVersion`、`ping`，并通过 `ip` 属性取得当前网络地址。
- `GameManager.Instance.adminTools.Users.GetUserPermissionLevel(ClientInfo)` 在锁内比较原生身份、跨平台身份和 Steam 组权限，返回最小权限值；没有匹配项时返回 `1000`。
- `PlayerDataFile` 直接携带 `playerKills`、`zombieKills`、`deaths`、`score`、`bDead`、`distanceWalked`、`totalItemsCrafted`、`longestLife`、`currentLife` 和 `totalTimePlayed`。
- `PlayerDataFile.ecd.pos` 是序列化的浮点 `Vector3`；`ecd.stats.Health` 与 metadata 随同一次玩家数据上传到达。
- `distanceWalked` 的游戏语义为米，`totalTimePlayed`、`longestLife` 和 `currentLife` 的游戏语义为分钟；API 名称显式保存单位，避免调用方猜测。

这些反编译证据只确定字段来源和类型，不证明当前产品已经复制字段。Windows `v3.0.1-b4` 真实进程仍需验证值的实际可用性、权限读取和 JSON 输出。

## 在线玩家传输合同

成功响应根对象继续只含 `players`。每个玩家固定返回以下 25 个字段：

```json
{
  "entityId": 171,
  "name": "Player",
  "platformIdentity": {
    "combinedId": "Steam_76561198000000000",
    "platform": "Steam"
  },
  "crossplatformIdentity": null,
  "deviceType": "windows",
  "ip": "192.0.2.10",
  "ping": 42,
  "compatibilityVersion": "V 3.0.1",
  "discordUserId": null,
  "permissionLevel": 1000,
  "position": {
    "x": 100.5,
    "y": 51.0,
    "z": 200.25
  },
  "isDead": false,
  "health": 93,
  "maxHealth": 100,
  "level": 18,
  "score": 827,
  "zombieKills": 317,
  "playerKills": 2,
  "deaths": 4,
  "totalTimePlayedMinutes": 4823.5,
  "distanceWalkedMeters": 127540.75,
  "totalItemsCrafted": 2360,
  "longestLifeMinutes": 920.25,
  "currentLifeMinutes": 134.5,
  "observedAtUtc": "2026-07-24T09:30:00.0000000Z"
}
```

### 类型与空值

- `entityId`、`ping`、`permissionLevel`、`health`、`maxHealth`、`level`、`score`、`zombieKills`、`playerKills` 和 `deaths` 是 JSON integer；`totalItemsCrafted` 是非负 JSON integer。生命来源必须先为有限浮点，再沿用当前 C# `(int)` 向零截断语义；产品不额外推导或修正 `health <= maxHealth`。
- `position.x/y/z`、四个分钟或距离字段必须是有限 JSON number；累计距离、制作数量和时长不得为负。
- `deviceType` 只允许 `linux`、`mac`、`windows`、`playStation`、`xbox`、`unknown`。
- `crossplatformIdentity`、`ip`、`compatibilityVersion` 和 `discordUserId` 可为 `null`。非空字符串不得只含空白；Discord ID 使用十进制字符串，避免 JavaScript 对 `ulong` 的精度损失。
- `ClientInfo.DiscordUserId == 0` 映射为 `null`；非零值使用 invariant culture 转为十进制字符串。
- `permissionLevel` 直接采用游戏 `GameManager.Instance.adminTools.Users.GetUserPermissionLevel(ClientInfo)` 结果；`0` 权限最高，`1000` 是未匹配默认值。API 不额外返回 `isAdmin`。
- `observedAtUtc` 是 UTC 时间。不存在根级捕获时间，也不返回服务端 stale 标记。

## SavePlayerData 复制与失败语义

```text
SavePlayerData(ClientInfo client, PlayerDataFile playerData)
  -> validate entity, player data and identity
  -> copy required ClientInfo values
  -> best-effort copy nullable diagnostic values
  -> call GameManager.Instance.adminTools.Users.GetUserPermissionLevel(client)
  -> copy PlayerDataFile metadata, position, health and statistics
  -> reject non-finite or invalid required values
  -> capture one observedAtUtc
  -> construct one immutable PlayerSnapshot
  -> atomically upsert the existing observation
```

字段来源固定为：

| Application / HTTP 字段 | 游戏来源 | 复制规则 |
|---|---|---|
| `EntityId`、`Name` | `ClientInfo.entityId`、`PlayerDataFile.metadata.Name` | 必填；entity ID 必须与 `PlayerDataFile.id` 相等 |
| 两类身份 | `ClientInfo.PlatformId`、`CrossplatformId` | 原生身份必填；跨平台身份整体可空 |
| `DeviceType` | `ClientInfo.device` | 显式 switch，未知枚举值映射 `unknown` |
| `Ip` | `ClientInfo.ip` | 可空；属性访问异常只把本字段置空，不拒绝 observation |
| `CompatibilityVersion` | `ClientInfo.compatibilityVersion` | trim 后空白映射 `null` |
| `DiscordUserId` | `ClientInfo.DiscordUserId` | `0 -> null`，否则 invariant 十进制字符串 |
| `PermissionLevel` | `GameManager.Instance.adminTools.Users.GetUserPermissionLevel(ClientInfo)` | 必填；读取异常拒绝本次 observation |
| `Position` | `PlayerDataFile.ecd.pos` | 三轴都必须有限 |
| `IsDead` | `PlayerDataFile.bDead` | 直接复制 |
| `Health`、`MaxHealth` | `ecd.stats.Health.Value`、`ModifiedMax` | 同一 Health 对象的有限浮点值以 C# `(int)` 向零截断；结构缺失或非有限值拒绝，不强加两者关系 |
| `Level` | `PlayerDataFile.metadata.Level` | 直接复制整数 |
| 分数、击杀和死亡 | `score`、`zombieKills`、`playerKills`、`deaths` | 直接复制整数 |
| 累计时长、距离和制作 | `totalTimePlayed`、`distanceWalked`、`totalItemsCrafted`、`longestLife`、`currentLife` | 浮点必须有限且非负；制作数量保留非负整数语义 |
| `ObservedAtUtc` | 注入 UTC clock | 全部字段复制成功后捕获一次 |

`ip` 是唯一允许捕获单字段访问异常并降级为 `null` 的属性；已经取得但为空白的 IP 同样归一化为 `null`。其他必填复制失败、非有限数值或权限读取失败都拒绝整次 observation，保留上一条有效值并沿用现有安全日志与后续 Save 自然重试。

Application `PlayerSnapshot` 继续是一个不可变构造完成值。位置使用产品自有不可变值类型，不把 Unity `Vector3` 传出 SevenDays Adapter。Web DTO 只做显式字段映射，不直接序列化 Application 或游戏对象。

## Admin 页面设计

### 组件边界

| 组件 | 单一职责 | Props | Emits |
|---|---|---|---|
| `OnlinePlayersView` | 组合查询、详情选择和踢出流程 | 无 | 无 |
| `OnlinePlayersTable` | 桌面高频比较 | `players`、`canKick` | `viewDetails(player)`、`kickPlayer(player)` |
| `OnlinePlayersList` | 窄屏高频比较 | `players`、`canKick` | `viewDetails(player)`、`kickPlayer(player)` |
| `OnlinePlayerDetailsSlideover` | 展示一个只读 observation 及 unavailable 状态 | `open`、`player`、`unavailable`、`canKick` | `update:open`、`copyValue(value)`、`kickPlayer(player)` |
| `KickPlayerDialog` | 固定危险操作目标并收集原因 | 既有合同 | 既有合同 |

不创建详情 composable：选择键、最后 observation 和开关只由一个页面组合组件消费，三个 `shallowRef` 与纯派生 `computed` 足以表达。只有实现中出现独立复用或副作用复杂度，才在不改变合同的前提下提取 Feature 内 composable。

### 主表与移动列表

桌面列固定为玩家、状态、等级、延迟、设备、更新时间、操作：

- 玩家：名称和 entity ID。
- 状态：存活/死亡，以及 `health / maxHealth`。
- 等级、延迟和设备：便于横向比较；延迟显示 `ms`。
- 更新时间：逐玩家观察时间和既有 90 秒过期提示。
- 操作：详情入口始终存在；踢出入口只在现有授权允许时出现。

完整身份、IP、位置和累计统计从主表移到详情，降低横向密度。移动列表保留玩家、状态、等级、延迟、更新时间和详情入口；不依赖 CSS 隐藏主表中的关键操作。

### 详情抽屉

使用 `USlideover` 从右侧打开，桌面提供稳定宽度，窄屏占满可用宽度。抽屉按普通分区和分隔线排列，不把分区包成卡片：

1. **身份**：entity ID、原生身份、跨平台身份、Discord ID。
2. **连接**：IP、设备、兼容版本、Ping、权限等级。
3. **当前状态**：位置、存活状态、生命/最大生命、等级、观察时间。
4. **累计统计**：分数、丧尸击杀、玩家击杀、死亡次数、累计游戏时长、步行距离、制作物品数、最长存活时长、当前存活时长。

原生身份、跨平台身份、Discord ID 和 IP 使用等宽文本；存在的身份和 IP 提供 Lucide copy 图标按钮及明确 `aria-label`。空值统一显示“未知”，不显示“未绑定”、空白、破折号或 `null`。

坐标和距离以 `Math.round` 取整后使用当前语言数字格式；API 值不改写。分钟值按向下分解为天、小时和分钟，舍入到最接近的整分钟后格式化；值小于半分钟时显示“少于 1 分钟”，不虚构小数秒精度。技术标识和原始值不翻译，标签、设备名称、状态和单位按当前语言显示。

### 刷新、离线与危险操作

`OnlinePlayersView` 保存稳定选择键 `{ entityId, combinedId }` 和最后展示的完整 observation：

- 打开详情时同时保存键和当前 observation。
- 尚未锁存 unavailable 时，新成功快照包含同一 entity ID 与原生 combined ID 则用新 observation 更新抽屉。
- 新成功快照缺少该组合，或相同 entity ID 对应不同身份时，保留最后 observation，锁存 unavailable，显示“玩家已不在线或身份已变化”并禁用踢出。后续同身份重新出现也不自动恢复；关闭后重新打开才建立新的详情目标。
- 请求级 Stale 只表示刷新失败，继续展示最后 observation 和原观察时间；它不等同于玩家已经离线。
- 用户关闭抽屉后清除键、最后 observation 和 unavailable。

详情的 `canKick` 必须同时满足现有授权允许、查询 `state === 'fresh'` 且 unavailable 未锁存。Stale、Offline、Forbidden、Session expired 或最近明确的 `game_not_ready` 状态不允许从旧详情发起新踢出；恢复 Fresh 后，只有从未锁存 unavailable 的同一详情可以重新操作。

从抽屉或列表发起踢出时，现有 `selectedPlayer` 只保存点击当时的完整 observation。抽屉刷新、关闭或 unavailable 变化都不能替换已打开的确认目标；确认成功、离线、身份变化和权限变化继续使用现有处理语义。

## API 边界校验

Players Feature 的严格 parser 必须逐字段验证，不通过类型断言把未验证 JSON 直接视为页面模型：

- 根对象只能按既有合同读取 `players`；每个玩家必须具备全部 25 个键。
- 身份对象、位置对象、设备枚举、UTC 时间、integer、有限 number 和可空字符串分别校验。
- `NaN`/Infinity 不会出现在合法 JSON，但 parser 仍只接受 `Number.isFinite` 的数值。
- 数组任一玩家无效即拒绝整个新响应，保留最后成功快照；不混合不同响应的玩家字段。
- 页面模型保持单位后缀，不把分钟或米转换后的展示文本写回权威数据。

## 验证设计

### 后端

- Application 快照保存 25 字段、位置不可变值和逐玩家观察时间，源集合或后续 observation 更新不能修改既有结果。
- SevenDays 投影测试用一条 Save 复制全部字段，并断言所有值来自同一输入；下一条 Save 整体替换，上一查询结果保持稳定。
- 覆盖全部设备枚举及未知值、Discord `0`/最大 `ulong`、权限原生/跨平台/组最小值和默认 `1000`。
- 覆盖 IP getter 异常与空白归一化、可空兼容版本和跨平台身份，以及位置/统计非有限或负值拒绝并保留旧 observation。
- Web API 测试锁定根对象和 25 字段 camelCase 白名单、位置对象、JSON 类型、可空值、单位后缀、排序、Owner-only 和 readiness；查询不得调用 Dispatcher 或游戏活对象。

### 前端

- Parser 接受完整合同和所有合法可空值；逐项拒绝缺字段、未知设备、错误 identity/position、错误 null、非 integer 及非有限数值。
- 纯格式化测试覆盖正负坐标四舍五入、距离千位分隔、分钟边界、所有空值“未知”和设备本地化。
- Table/List 组件都显示批准的主字段并上抛同一个详情玩家；不在主表渲染 IP、完整身份、位置或累计统计。
- Slideover 测试覆盖四个分区、全部字段、复制入口、权限说明、过期 observation 和 unavailable 告警。
- View 测试覆盖同身份刷新更新详情、`A 在线 -> A 缺失 -> A 同身份重现` 仍锁存 unavailable、entity ID 复用不偷换身份、关闭后重新打开建立新目标、Stale/Offline/Forbidden/Session expired/game-not-ready 禁用详情踢出，以及踢出确认继续固定原目标。
- 生产构建、lint、typecheck 和完整 Vitest 通过；真实浏览器在桌面、`390x844` 和 320 CSS 像素宽度验证抽屉、焦点、遮罩、滚动、文本溢出和无水平页面滚动。

### 真实进程

- Windows `v3.0.1-b4` 受控玩家至少经过两次 `SavePlayerData`，验证 25 字段 JSON、逐玩家观察时间更新和无请求时主线程任务。
- 将一个测试身份配置为直接、跨平台或组权限，核对 `permissionLevel`；未配置身份验证默认 `1000`。
- 核对位置、死亡/生命、分数、击杀、死亡、时长、距离和制作字段与同一次上传的游戏数据；无法稳定操纵的统计必须记录证据限制，不能从非零 JSON 推导单位正确。
- 玩家断开后 API 删除条目；已打开 Admin 详情在下一次成功刷新后进入 unavailable，不切换到复用 entity ID 的其他身份。
- 本切片不要求发布归档、Linux smoke 或真实踢出；它们仍由候选发布和各自边界负责。

## 文件与实施边界

预期修改集中在现有所有者：

- Application `PlayerSnapshot` 与产品自有位置值。
- SevenDays `SevenDaysOnlinePlayerProjection` 的同步字段复制。
- Web `PlayersController` 的显式 DTO 映射。
- Admin Players Feature 的 API parser、格式化、Table/List/View 和新增详情 Slideover。
- 对应后端、前端测试与实现完成后的 Current 文档提升。

不新增项目、数据库 migration、HTTP 路由、npm/NuGet 依赖、全局 Store、共享包或通用抽象。若实现发现 25 字段不能从一次 `SavePlayerData` 安全取得，停止实施并修订本规格，不以请求时回源或在线实体读取绕过设计边界。

## 文档影响与提升条件

- [产品需求](../../PRD.md)拥有完整在线玩家信息、单位和授权结果；本规格不改变其产品含义。
- [产品设计](../../design.md)拥有主表、详情抽屉、格式化和刷新交互；本规格只为实施提供组件与状态细节。
- 两个 Target 蓝图拥有后端字段来源和前端状态边界；未经实现不得提升为 Current。
- 实现并验证后，更新[系统架构](../../architecture.md)的投影字段、HTTP DTO、Admin 组件和当前测试证据，删除“不返回 IP、位置、战斗统计”的旧 Current 事实。
- [测试策略](../../test.md)拥有自动化、真实进程和浏览器门禁；历史旧字段 smoke 保留为历史证据并明确不证明新合同。
- 不修改 `CHANGELOG.md`，因为本次仍是未发布设计。

## 书面批准检查点

批准本规格即确认：

- API 返回固定 25 字段，单位后缀、可空值和设备枚举按本文定义；
- 全部字段从同一次成功 `SavePlayerData` observation 复制并共享 `observedAtUtc`；
- `permissionLevel` 使用游戏现有综合权限算法，Discord ID 使用可空十进制字符串；
- 查询不访问游戏活对象，不增加详情端点、PlayerStats patch 或请求时回源；
- Admin 使用主表加详情抽屉，坐标和距离显示整数，所有空值显示“未知”；
- 详情刷新按 entity ID 与原生身份匹配；成功刷新确认离线或身份变化后锁存最后值与不可用状态直到关闭，同身份重现也不自动恢复，并始终禁用危险操作；
- 踢出确认目标保持独立固定；
- 实现前先创建并审核一份链接本规格的实施计划。