param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release', [switch]$Publish)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$localSdk = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path $localSdk) { $localSdk } else { 'dotnet' }
Push-Location $projectRoot
try {
    & $dotnet restore TaskIsland.slnx --locked-mode
    if ($LASTEXITCODE) { throw 'Dependency restore failed' }
    & $dotnet test TaskIsland.slnx -c $Configuration --no-restore
    if ($LASTEXITCODE) { throw 'Tests failed' }
    & $dotnet build TaskIsland.slnx -c $Configuration --no-restore
    if ($LASTEXITCODE) { throw 'Build failed' }
    if ($Publish) {
        $publishPath = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts/publish/m4-win-x64'))
        $artifactRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
        if (-not $publishPath.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid publish path' }
        if (Test-Path -LiteralPath $publishPath) { Remove-Item -LiteralPath $publishPath -Recurse -Force }
        & $dotnet publish src/TaskIsland.Presentation -c $Configuration -r win-x64 --self-contained true --no-restore -o artifacts/publish/m4-win-x64
        if ($LASTEXITCODE) { throw 'Publish failed' }
    }
} finally { Pop-Location }
