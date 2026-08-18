$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$scriptPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'Test-SimplificationDocs.ps1'
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('7dpanel-simplification-docs-' + [Guid]::NewGuid().ToString('N'))
$simplificationRoot = Join-Path $temporaryRoot 'docs\simplification'

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Write-Fixture {
    param(
        [string] $Readme = $null,
        [string] $Inventory = $null,
        [string] $Roadmap = $null
    )

    if ([string]::IsNullOrEmpty($Readme)) {
        $Readme = @'
# 简化

[测试](../test.md)

### 简单查询
CAP-01

### 普通修改
CAP-07

### 危险异步操作
CAP-03
'@
    }
    if ([string]::IsNullOrEmpty($Inventory)) {
        $Inventory = @'
# 复杂度盘点

### SIM-001: sample
- status: `completed`
'@
    }
    if ([string]::IsNullOrEmpty($Roadmap)) {
        $Roadmap = @'
# 路线图

| 阶段 | 名称 | 状态 | 主要产物 |
|---|---|---|---|
| 1 | one | completed | x |
| 2 | two | not-started | x |
| 3 | three | not-started | x |
| 4 | four | not-started | x |
| 5 | five | not-started | x |
| 6 | six | not-started | x |

# Phase 1

# Phase 2

# Phase 3

# Phase 4

# Phase 5

# Phase 6
'@
    }

    New-Item -ItemType Directory -Path $simplificationRoot -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $temporaryRoot 'docs\test.md') -Value '# Test' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $simplificationRoot 'README.md') -Value $Readme -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $simplificationRoot 'inventory.md') -Value $Inventory -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $simplificationRoot 'roadmap.md') -Value $Roadmap -Encoding UTF8
}

function Invoke-Check([bool] $ExpectSuccess) {
    $success = $true
    try { & $scriptPath -RepositoryRoot $temporaryRoot | Out-Null } catch {
        Write-Host $_.Exception.Message
        $success = $false
    }
    Assert-True ($success -eq $ExpectSuccess) "Expected simplification checker success=$ExpectSuccess, got $success."
}

try {
    Write-Fixture
    Invoke-Check $true

    Write-Fixture -Readme @'
# 简化

[broken](../missing.md)

### 简单查询
CAP-01

### 普通修改
CAP-07

### 危险异步操作
CAP-03
'@
    Invoke-Check $false

    Write-Fixture -Inventory @'
# 复杂度盘点

### SIM-001: first
- status: `completed`

### SIM-001: duplicate
- status: `deferred`
'@
    Invoke-Check $false

    Write-Fixture -Roadmap @'
# 路线图

| 阶段 | 名称 | 状态 | 主要产物 |
|---|---|---|---|
| 1 | one | completed | x |
| 2 | two | not-started | x |
| 3 | three | not-started | x |
| 4 | four | not-started | x |
| 5 | five | unknown | x |
| 6 | six | not-started | x |

# Phase 1

# Phase 2

# Phase 3

# Phase 4

# Phase 5

# Phase 6
'@
    Invoke-Check $false

    Write-Fixture -Roadmap @'
# Roadmap

| Phase | Name | Status | Output |
|---|---|---|---|
| 1 | one | completed | x |
| 2 | two | not-started | x |
| 3 | three | not-started | x |
| 4 | four | not-started | x |
| 5 | five | not-started | x |
| 6 | six | not-started | x |

# Phase 1

- [ ] pending item

# Phase 2

# Phase 3

# Phase 4

# Phase 5

# Phase 6
'@
    Invoke-Check $false

    Write-Host 'Simplification documentation checker self-tests passed.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
