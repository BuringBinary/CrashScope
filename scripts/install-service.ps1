param(
    [Parameter(Mandatory = $false)]
    [string]$ServicePath
)

$ErrorActionPreference = 'Stop'

if (-not $ServicePath) {
    $repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $ServicePath = Join-Path $repo 'src\CrashScope.Service\bin\Release\net8.0-windows\publish\CrashScope.Service.exe'
}

if (-not (Test-Path $ServicePath)) {
    Write-Error "Service executable not found: $ServicePath"
    Write-Host "Build and publish first: dotnet publish src/CrashScope.Service -c Release -o src/CrashScope.Service/bin/Release/net8.0-windows/publish"
    exit 1
}

$serviceName = 'CrashScope'
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Service '$serviceName' already exists. Stopping and removing..."
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName
    Start-Sleep -Seconds 2
}

Write-Host "Creating Windows Service: $serviceName"
New-Service -Name $serviceName `
    -BinaryPathName $ServicePath `
    -DisplayName 'CrashScope Flight Recorder' `
    -Description 'Background hardware telemetry recorder for crash diagnostics' `
    -StartupType Automatic

Write-Host "Starting service..."
Start-Service -Name $serviceName

Write-Host "CrashScope Service installed and started successfully."
Write-Host "Service name: $serviceName"
Write-Host "Status: $( (Get-Service -Name $serviceName).Status )"