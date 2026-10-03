$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts\win-x64"

dotnet publish (Join-Path $root "src\PCInfoScreenStudio.App\PCInfoScreenStudio.App.csproj") `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o $out `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishTrimmed=false

Write-Host "Published standalone build to $out"
