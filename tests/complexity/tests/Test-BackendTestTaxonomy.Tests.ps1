[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$auditScript = Join-Path $PSScriptRoot "..\Test-BackendTestTaxonomy.ps1"
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("seven-dpanel-taxonomy-" + [guid]::NewGuid().ToString("N"))

function Assert-ExitCode {
    param(
        [int] $Expected,
        [int] $Actual,
        [string] $Name
    )

    if ($Actual -ne $Expected) {
        throw "$Name returned exit code $Actual; expected $Expected."
    }
}

function Write-Fixture {
    param(
        [string] $Name,
        [string] $Content
    )

    Set-Content -LiteralPath (Join-Path $fixtureRoot $Name) -Value $Content -NoNewline
}

function Invoke-Audit {
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $auditScript -SourceRoot $fixtureRoot *> $null
        return $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}

try {
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null

    Write-Fixture -Name "ValidTests.cs" -Content @'
using Xunit;
[Trait("Capability", "Players")]
[Trait("Boundary", "Application")]
public sealed class ValidTests
{
    [Fact]
    public void Works() { }
}
'@
    Assert-ExitCode -Expected 0 -Actual (Invoke-Audit) -Name "valid fixture"

    Write-Fixture -Name "MissingCapabilityTests.cs" -Content @'
using Xunit;
[Trait("Boundary", "Application")]
public sealed class MissingCapabilityTests
{
    [Fact]
    public void Works() { }
}
'@
    Assert-ExitCode -Expected 1 -Actual (Invoke-Audit) -Name "missing capability fixture"
    Remove-Item -LiteralPath (Join-Path $fixtureRoot "MissingCapabilityTests.cs")

    Write-Fixture -Name "DuplicateCapabilityTests.cs" -Content @'
using Xunit;
[Trait("Capability", "Players")]
[Trait("Capability", "Operations")]
[Trait("Boundary", "Application")]
public sealed class DuplicateCapabilityTests
{
    [Fact]
    public void Works() { }
}
'@
    Assert-ExitCode -Expected 1 -Actual (Invoke-Audit) -Name "duplicate capability fixture"
    Remove-Item -LiteralPath (Join-Path $fixtureRoot "DuplicateCapabilityTests.cs")

    Write-Fixture -Name "InvalidBoundaryTests.cs" -Content @'
using Xunit;
[Trait("Capability", "Players")]
[Trait("Boundary", "Unsupported")]
public sealed class InvalidBoundaryTests
{
    [Fact]
    public void Works() { }
}
'@
    Assert-ExitCode -Expected 1 -Actual (Invoke-Audit) -Name "unsupported boundary fixture"
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

Write-Host "Backend test taxonomy audit self-tests passed."
