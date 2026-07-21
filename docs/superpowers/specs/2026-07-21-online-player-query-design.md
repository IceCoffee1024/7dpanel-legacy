---
state: Current
document_role: Design Spec
last_updated: "2026-07-21"
---

# 在线玩家只读查询纵向切片设计规格

## 上游与范围

本规格落实[产品需求](../../PRD.md)中的 `CAP-01`、`CAP-02`、`NFR-01` 和 `NFR-02`，并实现[后端目标架构蓝图](../../architecture/backend-target-blueprint.md#在线玩家)中已经批准的认证 HTTP -> Application 用例 -> 类型化玩家查询端口 -> 游戏主线程 -> 不可变玩家快照路径。

当前后端已经具备持久 `Owner`、Header Basic/Bearer 认证、统一 Problem Details、Microsoft DI 请求作用域、独立 game-readiness 状态、Application 项目和 `GameThreadDispatcher`。本切片在这些边界上增加首个在线玩家只读查询，用真实玩家消费者验证从活动 7DTD 对象到线程安全 Application 读模型的映射。

当前产品开始使用阶段只有服主本人，因此本切片只授权 `Owner`。这是一项当前交付约束，不删除[产品需求](../../PRD.md)中未来 `Admin`/`Viewer` 角色方向，也不提前实现角色权限矩阵或用户管理。

## 目标

- 提供 Owner-only 的 `GET /api/v1/players/online`，返回当前游戏进程中的在线玩家快照。
- 在游戏主线程读取连接和玩家活对象，并在离开主线程前映射为 Application 定义的不可变值。
- 只返回当前巡检所需的精简身份与状态字段，不暴露 IP、精确位置、封禁信息或活动游戏对象。
- 复用现有 `GameThreadDispatcher` 的取消、启动截止时间和真实结果语义，为玩家查询增加独立 single-flight 门禁。
- 用稳定 HTTP 错误区分未认证、角色不允许、游戏未就绪、查询繁忙、主线程启动超时和快照暂不可用。
- 通过单元、Adapter、Katana、依赖规则和 Windows `v3.0.1-b4` 真实进程证据证明字段映射与主线程边界。

## 非目标

- 不实现 Admin 登录、玩家页面、筛选、轮询或任何前端代码。
- 不实现 `Admin`、`Viewer` 或自定义权限；当前端点只接受 `Owner`。
- 不实现踢出、禁言、封禁、传送、公告或任意改变游戏状态的动作。
- 不查询离线玩家、历史上线时间、总游玩时间、持久玩家档案或审计记录。
- 不返回 IP 地址、精确位置、耐力、分数、死亡/击杀、封禁原因或封禁期限。
- 不把在线玩家快照写入 SQLite、服务器事件窗口、日志或缓存。
- 不解析 `lp`/`listplayers` 控制台文本，也不复用任意控制台命令 Gateway。
- 不建立通用查询总线、Repository、Mapper 框架、Domain 项目、共享主线程队列或角色权限系统。
- 不宣称 Linux 7DTD 主线程和玩家 API 兼容已经验证。

## 已验证的游戏证据

只读私有参考子模块中的 7DTD Dedicated Server `v3.0.1-b4` 反编译代码表明：

- 官方 `Webserver.WebAPI.APIs.WorldState.Player` 从 `ConnectionManager.Instance.Clients.List` 枚举在线连接；
- 它用 `ClientInfo.entityId` 从 `GameManager.Instance.World.Players.dict` 取得对应 `EntityPlayer`；
- `ClientInfo` 提供玩家名称、原生平台身份、可选跨平台身份和 ping；
- `EntityPlayer` 提供 `Progression.Level` 与 `Health`；
- `PlatformUserIdentifierAbs` 提供 `CombinedString`、`PlatformIdentifierString` 和可读用户标识，官方 JSON 契约也将这三项复制为字符串。

这些证据只确定 `v3.0.1-b4` 候选读取路径和字段，不等同于产品实现或运行兼容证明。产品 Adapter 必须通过当前编译引用和真实进程验证后，才能把对应事实提升到[系统架构](../../architecture.md)。

## 公开 HTTP 契约

| 方法与路径 | 认证 | 成功语义 | 失败语义 |
|---|---|---|---|
| `GET /api/v1/players/online` | Basic 或 Bearer；角色必须为 `Owner` | 返回一次当前进程玩家快照和服务端捕获时间 | 使用统一 Problem Details 返回认证、授权、就绪、繁忙、启动超时和快照不可用错误 |

成功响应固定为：

```json
{
  "capturedAtUtc": "2026-07-21T10:30:00Z",
  "players": [
    {
      "entityId": 171,
      "name": "PlayerName",
      "platformIdentity": {
        "combinedId": "Steam_76561198000000000",
        "platform": "Steam"
      },
      "crossplatformIdentity": null,
      "ping": 42,
      "level": 18,
      "health": 100
    }
  ]
}
```

- `capturedAtUtc` 是 Adapter 在游戏主线程完成快照映射后的 UTC 时间，不是 HTTP 序列化时间，也不承诺多个请求共享同一版本。
- `players` 无在线玩家时是空数组，返回 200；它不为 null，也不使用 404 表示空集合。
- `entityId` 只在当前 7DTD 进程和当前在线会话中有效，不能作为跨重启、跨重连或持久审计身份。
- `platformIdentity.combinedId` 复制 `PlatformUserIdentifierAbs.CombinedString` 作为不透明游戏身份字符串；`platform` 复制 `PlatformIdentifierString`。Application 和 Web 不解析或重建游戏身份对象。
- `crossplatformIdentity` 使用相同结构；游戏未提供时固定为 null。
- 玩家按 `entityId` 升序返回，保证同一快照的确定性序列化和测试结果；排序不代表产品排名。
- 名称、平台身份、ping、level 和 health 都是该快照捕获时的值，不承诺在响应到达时仍未变化。

## Application 边界

```text
PlayersController
  -> GetOnlinePlayersUseCase
  -> IOnlinePlayerQuery.GetOnlineAsync
  -> OnlinePlayersSnapshot
       -> CapturedAtUtc
       -> IReadOnlyList<PlayerSnapshot>
```

- `GetOnlinePlayersUseCase` 只委托一个类型化 `IOnlinePlayerQuery`，不接收 Controller、Hosting、7DTD 或 Unity 类型。
- `OnlinePlayersSnapshot` 拥有快照时间和玩家列表；`PlayerSnapshot` 拥有公开响应所需的精简字段。
- `PlayerPlatformIdentity` 只保存 `CombinedId` 和 `Platform` 两个不可变字符串；跨平台身份可空，原生平台身份不可空。
- 构造时复制传入玩家集合，避免 Adapter 或测试调用方后续修改列表。
- Application 项目继续只依赖 .NET Framework BCL，不创建 Domain 项目，也不引入映射库。

## SevenDays 查询 Adapter

```text
SevenDaysOnlinePlayerQuery.GetOnlineAsync
  -> acquire query-local single-flight gate
  -> GameThreadDispatcher.Enqueue
  -> read ConnectionManager clients on the game thread
  -> resolve EntityPlayer from World.Players.dict
  -> copy approved fields into immutable values
  -> sort by entityId
  -> capture UTC completion time
  -> return snapshot and release gate
```

- Adapter 只从当前在线 `ClientInfo` 枚举构造结果，不扫描持久玩家列表或存档文件。
- 读取 `ConnectionManager`、`ClientInfo`、`World.Players.dict`、`EntityPlayer`、Progression 和平台身份全部发生在游戏主线程。
- 每个玩家的 `ClientInfo` 与 `EntityPlayer` 必须在同一次主线程委托内配对并复制；任何游戏对象、可变集合或 Unity 类型都不得离开委托。
- 连接枚举期间找不到对应 `EntityPlayer` 表示玩家连接状态正在转换。Adapter 跳过该不完整条目，但只有在游戏 World 与连接集合本身可用时才返回快照。
- World、连接管理器、客户端集合或玩家字典尚不可用时抛出 `OnlinePlayerSnapshotUnavailableException`，不能把基础设施不可用伪装成空服务器。
- 字段读取失败不返回部分玩家对象；整个请求进入统一 500 边界并记录关联异常。后续只有出现真实兼容证据时才评估按玩家隔离失败。

## 并发、取消与新鲜度

- 玩家查询使用自己的 singleton Adapter 和独立 single-flight 门禁，不与控制台 `version` 命令共享门禁。
- 同一时刻第二个玩家查询立即抛出 `OnlinePlayerQueryBusyException`；不等待、不缓存、不合并，也不向 7DTD 主线程增加第二个查询任务。
- 首版继续使用 `GameThreadDispatcher` 的 5 秒启动截止时间。请求仍在排队时取消或超时必须保证委托稍后不执行。
- 一旦主线程开始捕获快照，取消或启动截止时间不再伪造失败；调用方等待本次只读复制的真实结果或异常。
- 本切片不引入 HTTP 级缓存、ETag、后台轮询或 stale fallback。每个 200 都对应一次真实主线程快照，并显式返回 `capturedAtUtc`。
- 后续概览和玩家页形成多个真实消费者后，再依据主线程耗时和请求并发证据决定是否合并同一 in-flight 查询或采用有界短时缓存。

## 认证、授权与错误

- Controller 使用 `[Authorize(Roles = "Owner")]`。未认证请求在调用 Application 前返回现有 401 Problem Details；非 Owner 身份返回现有 403。
- Controller 在调用用例前检查 `GameReadinessState.Ready`。未就绪返回 503 `game_not_ready`，不向主线程投递查询。
- 查询门禁已占用返回 503 `online_player_query_busy`。
- 5 秒内未进入游戏主线程返回 503 `game_thread_timeout`，沿用现有命令切片的稳定分类。
- World 或在线玩家基础设施不可用返回 503 `online_player_snapshot_unavailable`。
- 其他异常进入现有 API exception/Problem Details 边界，返回 500 且只在服务端日志记录 traceId 和异常。
- Problem Details 的 `instance` 只包含 Path；错误响应和日志不得包含平台身份、玩家名、IP、位置或游戏对象文本。

## 依赖注入与生命周期

- Bootstrap 把 `SevenDaysOnlinePlayerQuery` 注册为 singleton，并把同一实例暴露为 `IOnlinePlayerQuery`。
- `GetOnlinePlayersUseCase` 注册为 singleton；Controller 继续由 Web API 的 Microsoft DI resolver 构造。
- Query 不拥有独立 Start/Stop 生命周期、后台线程、计时器或缓存；停止语义由请求取消、game readiness 和现有 Dispatcher 状态竞争承担。
- Web Adapter 与 SevenDays Adapter 继续只通过 Application 类型相连，不产生 Adapter-to-Adapter 引用。

## 测试策略

### Application 单元测试

- 查询用例只调用一次 `IOnlinePlayerQuery` 并原样返回不可变快照。
- 模型构造复制玩家集合；调用方后续修改源集合不改变结果。
- 平台身份、可选跨平台身份、排序和空列表语义保持精确。

### SevenDays Adapter 测试

- 使用内部构造委托或最小静态访问包装点提供可控在线玩家源，不把测试 seam 提升为 Application 或 Hosting 接口。
- 验证玩家字段只在调度委托中读取并映射，结果不含游戏类型。
- 验证空服务器、多个玩家排序、可空跨平台身份、转换中缺失 `EntityPlayer` 跳过和基础设施不可用失败。
- 验证独立 single-flight、门禁在成功/取消/超时/异常后释放，并复用 `GameThreadDispatcherTests` 已证明的状态竞争语义。

### Web API 与 DI 测试

- Katana 测试覆盖匿名 401、非 Owner 403、Owner 空列表 200、多玩家 200、游戏未 Ready 503、查询繁忙 503、主线程启动超时 503 和快照不可用 503。
- 精确断言 camelCase JSON、UTC 时间、空数组、字段白名单、确定性排序和所有稳定 Problem Details code。
- 证明拒绝路径不调用 Query；成功路径只调用一次。
- DI 测试证明 singleton Query、Application 用例和 Controller 可解析，Provider 验证保持通过。
- 依赖规则证明 Application 不引用 Hosting、Web、SevenDays、SQLite 或游戏程序集，Web 与 SevenDays Adapter 不互相引用。

### 构建与真实进程

- Release Rebuild 必须零警告，后端全量测试通过，六个产品 DLL 和现有发布依赖边界保持不变。
- Windows `v3.0.1-b4` 真实进程先验证无玩家时返回 200 空数组，再连接至少一个受控测试客户端，验证名称、原生/跨平台身份、ping、level、health、捕获时间和当前 entity id 与游戏实际状态一致。
- smoke 同时验证无 IP、位置和封禁字段，完成后正常关服、释放端口，并保留服主配置和数据库。
- 若无法连接真实测试玩家，本切片只能声明 API 空列表和自动化通过，不能宣称真实玩家字段兼容已经完成。
- Linux 官方进程仍作为独立证据缺口，不阻塞本 Windows 基线切片，但不得声明 Linux 支持。

## 文档影响

- 本规格获得批准后才创建对应 implementation plan；批准前不修改产品代码。
- 实现并验证后，先更新[系统架构](../../architecture.md)中的 Application 玩家查询、SevenDays 快照映射、HTTP 接口、DI、主线程并发和残余风险。
- 根据验证结果更新[测试策略](../../test.md)的 `CAP-01`/`CAP-02` 覆盖、玩家快照风险、自动化数量和 Windows 真实进程证据。
- 只有已验证事实才从[后端目标架构蓝图](../../architecture/backend-target-blueprint.md)提升到 Current；Target 继续保留玩家动作、通用权限、审计和未来多消费者策略。
- 更新 `backend/README.md` 的当前接口和能力边界；根 `README.md` 只在当前实现摘要需要同步时做简短更新。
- 本切片没有 Admin 页面、导航或交互变化，因此不修改[产品设计](../../design.md)和 Admin Target 蓝图，也不执行浏览器视觉测试。
- 不更新 `CHANGELOG.md`，直到在线玩家查询作为用户或运维可见版本发布。

## 批准检查点

批准本规格即同时确认：

- 当前在线玩家查询只授权 `Owner`，不实现 `Admin`/`Viewer`；
- 首个响应字段固定为 entity id、名称、原生/可选跨平台身份、ping、level、health 和快照时间；
- IP、精确位置、封禁、战斗统计和离线历史不进入本切片；
- 平台 `combinedId` 是不透明游戏身份字符串，entity id 只是当前进程临时标识；
- 玩家查询拥有独立 single-flight，不共享控制台命令门禁，也不建立缓存或通用队列；
- 转换中缺失 `EntityPlayer` 的连接被跳过，但玩家基础设施不可用返回明确 503；
- 必须连接真实测试玩家后才能宣称字段兼容完成，否则如实保留验证缺口；
- 前端玩家页面和任何状态变更动作进入后续独立纵向切片。