param(
    [Parameter(Mandatory=$true)]
    [string]$ExecutablePath
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $ExecutablePath)) { throw "Executable not found: $ExecutablePath" }

$taskName = 'CrashScope'
$action = New-ScheduledTaskAction -Execute $ExecutablePath
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force
Write-Host "Installed startup task: $taskName"
