---
state: Current
document_role: Implementation Plan
primary_spec: ../specs/2026-07-22-api-key-authentication-design.md
last_updated: "2026-07-23"
---

# 无 Basic 与用户 API Key 认证实施计划

> **面向智能体执行者：** 实施时必须使用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans`，逐任务执行规格符合性与代码质量审查。以下步骤使用复选框跟踪。

**对应规格：** [无 Basic 与用户 API Key 认证设计规格](../specs/2026-07-22-api-key-authentication-design.md)

**目标：** 完全移除 Basic Authentication，保留低频 password grant，签发默认 8 小时网站 Access Token，并让用户创建、列出和撤销继承其当前角色的长期 API Key。

**架构：** Hosting 增加技术中立的 API Key Store 与凭据类型；现有 SQLite 认证 Store 继续独占密码、Access Token 和 API Key 的 schema、SQL、摘要及事务。Web 用一个 Bearer provider 按严格前缀路由 Access Token/API Key，API Key Controller 只消费 Hosting Store；Admin 保持内存网站会话，并新增独立 API Key Feature。

**技术栈：** .NET Framework 4.8、C# 11、xUnit v3、ASP.NET Web API 2、Katana OWIN/OAuth 4.2.3、Dapper、DbUp、Microsoft.Data.Sqlite、PBKDF2-HMAC-SHA256、SHA-256、Vue 3 Composition API、TypeScript、Vue Router、Pinia、Nuxt UI 4、Vitest、Vue Test Utils、Playwright、pnpm 11。

## 全局约束

- 产品不得接受或声明 Basic Authentication；用户名和密码只进入 `POST /api/v1/auth/token` 的 password grant。
- password grant 继续按远端地址限制为每分钟 20 次、最多 1024 个地址 bucket。
- 密码摘要固定为 16 字节随机盐、32 字节 PBKDF2-HMAC-SHA256、`1000` 次迭代。
- 网站 Access Token 默认生命周期为 480 分钟；配置范围保持 5 至 1440 分钟；浏览器只在内存保存，不增加 refresh token、Cookie 或 CSRF Token。
- API Key 格式固定为 `7dp_k_<key-id>_<random-secret>`；secret 至少 32 随机字节，数据库只保存 SHA-256 摘要并固定时间比较。
- 每个 Subject 最多 32 把尚未撤销的 Key；名称 trim 后 1 至 80 个 Unicode 字符；可选到期时间必须晚于创建时间。
- API Key 继承创建者当前启用状态和角色；不复制角色，不提供 scopes，不允许 API Key 创建另一把 Key。
- 完整 API Key 只在成功创建响应返回一次，并使用 `Cache-Control: no-store`；不得进入列表、日志、错误、审计、Pinia、URL 或浏览器存储。
- `last_used_utc` 每把 Key 每小时最多写一次；该元数据写失败 fail-open，身份、摘要和授权读取失败 fail-closed。
- 直接重写首个认证 migration；不迁移旧数据库、不重哈希旧摘要。真实部署必须停服后删除 DB/WAL/SHM。
- 不修改 `7dtd-reference/`；不增加外部认证或密码学依赖。
- 每项生产行为执行 RED、确认预期失败、GREEN、复跑同一检查；最终再运行聚合门禁。
- 本计划不授权 `git commit`、`git push`、`git reset`、`git revert`、远程发布、停服或删除测试服务器数据库。

---

### 任务 1：重置密码参数与网站 Token 默认值

**文件：**

- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Migrations/001_Authentication.sql`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqliteAuthenticationStore.cs`
- 修改：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/Authentication/PanelUserIdentity.cs`
- 修改：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/PanelAuthenticationOptions.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/config.example.json`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/Authentication/PersistentAccessTokenProvider.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/SqliteAuthenticationStoreTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/PanelHostOptionsTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/AuthenticationTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/ServerEventSseSessionTests.cs`

