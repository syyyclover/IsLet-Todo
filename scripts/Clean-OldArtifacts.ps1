# Removes generated files only; test source, project files and user data are excluded.
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
[xml]$project = Get-Content (Join-Path $projectRoot 'src/TaskIsland.Presentation/TaskIsland.Presentation.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
$targets = @()
$publish = Join-Path $projectRoot 'artifacts/publish'
if (Test-Path $publish) { $targets += @(Get-ChildItem $publish -Directory | Where-Object Name -ne 'current') }
$release = Join-Path $projectRoot 'artifacts/release'
if (Test-Path $release) {
    $targets += @(Get-ChildItem $release -File | Where-Object { $_.Name -match '^IsLet-Todo-.*\.(exe|zip)$' -and $_.Name -notin @("IsLet-Todo-$version-setup-x64.exe", "IsLet-Todo-$version-portable-x64.zip") })
}
$results = Join-Path $projectRoot 'artifacts/test-results'
if (Test-Path $results) { $targets += Get-Item $results }
foreach ($parent in 'src','tests') {
    $path = Join-Path $projectRoot $parent
    if (Test-Path $path) {
        foreach ($folder in Get-ChildItem $path -Directory) {
            $targets += @(Get-ChildItem $folder.FullName -Directory | Where-Object Name -in 'bin','obj')
        }
    }
}
foreach ($target in $targets) {
    $absolute = [IO.Path]::GetFullPath($target.FullName)
    if (!$absolute.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Cleanup target escapes workspace' }
    if ($target.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing reparse point' }
    foreach ($process in Get-Process TaskIsland -ErrorAction SilentlyContinue) {
        if ($process.Path -and $process.Path.StartsWith($absolute + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Old application is still running' }
    }
}
$targets.FullName | ForEach-Object { Write-Output $_ }
foreach ($target in $targets) { Remove-Item -LiteralPath $target.FullName -Recurse }
