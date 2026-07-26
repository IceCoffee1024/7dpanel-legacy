---
state: Current
last_updated: "2026-07-26"
---

# PolySharp 与 required members 设计

## 目标与依据

后端继续以 [当前系统架构](../../architecture.md) 中的 `net48`、C# `11.0` 和 Nullable Reference Types 为编译基线，通过 `backend/Directory.Build.props` 为全部后端项目统一引入 `PolySharp 1.16.0` 作为仅构建期 source generator，使现有及未来后端编译单元均可直接使用 C# `required`、`init` 等现代语法所需的兼容类型。本变更不改变产品能力、HTTP JSON 契约或运行时发布依赖。

## 边界

- 在 `backend/Directory.Build.props` 统一引用 PolySharp，固定版本为 `1.16.0`，设置 `PrivateAssets=all`，只包含 source generator 所需资产；各 `.csproj` 不重复声明该依赖。
- 第一处生产使用限定为纯输出 DTO `OverviewAttentionHttpResponse.Code`，改用 `required string Code { get; init; }`；映射仍从已验证的 `OverviewAttention` 创建，不新增输入路径。
- 领域对象、Application 快照、配置模型和 HTTP 请求模型保持现有构造函数及运行时校验。`required` 只提供编译期初始化约束，不能替代反序列化、反射或业务边界验证。
- PolySharp 不进入 Mod 发布目录，不改变游戏 Mono API 兼容面。

## 验证

- 结构测试固定 PolySharp 的共享后端归属、版本、`PrivateAssets` 和 `IncludeAssets`，并禁止各 `.csproj` 重复声明。
- 反射测试确认 DTO 类型和属性包含 `System.Runtime.CompilerServices.RequiredMemberAttribute`，证明 source generator 与 C# 11 编译链路实际生效。
- 运行概览 HTTP 测试和后端聚合测试，确认 JSON 契约与现有运行边界未回归。