**接口：** 保留 `IPanelCredentialStore` 和 `IPanelAccessTokenStore` 签名；`PanelUserIdentity` 新增只读 `Role`，构造函数要求 `Owner`、`Admin` 或 `Viewer`；产出 `PasswordIterationCount = 1000` 与 `DefaultAccessTokenLifetimeMinutes = 480`。引导 `owner` 固定写入 `Owner`。

- [x] **步骤 1：写失败测试**

  在 SQLite 测试读取新建 `users.password_iterations`/`role` 并断言 `1000`/`Owner`；身份值对象拒绝空角色和未知角色；配置测试断言默认和 example 都为 480，且 5/1440 仍有效、4/1441 仍失败。

  ```csharp
  Assert.Equal(1000L, ReadScalar<long>(databasePath,
      "SELECT password_iterations FROM users WHERE subject = 'owner';"));
    Assert.Equal("Owner", ReadScalar<string>(databasePath,
      "SELECT role FROM users WHERE subject = 'owner';"));
  Assert.Equal(TimeSpan.FromHours(8), options.Authentication.AccessTokenLifetime);
  ```

- [x] **步骤 2：运行 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj -c Release --filter "FullyQualifiedName~SqliteAuthenticationStoreTests|FullyQualifiedName~PanelHostOptionsTests"
  ```

  预期：当前值仍为 600000/30，断言失败。

- [x] **步骤 3：最小实现并验证 GREEN**

  将 migration 约束改为 `CHECK (password_iterations = 1000)`，给 users 增加受 `Owner`/`Admin`/`Viewer` CHECK 约束的 `role`，常量改为 `1000`，默认生命周期改为 `480`，同步 example。Store 的所有身份查询读取角色，`EnsureBootstrapOwner` 写入 `Owner`；所有生产与测试构造点显式传入现有 `Owner` 角色，使整个测试项目恢复编译。复跑步骤 2，预期通过。

### 任务 2：建立 API Key Store 与统一 Bearer 运行时路径

**文件：**

- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/Authentication/IPanelApiKeyStore.cs`
- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/Authentication/CreatedApiKey.cs`
- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/Authentication/ApiKeyCreateResult.cs`
- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/Authentication/ApiKeyCreateStatus.cs`
- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/Authentication/StoredApiKey.cs`
- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/Authentication/PanelCredentialType.cs`
- 新建：`backend/src/Runtime/LSTY.SevenDPanel.Hosting/Authentication/PanelClaimTypes.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/Migrations/001_Authentication.sql`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Persistence.Sqlite/SqliteAuthenticationStore.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/Authentication/PanelClaimsIdentityFactory.cs`
- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/Authentication/PersistentBearerCredentialProvider.cs`
- 删除：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/Authentication/PersistentAccessTokenProvider.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OwinStartup.cs`
- 修改：`backend/src/Bootstrap/LSTY.SevenDPanel/DependencyInjection/PanelServiceProviderFactory.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/SqliteAuthenticationStoreTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/AuthenticationTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyInjectionTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/DependencyRulesTests.cs`

**接口：**

```csharp
public interface IPanelApiKeyStore
{
  ApiKeyCreateResult Create(string subject, string name, DateTimeOffset createdUtc, DateTimeOffset? expiresUtc);
    IReadOnlyList<StoredApiKey> List(string subject, DateTimeOffset utcNow);
    bool Revoke(string subject, string keyId, DateTimeOffset revokedUtc);
    bool TryValidate(string apiKey, DateTimeOffset utcNow, out StoredApiKey storedKey);
}

public enum ApiKeyCreateStatus
{
  Created,
  SubjectNotFound,
  InvalidName,
  InvalidExpiration,
  CapacityReached
}

public sealed class CreatedApiKey
{
    public string ApiKey { get; }
    public StoredApiKey Metadata { get; }
}

public sealed class StoredApiKey
{
    public string KeyId { get; }
    public PanelUserIdentity Identity { get; }
    public string Name { get; }
    public DateTimeOffset CreatedUtc { get; }
    public DateTimeOffset? LastUsedUtc { get; }
    public DateTimeOffset? ExpiresUtc { get; }
    public DateTimeOffset? RevokedUtc { get; }
}
```

