---
state: Current
document_role: Design Spec
last_updated: "2026-07-23"
---

# Admin 浏览器持久会话设计规格

> 本文描述已经批准、尚待实现的浏览器会话目标变更。产品行为由[产品需求](../../PRD.md)中的 `CAP-05` 和 `NFR-04` 定义，当前页面行为由[产品设计](../../design.md)记录，当前实现事实与验证证据仍分别以[系统架构](../../architecture.md)和[测试策略](../../test.md)为准。

## 上游与范围

当前 Admin 只把网站 Access Token 和到期时间保存在 Pinia 内存中。页面刷新会创建新的 Store，因此服务端仍认可的 Token 也无法由浏览器恢复，用户必须重新输入凭据。账户菜单同时硬编码显示 `Owner`，不能说明当前登录用户名。

本变更让普通登录会话跨当前标签页刷新，让用户可以显式选择在关闭标签页或重启浏览器后继续登录，并在应用壳显示服务端确认的用户名和角色。它继续使用现有 password grant、可撤销不透明 Access Token 和 `Authorization` Header，不改变受保护 API 的认证方式。

## 目标

- 登录页增加默认不选中的“保持登录”选项。
- 未选择时在当前标签页生命周期内恢复会话；选择后在同一浏览器中跨标签页关闭和浏览器重启恢复会话。
- 登录响应返回服务端确认的当前用户名和角色，避免把未经确认的表单输入当作身份资料。
- 应用壳显示当前用户名和角色，并保留明确退出入口。
- 对恢复记录执行严格校验，并在到期、登出、401 或损坏时清除所有相关状态。
- 保持密码、API Key、URL、Cookie、日志、错误和前端构建产物的既有敏感信息边界。

## 非目标

- 不增加 Cookie 会话、CSRF Token、refresh token、JWT、silent refresh 或独立的长期 refresh credential。
- 不延长 Access Token 的服务端有效期，也不让浏览器恢复越过 Token 原到期时间。
- 不增加当前用户资料接口、用户管理、角色管理、设备列表、远程会话列表或 Access Token 撤销 UI。
- 不把用户名或角色作为前端授权依据；受保护请求仍由服务端按 Token 和用户当前状态授权。
- 不在浏览器中保存密码、完整 API Key、API Key 创建结果或危险操作确认状态。
- 不以客户端加密掩盖 Web Storage 的同源脚本可读性，也不宣称本变更能够抵御同源 XSS。

## 登录响应合同

password grant 成功响应在现有 OAuth 字段之外增加：

```json
{
  "access_token": "<opaque-token>",
  "token_type": "bearer",
  "expires_in": 28800,
  "username": "admin",
  "role": "Owner"
}
```

- `username` 和 `role` 必须来自密码验证成功后重建的服务端身份，不能直接回显未验证的请求字段。
- `username` 必须是非空字符串；`role` 必须是当前支持的 `Owner`、`Admin` 或 `Viewer`。
- 前端必须把缺少身份字段、字段类型错误、不支持角色或既有 OAuth 字段无效的成功响应视为 `invalid-response`，且不得建立或保存部分会话。
- 用户名和角色用于显示当前身份。后续请求的权限仍由服务端根据 Bearer Token、当前用户启用状态和当前角色决定。
- 登录失败继续使用现有统一 OAuth 错误，不返回可用于枚举账号的身份元数据。

## 客户端会话记录

客户端只保存一个带版本的认证记录：

```ts
interface PersistedAuthSessionV1 {
  version: 1
  token: string
  expiresAt: number
  username: string
  role: 'Owner' | 'Admin' | 'Viewer'
}
```

- 两种 Storage 使用同一个版本化键 `7dpanel.auth.session.v1`，其他应用偏好不能复用该键或混入认证记录。
- `expiresAt` 是前端根据收到响应时刻和 `expires_in` 计算的 Unix epoch 毫秒值。
- 恢复时必须先解析 JSON，再严格校验版本、字段集合、类型、非空值、角色和未来到期时间。校验失败立即删除该记录并保持匿名。
- 会话记录不保存密码、“保持登录”表单值、Authorization Header 前缀、API Key、Subject 或服务端内部 claims。
- Storage API 抛错、被禁用或空间不可用时，登录仍可建立当前页面内存会话；界面不得谎称会话已经持久保存。

## 存储选择与优先级

登录提交按“保持登录”选择决定唯一目标存储：

| 选择 | 存储 | 刷新当前标签页 | 关闭标签页后重新访问 | 重启浏览器后重新访问 |
|---|---|---|---|---|
| 未选择 | `sessionStorage` | 恢复 | 不恢复 | 不恢复 |
| 已选择 | `localStorage` | 恢复 | 恢复 | 在 Token 有效期内恢复 |

