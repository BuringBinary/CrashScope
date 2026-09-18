<#
.SYNOPSIS
  Publishes a non-destructive Windows x64 test package into a new, unique directory.
  Does not stop applications, clean dist, install services, or launch the Desktop.
#>
param(
    [ValidatePattern('^[0-9]{8}-[0-9]{6}$')]
    [string]$BuildId = (Get-Date -Format 'yyyyMMdd-HHmmss'),
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$BaseVersion = '0.9.2'
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repo = Split-Path -Parent $PSScriptRoot
$parent = Join-Path $repo 'artifacts\test-packages'
$delivery = Join-Path $parent $BuildId
if (Test-Path -LiteralPath $delivery) { throw "Refusing to overwrite existing delivery: $delivery" }
New-Item -ItemType Directory -Path $parent -Force | Out-Null
New-Item -ItemType Directory -Path $delivery | Out-Null
$package = Join-Path $delivery 'Toolbox'
$logs = Join-Path $delivery 'verification'
New-Item -ItemType Directory -Path $package, $logs | Out-Null
$version = $BaseVersion + '-test.' + $BuildId.Replace('-', '')
$assemblyVersion = $BaseVersion + '.0'
$utf8 = New-Object System.Text.UTF8Encoding($false)
function Write-Utf8([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, $utf8)
}
function Write-Json([string]$Path, $Value) {
    Write-Utf8 $Path (($Value | ConvertTo-Json -Depth 12) + "`n")
}
function Relative-Path([string]$Root, [string]$Path) {
    $Path.Substring($Root.TrimEnd('\').Length + 1).Replace('\', '/')
}
function Get-SourceSnapshot {
    $files = @()
    foreach ($directory in @('src', 'test', 'scripts')) {
        $files += Get-ChildItem (Join-Path $repo $directory) -Recurse -File |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj|TestResults|packages)[\\/]' -and
                $_.Extension -in @('.cs','.csproj','.xaml','.json','.props','.targets','.ps1','.cmd','.resx','.config','.txt') }
    }
    $files += Get-ChildItem $repo -File -Force | Where-Object {
        $_.Name -in @('global.json','NuGet.Config','nuget.config','Directory.Build.props','Directory.Build.targets','Directory.Packages.props') }
    @($files | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = Relative-Path $repo $_.FullName; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash }
    })
}
function Get-ProtectedSnapshot {
    $files = @()
    $dist = Join-Path $repo 'dist'
    if (Test-Path $dist) {
        $files += Get-ChildItem $dist -File -Filter '*.zip'
        foreach ($relative in @('toolbox-win-x64\Desktop\CrashScope.Desktop.exe','toolbox-win-x64\Desktop\CrashScope.Desktop.dll',
            'toolbox-win-x64\Desktop\CrashScope.Core.dll','toolbox-win-x64\Agent\CrashScope.Agent.exe','toolbox-win-x64\Service\CrashScope.Service.exe')) {
            $p = Join-Path $dist $relative
            if (Test-Path $p) { $files += Get-Item $p }
        }
    }
    $incidents = Join-Path $env:LOCALAPPDATA 'CrashScope\incidents'
    foreach ($name in @('recovered-d118b296401c4294a704a52b5a0ad80c','recovered-bf0ada058c2e4ef68cf19cf9b8376bd4')) {
        $p = Join-Path $incidents $name
        if (Test-Path $p) { $files += Get-ChildItem $p -Recurse -File }
    }
    @($files | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = $_.FullName; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash }
    })
}
function Fingerprint($Snapshot) {
    $text = ($Snapshot | ForEach-Object { $_.path + ':' + $_.sha256 }) -join "`n"
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($hasher.ComputeHash($utf8.GetBytes($text)))).Replace('-', '') }
    finally { $hasher.Dispose() }
}
function Invoke-Dotnet([string[]]$Arguments, [string]$LogName) {
    Write-Host ('dotnet ' + ($Arguments -join ' '))
    & $script:dotnet @Arguments 2>&1 | Out-File (Join-Path $logs $LogName) -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed (exit $LASTEXITCODE); see $LogName" }
}
function Assert-X64Exe([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $reader = New-Object IO.BinaryReader($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5a4d) { throw "Not an MZ executable: $Path" }
        $stream.Position = 0x3c
        $offset = $reader.ReadInt32()
        $stream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw "Not an x64 PE executable: $Path" }
    } finally { $reader.Dispose(); $stream.Dispose() }
}
try {
    $dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (-not (Test-Path $dotnet)) {
        $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
        if (-not $cmd) { throw 'A .NET 8 SDK is required.' }
        $dotnet = $cmd.Source
    }
    $sdk = (& $dotnet --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'dotnet --version failed.' }
    $gitHead = (& git -C $repo rev-parse HEAD | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve source Git revision.' }
    $gitStatus = @(& git -C $repo status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect working tree.' }
    $source = Get-SourceSnapshot
    $sourceFingerprint = Fingerprint $source
    Write-Json (Join-Path $logs 'source-snapshot.json') $source
    Write-Host 'Hashing protected old packages and the two existing incident bundles (read-only).'
    $protected = Get-ProtectedSnapshot
    Write-Json (Join-Path $logs 'protected-before.json') $protected
    $infoVersion = "$version+worktree.$($sourceFingerprint.Substring(0,12).ToLowerInvariant())"

    $classes = @('SensorHistoryBufferTests','RealMachineFixTests','HypothesisEvaluatorEtwTests','StorageEvidenceTests',
        'SensorIndicesTests','ReliabilityConvergenceTests','TimelineTelemetryTests','CrossIncidentAnalyzerTests','IncidentAccessTests',
        'SessionJournalTests','RetentionServiceTests','CrashSafeJsonlWriterTests','DiagnosticExportRedactionTests',
        'DisplayTopologyTrackerTests','EtwModelJsonRoundtripTests','GpuTransitionDetectorTests','GraphicsContextBuilderTests',
        'ModelsJsonRoundtripTests','RpcFramingTests','TimelineBuilderEtwTests','CollectionChainTests')
    $filter = ($classes | ForEach-Object { 'FullyQualifiedName~' + $_ }) -join '|'
    Invoke-Dotnet @('test',(Join-Path $repo 'test\CrashScope.Core.Tests\CrashScope.Core.Tests.csproj'),'-c','Release',
        '--filter',$filter,'--results-directory',$logs,'--logger','trx;LogFileName=package-regression.trx') 'regression.log'
    [xml]$trx = Get-Content (Join-Path $logs 'package-regression.trx') -Raw
    $counts = $trx.SelectSingleNode('//*[local-name()="Counters"]')
    if (-not $counts -or [int]$counts.failed -ne 0 -or [int]$counts.total -lt 237 -or [int]$counts.passed -ne [int]$counts.total) {
        throw 'Regression counts are missing, failing, skipped, or unexpectedly incomplete.'
    }

    $hosts = @('Desktop','Agent','Service')
    foreach ($hostName in $hosts) {
        $out = Join-Path $package $hostName
        $project = Join-Path $repo "src\CrashScope.$hostName\CrashScope.$hostName.csproj"
        Invoke-Dotnet @('publish',$project,'-c','Release','-r','win-x64','--self-contained','true','-o',$out,
            '-p:DebugType=None','-p:DebugSymbols=false',"-p:Version=$version","-p:AssemblyVersion=$assemblyVersion","-p:FileVersion=$assemblyVersion",
            "-p:InformationalVersion=$infoVersion",'-p:IncludeSourceRevisionInInformationalVersion=false') "publish-$hostName.log"
        $exe = Join-Path $out "CrashScope.$hostName.exe"
        Assert-X64Exe $exe
        foreach ($file in @("CrashScope.$hostName.dll","CrashScope.$hostName.deps.json","CrashScope.$hostName.runtimeconfig.json",
            'CrashScope.Core.dll','coreclr.dll','hostfxr.dll','hostpolicy.dll','System.Private.CoreLib.dll')) {
            if (-not (Test-Path (Join-Path $out $file))) { throw "Missing $hostName/$file" }
        }
        $runtime = Get-Content (Join-Path $out "CrashScope.$hostName.runtimeconfig.json") -Raw | ConvertFrom-Json
        if (-not $runtime.runtimeOptions.includedFrameworks) { throw "Self-contained runtime config is missing: $hostName" }
        $assembly = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $out "CrashScope.$hostName.dll"))
        if ($assembly.Version.ToString() -ne $assemblyVersion) { throw "Wrong assembly version: $hostName" }
        foreach ($dll in @("CrashScope.$hostName.dll",'CrashScope.Core.dll')) {
            $product = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $out $dll)).ProductVersion
            if ($product -ne $infoVersion) { throw "Build identity mismatch: $hostName/$dll ($product)" }
        }
    }
    foreach ($relative in @('Locales\en-US.json','Locales\zh-CN.json','Themes\DarkTheme.xaml','Themes\LightTheme.xaml',
        'PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll','OxyPlot.Wpf.dll','LibreHardwareMonitorLib.dll')) {
        if (-not (Test-Path (Join-Path $package "Desktop\$relative"))) { throw "Desktop asset missing: $relative" }
    }
    # The --help branch returns before creating data directories or RecorderEngine (verified in Program.cs).
    & (Join-Path $package 'Agent\CrashScope.Agent.exe') --help 2>&1 | Out-File (Join-Path $logs 'agent-help.txt') -Encoding utf8
    if ($LASTEXITCODE -ne 0 -or (Get-Item (Join-Path $logs 'agent-help.txt')).Length -eq 0) { throw 'Published Agent --help failed.' }

    $launcher = @'
