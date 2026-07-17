[CmdletBinding()]
param(
    [string] $ComputerName,
    [int] $TelnetPort,
    [switch] $Local,
    [ValidateRange(1, 600)]
    [int] $TimeoutSeconds = 60,
    [System.Management.Automation.PSCredential] $Credential,
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
if (-not $TelnetPort) { $TelnetPort = if ($environment['SEVENDPANEL_TELNET_PORT']) { [int]$environment['SEVENDPANEL_TELNET_PORT'] } else { 8081 } }
if ($TelnetPort -lt 1 -or $TelnetPort -gt 65535) { throw 'Telnet port must be between 1 and 65535.' }
$scheduledTaskName = '7DPanel-Start-7DTD'

$stopServer = {
    param($port, $timeoutSeconds, $taskName)

    function Stop-LauncherTask {
        param($name)
        if (-not $name) { return }

        $task = Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
        if (-not $task -or $task.State -ne 'Running') { return }

        Write-Host "Stopping scheduled task launcher $name..."
        Stop-ScheduledTask -TaskName $name
        $deadline = (Get-Date).AddSeconds(5)
        do {
            Start-Sleep -Milliseconds 100
            $task = Get-ScheduledTask -TaskName $name
        } while ($task.State -eq 'Running' -and (Get-Date) -lt $deadline)

        if ($task.State -eq 'Running') { throw "Scheduled task launcher $name did not stop within 5 seconds." }
    }

    $running = Get-Process -Name '7DaysToDieServer' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $running) {
        Stop-LauncherTask $taskName
        return [PSCustomObject]@{ Status = 'AlreadyStopped'; ProcessId = $null; TelnetPort = $port; ScheduledTask = $taskName }
    }

    Write-Host "Connecting to local 7DTD Telnet endpoint 127.0.0.1:$port..."
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $connect = $client.ConnectAsync('127.0.0.1', $port)
        if (-not $connect.Wait(5000)) { throw "Timed out connecting to local Telnet port $port." }
        $stream = $client.GetStream()
        $bannerDeadline = (Get-Date).AddSeconds(5)
        while (-not $stream.DataAvailable -and (Get-Date) -lt $bannerDeadline) {
            Start-Sleep -Milliseconds 50
        }
        if (-not $stream.DataAvailable) { throw "The Telnet server did not send its welcome banner on port $port." }

        $buffer = New-Object byte[] 8192
        do {
            while ($stream.DataAvailable) { $null = $stream.Read($buffer, 0, $buffer.Length) }
            Start-Sleep -Milliseconds 50
        } while ($stream.DataAvailable)

        $writer = New-Object System.IO.StreamWriter($stream)
        $writer.AutoFlush = $true
        $writer.WriteLine('shutdown')

        $acknowledged = $false
        $response = New-Object System.Text.StringBuilder
        $ackDeadline = (Get-Date).AddSeconds(5)
        do {
            if ($stream.DataAvailable) {
                $read = $stream.Read($buffer, 0, $buffer.Length)
                $null = $response.Append([System.Text.Encoding]::ASCII.GetString($buffer, 0, $read))
                $acknowledged = $response.ToString().Contains("Executing command 'shutdown'") -or
                    $response.ToString().Contains('Shutting server down')
            }
            if (-not $acknowledged) { Start-Sleep -Milliseconds 50 }
        } while (-not $acknowledged -and (Get-Date) -lt $ackDeadline)

        if (-not $acknowledged) { throw 'The Telnet server did not acknowledge the shutdown command.' }
        Write-Host 'Graceful shutdown command acknowledged.'
    }
    finally {
        if ($writer) { $writer.Dispose() }
        $client.Dispose()
    }

    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    $nextProgress = (Get-Date).AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 500
        $running = Get-Process -Name '7DaysToDieServer' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($running -and (Get-Date) -ge $nextProgress) {
            Write-Host "Waiting for 7DaysToDieServer to exit..."
            $nextProgress = (Get-Date).AddSeconds(5)
        }
    } while ($running -and (Get-Date) -lt $deadline)

    if ($running) { throw "7DaysToDieServer did not exit within $timeoutSeconds seconds after acknowledging shutdown." }
    Stop-LauncherTask $taskName

    [PSCustomObject]@{
        Status = 'Stopped'
        ProcessId = $null
        TelnetPort = $port
        ScheduledTask = $taskName
    }
}

$result = if ($ComputerName) {
    Write-Host "Connecting to remote server $ComputerName through WinRM..."
    $sessionOption = New-PSSessionOption -OpenTimeout 10000 -OperationTimeout (($TimeoutSeconds + 15) * 1000)
    $invokeParameters = @{ ComputerName = $ComputerName; ScriptBlock = $stopServer; ArgumentList = @($TelnetPort, $TimeoutSeconds, $scheduledTaskName); SessionOption = $sessionOption }
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
} else { & $stopServer $TelnetPort $TimeoutSeconds $null }
$result | Select-Object Status, ProcessId, TelnetPort, ScheduledTask, PSComputerName | Format-List