- 每次开始新登录前清除内存会话以及两种 Storage 中的旧认证记录。登录成功后只写入所选目标，防止同一浏览器存在两个互相冲突的身份。
- 应用启动时先检查 `localStorage`，没有有效记录时再检查当前标签页的 `sessionStorage`。任一位置存在损坏或过期记录时只清除该记录，并继续检查另一位置。
- 如果异常状态下两处都存在有效记录，优先采用 `localStorage`，同时删除 `sessionStorage` 记录，使后续状态重新唯一。
- 从持久会话改为当前标签页会话或反向切换时，下一次成功登录完成上述清理和唯一写入，不迁移旧 Token。
- 本变更使用认证 Feature 自有的小型 Storage Adapter，不安装通用 Pinia 持久化插件，避免把其他 Store 或未来敏感状态隐式写入浏览器。

## 会话生命周期

```text
application start
  -> read and validate persisted record
  -> restore Pinia auth state
  -> schedule remaining expiry
  -> route guard evaluates authenticated state

password login succeeds
  -> validate server response
  -> clear previous auth records
  -> set Pinia auth state
  -> write selected storage record
  -> schedule expiry

logout / expiry / protected request returns 401
  -> clear expiry timer
  -> clear Pinia auth state
  -> remove both storage records
  -> route to login without replaying mutations
```

- Store 创建时同步完成恢复，确保 Router 首次守卫不会在恢复前错误跳转登录页。
- 恢复成功后按 `expiresAt - now` 设置剩余到期定时器；已到期 Token 不发送给服务端。
- 401 沿用各受保护 Feature 的 `expireSession()` 边界，并把清理范围扩大到内存和两种 Storage。
- 403 不清除会话；它表示当前身份存在但权限不足。
- 网络错误、5xx 或 SSE 断开不清除尚未被确认无效的会话。
- 登出只执行本地凭据清理；本切片不新增服务端撤销端点。清除后不得自动重放先前失败或未完成的状态变更请求。

## 跨标签页行为

- `localStorage` 记录的写入、替换和删除通过浏览器 `storage` 事件同步到同源其他标签页。
- 其他标签页收到有效的新会话记录时，先删除本标签页旧 `sessionStorage` 认证记录，再恢复该身份并重设到期定时器。
- 其他标签页收到持久会话删除或无效记录时，必须同时清除本标签页内存会话和 `sessionStorage` 认证记录并进入匿名状态，防止刷新后恢复旧的标签页会话。
- `sessionStorage` 会话保持当前标签页隔离，不尝试使用 `BroadcastChannel` 或其他机制跨标签页同步。
- 事件处理只响应版本化认证键，必须复用与启动恢复相同的严格解析逻辑，并避免把本标签页清理再次循环写回。

## 登录与账户界面

### 登录表单

- 密码字段下方增加“保持登录”复选框，默认未选择。
- 复选框只控制成功会话写入位置，不改变 Token 生命周期、服务端权限或登录错误语义。
- 用户名继续使用 `autocomplete="username"`，密码继续使用 `autocomplete="current-password"`；浏览器密码管理器行为不等同于产品把密码写入 Web Storage。
- 登录失败保留用户名、清空密码，并保持用户本次“保持登录”选择，方便修正凭据后重试。
- 存储不可用时成功登录仍进入应用，但只保留当前页面内存会话，并以非阻断提示说明关闭或刷新后需要重新登录；不新增会阻断管理入口的持久化错误页。

### 账户菜单

- 侧栏账户按钮用服务端确认的 `username` 替换硬编码 `Owner`；折叠时继续使用用户图标，并以用户名提供可访问名称和提示。
- 展开菜单顶部以非命令项显示用户名和角色，角色使用原始稳定标识 `Owner`、`Admin` 或 `Viewer`。
- 菜单保留带标准退出图标的“退出登录”命令。
- 用户名过长时按钮保持稳定宽度并截断显示，完整值通过提示或菜单内容可读，不得撑开侧栏或遮挡其他控件。

## 组件与所有权

```text
features/auth/api
  -> parse password grant response including username and role

features/auth/model
  -> auth session codec and storage adapter
  -> auth Store owns restore, persistence, expiry and cross-tab synchronization

features/auth/ui/LoginForm.vue
  -> owns credentials and remember-login input

app/AppShell.vue
  -> reads authenticated username and role
  -> renders account menu and logout command
```

- Auth Store 继续是浏览器会话的唯一运行时真相；组件不直接读写 Web Storage。
- Codec 只负责认证记录的序列化和严格解析；Storage Adapter 只负责两个浏览器存储位置及事件订阅。
- Store 的 `login` action 接收用户名、密码和保持登录选择，使用登录响应中的身份元数据更新状态。
- App Shell 只展示 Store 提供的当前身份，不缓存第二份用户名或角色。

## 安全与失败边界

