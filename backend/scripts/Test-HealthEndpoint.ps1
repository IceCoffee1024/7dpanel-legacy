[CmdletBinding()]
param(
    [string] $HealthUrl,
    [ValidateRange(1, 600)]
    [int] $TimeoutSeconds = 30,
    [switch] $ExpectUnavailable,
    [string] $EnvironmentFile
)

$ErrorActionPreference = 'Stop'
if (-not $EnvironmentFile) { $EnvironmentFile = Join-Path $PSScriptRoot '..\.env.local' }
$environment = @{}
if (Test-Path -LiteralPath $EnvironmentFile) {
    Get-Content -LiteralPath $EnvironmentFile | ForEach-Object {
        $line = $_.Trim()
        if ($line -and -not $line.StartsWith('#')) {
            $parts = $line -split '=', 2
            if ($parts.Count -eq 2) { $environment[$parts[0].Trim()] = $parts[1].Trim().Trim('"', "'") }
        }
    }
}
if (-not $HealthUrl -and $environment['SEVENDPANEL_HEALTH_URL']) { $HealthUrl = $environment['SEVENDPANEL_HEALTH_URL'] }
if (-not $HealthUrl) { $HealthUrl = 'http://127.0.0.1:18080/health' }
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$lastError = $null

do {
    try {
        $response = Invoke-WebRequest -Uri $HealthUrl -UseBasicParsing -TimeoutSec 5
    }
    catch {
        $lastError = $_
        if ($ExpectUnavailable) {
            if ($_.Exception.Response) {
                $statusCode = [int]$_.Exception.Response.StatusCode
                throw "Expected $HealthUrl to be unavailable, but received HTTP $statusCode."
            }
            Write-Host "Health endpoint is unavailable as expected: $HealthUrl"
            return
        }
        Start-Sleep -Milliseconds 500
        continue
    }

    if ($ExpectUnavailable) {
        throw "Expected $HealthUrl to be unavailable, but received HTTP $($response.StatusCode)."
    }
    Write-Host "HTTP $($response.StatusCode) $HealthUrl"
    Write-Host $response.Content
    return
} while ((Get-Date) -lt $deadline)

throw "Health endpoint did not respond within $TimeoutSeconds seconds: $lastError"
