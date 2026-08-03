$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$scriptPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'Test-RestoreDrill.ps1'
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('7dpanel-restore-drill-' + [Guid]::NewGuid().ToString('N'))
function Assert-True([bool] $Condition, [string] $Message) { if (-not $Condition) { throw $Message } }
try {
    $isolation = Join-Path $temporaryRoot 'isolated'
    $server = Join-Path $isolation 'server'
    $evidence = Join-Path $isolation 'evidence'
    New-Item -ItemType Directory -Path $server -Force | Out-Null
    $failed = $false
    try { & $scriptPath -ServerRoot $server -ExpectedWorldName 'Navezgane' -EnvironmentId 'fixture' -EvidenceDirectory $evidence -IsolationRoot $isolation -BackupId 'backup-1' | Out-Null } catch { $failed = $true }
    Assert-True $failed 'Missing destructive confirmation must fail closed.'
    $failed = $false
    try { & $scriptPath -ServerRoot $server -ExpectedWorldName 'Navezgane' -EnvironmentId 'fixture' -EvidenceDirectory $evidence -IsolationRoot $isolation -ConfirmDestructiveRestoreDrill | Out-Null } catch { $failed = $true }
    Assert-True $failed 'Missing backup ID or creation policy must fail closed.'
    & $scriptPath -ServerRoot $server -ExpectedWorldName 'Navezgane' -EnvironmentId 'fixture' -EvidenceDirectory $evidence -IsolationRoot $isolation -BackupId 'backup-1' -ConfirmDestructiveRestoreDrill | Out-Null
    $run = Get-ChildItem -LiteralPath $evidence -Directory | Select-Object -First 1
    Assert-True ($null -ne $run) 'Restore preflight must create an evidence directory.'
    $preflight = Get-Content -LiteralPath (Join-Path $run.FullName 'preflight.json') -Raw | ConvertFrom-Json
    Assert-True ($preflight.status -eq 'Skipped') 'Preflight must remain skipped until a frozen candidate lane executes.'
    Write-Host 'Restore drill safety tests passed.'
}
finally { if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force } }