- `localStorage` 和 `sessionStorage` 中的 Bearer Token 可被同源 JavaScript 读取；选择浏览器持久会话明确接受 Token 在原有效期内扩大暴露窗口的风险。
- 客户端加密不能抵御同源 XSS，因为解密代码和密钥与密文处于同一执行环境，本切片不增加无效的加密包装。
- Admin 文档响应必须增加与当前同源静态资产和 API/SSE 用法兼容的 Content Security Policy：脚本和连接只允许同源，不允许第三方运行时脚本或 `unsafe-eval`；样式、图片和字体只开放当前构建确实需要的最小来源。开发服务器可以使用独立开发策略，不得把放宽项带入生产 OWIN 响应。
- 实现与发布检查必须继续禁止任意 `v-html` 敏感渲染、第三方运行时脚本、Token 日志、URL Token、Cookie Token 和错误详情泄漏，并复核新增或升级前端依赖不会引入远程脚本或绕过 CSP 的运行时资源。
- 浏览器会话只保存网站 Access Token。完整 API Key 的一次性显示、关闭清理和 `Cache-Control: no-store` 边界不变。
- 用户禁用、角色变化、Token 撤销或凭据轮换仍由服务端每次请求复验；已保存的展示用户名和角色不能让失效 Token 获得权限。
- 服务端返回 401 后清除会话；角色变化但 Token 仍有效时，当前页面展示可能持续到下一次重新登录。本切片不增加当前用户资料刷新端点，授权结果仍以服务端为准。

## 自动化与验证

### 后端

- password grant 集成测试验证成功响应包含服务端身份中的 `username` 和 `role`，OpenAPI success schema 同步声明并要求这两个字段。
- 错误登录不返回身份元数据，现有 password grant 限流、Token 到期和 Header-only Bearer 行为不回归。
- OWIN 集成测试验证 Admin HTML 带有批准的 Content Security Policy，API、SSE、Swagger 和静态资产响应不被错误改写，生产策略不包含第三方脚本来源或 `unsafe-eval`。

### 前端单元与组件

- token response parser 覆盖合法身份、缺失或空用户名、未知角色、错误类型和既有 OAuth 字段错误。
- session codec 覆盖合法记录、未知版本、损坏 JSON、额外或缺失字段、过期记录和未知角色。
- Auth Store 覆盖 `sessionStorage`/`localStorage` 选择、启动恢复、优先级、唯一写入、Storage 异常降级、到期、登出、401、403 和定时器清理。
- 跨标签页测试覆盖持久会话创建、替换、删除、无效事件、本标签页旧 `sessionStorage` 清理和不循环写回；标签页会话不主动广播。
- LoginForm 覆盖默认未选择、提交选择、失败后保留选择和密码清理。
- App Shell 覆盖用户名、角色、长用户名布局、折叠可访问名称和退出清理。

### 浏览器 E2E

- 未选择“保持登录”时，刷新受保护深链接仍保持登录；关闭该页面上下文后新上下文要求登录。
- 选择“保持登录”时，新页面和浏览器重建后在 Token 有效期内恢复，并显示服务端确认的用户名和角色。
- 登出后刷新、新页面和浏览器重建均不能恢复；`localStorage` 与 `sessionStorage` 中不再存在认证记录。
- 注入损坏或过期记录时应用安全回到登录页，不发送该 Token，也不出现重定向循环。
- 模拟 Storage 写入失败时登录仍可用于当前页面，并显示会话无法持久保存的非阻断提示。
- 认证请求继续只在 `Authorization` Header 携带 Token；URL、Cookie、控制台、错误、非认证 Storage 项和生产资产不包含密码、API Key 或 Bearer 文本副本。
- 生产 OWIN Admin HTML 带有批准的 Content Security Policy，页面、API 请求和认证 SSE 在 Edge 中保持可用，浏览器控制台无策略违规。
- 桌面和 `390x844` 视口验证账户按钮、菜单、复选框和长用户名无重叠或水平溢出。

## 文档影响与提升

- [产品需求](../../PRD.md)拥有会话恢复、保持登录、身份显示和浏览器敏感数据边界的产品合同。
- 本规格批准后创建一份实施计划，不在计划中重新决定存储模式或身份来源。
- 实现完成后更新[产品设计](../../design.md)中的登录、刷新、会话过期和账户菜单流程。
- 代码与验证稳定后，把实际 Auth Store、登录响应和浏览器存储边界提升到[系统架构](../../architecture.md)，并把单元、E2E 和安全门禁更新到[测试策略](../../test.md)。
- [Admin 前端目标蓝图](../../architecture/admin-frontend-target-blueprint.md)中关于内存会话的目标描述随实现同步，不能在代码完成前作为当前实现证据。
- 发布后才把用户可见变化加入 `CHANGELOG.md`；设计批准和实现提交均不等于已经发布。