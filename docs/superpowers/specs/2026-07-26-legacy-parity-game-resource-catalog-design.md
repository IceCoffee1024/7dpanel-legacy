---
state: Current
document_role: Change Record
last_updated: "2026-07-26"
---

# 旧版本功能对齐：游戏资源目录设计规格

## 上游与状态

本规格定义旧版本功能对齐的第一项可实施纵向切片，落实[产品需求](../../PRD.md) `CAP-08`、`CAP-12` 与 `NFR-02`，遵循[旧版本功能对齐目标蓝图](../../architecture/legacy-feature-parity-target-blueprint.md)的阶段 1。当前系统事实仍以[系统架构](../../architecture.md)为准；代码和适用验证完成前，本文不得作为已实现证据。

本规格是用户于 2026-07-26 批准的 Current Change Record，表示设计合同已经确认，不表示代码已经实现。实施按唯一对应计划 `docs/superpowers/plans/2026-07-26-legacy-parity-game-resource-catalog.md` 推进；当前实现事实仍只能从代码、自动化证据和 Current 系统文档确认。

## 用户结果

认证后的管理用户可以搜索当前 7DTD 进程已加载的物品和方块，查看内部名称、当前界面语言的游戏本地化名称、类型、最大堆叠、品质能力、创意模式可见性、图标和图标色值，并复制内部名称。该目录随后由背包、发放物品、奖励包、商店和自动化复用；本切片不实现这些写能力。

## 旧版可执行证据与采纳边界

固定旧 backend `277996d` 的 `GameServerController.GetGameItems` 提供以下来源线索：

- 遍历 `ItemClass.list` 并跳过无效记录；
- 以 `Block.ItemsStartHere`、`ItemClass.IsBlock()` 和 `CreativeMode` 区分物品、方块与用户隐藏项；
- 提取内部名称、本地化名称、图标名、图标 tint、最大堆叠和是否支持品质；
- 旧前端按本地化名称与内部名称展示和筛选，并把内部名称复用于商店与发放物品选择器。

旧版图标接口接受文件名和可选色值，在基础目录及已加载 Mod 的 atlas 目录中搜索 PNG；该行为证明“管理页面需要实际游戏图标”，但不采纳以下实现：

- HTTP 请求线程直接读取 `ItemClass`、`Localization` 或 Mod 活对象；
- 匿名访问图标；
- 浏览器提交图标文件名、相对路径或 tint 后缀；
- 每个请求递归扫描目录并选取第一个匹配文件；
- 通过旧 Controller、DTO、缓存特性、Element Plus 页面或生成代码进行复制。

新实现只根据上述行为证据和当前 `7dtd-reference/v3.0.1-b4` 重新编写字段映射。字段不存在、含义变化或无法在当前版本证明时，使用明确不可用语义，不以旧代码作为正确性依据。

## 范围

### 包含

- 当前进程内物品与方块的版本化只读快照；
- `zh-CN` 与 `en` 对应游戏本地化名称，缺失时保留内部名称；
- 内部名称、资源类别、创意模式可见性、最大堆叠、品质能力、图标可用状态和六位大写 tint；
- 服务端搜索、类别筛选、隐藏项筛选和有界分页；
- 不接受路径的认证图标读取；
- Admin 独立路由、导航入口、筛选、列表/窄屏布局、复制与诚实状态；
- OpenAPI 与现有生成客户端链路。

### 不包含

- 背包、技能、玩家 Profile、发放/删除物品或玩家重置；
- 奖励包、商店、经济、兑换码或自动化动作；
- 完整本地化字典查询或任意 key 浏览器；
- 图标上传、替换、编辑、下载打包或文件浏览；
- 技能及其他通用 UI 图标；它们在玩家技能切片出现真实消费者时扩展，不提前开放任意 UI atlas 浏览；
- 服务端 PNG tint 重绘；页面展示原始 PNG、tint 色块和色值，不声称缩略图已按游戏内颜色重绘；
- SQLite migration、历史版本存档、旧数据库导入、审计记录或 `gap` 表；
- 通用资源 registry、通用文件服务、通用缓存框架或跨 Feature 组件包。

