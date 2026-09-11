$ErrorActionPreference = 'Stop'
$taskName = 'CrashScope'
Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
Write-Host "Removed startup task: $taskName"
