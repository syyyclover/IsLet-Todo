param([string]$Version = '0.4.0-preview.1')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?$') { throw 'Invalid package version' }
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    if (!(Test-Path 'artifacts/publish/current/TaskIsland.exe')) { throw 'Build and publish current version first' }
    & "$PSScriptRoot/Install-Packager.ps1"
    & "$projectRoot/.tools/InnoSetup/ISCC.exe" "/DAppVersion=$Version" 'installer/IsLet-Todo.iss'
    if ($LASTEXITCODE) { throw 'Installer compilation failed' }
    $zip = "artifacts/release/IsLet-Todo-$Version-portable-x64.zip"
    Compress-Archive -Path 'artifacts/publish/current/*' -DestinationPath $zip -Force
    Get-ChildItem 'artifacts/release' -File | Where-Object Extension -in '.exe', '.zip' | Get-FileHash -Algorithm SHA256 |
        ForEach-Object { $_.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_.Path) } |
        Set-Content 'artifacts/release/SHA256SUMS.txt' -Encoding ascii
} finally { Pop-Location }
