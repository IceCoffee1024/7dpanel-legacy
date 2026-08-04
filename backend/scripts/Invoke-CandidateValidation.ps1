[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $SevenDaysReferenceRoot,
    [Parameter(Mandatory = $true)] [string] $GameVersion,
    [Parameter(Mandatory = $true)] [string] $ProductVersion,
    [Parameter(Mandatory = $true)] [string] $OperatingSystem,
    [Parameter(Mandatory = $true)] [string] $BrowserVersion,
    [Parameter(Mandatory = $true)] [string] $EnvironmentId,
    [Parameter(Mandatory = $true)] [string] $IsolationRoot,
    [Parameter(Mandatory = $true)] [string] $ServerRoot,
    [Parameter(Mandatory = $true)] [string] $CandidateArtifactDirectory,
    [Parameter(Mandatory = $true)] [string] $EvidenceDirectory,
    [Parameter(Mandatory = $true)] [hashtable[]] $Automation,
    [Parameter(Mandatory = $true)] [hashtable[]] $Lane,
    [switch] $ConfirmIsolatedInstance
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

function Fail([string] $Message) {
    throw "Candidate validation preflight failed: $Message"
}

function Get-FullPath([string] $Path, [string] $Name) {
    if ([string]::IsNullOrWhiteSpace($Path)) { Fail "$Name is required." }
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $trimmedPath = $fullPath.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $volumeRoot = [System.IO.Path]::GetPathRoot($fullPath).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    if ($trimmedPath -eq $volumeRoot) { Fail "$Name must not be a filesystem root." }
    return $trimmedPath
}

function Assert-ExistingDirectory([string] $Path, [string] $Name) {
    $full = Get-FullPath $Path $Name
    if (-not (Test-Path -LiteralPath $full -PathType Container)) { Fail "$Name must be an existing directory." }
    if ((Get-Item -LiteralPath $full -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
        Fail "$Name must not be a reparse point."
    }
    return $full
}

function Test-IsStrictChild([string] $Child, [string] $Parent) {
    $childPath = Get-FullPath $Child 'Child'
    $parentPath = Get-FullPath $Parent 'Parent'
    if ($childPath.Equals($parentPath, [System.StringComparison]::OrdinalIgnoreCase)) { return $false }
    return ($childPath + [System.IO.Path]::DirectorySeparatorChar).StartsWith(
        $parentPath + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-ChildPath([string] $Child, [string] $Parent, [string] $Name) {
    if (-not (Test-IsStrictChild $Child $Parent)) { Fail "$Name must be inside $Parent." }
}

function Invoke-GitValue([string[]] $Arguments) {
    $global:LASTEXITCODE = 0
    $output = @(& git -C $repositoryRoot @Arguments 2>$null)
    if ($LASTEXITCODE -ne 0) { Fail "Git command failed: git $($Arguments -join ' ')" }
    return ($output -join "`n").Trim()
}

function Assert-CleanGitWorktree([string] $Message) {
    if (-not [string]::IsNullOrWhiteSpace((Invoke-GitValue @('status', '--porcelain')))) {
        throw $Message
    }
}

function Get-Descriptor([hashtable] $DescriptorInput, [string] $Category, [bool] $RequiresEvidenceDirectory) {
    foreach ($required in @('Name', 'ScriptPath')) {
        if (-not $DescriptorInput.ContainsKey($required) -or [string]::IsNullOrWhiteSpace([string] $DescriptorInput[$required])) {
            Fail "$Category descriptor is missing $required."
        }
    }

    $name = [string] $DescriptorInput['Name']
    if ($name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
        Fail "$Category descriptor '$name' has an unsafe Name."
    }

    $configuredScriptPath = [string] $DescriptorInput['ScriptPath']
    if (-not [System.IO.Path]::IsPathRooted($configuredScriptPath)) {
        $configuredScriptPath = Join-Path $repositoryRoot $configuredScriptPath
    }
    $scriptPath = Resolve-Path -LiteralPath $configuredScriptPath -ErrorAction Stop
    if (-not (Test-Path -LiteralPath $scriptPath.ProviderPath -PathType Leaf)) {
        Fail "$Category descriptor '$name' ScriptPath is not a file."
    }

    $parameters = @{}
    if ($DescriptorInput.ContainsKey('Parameters')) {
        if ($DescriptorInput['Parameters'] -isnot [System.Collections.IDictionary]) {
            Fail "$Category descriptor '$name' Parameters must be a hashtable."
        }
        foreach ($entry in $DescriptorInput['Parameters'].GetEnumerator()) {
            $parameters[$entry.Key] = $entry.Value
        }
    }
    if ($RequiresEvidenceDirectory -and $parameters.ContainsKey('EvidenceDirectory')) {
        Fail "Lane descriptor '$name' must not set EvidenceDirectory; the orchestrator owns it."
    }

    return [pscustomobject]@{
        Name = $name
        ScriptPath = $scriptPath.ProviderPath
        Parameters = $parameters
    }
}

function Invoke-CandidateScript([pscustomobject] $Descriptor, [string] $Category) {
    Write-Host "Running ${Category}: $($Descriptor.Name)"
    $global:LASTEXITCODE = 0
    $childParameters = $Descriptor.Parameters
    & $Descriptor.ScriptPath @childParameters
    if ($LASTEXITCODE -ne 0) {
        throw "Candidate $Category '$($Descriptor.Name)' failed with native exit code $LASTEXITCODE."
    }
}

if (-not $ConfirmIsolatedInstance) {
    Fail 'ConfirmIsolatedInstance is required; no automation or real lane was started.'
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$isolationRoot = Assert-ExistingDirectory $IsolationRoot 'IsolationRoot'
$serverRoot = Assert-ExistingDirectory $ServerRoot 'ServerRoot'
$candidateArtifact = Get-FullPath $CandidateArtifactDirectory 'CandidateArtifactDirectory'
$evidenceRoot = Get-FullPath $EvidenceDirectory 'EvidenceDirectory'

if ($isolationRoot.Equals($repositoryRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
    (Test-IsStrictChild $isolationRoot $repositoryRoot) -or
    (Test-IsStrictChild $repositoryRoot $isolationRoot)) {
    Fail 'IsolationRoot must not overlap the repository.'
}
Assert-ChildPath $serverRoot $isolationRoot 'ServerRoot'
Assert-ChildPath $candidateArtifact $serverRoot 'CandidateArtifactDirectory'
Assert-ChildPath $evidenceRoot $isolationRoot 'EvidenceDirectory'
if ((Test-Path -LiteralPath $candidateArtifact) -and
    ((Get-Item -LiteralPath $candidateArtifact -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
    Fail 'CandidateArtifactDirectory must not be a reparse point.'
}
if ((Test-Path -LiteralPath $evidenceRoot) -and
    ((Get-Item -LiteralPath $evidenceRoot -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
    Fail 'EvidenceDirectory must not be a reparse point.'
}

foreach ($field in @(
    @{ Name = 'GameVersion'; Value = $GameVersion },
    @{ Name = 'ProductVersion'; Value = $ProductVersion },
    @{ Name = 'OperatingSystem'; Value = $OperatingSystem },
    @{ Name = 'BrowserVersion'; Value = $BrowserVersion },
    @{ Name = 'EnvironmentId'; Value = $EnvironmentId }
)) {
    if ([string]::IsNullOrWhiteSpace([string] $field.Value)) { Fail "$($field.Name) is required." }
}

$referenceRoot = Assert-ExistingDirectory $SevenDaysReferenceRoot 'SevenDaysReferenceRoot'
$runtimeRoot = Join-Path (Join-Path $referenceRoot $GameVersion) 'runtime'
$requiredReferenceFiles = @(
    (Join-Path $runtimeRoot 'Mods\0_TFP_Harmony\0Harmony.dll'),
    (Join-Path $runtimeRoot '7DaysToDieServer_Data\Managed\Assembly-CSharp.dll'),
    (Join-Path $runtimeRoot '7DaysToDieServer_Data\Managed\LogLibrary.dll'),
    (Join-Path $runtimeRoot '7DaysToDieServer_Data\Managed\Newtonsoft.Json.dll'),
    (Join-Path $runtimeRoot '7DaysToDieServer_Data\Managed\UnityEngine.CoreModule.dll')
)
$missingReferenceFiles = @($requiredReferenceFiles | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) })
if ($missingReferenceFiles.Count -gt 0) {
    Fail "SevenDaysReferenceRoot is missing required $GameVersion references: $($missingReferenceFiles -join ', ')."
}

$commit = Invoke-GitValue @('rev-parse', 'HEAD')
if ($commit -notmatch '^[0-9A-Fa-f]{40}$') { Fail 'Git HEAD is not a full commit SHA.' }
Assert-CleanGitWorktree 'Candidate validation preflight failed: Git working tree must be clean.'

if ($Automation.Count -eq 0) { Fail 'At least one existing automation descriptor is required.' }
if ($Lane.Count -eq 0) { Fail 'At least one existing real-lane descriptor is required.' }
$automationDescriptors = @()
foreach ($automationDescriptor in $Automation) {
    $automationDescriptors += Get-Descriptor -DescriptorInput $automationDescriptor -Category 'Automation' -RequiresEvidenceDirectory $false
}
$laneDescriptors = @()
foreach ($laneDescriptor in $Lane) {
    $laneDescriptors += Get-Descriptor -DescriptorInput $laneDescriptor -Category 'Lane' -RequiresEvidenceDirectory $true
}
$seenNames = @{}
foreach ($descriptor in @($automationDescriptors + $laneDescriptors)) {
    $key = $descriptor.Name.ToUpperInvariant()
    if ($seenNames.ContainsKey($key)) { Fail "Candidate descriptor name is duplicated: $($descriptor.Name)" }
    $seenNames[$key] = $true
}

foreach ($descriptor in $automationDescriptors) {
    Invoke-CandidateScript $descriptor 'automation'
}
Assert-CleanGitWorktree 'Candidate validation automation changed the Git working tree; no candidate artifact was published.'

$previousEnvironment = @{}
foreach ($name in @('SevenDaysReferenceRoot', 'SevenDaysGameVersion')) {
    $environmentPath = 'Env:' + $name
    $previousEnvironment[$name] = [pscustomobject]@{
        WasSet = Test-Path -LiteralPath $environmentPath
        Value = if (Test-Path -LiteralPath $environmentPath) { (Get-Item -LiteralPath $environmentPath).Value } else { $null }
    }
}
try {
    Set-Item -LiteralPath 'Env:SevenDaysReferenceRoot' -Value $referenceRoot
    Set-Item -LiteralPath 'Env:SevenDaysGameVersion' -Value $GameVersion
    & (Join-Path $PSScriptRoot 'Publish-Mod.ps1') -PublishDirectory $candidateArtifact
    if ($LASTEXITCODE -ne 0) { throw "Candidate publish failed with native exit code $LASTEXITCODE." }
}
finally {
    foreach ($name in $previousEnvironment.Keys) {
        $environmentPath = 'Env:' + $name
        if ($previousEnvironment[$name].WasSet) {
            Set-Item -LiteralPath $environmentPath -Value $previousEnvironment[$name].Value
        }
        else {
            Remove-Item -LiteralPath $environmentPath -ErrorAction SilentlyContinue
        }
    }
}

& (Join-Path $PSScriptRoot 'Test-ReleaseArtifact.ps1') -ArtifactPath $candidateArtifact
if ($LASTEXITCODE -ne 0) { throw "Candidate artifact validation failed with native exit code $LASTEXITCODE." }
$artifactIdentity = & (Join-Path $PSScriptRoot 'Get-ReleaseArtifactIdentity.ps1') -ArtifactPath $candidateArtifact
if ($LASTEXITCODE -ne 0) { throw "Candidate artifact identity failed with native exit code $LASTEXITCODE." }

New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null
$runDirectory = Join-Path $evidenceRoot ('candidate-release-' +
    [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' +
    [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $runDirectory | Out-Null
$subEvidencePaths = @()
foreach ($descriptor in $laneDescriptors) {
    $laneEvidencePath = Join-Path (Join-Path $runDirectory 'lanes') $descriptor.Name
    New-Item -ItemType Directory -Path $laneEvidencePath -Force | Out-Null
    $descriptor.Parameters['EvidenceDirectory'] = $laneEvidencePath
    Invoke-CandidateScript $descriptor 'real lane'
    $subEvidencePaths += 'lanes/' + $descriptor.Name
}

Assert-CleanGitWorktree 'Candidate validation changed the Git working tree; no candidate manifest was written.'

& (Join-Path $PSScriptRoot 'New-EvidenceManifest.ps1') `
    -EvidenceDirectory $runDirectory `
    -EvidenceKind 'candidate-release' `
    -GitCommit $commit `
    -GitDirty:$false `
    -ArtifactIdentity $artifactIdentity `
    -ProductVersion $ProductVersion `
    -GameVersion $GameVersion `
    -OperatingSystem $OperatingSystem `
    -BrowserVersion $BrowserVersion `
    -EnvironmentId $EnvironmentId `
    -ExecutionScope 'candidate-validation' `
    -Status 'Passed' `
    -SubEvidencePaths $subEvidencePaths