@echo off
setlocal
set "CRASHSCOPE_PACKAGE_ROOT=%~dp0"
echo CrashScope TEST PACKAGE - stop recording and exit the old app from its tray first.
powershell.exe -NoProfile -Command "$ErrorActionPreference='Stop'; $p=Get-Process -Name 'CrashScope.Desktop','CrashScope.Agent','CrashScope.Service' -ErrorAction SilentlyContinue; if($p){Write-Host 'An existing CrashScope host is running. Stop recording and exit it normally before launching this test package.'; $p | Select-Object Id,ProcessName | Format-Table; exit 2}; $exe=Join-Path $env:CRASHSCOPE_PACKAGE_ROOT 'Desktop\CrashScope.Desktop.exe'; Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe)"
if errorlevel 1 pause
endlocal
'@
    [IO.File]::WriteAllText((Join-Path $package 'Start-CrashScope.cmd'), ($launcher.Replace("`r`n","`n").Replace("`n","`r`n") + "`r`n"), [Text.Encoding]::ASCII)
    [IO.File]::WriteAllText((Join-Path $package 'Agent\start-agent-passive.cmd'), "@echo off`r`n`"%~dp0CrashScope.Agent.exe`" --no-actions %*`r`n", [Text.Encoding]::ASCII)
    $readme = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'test-package-readme.zh-CN.txt'), $utf8)
    $readme = $readme.Replace('{{VERSION}}', $version).Replace('{{INFO_VERSION}}', $infoVersion).Replace('{{TEST_COUNT}}', [string]$counts.passed).Replace('{{ASSEMBLY_VERSION}}', $assemblyVersion)
    Write-Utf8 (Join-Path $package 'README.txt') ($readme + "`n")
    Write-Json (Join-Path $package 'build-info.json') ([ordered]@{
        version=$version; buildId=$BuildId; informationalVersion=$infoVersion; assemblyVersion=$assemblyVersion;
        gitCommit=$gitHead; dirtyWorkingTree=($gitStatus.Count -gt 0); sourceSha256=$sourceFingerprint;
        sdk=$sdk; runtimeIdentifier='win-x64'; selfContained=$true; createdUtc=[DateTime]::UtcNow.ToString('o');
        selectedTestsPassed=[int]$counts.passed; desktopGuiSmokeExecuted=$false; serviceInstalled=$false
    })
    if (Test-Path (Join-Path $repo 'LICENSE')) { Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $package 'LICENSE.txt') }
    $sourceAfter = Get-SourceSnapshot
    if ((Fingerprint $sourceAfter) -ne $sourceFingerprint) { throw 'Source files changed during publication; do not use this package.' }
    $protectedAfter = Get-ProtectedSnapshot
    if ((Fingerprint $protectedAfter) -ne (Fingerprint $protected)) { throw 'Protected old artifacts/incidents changed during publication; inspect before use.' }
    Write-Json (Join-Path $logs 'protected-after.json') $protectedAfter

    $manifest = @(Get-ChildItem $package -Recurse -File | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path=Relative-Path $package $_.FullName; bytes=$_.Length; sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash }
    })
    Write-Json (Join-Path $package 'file-manifest.json') $manifest
    $expected = @{}
    foreach ($file in Get-ChildItem $package -Recurse -File) {
        $expected[(Relative-Path $package $file.FullName)] = (Get-FileHash $file.FullName -Algorithm SHA256).Hash
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipPath = Join-Path $delivery "CrashScope.Toolbox.$version.win-x64.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($package, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    $seen = @{}
    try {
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName.EndsWith('/')) { continue }
            $name = $entry.FullName.Replace('\','/')
            if (-not $expected.ContainsKey($name) -or $seen.ContainsKey($name)) { throw "Unexpected/duplicate archive member: $name" }
            $stream = $entry.Open(); $hasher = [Security.Cryptography.SHA256]::Create()
            try { $hash = ([BitConverter]::ToString($hasher.ComputeHash($stream))).Replace('-','') }
            finally { $hasher.Dispose(); $stream.Dispose() }
            if ($hash -ne $expected[$name]) { throw "Archive hash mismatch: $name" }
            $seen[$name] = $true
        }
        if ($seen.Count -ne $expected.Count) { throw 'Archive is missing files.' }
    } finally { $archive.Dispose() }
    $zipHash = (Get-FileHash $zipPath -Algorithm SHA256).Hash
    Write-Utf8 ($zipPath + '.sha256') ($zipHash + '  ' + [IO.Path]::GetFileName($zipPath) + "`n")
    $result = [ordered]@{ status='verified'; version=$version; buildId=$BuildId; packageDirectory=$package; zipPath=$zipPath;
        zipBytes=(Get-Item $zipPath).Length; zipSha256=$zipHash; verifiedFiles=$seen.Count; testsPassed=[int]$counts.passed;
        protectedFilesUnchanged=$protected.Count; sourceUnchanged=$true; desktopGuiSmokeExecuted=$false;
        launcher=(Join-Path $package 'Start-CrashScope.cmd'); informationalVersion=$infoVersion }
    Write-Json (Join-Path $delivery 'verification-result.json') $result
    Write-Host 'PACKAGE VERIFIED'
    $result | ConvertTo-Json -Depth 6 | Write-Host
} catch {
    Write-Json (Join-Path $delivery 'verification-result.json') ([ordered]@{ status='failed'; buildId=$BuildId; error=$_.Exception.Message })
    throw
}
