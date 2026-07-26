# PolySharp Required Members Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为全部 `net48` 后端项目统一加入仅构建期 PolySharp 支持，并在一个纯 HTTP 输出 DTO 上采用可验证的 C# `required init`。

**Architecture:** `backend/Directory.Build.props` 为全部后端项目统一提供 PolySharp，具体 `.csproj` 不重复声明，且该依赖不进入运行时发布闭包。首个使用点只改造 `OverviewAttentionHttpResponse`，保留领域与请求边界的运行时校验；其他后端项目可直接采用所需现代语法。

**Tech Stack:** .NET Framework 4.8、C# 11、PolySharp 1.16.0、xUnit v3、Newtonsoft.Json

**Primary design:** [PolySharp 与 required members 设计](../specs/2026-07-25-polysharp-required-members-design.md)

---

### Task 1: 锁定构建依赖与 required 元数据

**Files:**
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/DependencyRulesTests.cs`
- Modify: `backend/tests/LSTY.SevenDPanel.Tests/OverviewHttpTests.cs`
- Modify: `backend/Directory.Build.props`
- Modify: `backend/src/Adapters/LSTY.SevenDPanel.Adapters.Web/Inbound/Http/OverviewHttpModels.cs`

- [x] **Step 1: 写入失败测试**

在 `DependencyRulesTests` 断言 `backend/Directory.Build.props` 统一拥有 PolySharp，各 `.csproj` 不重复声明，并固定版本为 `1.16.0`、`PrivateAssets` 为 `all`、`IncludeAssets` 为 `runtime; build; native; contentfiles; analyzers`。在 `OverviewHttpTests` 通过 `GetCustomAttributesData()` 断言 `OverviewAttentionHttpResponse` 及其 `Code` 属性具有 `System.Runtime.CompilerServices.RequiredMemberAttribute`。

- [x] **Step 2: 验证测试按预期失败**

```powershell
dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --configuration Release --filter "FullyQualifiedName~DependencyRulesTests.PolySharp_is_a_shared_private_build_only_backend_dependency|FullyQualifiedName~OverviewHttpTests.Overview_attention_uses_required_member_metadata"
```

预期：共享 PolySharp 引用不存在，且 DTO 没有 required member 元数据。

- [x] **Step 3: 加入最小实现**

在 `backend/Directory.Build.props` 加入固定的私有 `PackageReference`。将 DTO 改为：

```csharp
public sealed class OverviewAttentionHttpResponse
{
    public required string Code { get; init; }
}
```

并将映射改为显式对象初始化器：

```csharp
new OverviewAttentionHttpResponse { Code = item.Code }
```

- [x] **Step 4: 验证目标测试和 HTTP 契约**

运行 Step 2 的命令，并运行 `OverviewHttpTests` 全部测试；预期全部通过。

### Task 2: 更新当前架构并完成聚合验证

**Files:**
- Modify: `docs/architecture.md`

- [x] **Step 1: 更新依赖矩阵**

记录 `PolySharp 1.16.0` 是全部后端项目共享的 source-only 编译期依赖，只补齐旧框架缺失的编译器类型，不进入 Mod 发布物；同时删除已经失效的“综合概览第一阶段不新增 NuGet 包”目标说明。

- [x] **Step 2: 运行后端聚合验证**

```powershell
dotnet test backend/7DPanel.sln --configuration Release
```

预期：解决方案构建通过。当前分支的全量测试基线另有 OWIN 测试装配、旧迁移计数和架构规则漂移失败；本次记录实际数量但不扩展修复。发布或真实 7DTD 验证不在本次范围内。

实际结果：Release 解决方案构建为 `0` 警告、`0` 错误；PolySharp 边界与全部概览 HTTP 测试 `13/13` 通过。全量测试运行 `516` 项，其中 `445` 通过、`71` 失败，失败集中在当前分支既有的 OWIN 测试服务装配、旧 migration 数量断言和架构规则漂移，不由本次 package/DTO 差异引起。

- [x] **Step 3: 检查改动质量**

运行 `git diff --check`，确认没有冲突标记、占位符或意外运行时包文件。Git 提交与合并仅在用户明确授权后执行。
