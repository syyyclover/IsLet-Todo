# Manual cleanup only: automated deletion was rejected by the host policy.
# User data is outside these exact workspace paths and is never included.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$relativePaths = @(
    'artifacts/publish/m1-final', 'artifacts/publish/m1-flash-win-x64',
    'artifacts/publish/m1-win-x64', 'artifacts/publish/m2-win-x64',
    'artifacts/publish/m3-interaction-win-x64', 'artifacts/publish/m3-win-x64',
    'artifacts/publish/m4-staging', 'artifacts/publish/m4-win-x64', 'artifacts/publish/win-x64',
    'artifacts/interaction-review', 'artifacts/m4-review', 'artifacts/motion',
    'artifacts/quiet-feedback', 'artifacts/ui-m2-fix',
    'artifacts/idle-baseline.json', 'artifacts/inspect-notification-history.ps1'
)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
$resolvedPaths = foreach ($relative in $relativePaths) {
    $path = [IO.Path]::GetFullPath((Join-Path $projectRoot $relative))
    if (!$path.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe cleanup target' }
    if (Test-Path -LiteralPath $path) {
        $entry = Get-Item -LiteralPath $path
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing reparse point' }
        $path
    }
}
$running = Get-Process TaskIsland -ErrorAction SilentlyContinue
foreach ($process in $running) {
    foreach ($path in $resolvedPaths) {
        if ($process.Path -and $process.Path.StartsWith($path + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'An old application is still running; exit it from the tray first'
        }
    }
}
$resolvedPaths | ForEach-Object { Write-Output $_ }
foreach ($path in $resolvedPaths) { Remove-Item -LiteralPath $path -Recurse -Force }
