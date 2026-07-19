---
state: Draft
document_role: Change Record
last_updated: "2026-07-19"
---

# Admin 健康概览与 OWIN 静态托管设计规格

> 本规格描述一个独立的前端纵向切片，不代表当前功能已经实现。当前产品、界面和架构事实分别以 [PRD](../../PRD.md)、[界面设计](../../design.md) 和 [系统架构](../../architecture.md) 为准；Admin 目标边界见 [Admin 前端目标蓝图](../../architecture/admin-frontend-target-blueprint.md)。

## 目标

将 Admin 的 Overview 从未连接状态壳提升为第一条真实浏览器链路：页面通过类型安全的同源 API Client 请求 `GET /api/v1/health`，呈现加载、最新、过期和离线状态；发布形态下由 OWIN 提供 Admin 构建产物。OWIN 在 `InitMod` 完成配置和关闭事件注册后立即启动，不等待 `GameStartDone`。

## 范围

本切片包含：

- `ServerHealth` 响应类型和独立 API Client。
- `useServerHealth` 本地 composable，不引入全局 Store 或查询缓存库。
- Overview 的 Loading、Fresh、Stale、Offline 状态。
- 开发期 Vite `/api` proxy 和 `VITE_BACKEND_URL`。
- 生产环境相对路径 `/api/v1`。
- OWIN 静态文件托管、SPA 页面回退和 API 路由优先级。
- `InitMod` 阶段启动面板 HTTP Host，`GameStartDone` 只表示游戏运行时就绪。
- Web API JSON 属性统一使用 camelCase。
- 发布物中的 Admin `dist` 资源和浏览器到 Mod 的验证。

本切片不包含认证、玩家、备份、公告、审计、SQLite 或完整 E2E 测试框架。

## 现有契约

后端当前真实契约为：

```http
GET /api/v1/health
```

```json
{
  "status": "ok",
  "product": "7DPanel",
  "version": "0.1.0"
}
```

客户端必须验证 `status`、`product` 和 `version`，不能把任意 `2xx` 或未验证 JSON 直接渲染为正常。

当前 `status: "ok"` 只表示 7DPanel HTTP Host 已启动并能响应请求，不表示 7DTD 已完成 `GameStartDone`。未来依赖游戏对象的 API 必须拥有独立的游戏就绪判断；就绪前返回 `503` 和稳定错误码，不能借用健康端点的 `ok` 推断游戏操作可用。

## 用户与数据流

```text
Overview
  -> useServerHealth
  -> typed same-origin API client
  -> GET /api/v1/health
  -> Loading / Fresh / Stale / Offline
  -> product name, version, last successful sample
```

开发环境中，浏览器仍请求相对路径 `/api/v1/health`，Vite 只代理 `/api` 到 `VITE_BACKEND_URL`。生产环境不把后端地址编译进前端，而是由 OWIN 在同一来源提供静态资源和 API。

## 状态语义

- `Loading`：首次请求尚未完成。
- `Fresh`：最近一次请求成功，且客户端采样时间未超过新鲜度阈值。
- `Stale`：曾经成功，但连续失败或超过客户端新鲜度阈值；保留最后成功数据并明确显示采样时间。
- `Offline`：尚无成功采样，或请求失败且没有可显示的有效数据。

健康响应没有服务端时间戳，因此 `Stale` 只表示客户端对最后成功采样的判断，不表示服务器自身报告了过期状态。

请求必须支持 `AbortController`。刷新前取消未完成的旧请求；组件卸载时取消当前请求，避免过期响应覆盖新状态。

## OWIN 托管边界

- Bootstrap 选择发布后的 Admin 资源根目录，并通过显式配置传给 Web Adapter。
- Bootstrap 在 `InitMod` 中先注册 `WorldShuttingDown` 和 `GameShutdown` 关闭事件，再立即启动 OWIN；`GameStartDone` 不重复启动 OWIN。
- OWIN 与游戏运行时就绪是两个状态边界。当前切片只实现 HTTP Host 存活；未来游戏依赖组件必须由 `GameStartDone` 驱动，不得加入 `InitMod` 的早期启动阶段。
- Web Adapter 负责 OWIN middleware 顺序，不在运行时猜测仓库路径。
- 发布 Mod 的 Admin 资源根目录固定为 `<ModDirectory>/wwwroot`；Bootstrap 从 `modInstance.Path` 显式传递该目录，资源不存在时仍允许健康 API 启动，但发布验证必须拒绝缺失资源。
- Web Adapter 使用 `Microsoft.Owin.StaticFiles` 提供发布资源；该依赖固定使用当前 Katana `4.2.3` 版本线。
- Web API 路由先于静态文件和 SPA fallback。
- Web API 2 的 `JsonFormatter.SerializerSettings` 在管线配置处统一使用 camelCase；响应测试必须区分属性名大小写。
- `/api/*` 永远不能回退到 `index.html`。
- `/`、`/overview` 等浏览器路由在资源存在时返回 `index.html`。
- 缺失静态资源返回普通 `404`，不暴露仓库绝对路径。

## 验证标准

- Admin `pnpm lint`、`pnpm typecheck` 和 `pnpm build` 通过。
- 后端 Release build 和现有测试通过。
- 开发服务器通过 Vite proxy 访问真实 `/api/v1/health`。
- 发布目录包含 `index.html` 和哈希资源，不包含 `node_modules`、仓库源文件或 `7dtd-reference` 内容。
- `Publish-Mod.ps1` 在 dotnet publish 后从 `frontend/apps/admin/dist` 复制到 `<publishDir>/wwwroot`，并在缺少构建产物时失败。
- 真实 OWIN 页面可以加载 Overview，且 `/api/v1/health` 仍由 API 处理。
- 真实响应必须精确包含 `status`、`product`、`version`，不得返回 `Status`、`Product`、`Version`。
- 真实进程在 `GameStartDone` 前即可访问 OWIN；`GameStartDone` 不创建第二个监听器，关服后端口正常释放。
- 浏览器检查确认窄屏布局、控制台无未处理错误，以及失败后的 `Stale`/`Offline` 语义。

## 后续设计约束

该切片形成后续 Admin 功能复用的 API Client、错误模型、请求取消和发布托管边界。玩家、备份、身份和任务数据必须在各自真实后端契约存在后单独设计，不得在本切片中使用假数据占位。