## 权限

| 能力 | `Owner` | `Admin` | `Viewer` |
|---|---|---|---|
| 查询正常创意模式物品与方块 | 允许 | 允许 | 允许 |
| 读取查询结果对应图标 | 允许 | 允许 | 允许 |
| 包含用户隐藏、开发或测试资源 | 允许 | 拒绝 | 拒绝 |
| 读取隐藏资源图标 | 允许 | 拒绝 | 拒绝 |

所有接口要求现有 Header Bearer 身份，不提供匿名图标端点。非 `Owner` 请求 `includeHidden=true` 返回 403；未授权主体请求隐藏资源图标返回 404，避免通过资源 ID 探测隐藏目录。前端隐藏筛选不代替服务端授权。

## 运行链路与状态所有权

```text
GameStartDone
  -> bounded game-thread dispatcher
  -> copy ItemClass + localization + loaded-mod asset-root descriptors
  -> immutable scalar draft
  -> bounded background icon indexing under approved roots
  -> atomically publish GameResourceCatalogSnapshot

Authenticated HTTP
  -> Application query + role policy
  -> immutable snapshot only
  -> JSON page or opaque resource-id icon stream
  -> Admin generated client + game-resources feature
```

- `GameStartDone` 后只构建一次当前进程快照；Mod、物品定义和本地化只有重启后才变化，不增加周期轮询。
- 对 `ItemClass`、`Localization`、`ModManager` 或 Unity 类型的访问全部在现有有界游戏线程调度器内完成，并立即复制为产品自有标量。
- 图标目录枚举和文件元数据读取不在游戏主线程执行。构建任务只能有一个；关服时停止接收新请求并在现有生命周期时限内结束，不为目录引入独立无界线程。
- 快照以一次原子替换发布。HTTP、OpenAPI DTO 和 Admin 永远不持有 7DTD、Unity、`FileInfo` 或 `DirectoryInfo` 对象。
- 本切片不持久化快照。进程重启后重新构建；构建失败时没有跨版本旧快照可冒充当前数据。

## 快照与字段合同

### 目录元数据

| 字段 | 语义 |
|---|---|
| `catalogVersion` | 本进程不可预测的目录版本标识；每次成功发布新快照变化，不含服务器路径 |
| `gameVersion` | 当前产品已确认的 7DTD 版本字符串；不可用时为 `null` |
| `observedAtUtc` | 完成游戏标量复制的 UTC 时间，不使用 HTTP 查询时间 |
| `total` | 应用全部筛选后的结果数量 |
| `page` / `pageSize` | 从 1 开始的页码和实际有界页大小 |

### 资源条目

| 字段 | 类型与规则 |
|---|---|
| `resourceId` | 由服务端为当前快照生成的不透明 URL-safe 标识；浏览器不能据此构造路径，重启后不承诺稳定 |
| `numericId` | 当前游戏表中的整数 ID，仅用于诊断与排序兜底，不作为跨版本业务主键 |
| `internalName` | 非空游戏内部名称，是未来配置和类型化动作保存的稳定候选键；原样保留大小写 |
| `localizedName` | 按请求 `language` 返回的游戏名称；无条目、空白或列不可用时为 `null`，页面回退显示 `internalName` |
| `kind` | `item` 或 `block`，来自当前版本确认的 `IsBlock` 语义；不只依赖 ID 分界猜测 |
| `visibility` | `public` 或 `hidden`；未知枚举值按 `hidden` 处理 |
| `maxStack` | 大于等于 1 的整数；原始值无效时为 `null`，不伪造为 1 |
| `hasQuality` | 当前版本可确认时为布尔值，否则为 `null` |
| `iconStatus` | `available`、`missing` 或 `invalid`；目录查询不因单个图标失败而失败 |
| `iconTintHex` | 规范化六位大写 RGB，不带 `#`；白色或无 tint 为 `null`，非法值为 `null` 并使诊断计数增加 |

