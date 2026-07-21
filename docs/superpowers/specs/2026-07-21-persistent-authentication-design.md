---
state: Current
document_role: Design Spec
last_updated: "2026-07-21"
---

# SQLite 持久认证设计规格

> 追溯说明：本文在提交 `06a83e6 feat(backend): persist authentication state` 完成后补录，只还原该切片的设计边界、实施结果和后续替代关系，不证明规格曾在实施前获得批准。当前实现、依赖版本、发布布局和验证结论以[系统架构](../../architecture.md)与[测试策略](../../test.md)为准。

## 上游与范围

本规格落实[产品需求](../../PRD.md)中的 `CAP-05`、`NFR-01`、`NFR-02` 和 `NFR-04`，并实现[后端目标架构蓝图](../../architecture/backend-target-blueprint.md)中已经批准的 SQLite Persistence Adapter、固定引导 `Owner`、持久不透明 Bearer Token 和认证实时连接复验边界。

前序[认证服务器事件设计规格](2026-07-20-authenticated-server-events-design.md)只交付配置身份和进程内 Token，并明确把 SQLite 身份、持久会话和完整用户管理列为非目标。本切片以 SQLite Store 替换该过渡状态，但不扩大为完整用户管理或角色管理切片。

## 目标

- 新增独立 SQLite Persistence Adapter，由它拥有认证 schema、migration、连接和事务。
- 每次启动把 `config.json` 凭据同步到稳定 `Subject=owner` 的唯一引导 `Owner`，而不是创建第二套身份来源。
- 让 Basic 与 OAuth password grant 验证 SQLite 中用户的当前凭据和状态。
- 用可过期、可撤销且跨 7DTD 进程重启有效的不透明 Bearer Token 替换进程内 Token 表。
- 在认证 SSE 存续期间复验 Token 和用户状态，使撤销、到期或禁用能够关闭现有连接。
- 为 Windows/Linux x64 发布物布置 Microsoft.Data.Sqlite、SQLitePCLRaw、Dapper、DbUp 及所需托管和原生依赖。
- 在 7DTD 内存加载 Mod 的约束下恢复当前 Mod 程序集位置，供标准依赖解析和 SQLite migration 使用。

## 非目标

- 不提供用户创建、删除、禁用、角色变更或密码管理 API 和 Admin UI。
- 不创建第二个引导用户，也不改变固定 `Subject=owner` 的身份语义。
- 不采用 Cookie、CSRF Token、refresh token、JWT 或 QueryString Token。
- 不持久化控制台日志、服务器事件、审计、后台作业或备份目录。
- 不宣称 Linux 官方 7DTD 进程兼容已经验证；本切片只建立双平台发布布局和 Windows 真实进程证据。
- 不让 Application、Web Adapter 或 Hosting 接收 `SqliteConnection`、Dapper、DbUp 或数据库事务类型。

## 组件边界

```text
Bootstrap
  -> resolve <ModDirectory>/data/7dpanel.db
  -> initialize SQLite runtime and run DbUp migrations
  -> synchronize configured bootstrap Owner
  -> compose OWIN and the game lifecycle adapter

Basic / password grant
  -> Web authentication bridge
  -> IPanelCredentialStore
  -> SqliteAuthenticationStore

Header Bearer / authenticated SSE
  -> Web token bridge and periodic revalidation
  -> IPanelAccessTokenStore + IPanelCredentialStore
  -> SqliteAuthenticationStore
```

- Hosting 只定义 `IPanelCredentialStore`、`IPanelAccessTokenStore`、`PanelUserIdentity` 和 `StoredAccessToken` 等技术中立端口与值对象。
- Persistence Adapter 实现两个 Store 端口，并独占连接工厂、DbUp bootstrapper、嵌入 migration 和业务 SQL。
- Web Adapter 把 Basic、password grant 和 Bearer middleware 桥接到 Hosting 端口；它不直接引用 SQLite provider。
- Bootstrap 是唯一组合根，负责数据库物理路径、初始化顺序、Store 实例共享和生命周期释放。

## 数据与迁移

- 数据库固定为 `<ModDirectory>/data/7dpanel.db`，不得依赖当前工作目录或开发机 NuGet 缓存路径。
- 首个嵌入 migration 创建用户、凭据版本和访问 Token 所需 schema；DbUp 按 migration 独立事务执行并允许重复启动。
- 连接采用短生命周期，初始化 WAL，并为锁竞争配置有限等待；连接和事务不能越过 Persistence Adapter 边界。
- 密码使用带随机盐的 PBKDF2-HMAC-SHA256 摘要，数据库不得保存明文配置密码或明文 Bearer Token。
- Token Store 必须保持严格有界；签发、验证和清理使用数据库中的到期状态，不使用仅存在于当前进程的票据表。
- migration、WAL 或引导身份同步失败时，受保护监听不得以匿名或旧内存身份继续启动。

## 引导 Owner 同步