`ApiKeyCreateResult` 保证 `Status == Created` 时 `CreatedApiKey` 非空，其余状态为空；Controller 后续只能按枚举映射稳定 Problem Details，不解析异常消息。`PanelCredentialType` 提供 `access_token`/`api_key`；`PanelClaimTypes.CredentialType = "7dpanel:credential_type"`。claims 从 `PanelUserIdentity.Role` 产生 `ClaimTypes.Role`，不再硬编码 `Owner`，并增加内部凭据类型 claim。`PersistentBearerCredentialProvider` 实现 OAuth `AuthenticationTokenProvider`，构造函数消费 access-token、API-key、credential 三个 Store。

- [x] **步骤 1：写 Store 失败测试**

  覆盖格式/高熵、只存摘要、名称 trim 与 1–80、到期校验、32 把未撤销上限、按时间和 ID 稳定降序、跨实例验证、错误/过期/撤销、并发幂等撤销、跨 Subject 隔离、用户禁用与持久角色变化、每小时 `last_used_utc` 节流及节流 UPDATE 故障 fail-open。

  ```csharp
  Assert.StartsWith("7dp_k_", created.ApiKey);
  Assert.DoesNotContain(created.ApiKey, File.ReadAllText(databasePath));
  Assert.False(store.TryValidate(created.ApiKey, now.AddHours(2), out _));
  ```

- [x] **步骤 2：运行 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj -c Release --filter FullyQualifiedName~SqliteAuthenticationStoreTests
  ```

  预期：`IPanelApiKeyStore`/API Key 方法不存在。

- [x] **步骤 3：实现 schema 与 Store**

  `api_keys` 保存 `key_id`、`subject`、`name`、`secret_hash`、`created_utc`、可空 `last_used_utc`/`expires_utc`/`revoked_utc`，外键指向 users，并建立 Subject/状态索引。创建事务先检查容量再插入；验证按 key ID 定位、固定时间比较摘要并重建当前用户；最后使用时间用条件 UPDATE 节流且捕获该 UPDATE 的 SQLite 失败。

- [x] **步骤 4：注册端口并接入严格 Bearer 运行时消费者**

  把同一 `SqliteAuthenticationStore` 注册为 `IPanelApiKeyStore`。实现严格路由：现有 `7dp_` 走 Access Token、`7dp_k_` 只走 API Key、畸形 Key 不回退；两条路径都重建当前身份和角色并写入凭据类型 claim。增加 QueryString/Cookie/Basic 均不能建立 Bearer 身份的回归测试，以及 DI/依赖规则断言；复跑步骤 2 及：

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj -c Release --filter "FullyQualifiedName~AuthenticationTests|FullyQualifiedName~DependencyInjectionTests|FullyQualifiedName~DependencyRulesTests"
  ```

### 任务 3：删除 Basic 并更新限流、Challenge 与 OpenAPI

**文件：**

- 删除：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/Authentication/BasicAuthenticationHandler.cs`
- 删除：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/Authentication/BasicAuthenticationMiddleware.cs`
- 删除：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/Authentication/BasicAuthenticationOptions.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/Authentication/AuthenticationRateLimitMiddleware.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OwinStartup.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OpenApi/PanelOpenApiDocumentProcessor.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OpenApi/PanelOpenApiOperationProcessor.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/AuthenticationTests.cs`

**接口：** password grant 路由和 OAuth JSON 错误保留；受保护操作只声明 Bearer；Basic Header 等同无有效凭据，只返回 Bearer challenge。

- [x] **步骤 1：写失败测试**

  把 Basic 成功测试改为 REST/SSE 401；断言 `WWW-Authenticate` 不含 Basic；OpenAPI 无 Basic scheme，受保护操作只含 Bearer；限流只对 token endpoint 生效。

- [x] **步骤 2：运行 RED**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj -c Release --filter "FullyQualifiedName~AuthenticationTests|FullyQualifiedName~OwinWebHostTests"
  ```

  预期：Basic 当前仍成功且 OpenAPI 仍声明 Basic。