同一 `internalName` 出现多个有效定义时，快照按游戏最终可见定义保留一项，并记录不含路径的重复诊断计数。若当前 API 不能证明最终覆盖顺序，构建失败为 `catalog-ambiguous`，不得按文件系统返回顺序任意选择。

## 本地化

- API 只接受 `zh-CN` 或 `en`，省略时使用 `en`；其他值返回 400 `unsupported-game-resource-language`。
- SevenDays Adapter 从当前 `Localization` 元数据确认列索引，再复制每个资源键的简体中文和英文值；不得硬编码数组下标或假设 `enum` 整数永远等于列位置。
- 简体中文映射到当前版本确认的 `schinese` 列，英文映射到 `english`；列不存在时对应名称全部为 `null`，快照仍可部分可用。
- 不自动用英文填充缺失中文。Admin 先显示所选语言名称，缺失时明确回退内部名称，避免把英文误称为中文游戏文本。

## 图标安全边界

### 建索引

- 只使用进程组合时确认的基础 Item Icon 根目录，以及游戏报告的已加载 Mod 根目录下批准的 Item Icon atlas 子目录。
- 在游戏线程复制根目录描述后，后台将每个根解析为绝对规范路径并验证位于批准的游戏/Mod 根内；不存在、符号链接或重解析点逃逸、权限拒绝和非法路径只产生受控诊断。
- 文件索引只接受普通 `.png` 文件和由当前资源 `iconName` 推导的叶文件名。图标名含 `/`、`\`、`..`、驱动器/卷分隔符、控制字符或超过 128 字符时标记 `invalid`。
- 搜索顺序使用当前游戏确认的基础资源与已加载 Mod 覆盖顺序；目录枚举顺序不能决定业务结果。HTTP 请求期间不递归扫描目录。
- 快照只保存 `resourceId` 到已验证规范文件的私有映射；JSON 不返回图标名、根目录、相对路径或绝对路径。

### 读取

- `GET /api/v1/game-resources/{resourceId}/icon` 只在当前原子快照映射中查找，不接受文件名、扩展名、tint、路径或查询参数。
- 打开文件前再次按 `LiteralPath` 等价语义确认规范路径仍位于已批准根目录，且文件仍是普通 PNG；被替换、删除或越界时返回 404 并记录安全诊断。
- 响应固定为 `image/png`、`X-Content-Type-Options: nosniff` 和 `Cache-Control: private`，ETag 至少关联 `catalogVersion`、资源 ID、文件长度与最后写入时间；匹配 `If-None-Match` 时返回 304。
- 解码或读取失败返回 404/稳定 Problem Details，不回显真实路径或异常。单图标失败不使目录快照 Stale。

## HTTP 合同

### 查询目录

`GET /api/v1/game-resources`

| 参数 | 合同 |
|---|---|
| `search` | 可选，trim 后 1..100 字符；按 `internalName` 和所选 `localizedName` 做 ordinal-ignore-case 包含匹配 |
| `kind` | `all`、`item` 或 `block`，默认 `all` |
| `includeHidden` | 默认 `false`，只有 `Owner` 可设为 `true` |
| `language` | `zh-CN` 或 `en`，默认 `en` |
| `page` | 1..100000，默认 1 |
| `pageSize` | 1..100，默认 50 |

结果先按隐藏策略、类别和搜索筛选，再按 `localizedName ?? internalName`、`internalName`、`numericId` 做 ordinal-ignore-case 稳定排序并分页。超出最后一页返回 200 空 `items` 和真实 `total`，不是 404。

### 错误与可用状态

| 条件 | HTTP/错误码 |
|---|---|
| 未认证或凭据失效 | 401，沿用现有认证处理 |
| 非 Owner 查询隐藏项 | 403 `game-resource-hidden-forbidden` |
| 查询参数无效 | 400 稳定 Problem Details |
| 游戏尚未 ready 或目录正在首次构建 | 503 `game-resource-catalog-building`，可带有界 `Retry-After` |
| 构建失败或当前版本来源不可用 | 503 `game-resource-catalog-unavailable`，不返回空数组冒充成功 |
| 当前资源无图标、图标失效或无权探测 | 404 `game-resource-icon-not-found` |

目录成功但部分本地化或图标不可用时仍返回 200，每项使用可空字段和 `iconStatus`，根响应带不含路径与内部异常的 `warnings` 代码集合。HTTP 200 只表示当前目录快照查询成功，不表示所有游戏资源都有翻译或图标。

## 后端边界

### Application

- 新建能力范围内的 `GameResources` 查询模型、参数验证、角色策略和不可变快照读取端口；不放入 Domain，因为本切片没有持久业务不变量。
- 列表查询和图标查询分别为类型化用例。图标读取端口接收 `resourceId` 和当前目录版本上下文，不接收路径。
- 搜索、权限裁剪、排序和分页由 Application 拥有，确保 HTTP 与后续内部消费者使用同一语义。

### SevenDays 与 Local 边界

- SevenDays Adapter 负责游戏线程内的 ItemClass、Localization、游戏版本和已加载 Mod 根描述复制。
- SevenDays Adapter 同时负责游戏资产根描述、图标索引和受限文件打开，因为这些路径与覆盖顺序属于 7DTD/Mod 外部边界；本切片不创建新的 Local 项目或通用文件服务。
- Bootstrap 只组合目录构建生命周期、用例和 Controller 依赖；目录服务不自行定位全局 Service Provider。

### Web 与 OpenAPI

- Web Adapter 只绑定参数、现有角色身份、Problem Details、ETag 和二进制响应；Controller 不访问 7DTD、Unity、目录枚举或缓存实现。
- JSON 使用现有 camelCase、UTC 和 Nullable 规则；图标二进制端点必须进入运行时 OpenAPI，并保留与 SPA fallback 的隔离。
- Admin 继续通过现有 Hey API 生成客户端调用 JSON 查询。图标 URL 由一个 Feature 内辅助函数根据服务端 `resourceId` 构造，并由现有认证 fetch 取得 Blob；Bearer 不进入 URL。

## Admin 体验

- 新增受保护 `/game-resources` 路由，导航名称为“游戏资源”/“Game resources”，位于“玩家与世界”组。三个认证角色可见；只有 `Owner` 看见“包含隐藏资源”筛选。
- 页面顶部是一个搜索框、物品/方块分段筛选、Owner 隐藏项开关和结果数量。筛选写入 URL；搜索输入停顿 250ms 后请求，路由恢复时重建同一查询。
- 桌面使用紧凑表格：图标、本地化名称、内部名称、类型、最大堆叠、品质、可见性和复制操作。窄屏使用单列条目，内部名称和复制入口始终可见，不依赖横向滚动。
- 图标按进入视口后懒加载，通过 Header Bearer fetch 为 Blob URL；组件卸载、结果替换或会话结束时撤销 Blob URL。图标缺失、无权或失败显示固定占位，不显示破图或真实后端错误。
- tint 以色块和 `#RRGGBB` 文本展示；本切片不修改 PNG 像素，帮助文本说明实际游戏内图标可能应用该 tint。
- 点击复制只复制 `internalName`，成功反馈使用当前语言。页面不提供发放、删除、商店或奖励快捷动作，避免未实现能力形成伪入口。

