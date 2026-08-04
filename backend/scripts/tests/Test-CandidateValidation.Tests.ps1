$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$sourceRoot = Split-Path $PSScriptRoot -Parent
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('7dpanel-candidate-validation-' + [Guid]::NewGuid().ToString('N'))
$repositoryRoot = Join-Path $temporaryRoot 'repository'
$scriptRoot = Join-Path $repositoryRoot 'backend\scripts'
$gateRoot = Join-Path $repositoryRoot 'gates'
$laneRoot = Join-Path $repositoryRoot 'lanes'
$isolationRoot = Join-Path $temporaryRoot 'isolated'
$serverRoot = Join-Path $isolationRoot 'server'
$artifactRoot = Join-Path $serverRoot 'Mods\7DPanel'
$evidenceRoot = Join-Path $isolationRoot 'evidence'
$referenceRoot = Join-Path $temporaryRoot 'references'
$tracePath = Join-Path $temporaryRoot 'trace.txt'
$env:SEVENDPANEL_CANDIDATE_TEST_TRACE = $tracePath
$env:SEVENDPANEL_CANDIDATE_TEST_REFERENCE_ROOT = $referenceRoot

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Equal($Expected, $Actual, [string] $Message) {
    if ($Expected -ne $Actual) { throw "$Message Expected '$Expected', got '$Actual'." }
}

function Assert-Fails([scriptblock] $Action, [string] $ExpectedMessage) {
    try {
        & $Action
        throw "Expected failure containing '$ExpectedMessage'."
    }
    catch {
        if ($_.Exception.Message -notlike "*$ExpectedMessage*") {
            throw "Expected failure containing '$ExpectedMessage', got '$($_.Exception.Message)'."
        }
    }
}

function New-FixtureFile([string] $Path, [string] $Content = 'fixture') {
    New-Item -ItemType Directory -Path (Split-Path $Path -Parent) -Force | Out-Null
    [System.IO.File]::WriteAllText($Path, $Content, (New-Object System.Text.UTF8Encoding($false)))
}

function New-ReferenceFixture([string] $Root) {
    foreach ($relativePath in @(
        'v3.0.1-b4/runtime/Mods/0_TFP_Harmony/0Harmony.dll',
        'v3.0.1-b4/runtime/7DaysToDieServer_Data/Managed/Assembly-CSharp.dll',
        'v3.0.1-b4/runtime/7DaysToDieServer_Data/Managed/LogLibrary.dll',
        'v3.0.1-b4/runtime/7DaysToDieServer_Data/Managed/Newtonsoft.Json.dll',
        'v3.0.1-b4/runtime/7DaysToDieServer_Data/Managed/UnityEngine.CoreModule.dll'
    )) {
        New-FixtureFile (Join-Path $Root $relativePath)
    }
}

function Get-Invocation([switch] $Confirm) {
    $automation = @(
        @{ Name = 'fixture-automation'; ScriptPath = 'gates/automation.ps1'; Parameters = @{} }
    )
    $lanes = @(
        @{ Name = 'fixture-lane'; ScriptPath = 'lanes/lane.ps1'; Parameters = @{} }
    )
    return @{
        SevenDaysReferenceRoot = $referenceRoot
        GameVersion = 'v3.0.1-b4'
        ProductVersion = '3.0.1'
        OperatingSystem = 'Windows Server 2022'
        BrowserVersion = 'Chromium 140'
        EnvironmentId = 'fixture-environment'
        IsolationRoot = $isolationRoot
        ServerRoot = $serverRoot
        CandidateArtifactDirectory = $artifactRoot
        EvidenceDirectory = $evidenceRoot
        Automation = $automation
        Lane = $lanes
        ConfirmIsolatedInstance = $Confirm.IsPresent
    }
}