- [x] **步骤 3：删除 Basic 管道并验证 GREEN**

  删除三文件和 `app.Use<BasicAuthenticationMiddleware>`，`ShouldLimit` 只匹配 token endpoint；清除 OpenAPI Basic scheme/替代关系。复跑步骤 2。

### 任务 4：让 SSE 周期复验两种 Bearer 凭据

**文件：**

- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ServerEventsController.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ServerEventSseSession.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/ServerEventSseSessionTests.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs`

**接口：** `TryAuthorize(string subject, string bearerCredential, PanelCredentialType credentialType, IReadOnlyCollection<string> allowedRoles)`；Controller 从自身授权合同传入允许角色。复验按类型调用对应 Store，Subject 必须保持一致，重建后的当前角色必须仍在 `allowedRoles` 中。

- [x] **步骤 1：写失败测试**

  覆盖 Access Token/API Key 建流、API Key 撤销/到期/创建者禁用后关闭、角色变化到允许集合外后关闭、Basic/QueryString/Cookie 不能建流；持续事件不能推迟 15 秒复验。

- [x] **步骤 2：运行 RED、实现类型化复验、验证 GREEN**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj -c Release --filter "FullyQualifiedName~ServerEventSseSessionTests|FullyQualifiedName~Server_event"
  ```

### 任务 5：交付 API Key HTTP 合同

**文件：**

- 新建：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/ApiKeysController.cs`
- 修改：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OpenApi/PanelOpenApiOperationProcessor.cs`
- 修改：`backend/tests/LSTY.SevenDPanel.Tests/OwinWebHostTests.cs`

**接口：** `GET /api/v1/api-keys`、`POST /api/v1/api-keys`、`DELETE /api/v1/api-keys/{keyId}`；Controller 从 `NameIdentifier` 和 credential-type claim 取身份。POST body 为 `name`/可空 `expiresAtUtc`；POST 返回一次性 `apiKey` 并 `Cache-Control: no-store`；GET 不返回 secret/hash；DELETE 按 Subject 限定且幂等。POST 和 DELETE 都要求 `access_token`，API Key 调用统一 403。

- [x] **步骤 1：写 HTTP 失败测试**

  覆盖匿名 401、API Key 调 POST/DELETE 403、Access Token 创建 201、无缓存头、GET 元数据白名单/稳定排序、跨用户不可枚举、并发 DELETE 确定且幂等、输入/容量稳定 Problem Details。错误响应和捕获日志不得含完整 Key；API Key 不能从 QueryString 或 Cookie 调用任何路由。

  ```csharp
  Assert.Equal(HttpStatusCode.Created, response.StatusCode);
  Assert.Contains("no-store", response.Headers.CacheControl.ToString());
  Assert.DoesNotContain("apiKey", listJson);
  ```

- [x] **步骤 2：运行 RED、实现 Controller、验证 GREEN**

  ```powershell
  dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj -c Release --filter "FullyQualifiedName~Api_key|FullyQualifiedName~ApiKeys"
  ```

  使用现有 `ApiProblemDetailsFactory`，按 `ApiKeyCreateStatus` 固定映射 `invalid_api_key_name`、`invalid_api_key_expiration`、`api_key_capacity_reached`，不可枚举撤销失败为 `api_key_not_found`；`SubjectNotFound` 失败关闭为 401。复跑同一命令。

### 任务 6：交付 Admin API Keys 页面

**文件：**

