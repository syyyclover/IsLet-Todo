$ErrorActionPreference = 'Stop'
$toolsDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) '.tools'
$compiler = Join-Path $toolsDirectory 'InnoSetup/ISCC.exe'
if (Test-Path $compiler) { return }
[IO.Directory]::CreateDirectory($toolsDirectory) | Out-Null
$setup = Join-Path $toolsDirectory 'innosetup-6.7.4.exe'
$url = 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_4/innosetup-6.7.4.exe'
Invoke-WebRequest $url -OutFile $setup
$expected = 'A549929319F9120716677C685535CA4671E4B937B5FDC87E2585F32D8EB30E8C'
if ((Get-FileHash $setup -Algorithm SHA256).Hash -ne $expected) { throw 'Inno Setup SHA256 mismatch' }
$destination = Join-Path $toolsDirectory 'InnoSetup'
$process = Start-Process $setup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER', '/NOICONS', ('/DIR="' + $destination + '"')) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0 -or !(Test-Path $compiler)) { throw 'Inno Setup installation failed' }