try {
    New-Item -ItemType Directory -Path $scriptRoot, $gateRoot, $laneRoot, $serverRoot -Force | Out-Null
    foreach ($file in @(
        'Invoke-CandidateValidation.ps1',
        'Get-ReleaseArtifactIdentity.ps1',
        'New-EvidenceManifest.ps1',
        'Test-ReleaseArtifact.ps1',
        'release-manifest.json'
    )) {
        Copy-Item -LiteralPath (Join-Path $sourceRoot $file) -Destination $scriptRoot
    }
    New-ReferenceFixture $referenceRoot

    Set-Content -LiteralPath (Join-Path $gateRoot 'automation.ps1') -Encoding UTF8 -Value @'
Add-Content -LiteralPath $env:SEVENDPANEL_CANDIDATE_TEST_TRACE -Value 'automation'
'@
    Set-Content -LiteralPath (Join-Path $gateRoot 'mutate.ps1') -Encoding UTF8 -Value @'
Add-Content -LiteralPath $env:SEVENDPANEL_CANDIDATE_TEST_TRACE -Value 'mutate'
[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot '..\dirty-after-gate.txt'), 'dirty')
'@
    Set-Content -LiteralPath (Join-Path $laneRoot 'lane.ps1') -Encoding UTF8 -Value @'
param([Parameter(Mandatory = $true)] [string] $EvidenceDirectory)
Add-Content -LiteralPath $env:SEVENDPANEL_CANDIDATE_TEST_TRACE -Value 'lane'
New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
[System.IO.File]::WriteAllText((Join-Path $EvidenceDirectory 'lane.txt'), 'passed')
'@
    Set-Content -LiteralPath (Join-Path $scriptRoot 'Publish-Mod.ps1') -Encoding UTF8 -Value @'