### 页面状态

| 状态 | 行为 |
|---|---|
| Loading | 首次查询显示与表格/条目布局一致的骨架 |
| Success | 显示目录版本、游戏版本（可用时）、采样时间、总数和当前页 |
| Empty | 明确说明筛选无结果，保留筛选并提供清除入口 |
| Refresh failed | 保留最后成功页及原采样时间，标记 Stale，显式重试；不清空列表 |
| Building | 无成功快照时显示游戏目录正在构建并按 `Retry-After` 有界重试 |
| Unavailable | 显示目录不可用和手动重试，不把结果显示为 0 项 |
| Partial | 列表可用但存在翻译或图标 warning；仅受影响字段显示占位 |
| Forbidden | 直接路由无权或隐藏筛选被拒绝时显示稳定权限状态，不泄露隐藏资源 |

## 缓存与性能

- 目录快照只保留当前进程的一份不可变标量和图标映射，不复制 PNG 到内存，不写 SQLite。
- 列表查询的目标规模是当前游戏全部 ItemClass；页面大小最大 100，单次请求不返回完整目录。
- 搜索可以在不可变内存集合上线性执行。只有真实测量证明延迟不可接受时，才增加能力内索引；本切片不预建通用搜索服务。
- 图标按流读取并使用私有浏览器缓存；不得一次预取整页以外图标，也不得在服务端为 tint 生成无限组合缓存。

