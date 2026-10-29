param([string]$Version = '', [string]$PublishDirectory = 'artifacts/publish/current')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    if (!$Version) {
        [xml]$project = Get-Content 'src/TaskIsland.Presentation/TaskIsland.Presentation.csproj' -Raw
        $Version = [string]$project.Project.PropertyGroup.Version
    }
    if ($Version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?$') { throw 'Invalid package version' }
    $publishPath = [IO.Path]::GetFullPath($PublishDirectory)
    $binary = Join-Path $publishPath 'TaskIsland.dll'
    if (!(Test-Path $binary)) { throw 'Build and publish current version first' }
    $binaryVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($binary).ProductVersion.Split('+')[0]
    if ($binaryVersion -ne $Version) { throw "Published binary is $binaryVersion but requested package is $Version; rebuild first" }
    Copy-Item -LiteralPath 'docs/USER_GUIDE.md' -Destination (Join-Path $publishPath 'README.txt') -Force
    & "$PSScriptRoot/Install-Packager.ps1"
    & "$projectRoot/.tools/InnoSetup/ISCC.exe" "/DAppVersion=$Version" "/DPublishDir=$publishPath" 'installer/IsLet-Todo.iss'
    if ($LASTEXITCODE) { throw 'Installer compilation failed' }
    $zip = "artifacts/release/IsLet-Todo-$Version-portable-x64.zip"
    Compress-Archive -Path (Join-Path $publishPath '*') -DestinationPath $zip -Force
    Get-Item $zip,"artifacts/release/IsLet-Todo-$Version-setup-x64.exe" | Get-FileHash -Algorithm SHA256 |
        ForEach-Object { $_.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_.Path) } |
        Set-Content 'artifacts/release/SHA256SUMS.txt' -Encoding ascii
} finally { Pop-Location }
