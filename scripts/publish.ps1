$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\CrashScope.Agent\CrashScope.Agent.csproj'
$out = Join-Path $repo 'dist\win-x64'

dotnet publish $project -c Release -r win-x64 --self-contained false -o $out
Write-Host "Published to $out"
