param([string]$PublishDirectory = 'artifacts/publish/m2-win-x64', [ValidateRange(5,3600)][int]$Seconds = 300)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path (Join-Path $root $PublishDirectory) 'TaskIsland.exe'
$output = Join-Path $root 'artifacts/idle-baseline.json'
$process = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru
try {
    Start-Sleep -Seconds 20
    $process.Refresh()
    if ($process.HasExited) { throw 'Application exited before sampling' }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $initialCpu = $process.TotalProcessorTime.TotalSeconds
    $samples = [Collections.Generic.List[double]]::new()
    while ($watch.Elapsed.TotalSeconds -lt $Seconds) {
        Start-Sleep -Seconds 5
        $process.Refresh()
        if ($process.HasExited) { throw 'Application exited while sampling' }
        $samples.Add($process.WorkingSet64 / 1MB)
    }
    $cpu = 100 * ($process.TotalProcessorTime.TotalSeconds - $initialCpu) / $watch.Elapsed.TotalSeconds / [Environment]::ProcessorCount
    $result = [ordered]@{
        MeasuredAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        Environment = 'Local developer machine; current local dataset; not clean-machine or full-dataset acceptance'
        LogicalProcessors = [Environment]::ProcessorCount
        DurationSeconds = [math]::Round($watch.Elapsed.TotalSeconds,2)
        CpuPercentOfMachine = [math]::Round($cpu,4)
        WorkingSetMiBMin = [math]::Round(($samples | Measure-Object -Minimum).Minimum,2)
        WorkingSetMiBMax = [math]::Round(($samples | Measure-Object -Maximum).Maximum,2)
        WorkingSetMiBAverage = [math]::Round(($samples | Measure-Object -Average).Average,2)
        Samples = $samples.Count
        PublishDirectoryMiB = [math]::Round((Get-ChildItem (Split-Path $exe) -Recurse -File | Measure-Object Length -Sum).Sum/1MB,2)
    }
    $result | ConvertTo-Json | Set-Content -LiteralPath $output -Encoding utf8
    Get-Content -LiteralPath $output
} finally {
    $process.Refresh()
    if (-not $process.HasExited) { Write-Output 'Application remains running; exit through its tray menu when finished.' }
}