- `config.json` 是过渡期引导数据来源，不是绕过 SQLite 的第二个认证后端。
- 每次启动按稳定 `Subject=owner` 查找同一个用户；首次启动创建该用户，后续启动只同步用户名和凭据。
- 配置用户名或密码变化时，在同一持久边界更新现有用户并撤销其已有 Token，旧凭据和旧 Token 随即失效。
- 配置未变化时不得无故轮换凭据或撤销仍有效 Token。
- Basic 与 password grant 成功后都从持久记录重建当前用户名、Subject 和角色声明；禁用或读取失败必须拒绝认证。

## 持久 Bearer 与实时连接

- OAuth token endpoint 继续只接受 password grant，签发 Header-only 不透明 Bearer Token。
- Web bridge 只接受 Store 验证成功且关联用户仍处于活动状态的 Token，并使用用户当前状态重建 claims。
- Token 保持原签发和到期时间，跨 Store、连接工厂和 7DTD 进程重建后仍可验证；到期、删除、容量淘汰或凭据轮换后必须失效。
- `ServerEventSseSession` 保存认证 Subject 和原始 Header Token，并以独立时间边界复验 Token 与用户状态；持续事件流量不得推迟复验。
- 复验发现 Token 不存在、已到期或用户不再活动时停止写出并关闭连接，不把连接降级为匿名。

## 运行时兼容与发布

- 7DTD 会把 Bootstrap Mod 程序集加载到内存且可能令 `Assembly.Location` 为空；Bootstrap 只对当前 Mod 内位置为空的程序集应用游戏提供的 `0_TFP_Harmony` 补丁，并在组合 SQLite/OWIN 前验证位置已经恢复。
- 7DPanel 不发布 `0Harmony.dll`，不修改已有程序集位置，也不影响其他 Mod。
- 当前权威运行时使用 `Microsoft.Data.Sqlite` 与 `SQLitePCLRaw.bundle_e_sqlite3` 标准 Batteries；提交过程中出现、后来删除的自定义 native loader、ResourceManager shim 和显式 provider 绑定不属于保留设计。
- Persistence Adapter 布置 Windows/Linux x64 RID native asset；Mod 根目录不得放置会被游戏当作托管程序集扫描的 native SQLite。
- 本切片完成时的发布检查必须包含 Bootstrap、Hosting、Web、SevenDays 和 Persistence 五个产品 DLL、认证持久化依赖和平台资产，并继续排除游戏提供的 Harmony、Newtonsoft.Json、LogLibrary 和 Unity 程序集。随后控制台命令切片加入 Application，当前六个产品 DLL 的发布边界由[系统架构](../../architecture.md)维护。

## 失败与安全语义

- migration、数据库目录、native provider、引导同步或 Store 初始化失败时记录不含凭据的运维错误并保持失败关闭。
- 用户名不存在、密码错误、用户禁用和凭据读取失败对客户端使用相同认证失败语义。
- Token、密码、摘要材料、数据库连接字符串和内部路径不得进入 Problem Details、产品日志、SSE payload 或前端产物。
- 数据库和配置文件由服主控制；用户管理落地并能安全维护至少一个 `Owner` 后，必须移除配置引导职责和已知默认凭据。

## 验证标准

- migration 首次执行和重复执行均成功，数据库启用 WAL，连接工厂释放后拒绝新连接并清理连接池。
- 引导同步只创建稳定 `owner`，配置轮换更新同一用户并撤销旧 Token，数据库中不存在明文密码或明文 Token。
- Basic 与 password grant 验证持久用户；Bearer 能跨 Store、连接工厂和进程重建验证，并正确处理到期、撤销、容量和用户状态变化。
- SSE 在不超过批准间隔和 Token 更早到期边界复验；撤销、到期或禁用后停止写出。
- 依赖规则证明 Hosting/Web/Application 不引用 SQLite provider、Dapper 或 DbUp，并证明只有 Persistence Adapter 实现持久认证端口。
- Release Rebuild、后端全量测试、Windows/Linux x64 发布物清单和 Windows `v3.0.1-b4` 真实进程 smoke 覆盖 migration、同一引导 Owner、跨进程 Bearer、Basic/Bearer SSE、正常关服和端口释放。
- Linux 官方 7DTD 进程 smoke 继续作为[测试策略](../../test.md#已知缺口)中的未完成证据，不因双平台发布布局而视为通过。

## 文档影响

- 当前实现和后续标准 Batteries 修正已经写入[系统架构](../../architecture.md)，本文不复制其依赖版本和最新 smoke 时间线。
- 产品身份合同由[产品需求](../../PRD.md)拥有；本文只记录该合同在此切片中的技术实现。
- 风险、测试层级、真实进程证据和发布门槛由[测试策略](../../test.md)拥有。
- 未来用户管理、审计或第二种持久能力必须建立自己的设计规格，不在本追溯记录中扩展。

## 追溯结论

提交 `06a83e6` 完成了从进程内配置身份和 Token 到 SQLite 引导 Owner、持久 Token 与 SSE 周期复验的纵向替换。本文作为后补 Change Record 保存该设计依据；它不改变提交历史，也不把追溯补录表述为实施前审批。