param([Parameter(Mandatory = $true)] [string] $PublishDirectory)
Add-Content -LiteralPath $env:SEVENDPANEL_CANDIDATE_TEST_TRACE -Value 'publish'
if ($env:SevenDaysReferenceRoot -ne $env:SEVENDPANEL_CANDIDATE_TEST_REFERENCE_ROOT -or $env:SevenDaysGameVersion -ne 'v3.0.1-b4') {
    throw 'Candidate publish did not receive the declared 7DTD references.'
}
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'release-manifest.json') -Raw | ConvertFrom-Json
$paths = @(
    @($manifest.productAssemblies) +
    @($manifest.requiredManagedAssemblies) +
    @($manifest.requiredFiles) +
    @($manifest.requiredNativeAssets) +
    @($manifest.admin.index) +
    ($manifest.admin.assetsDirectory + '/app.12345678.js')
)
foreach ($relativePath in $paths) {
    $path = Join-Path $PublishDirectory $relativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
    $content = if ($relativePath -eq 'config.example.json') { '{"port":18080}' } elseif ($relativePath -eq $manifest.admin.index) { '<!doctype html><html></html>' } else { 'fixture' }
    [System.IO.File]::WriteAllText($path, $content, (New-Object System.Text.UTF8Encoding($false)))
}
'@

    Push-Location $repositoryRoot
    try {
        & git init | Out-Null
        & git config user.email 'candidate-tests@example.invalid'
        & git config user.name 'Candidate Tests'
        & git add .
        & git commit -m 'candidate fixture' | Out-Null
    }
    finally { Pop-Location }

    $candidateScript = Join-Path $scriptRoot 'Invoke-CandidateValidation.ps1'
    $invocation = Get-Invocation -Confirm
    $result = @(& $candidateScript @invocation)
    $candidateManifest = @($result | Where-Object {
        $evidenceKind = $_.PSObject.Properties['evidenceKind']
        $null -ne $evidenceKind -and $evidenceKind.Value -eq 'candidate-release'
    } | Select-Object -Last 1)
    Assert-Equal 1 $candidateManifest.Count 'Successful candidate validation must return one candidate manifest.'
    Assert-Equal 'Passed' $candidateManifest[0].status 'Candidate manifest status is incorrect.'
    Assert-Equal 'Candidate' $candidateManifest[0].maturity 'Candidate manifest maturity is incorrect.'
    Assert-Equal 'candidate-validation' $candidateManifest[0].executionScope 'Candidate manifest scope is incorrect.'
    Assert-Equal 'lanes/fixture-lane' (@($candidateManifest[0].subEvidence) -join ',') 'Candidate manifest must reference lane evidence only.'
    Assert-True ($candidateManifest[0].artifactSha256 -match '^[A-F0-9]{64}$') 'Candidate manifest must use the shared artifact identity.'
    Assert-Equal 'automation,publish,lane' ((Get-Content -LiteralPath $tracePath) -join ',') 'Candidate validation sequence is incorrect.'
    $runDirectory = Get-ChildItem -LiteralPath $evidenceRoot -Directory | Select-Object -First 1
    Assert-True (Test-Path -LiteralPath (Join-Path $runDirectory.FullName 'manifest.json')) 'Candidate manifest file is missing.'
    Assert-True (Test-Path -LiteralPath (Join-Path $runDirectory.FullName 'lanes\fixture-lane\lane.txt')) 'Lane evidence was not written beneath the candidate run.'

    Remove-Item -LiteralPath $tracePath
    $unconfirmedInvocation = Get-Invocation
    Assert-Fails { & $candidateScript @unconfirmedInvocation } 'ConfirmIsolatedInstance is required'
    Assert-True (-not (Test-Path -LiteralPath $tracePath)) 'Candidate must fail confirmation preflight before any step runs.'

    $missingReference = Join-Path $referenceRoot 'v3.0.1-b4\runtime\7DaysToDieServer_Data\Managed\Assembly-CSharp.dll'
    Remove-Item -LiteralPath $missingReference
    $missingReferenceInvocation = Get-Invocation -Confirm
    Assert-Fails { & $candidateScript @missingReferenceInvocation } 'missing required'
    Assert-True (-not (Test-Path -LiteralPath $tracePath)) 'Missing references must fail before any step runs.'
    New-FixtureFile $missingReference

    New-FixtureFile (Join-Path $repositoryRoot 'dirty.txt')
    $dirtyInvocation = Get-Invocation -Confirm
    Assert-Fails { & $candidateScript @dirtyInvocation } 'Git working tree must be clean'
    Assert-True (-not (Test-Path -LiteralPath $tracePath)) 'Dirty worktree must fail before any step runs.'
    Remove-Item -LiteralPath (Join-Path $repositoryRoot 'dirty.txt')

    $mutatingInvocation = Get-Invocation -Confirm
    $mutatingInvocation.Automation = @(
        @{ Name = 'mutating-automation'; ScriptPath = 'gates/mutate.ps1'; Parameters = @{} }
    )
    Assert-Fails { & $candidateScript @mutatingInvocation | Out-Null } 'automation changed the Git working tree'
    Assert-True (Test-Path -LiteralPath (Join-Path $repositoryRoot 'dirty-after-gate.txt')) 'Mutating automation did not execute.'
    Assert-Equal 'mutate' ((Get-Content -LiteralPath $tracePath) -join ',') 'Dirty automation must stop before publish or a real lane.'
    Remove-Item -LiteralPath (Join-Path $repositoryRoot 'dirty-after-gate.txt')
    Remove-Item -LiteralPath $tracePath

    $sharedInvocation = Get-Invocation -Confirm
    $sharedInvocation.IsolationRoot = $repositoryRoot
    $sharedInvocation.ServerRoot = $repositoryRoot
    $sharedInvocation.CandidateArtifactDirectory = Join-Path $repositoryRoot 'Mods\7DPanel'
    $sharedInvocation.EvidenceDirectory = Join-Path $repositoryRoot 'evidence'
    Assert-Fails { & $candidateScript @sharedInvocation } 'IsolationRoot must not overlap the repository'
    Assert-True (-not (Test-Path -LiteralPath $tracePath)) 'Shared-instance preflight must fail before any step runs.'

    $emptyVersionInvocation = Get-Invocation -Confirm
    $emptyVersionInvocation.ProductVersion = ' '
    Assert-Fails { & $candidateScript @emptyVersionInvocation } 'ProductVersion is required'
    Assert-True (-not (Test-Path -LiteralPath $tracePath)) 'Missing versions must fail before any step runs.'

    Write-Host 'Candidate validation tests passed.'
}
finally {
    Remove-Item Env:\SEVENDPANEL_CANDIDATE_TEST_TRACE -ErrorAction SilentlyContinue
    Remove-Item Env:\SEVENDPANEL_CANDIDATE_TEST_REFERENCE_ROOT -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