- 新建：`frontend/apps/admin/src/pages/api-keys.vue`
- 新建：`frontend/apps/admin/src/features/api-keys/index.ts`
- 新建：`frontend/apps/admin/src/features/api-keys/api/apiKeys.ts`
- 新建：`frontend/apps/admin/src/features/api-keys/api/apiKeys.test.ts`
- 新建：`frontend/apps/admin/src/features/api-keys/model/useApiKeys.ts`
- 新建：`frontend/apps/admin/src/features/api-keys/model/useApiKeys.test.ts`
- 新建：`frontend/apps/admin/src/features/api-keys/ui/ApiKeysView.vue`
- 新建：`frontend/apps/admin/src/features/api-keys/ui/ApiKeysView.test.ts`
- 新建：`frontend/apps/admin/src/features/api-keys/ui/CreateApiKeyDialog.vue`
- 新建：`frontend/apps/admin/src/features/api-keys/ui/ApiKeyCreatedDialog.vue`
- 新建：`frontend/apps/admin/src/features/api-keys/ui/RevokeApiKeyDialog.vue`
- 修改：`frontend/apps/admin/src/app/AppShell.vue`
- 修改：`frontend/apps/admin/src/route-map.d.ts`（由路由插件生成）
- 修改：`frontend/apps/admin/tests/e2e/admin-online-players.spec.ts` 或新建 `frontend/apps/admin/tests/e2e/admin-api-keys.spec.ts`

**组件边界：** route page 只组合 `ApiKeysView`；composable 拥有 loading/empty/fresh/failed/forbidden、创建一次性 secret 和撤销状态；三个 dialog 分别负责输入、一次性复制/清除、撤销确认，使用 typed props/emits，不写 Pinia。

- [x] **步骤 1：写 API 与 composable 失败测试**

  锁定 Header-only 请求、DTO 校验、401 清会话、403、创建结果只存在于 `createdApiKey`、关闭后清除、撤销失败不乐观改状态。

- [x] **步骤 2：运行 RED**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm exec vitest run src/features/api-keys
  ```

  预期：Feature 文件不存在。

- [x] **步骤 3：实现 API/composable 并验证 GREEN**

  使用现有 `requestJson` 和 `useAuthStore().authorizationHeader`；不要引入新全局 Store。复跑步骤 2。

- [x] **步骤 4：写组件失败测试并实现 UI**

  黑盒覆盖空列表、元数据、创建校验、一次性等宽值、复制成功/失败、关闭清除、刷新不可恢复、撤销确认、提交锁定和错误状态。使用 Nuxt UI/Lucide 图标，路由页保持薄组合。

- [x] **步骤 5：运行前端定向与聚合检查**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm exec vitest run src/features/api-keys
  pnpm lint
  pnpm typecheck
  pnpm build
  ```

  预期：全部 exit 0；构建更新路由类型，不出现 secret 持久化代码。

- [x] **步骤 6：增加 Playwright 流程**

  在具备真实 OWIN、凭据和可重置数据库的环境验证登录、创建、复制、关闭后不可恢复、API Key 调 API、撤销后 401，以及网站 Access Token 过期后回到登录并可重新登录。缺少前置条件时沿用现有明确 skip 门禁，不伪造通过；skip 意味着发布验收尚未完成。

### 任务 7：删除临时探针、聚合验证并提升当前文档

**文件：**

- 删除：`backend/src/Adapters/LSTY.SevenDPanel.Adapters.SevenDays/Inbound/ConsoleCommands/Pbkdf2BenchmarkConsoleCommand.cs`
- 删除：`backend/tests/LSTY.SevenDPanel.Tests/Pbkdf2BenchmarkConsoleCommandTests.cs`
- 修改：`backend/README.md`
- 修改：`docs/architecture.md`
- 修改：`docs/test.md`
- 修改：`docs/architecture/backend-target-blueprint.md`
- 修改：`docs/architecture/admin-frontend-target-blueprint.md`

**接口：** 当前架构/测试只在代码与验证通过后提升无 Basic、PBKDF2 1000、8 小时 Token、API Key 和 Admin Feature；不改 PRD 决策。

