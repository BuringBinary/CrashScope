param(
    [ValidateSet('status', 'latest-telemetry', 'sensor-catalog')]
    [string]$Command = 'status'
)

$ErrorActionPreference = 'Stop'
$pipeName = 'CrashScope.Agent.v1'
$pipe = [System.IO.Pipes.NamedPipeClientStream]::new(
    '.',
    $pipeName,
    [System.IO.Pipes.PipeDirection]::InOut,
    [System.IO.Pipes.PipeOptions]::None)

try {
    $pipe.Connect(2000)
    $writer = [System.IO.StreamWriter]::new($pipe, [System.Text.UTF8Encoding]::new($false), 1024, $true)
    $reader = [System.IO.StreamReader]::new($pipe, [System.Text.UTF8Encoding]::new($false), $false, 1024, $true)
    $writer.AutoFlush = $true

    $request = @{ command = $Command } | ConvertTo-Json -Compress
    $writer.WriteLine($request)

    $response = $reader.ReadLine()
    if (-not $response) { throw 'Agent returned no response.' }
    $response | ConvertFrom-Json | ConvertTo-Json -Depth 20
}
finally {
    if ($null -ne $reader) { $reader.Dispose() }
    if ($null -ne $writer) { $writer.Dispose() }
    $pipe.Dispose()
}
