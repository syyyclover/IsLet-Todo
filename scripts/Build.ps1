param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release', [switch]$Publish)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$localSdk = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path $localSdk) { $localSdk } else { 'dotnet' }
Push-Location $projectRoot
try {
    & $dotnet restore TaskIsland.slnx --locked-mode
    if ($LASTEXITCODE) { throw 'Dependency restore failed' }
    & $dotnet test TaskIsland.slnx -c $Configuration --no-restore --logger trx --results-directory artifacts/test-results
    if ($LASTEXITCODE) {
        if ($env:GITHUB_ACTIONS -eq 'true') {
            Get-ChildItem artifacts/test-results -Filter '*.trx' | ForEach-Object {
                [xml]$report = Get-Content $_.FullName -Raw
                foreach ($result in $report.SelectNodes("//*[local-name()='UnitTestResult' and @outcome='Failed']")) {
                    $detail = $result.Output.ErrorInfo.Message + [Environment]::NewLine + $result.Output.ErrorInfo.StackTrace
                    if ($detail.Length -gt 6000) { $detail = $detail.Substring(0, 6000) }
                    $detail = $detail.Replace('%','%25').Replace("`r",'%0D').Replace("`n",'%0A')
                    $name = $result.testName.Replace('%','%25').Replace(',', '%2C').Replace(':','%3A')
                    Write-Output "::error title=$name::$detail"
                }
            }
        }
        throw 'Tests failed'
    }
    & $dotnet build TaskIsland.slnx -c $Configuration --no-restore
    if ($LASTEXITCODE) { throw 'Build failed' }
    if ($Publish) {
        $publishPath = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts/publish/current'))
        $artifactRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
        if (-not $publishPath.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid publish path' }
        if (Test-Path -LiteralPath $publishPath) { Remove-Item -LiteralPath $publishPath -Recurse -Force }
        & $dotnet publish src/TaskIsland.Presentation/TaskIsland.Presentation.csproj -c $Configuration -r win-x64 --self-contained true --no-restore -o artifacts/publish/current
        if ($LASTEXITCODE) { throw 'Publish failed' }
    }
} finally { Pop-Location }
