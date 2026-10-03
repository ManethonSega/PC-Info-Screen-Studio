$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts\win-arm64"

dotnet publish (Join-Path $root "src\PCInfoScreenStudio.App\PCInfoScreenStudio.App.csproj") -c Release -r win-arm64 --self-contained true -o $out -p:PublishSingleFile=false -p:PublishTrimmed=false

Write-Host "Published self-contained Windows ARM64 build to $out"
Write-Host "Keep the complete folder together when copying or sharing this alpha build."