- [x] **步骤 1：删除临时命令并运行仓库权威后端聚合门禁**

  ```powershell
  dotnet restore backend/7DPanel.sln
  dotnet build backend/7DPanel.sln --configuration Release --no-restore
  dotnet test backend/7DPanel.sln --configuration Release --no-build --no-restore
  ```

  预期：全部测试通过、0 失败，Release build exit 0。

- [x] **步骤 2：运行 Admin 聚合门禁**

  ```powershell
  Set-Location frontend/apps/admin
  pnpm test
  pnpm lint
  pnpm typecheck
  pnpm build
  pnpm test:e2e
  ```

  预期：前四项全部 exit 0；`pnpm test:e2e` 必须报告真实执行且 0 skip 才满足发布验收。缺少受控 OWIN 环境变量而 skip 时，本计划仍未完成。

  2026-07-23 已运行前四项，均以 exit 0 通过；`pnpm test:e2e` 因未设置
  `SEVENDPANEL_ADMIN_URL`、`PANEL_USERNAME` 和 `PANEL_PASSWORD` 而跳过 6 个真实环境场景。
  因此仅本地实现门禁完成，真实浏览器验收仍由步骤 5 保持阻塞。

- [x] **步骤 3：执行敏感值静态与运行时泄露检查**

  测试捕获创建、列表、撤销、错误与审计日志到明确的测试结果目录，断言不含本次生成的完整 Key。构建后用匹配完整格式的正则扫描 tracked source、被 ignore 的 Admin `dist` 和测试结果；只允许格式示例 `7dp_k_<key-id>_<random-secret>`，不得出现实际高熵 secret。扫描命中必须逐项人工确认并记录结果。

  ```powershell
  git grep -n -E "7dp_k_[A-Za-z0-9_-]+_[A-Za-z0-9_-]{32,}"
  rg --no-ignore -n "7dp_k_[A-Za-z0-9_-]+_[A-Za-z0-9_-]{32,}" frontend/apps/admin/dist backend/tests/LSTY.SevenDPanel.Tests/TestResults frontend/apps/admin/test-results
  ```

  若某个结果目录尚不存在，先确认对应测试已真实运行并定位其实际输出目录；不得通过省略目标目录把扫描变为通过。

- [x] **步骤 4：更新当前文档与说明**

  把已验证实现提升到 architecture/test/backend README，删除已失效的 Basic/30 分钟当前事实；保留 Linux、浏览器和真实进程未取得的证据缺口。运行 `git diff --check` 和本地 Markdown 链接检查。

- [ ] **步骤 5：准备并执行真实服务器发布验收**

  清单必须按顺序写明：正常停服、备份测试数据、删除 `7dpanel.db`/`-wal`/`-shm`、发布、启动、password grant 登录、确认 `expires_in=28800`、创建/使用/撤销 API Key、Basic 401、REST/SSE smoke、关服释放。未获得用户对远程发布、停服和删库的单独授权前不得执行；在授权和证据取得前，本任务保持阻塞，不能声称发布验收完成。

## 完成定义

- Basic 在源码、发布物、OpenAPI、REST、SSE、Admin 和测试合同中均不再是受支持认证方式。
- password grant 仍可登录，默认 Access Token 为 8 小时，密码记录为 PBKDF2-HMAC-SHA256 1000 次。
- API Key 一次性明文、当前角色继承、容量、撤销、到期、SSE 复验和元数据节流均有自动化证据。
- Admin API Keys 页面通过单元、组件、类型、lint、生产构建和真实浏览器验收，包括会话过期重登；skip 不满足完成定义。
- 后端全量测试和 Release build 通过；获得单独授权后完成真实 Windows `v3.0.1-b4` 数据库重置与 REST/SSE 验收，未授权或未运行时只能报告实现门禁通过，不能报告本计划完成。
- 当前权威文档只记录已经实现和验证的事实，目标规格与本计划不替代实现证据。