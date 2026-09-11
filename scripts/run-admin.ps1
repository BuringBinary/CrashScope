$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\CrashScope.Agent\CrashScope.Agent.csproj'
Start-Process -FilePath 'dotnet' -ArgumentList @('run','--project',"`"$project`"") -Verb RunAs
