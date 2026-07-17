[CmdletBinding()]
param(
    [string] $ComputerName,
    [string] $ServerRoot,
    [System.Management.Automation.PSCredential] $Credential,
    [switch] $Local,
    [ValidateRange(1, 300)]
    [int] $TimeoutSeconds = 30,
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

if ($Local) { $ComputerName = $null }
elseif (-not $ComputerName) { $ComputerName = $environment['SEVENDPANEL_REMOTE_COMPUTER'] }
if (-not $ServerRoot) { $ServerRoot = if ($ComputerName) { $environment['SEVENDPANEL_REMOTE_SERVER_ROOT'] } else { $environment['SEVENDPANEL_LOCAL_SERVER_ROOT'] } }
$startScript = 'startdedicated.bat'
$scheduledTaskName = '7DPanel-Start-7DTD'

if (-not $ServerRoot) { throw 'Server root is required. Set the local or remote server root in .env.local or pass -ServerRoot.' }

$startServer = {
    param($serverRoot, $scriptName, $timeoutSeconds)
    $running = Get-Process -Name '7DaysToDieServer' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($running) { return [PSCustomObject]@{ Status = 'AlreadyRunning'; ProcessId = $running.Id; ServerRoot = $serverRoot } }
    if (-not (Test-Path -LiteralPath $serverRoot -PathType Container)) { throw "Server root does not exist: $serverRoot" }
    $startPath = Join-Path $serverRoot $scriptName
    if (-not (Test-Path -LiteralPath $startPath -PathType Leaf)) { throw "Server start script does not exist: $startPath" }
    Start-Process -FilePath 'cmd.exe' -ArgumentList ('/d /s /c ""' + $startPath + '""') -WorkingDirectory $serverRoot -WindowStyle Hidden | Out-Null

    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    do {
        Start-Sleep -Seconds 1
        $server = Get-Process -Name '7DaysToDieServer' -ErrorAction SilentlyContinue | Select-Object -First 1
    } while (-not $server -and (Get-Date) -lt $deadline)

    if (-not $server) { throw "The local launcher did not start 7DaysToDieServer within $timeoutSeconds seconds." }
    [PSCustomObject]@{ Status = 'Started'; ProcessId = $server.Id; ServerRoot = $serverRoot }
}

$result = if ($ComputerName) {
    Write-Host "Connecting to remote server $ComputerName through WinRM..."
    $sessionOption = New-PSSessionOption -OpenTimeout 10000
    $startRemoteServer = {
        param($serverRoot, $scriptName, $taskName, $timeoutSeconds)
        $running = Get-Process -Name '7DaysToDieServer' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($running) {
            return [PSCustomObject]@{
                Status = 'AlreadyRunning'
                ProcessId = $running.Id
                ServerRoot = $serverRoot
                ScheduledTask = $taskName
            }
        }
        if (-not (Test-Path -LiteralPath $serverRoot -PathType Container)) { throw "Server root does not exist: $serverRoot" }
        $startPath = Join-Path $serverRoot $scriptName
        if (-not (Test-Path -LiteralPath $startPath -PathType Leaf)) { throw "Server start script does not exist: $startPath" }

        $existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        if ($existingTask -and $existingTask.State -eq 'Running') {
            Stop-ScheduledTask -TaskName $taskName
            Start-Sleep -Seconds 1
        }

        $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        $arguments = '/d /s /c ""' + $startPath + '""'
        $action = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument $arguments -WorkingDirectory $serverRoot
        $principal = New-ScheduledTaskPrincipal -UserId $identity -LogonType S4U -RunLevel Highest
        $settings = New-ScheduledTaskSettingsSet `
            -AllowStartIfOnBatteries `
            -DontStopIfGoingOnBatteries `
            -ExecutionTimeLimit ([TimeSpan]::Zero) `
            -MultipleInstances IgnoreNew

        Register-ScheduledTask `
            -TaskName $taskName `
            -Action $action `
            -Principal $principal `
            -Settings $settings `
            -Description 'Starts the 7 Days to Die dedicated server for 7DPanel development.' `
            -Force | Out-Null

        Write-Host "Starting persistent scheduled task $taskName as $identity..."
        Start-ScheduledTask -TaskName $taskName

        $deadline = (Get-Date).AddSeconds($timeoutSeconds)
        $nextProgress = (Get-Date).AddSeconds(5)
        do {
            Start-Sleep -Seconds 1
            $server = Get-Process -Name '7DaysToDieServer' -ErrorAction SilentlyContinue | Select-Object -First 1
            if (-not $server -and (Get-Date) -ge $nextProgress) {
                Write-Host "Waiting for 7DaysToDieServer to start..."
                $nextProgress = (Get-Date).AddSeconds(5)
            }
        } while (-not $server -and (Get-Date) -lt $deadline)

        if (-not $server) {
            $task = Get-ScheduledTask -TaskName $taskName
            $taskInfo = Get-ScheduledTaskInfo -TaskName $taskName
            $taskResult = '0x{0:X8}' -f ([uint32]$taskInfo.LastTaskResult)
            throw "Scheduled task $taskName did not start 7DaysToDieServer within $timeoutSeconds seconds. TaskState=$($task.State); LastTaskResult=$taskResult."
        }

        [PSCustomObject]@{
            Status = 'Started'
            ProcessId = $server.Id
            ServerRoot = $serverRoot
            ScheduledTask = $taskName
        }
    }
    $invokeParameters = @{
        ComputerName = $ComputerName
        ScriptBlock = $startRemoteServer
        ArgumentList = @($ServerRoot, $startScript, $scheduledTaskName, $TimeoutSeconds)
        SessionOption = $sessionOption
    }
    if ($Credential) { $invokeParameters.Credential = $Credential }
    try {
        Invoke-Command @invokeParameters
    }
    catch {
        if ($_.Exception.Message -match 'TrustedHosts|ServerNotTrusted|Kerberos') {
            throw "WinRM does not trust $ComputerName. Add only that host to the local TrustedHosts list. Example: Set-Item WSMan:\localhost\Client\TrustedHosts -Value '$ComputerName' -Concatenate -Force"
        }
        throw
    }
} else { & $startServer $ServerRoot $startScript $TimeoutSeconds }
$result | Select-Object Status, ProcessId, ServerRoot, ScheduledTask, PSComputerName | Format-List
