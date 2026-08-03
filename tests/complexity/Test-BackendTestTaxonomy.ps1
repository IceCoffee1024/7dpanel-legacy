[CmdletBinding()]
param(
    [string] $SourceRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    $SourceRoot = Join-Path $PSScriptRoot "..\..\backend\tests\LSTY.SevenDPanel.Tests"
}

$validCapabilities = [System.Collections.Generic.HashSet[string]]::new([string[]] @(
    "Platform", "Operations", "Players", "Community", "Economy", "Automation", "Administration"
))
$validBoundaries = [System.Collections.Generic.HashSet[string]]::new([string[]] @(
    "Domain", "Application", "Persistence", "Local", "SevenDays", "Web", "Bootstrap", "CrossSystem"
))

if (-not (Test-Path -LiteralPath $SourceRoot -PathType Container)) {
    throw "Backend test source root does not exist: $SourceRoot"
}

$violations = [System.Collections.Generic.List[string]]::new()
$sourceFiles = @(Get-ChildItem -LiteralPath $SourceRoot -Filter "*.cs" -File -Recurse |
    Where-Object { $_.FullName -notmatch "\\(bin|obj)\\" } |
    Sort-Object FullName)

foreach ($sourceFile in $sourceFiles) {
    $source = Get-Content -LiteralPath $sourceFile.FullName -Raw
    $classMatches = [regex]::Matches($source, "(?m)^\s*(?:public|internal|private|protected)(?:\s+(?:sealed|static|abstract|partial|new))*\s+class\s+(?<name>\w+)\b")

    for ($index = 0; $index -lt $classMatches.Count; $index++) {
        $testClass = $classMatches[$index]
        $classEnd = if ($index + 1 -lt $classMatches.Count) { $classMatches[$index + 1].Index } else { $source.Length }
        $classSource = $source.Substring($testClass.Index, $classEnd - $testClass.Index)
        if ($classSource -notmatch "(?m)^\s*\[(Fact|Theory)(Attribute)?(?:\([^\]]*\))?\]") {
            continue
        }

        # Class attributes are the contiguous attribute block directly above its declaration.
        $prefix = $source.Substring(0, $testClass.Index)
        $attributeBlockMatch = [regex]::Match($prefix, "(?s)(?<attributes>(?:\s*\[[^\]]+\]\s*)+)$")
        $traits = if ($attributeBlockMatch.Success) {
            @([regex]::Matches($attributeBlockMatch.Groups["attributes"].Value,
                    'Trait(?:Attribute)?\s*\(\s*"(?<key>[^"]+)"\s*,\s*"(?<value>[^"]+)"\s*\)'))
        }
        else {
            @()
        }
        $capabilities = @($traits | Where-Object { $_.Groups["key"].Value -eq "Capability" })
        $boundaries = @($traits | Where-Object { $_.Groups["key"].Value -eq "Boundary" })
        $subject = "$($sourceFile.FullName):$($testClass.Groups["name"].Value)"

        if ($capabilities.Count -ne 1) {
            $violations.Add("$subject must declare exactly one class-level Trait(""Capability"", ""..."").")
        }
        else {
            $capability = $capabilities[0].Groups["value"].Value
            if (-not $validCapabilities.Contains($capability)) {
                $violations.Add("$subject declares unsupported Capability '$capability'.")
            }
        }

        if ($boundaries.Count -eq 0) {
            $violations.Add("$subject must declare at least one class-level Trait(""Boundary"", ""..."").")
        }

        foreach ($boundaryTrait in $boundaries) {
            $boundary = $boundaryTrait.Groups["value"].Value
            if (-not $validBoundaries.Contains($boundary)) {
                $violations.Add("$subject declares unsupported Boundary '$boundary'.")
            }
        }
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "Backend test taxonomy audit passed for $($sourceFiles.Count) source files."
