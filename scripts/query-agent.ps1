param(
    [ValidateSet('status', 'latest-telemetry', 'sensor-catalog', 'list-incidents', 'incident-detail')]
    [string]$Command = 'status',
    [string]$IncidentId
)

$ErrorActionPreference = 'Stop'
$pipeName = 'CrashScope.Agent.v1'
$pipe = [System.IO.Pipes.NamedPipeClientStream]::new(
    '.',
    $pipeName,
    [System.IO.Pipes.PipeDirection]::InOut,
    [System.IO.Pipes.PipeOptions]::None)

try {
    if ($Command -eq 'incident-detail' -and [string]::IsNullOrWhiteSpace($IncidentId)) {
        throw 'incident-detail requires -IncidentId.'
    }

    $pipe.Connect(2000)
    $writer = [System.IO.StreamWriter]::new($pipe, [System.Text.UTF8Encoding]::new($false), 1024, $true)
    $reader = [System.IO.StreamReader]::new($pipe, [System.Text.UTF8Encoding]::new($false), $false, 1024, $true)
    $writer.AutoFlush = $true

    $request = @{ command = $Command }
    if (-not [string]::IsNullOrWhiteSpace($IncidentId)) {
        $request.incidentId = $IncidentId
    }

    $writer.WriteLine(($request | ConvertTo-Json -Compress))

    $response = $reader.ReadLine()
    if (-not $response) { throw 'Agent returned no response.' }
    $response | ConvertFrom-Json | ConvertTo-Json -Depth 30
}
finally {
    if ($null -ne $reader) { $reader.Dispose() }
    if ($null -ne $writer) { $writer.Dispose() }
    $pipe.Dispose()
}
