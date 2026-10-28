$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sdkVersion = (Get-Content (Join-Path $root 'global.json') -Raw | ConvertFrom-Json).sdk.version
$toolsDirectory = Join-Path $root '.tools'
New-Item -ItemType Directory -Force -Path $toolsDirectory | Out-Null
$installer = Join-Path $toolsDirectory 'dotnet-install.ps1'
Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
& $installer -Version $sdkVersion -InstallDir (Join-Path $toolsDirectory 'dotnet') -NoPath
if ($LASTEXITCODE) { throw 'SDK installation failed' }
