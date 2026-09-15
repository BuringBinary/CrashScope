$ErrorActionPreference = 'Stop'
$serviceName = 'CrashScope'

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "Service '$serviceName' is not installed."
    exit 0
}

Write-Host "Stopping service '$serviceName'..."
Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue

Write-Host "Removing service..."
sc.exe delete $serviceName

Write-Host "CrashScope Service uninstalled successfully."