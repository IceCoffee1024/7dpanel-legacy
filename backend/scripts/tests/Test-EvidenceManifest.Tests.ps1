$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$scriptRoot = Split-Path $PSScriptRoot -Parent
$identityPath = Join-Path $scriptRoot 'Get-ReleaseArtifactIdentity.ps1'
$manifestPath = Join-Path $scriptRoot 'New-EvidenceManifest.ps1'
$releaseManifestPath = Join-Path $scriptRoot 'release-manifest.json'
$releaseManifest = Get-Content -LiteralPath $releaseManifestPath -Raw | ConvertFrom-Json
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('7dpanel-evidence-manifest-' + [Guid]::NewGuid().ToString('N'))

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Equal {
    param($Expected, $Actual, [string] $Message)
    if ($Expected -ne $Actual) { throw "$Message Expected '$Expected', got '$Actual'." }
}

function Assert-Fails {
    param([scriptblock] $Action, [string] $ExpectedMessage)
    try {
        & $Action
        throw "Expected failure containing '$ExpectedMessage'."
    }
    catch {
        if ($_.Exception.Message -notlike "*$ExpectedMessage*") {
            throw "Expected failure containing '$ExpectedMessage', got: $($_.Exception.Message)"
        }
    }
}

function New-FixtureFile {
    param([string] $Root, [string] $RelativePath, [string] $Content = 'fixture')
    $path = Join-Path $Root $RelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
    [System.IO.File]::WriteAllText($path, $Content, (New-Object System.Text.UTF8Encoding($false)))
}

function New-ValidArtifact {
    param([string] $Root, [switch] $ReverseOrder)
    New-Item -ItemType Directory -Path $Root -Force | Out-Null
    $paths = @(
        @($releaseManifest.productAssemblies) +
        @($releaseManifest.requiredManagedAssemblies) +
        @($releaseManifest.requiredFiles) +
        @($releaseManifest.requiredNativeAssets) +
        @($releaseManifest.admin.index) +
        ($releaseManifest.admin.assetsDirectory + '/app.12345678.js') +
        @($releaseManifest.player.index) +
        ($releaseManifest.player.assetsDirectory + '/app.12345678.js')
    )
    if ($ReverseOrder) { [array]::Reverse($paths) }
    foreach ($path in $paths) {
        $content = if ($path -eq 'config.example.json') { '{"port":18080}' } else { 'fixture' }
        if ($path -eq $releaseManifest.admin.index) { $content = '<!doctype html><html></html>' }
        if ($path -eq $releaseManifest.player.index) { $content = '<!doctype html><html></html>' }
        New-FixtureFile $Root $path $content
    }
}

function New-Identity {
    param([string] $ArtifactPath)
    return & $identityPath -ArtifactPath $ArtifactPath -ManifestPath $releaseManifestPath
}