## 日志、审计与 `gap`

目录是当前进程只读派生快照，不记录业务审计，也不创建 `_gap` 表。构建开始、成功、失败、重复资源数、无效图标数和读取安全拒绝进入不含真实路径的结构化产品日志与概览诊断；单个用户查询不逐条写业务审计。

`gap` 用于已经接受但可能未持久化的异步历史观察。目录构建失败是当前状态不可用，不是历史记录缺失，因此用 503、warning 和诊断表达，不能复用聊天、命令或玩家历史 gap。

## 精简验证

### 必须新增

- Application：语言、角色、隐藏策略、搜索、类别、稳定排序、分页和错误映射；
- SevenDays：所有游戏访问经 dispatcher、字段空值/枚举/单位映射、两种语言列、重复内部名和不可变快照；
- 图标边界：允许根、覆盖顺序、非法叶名、`..`/分隔符/绝对路径、重解析点逃逸、请求期间不扫描、文件替换、Content-Type、ETag/304 与路径脱敏；
- Web：401/403/400/503/200/404、固定 JSON 形状、二进制端点和 OpenAPI operationId；
- Admin：响应 parser、URL 筛选、Building/Partial/Stale/Empty、隐藏筛选权限、Blob URL 生命周期、图标占位、复制和双语文案。

### 稳定后只运行一次

- 受影响后端项目测试与 Release 构建；
- OpenAPI 快照、生成客户端漂移检查；
- Admin 聚焦 Vitest、typecheck、定向 lint 和生产构建。

### 本切片默认不运行

- SQLite/migration、备份、经济、玩家动作和聊天测试；
- Playwright、发布物组装、恢复演练；
- Windows/Linux 完整真实进程 smoke。

编译所用当前 7DTD 引用和确定性 Adapter 测试先证明 API 形状。只有字段列映射、Mod 图标覆盖顺序或真实文件根无法由引用证据确认时，实施完成前增加一次只读 Windows `v3.0.1-b4` 窄 smoke；它只查询目录和几个基础/Mod 图标，不触发玩家或世界副作用。

## 完成条件

1. 三个认证角色可以查询正常资源，只有 `Owner` 可以包含隐藏资源；
2. 目录只从游戏线程复制的不可变快照读取，HTTP 不接触游戏活对象；
3. 两种产品语言、缺失翻译、物品/方块、堆叠、品质、隐藏状态和 tint 语义明确；
4. 图标端点不接受路径或文件名，不能越出批准根，也不泄露服务器路径；
5. Admin 页面完整处理 Loading、Empty、Success、Partial、Stale、Building、Unavailable 和 Forbidden；
6. 页面只读且不出现未实现写能力入口；
7. OpenAPI、生成客户端和适用聚焦验证通过，且没有为本切片重复无关真实环境门禁；
8. 实现和验证证据更新到当前架构、测试和最近所属 README；未取得的证据保持明确缺口，不把本 Current 规格本身当作已实现证明。
