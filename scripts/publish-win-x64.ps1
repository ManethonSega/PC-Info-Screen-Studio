$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts\win-x64"

dotnet publish (Join-Path $root "src\PCInfoScreenStudio.App\PCInfoScreenStudio.App.csproj") -c Release -r win-x64 --self-contained true -o $out -p:PublishSingleFile=false -p:PublishTrimmed=false

$prereq = Join-Path $out "Prerequisites"
New-Item -ItemType Directory -Force -Path $prereq | Out-Null

$pawnUrl = "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe"
$pawnInstaller = Join-Path $prereq "PawnIO_setup.exe"
$pawnExpected = "1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032"

Invoke-WebRequest -Uri $pawnUrl -OutFile $pawnInstaller

$pawnActual = (Get-FileHash -Path $pawnInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
if ($pawnActual -ne $pawnExpected) {
    Remove-Item $pawnInstaller -Force -ErrorAction SilentlyContinue
    throw "PawnIO installer SHA-256 mismatch. Expected $pawnExpected, got $pawnActual."
}

Write-Host "Bundled verified PawnIO 2.2.0 prerequisite."
Write-Host "Published self-contained Windows x64 build to $out"
Write-Host "Keep the complete folder together when copying or sharing this alpha build."