try {
    $firstArtifact = Join-Path $temporaryRoot 'first'
    $secondArtifact = Join-Path $temporaryRoot 'second'
    New-ValidArtifact $firstArtifact
    New-ValidArtifact $secondArtifact -ReverseOrder
    $firstIdentity = New-Identity $firstArtifact
    $secondIdentity = New-Identity $secondArtifact
    Assert-Equal $firstIdentity.artifactSha256 $secondIdentity.artifactSha256 'Artifact identity must not depend on filesystem enumeration order.'
    Assert-True (@($firstIdentity.files | Where-Object { $_.path -match '^[A-Za-z]:|^/' }).Count -eq 0) 'Artifact identity must not expose absolute paths.'

    [System.IO.File]::AppendAllText((Join-Path $secondArtifact 'LSTY.SevenDPanel.dll'), 'changed')
    $changedContentIdentity = New-Identity $secondArtifact
    Assert-True ($firstIdentity.artifactSha256 -ne $changedContentIdentity.artifactSha256) 'Artifact content changes must change the identity.'

    $sizedArtifact = Join-Path $temporaryRoot 'sized'
    New-ValidArtifact $sizedArtifact
    [System.IO.File]::AppendAllText((Join-Path $sizedArtifact 'LSTY.SevenDPanel.dll'), 'x')
    $changedSizeIdentity = New-Identity $sizedArtifact
    Assert-True ($firstIdentity.artifactSha256 -ne $changedSizeIdentity.artifactSha256) 'Artifact size changes must change the identity.'

    $pathArtifact = Join-Path $temporaryRoot 'path'
    New-ValidArtifact $pathArtifact
    $asset = Join-Path $pathArtifact 'wwwroot\assets\app.12345678.js'
    Move-Item -LiteralPath $asset -Destination (Join-Path $pathArtifact 'wwwroot\assets\app.87654321.js')
    $changedPathIdentity = New-Identity $pathArtifact
    Assert-True ($firstIdentity.artifactSha256 -ne $changedPathIdentity.artifactSha256) 'Artifact path changes must change the identity.'

    New-FixtureFile $pathArtifact 'unapproved.txt'
    Assert-Fails { New-Identity $pathArtifact } 'not approved by the release manifest'
    Assert-Fails { New-Identity ([System.IO.Path]::GetPathRoot($firstArtifact)) } 'must not be a filesystem root'

    $reparseTarget = Join-Path $temporaryRoot 'reparse-target'
    $reparseArtifact = Join-Path $temporaryRoot 'reparse'
    New-ValidArtifact $reparseArtifact
    New-Item -ItemType Directory -Path $reparseTarget -Force | Out-Null
    try {
        New-Item -ItemType Junction -Path (Join-Path $reparseArtifact 'linked') -Target $reparseTarget -ErrorAction Stop | Out-Null
        Assert-Fails { New-Identity $reparseArtifact } 'contains reparse points'
    }
    catch [System.Management.Automation.ItemNotFoundException] {
        throw
    }

    $evidenceDirectory = Join-Path $temporaryRoot 'evidence'
    New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
    Assert-Fails { & $manifestPath -EvidenceDirectory $evidenceDirectory -EvidenceKind 'unknown' } 'Unsupported evidence kind'
    Assert-Fails { & $manifestPath -EvidenceDirectory $evidenceDirectory -EvidenceKind 'candidate-release' -GitCommit '' -ProductVersion '3.0.1' -GameVersion 'v3.0.1-b4' -OperatingSystem 'Windows' -BrowserVersion 'Chromium' -EnvironmentId 'test' -ExecutionScope 'candidate' } 'Git commit'
    Assert-Fails { & $manifestPath -EvidenceDirectory $evidenceDirectory -EvidenceKind 'candidate-release' -GitCommit ('a' * 40) -GitDirty -ProductVersion '3.0.1' -GameVersion 'v3.0.1-b4' -OperatingSystem 'Windows' -BrowserVersion 'Chromium' -EnvironmentId 'test' -ExecutionScope 'candidate' } 'clean Git working tree'
    Assert-Fails { & $manifestPath -EvidenceDirectory $evidenceDirectory -EvidenceKind 'candidate-release' -GitCommit ('a' * 40) -GitDirty:$false -ProductVersion '3.0.1' -GameVersion 'v3.0.1-b4' -OperatingSystem 'Windows' -BrowserVersion 'Chromium' -EnvironmentId 'test' -ExecutionScope 'candidate' } 'artifact identity'
    Assert-Fails { & $manifestPath -EvidenceDirectory $evidenceDirectory -EvidenceKind 'candidate-release' -GitCommit ('a' * 40) -GitDirty:$false -ArtifactIdentity $firstIdentity -ProductVersion '' -GameVersion 'v3.0.1-b4' -OperatingSystem 'Windows' -BrowserVersion 'Chromium' -EnvironmentId 'test' -ExecutionScope 'candidate' } 'ProductVersion'
    Assert-Fails { & $manifestPath -EvidenceDirectory $evidenceDirectory -EvidenceKind 'candidate-release' -GitCommit ('a' * 40) -GitDirty:$false -ArtifactIdentity ([pscustomobject]@{ artifactSha256 = 'not-a-sha' }) -ProductVersion '' -GameVersion 'v3.0.1-b4' -OperatingSystem 'Windows' -BrowserVersion 'Chromium' -EnvironmentId 'test' -ExecutionScope 'candidate' } 'valid SHA-256'
    Assert-Fails { & $manifestPath -EvidenceDirectory $evidenceDirectory -EvidenceKind 'candidate-release' -GitCommit ('a' * 40) -GitDirty:$false -ArtifactIdentity $firstIdentity -ProductVersion '3.0.1' -GameVersion 'v3.0.1-b4' -OperatingSystem 'Windows' -BrowserVersion 'Chromium' -EnvironmentId 'test' -ExecutionScope 'candidate' -Status Skipped } 'cannot be skipped'

    $summaryPath = Join-Path $evidenceDirectory 'summary.json'
    New-FixtureFile $evidenceDirectory 'summary.json' '{"status":"Failed","password":"not-recorded"}'
    $manifest = & $manifestPath -EvidenceDirectory $evidenceDirectory -EvidenceKind 'release-smoke' -GitCommit ('b' * 40) -GitDirty -EnvironmentId 'server=private-host;password=secret' -ExecutionScope 'development-smoke' -Status Failed -SubEvidencePaths @('summary.json', '01-stop-server.log')
    Assert-Equal 'Failed' $manifest.status 'Failed development evidence must remain failed.'
    Assert-Equal 'Development' $manifest.maturity 'Development smoke must not promote maturity.'
    Assert-True ([string]::IsNullOrEmpty($manifest.artifactSha256)) 'Development smoke may record a missing artifact.'
    Assert-True ($manifest.environmentId -notmatch 'private-host|password|secret') 'Manifest must not retain raw environment identifiers.'
    Assert-True ($manifest.environmentId -match '^[A-F0-9]{64}$') 'Manifest environment identifier must be a SHA-256 digest.'
    Assert-Equal 'summary.json,01-stop-server.log' (@($manifest.subEvidence) -join ',') 'Manifest sub-evidence paths are incorrect.'
    $manifestBytes = [System.IO.File]::ReadAllBytes((Join-Path $evidenceDirectory 'manifest.json'))
    Assert-True (-not ($manifestBytes.Length -ge 3 -and $manifestBytes[0] -eq 0xEF -and $manifestBytes[1] -eq 0xBB -and $manifestBytes[2] -eq 0xBF)) 'Manifest must use UTF-8 without a BOM.'

    Write-Output 'Evidence manifest tests passed.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
