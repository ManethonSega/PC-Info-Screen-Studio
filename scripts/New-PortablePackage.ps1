param(
    [string]$PublishDirectory = (Join-Path $PSScriptRoot '..\artifacts\win-x64'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\release'),
    [Parameter(Mandatory = $true)][string]$SourceCommit,
    [Parameter(Mandatory = $true)][string]$WorkflowUrl
)
$ErrorActionPreference = 'Stop'
if ($SourceCommit -notmatch '^[0-9a-f]{40}$') { throw 'A full source commit SHA is required.' }
$root = Split-Path $PSScriptRoot -Parent
[xml]$project = Get-Content (Join-Path $root 'src\PCInfoScreenStudio.App\PCInfoScreenStudio.App.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+-alpha\.\d+$') { throw 'Only a clearly labelled alpha version can be packaged here.' }
$publish = (Resolve-Path $PublishDirectory).Path
foreach ($name in @('PCInfoScreenStudio.exe', 'PCInfoScreenStudio.dll', 'PCInfoScreenStudio.runtimeconfig.json', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'Prerequisites\PawnIO_setup.exe', 'Assets\Fonts\OFL-1.1.txt')) {
    if (!(Test-Path (Join-Path $publish $name))) { throw "Required portable file is missing: $name" }
}
if (@(Get-ChildItem $publish -Filter libSkiaSharp.dll -Recurse).Count -eq 0) { throw 'The native renderer is missing.' }
if (@(Get-ChildItem (Join-Path $publish 'Assets\Fonts') -Filter *.ttf).Count -lt 4) { throw 'Bundled fonts are missing.' }
Copy-Item (Join-Path $root 'docs\PORTABLE-START-HERE.txt') (Join-Path $publish 'START-HERE.txt')
Copy-Item (Join-Path $root 'docs\COMPATIBILITY.md') (Join-Path $publish 'DEVICE-COMPATIBILITY.md')
Copy-Item (Join-Path $root 'docs\VALIDATION.md') (Join-Path $publish 'VALIDATION.md')
@{ version = $version; sourceCommit = $SourceCommit; sourceUrl = "https://github.com/ManethonSega/PC-Info-Screen-Studio/tree/$SourceCommit"; workflow = $WorkflowUrl; platform = 'win-x64'; selfContained = $true; signed = $false } |
    ConvertTo-Json | Set-Content (Join-Path $publish 'build-info.json') -Encoding utf8
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output = (Resolve-Path $OutputDirectory).Path
$folderName = "PCInfoScreenStudio-$version-win-x64"
$folder = Join-Path $output $folderName
if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
Copy-Item $publish $folder -Recurse
$zip = Join-Path $output "$folderName.zip"
Compress-Archive -Path $folder -DestinationPath $zip -Force
$verification = Join-Path $output 'verification'
if (Test-Path $verification) { Remove-Item $verification -Recurse -Force }
Expand-Archive $zip $verification
$extracted = Join-Path $verification $folderName
$process = Start-Process -FilePath (Join-Path $extracted 'PCInfoScreenStudio.exe') -ArgumentList '--smoke-test' -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Extracted portable ZIP did not start: $($process.ExitCode)" }
$info = Get-Content (Join-Path $extracted 'build-info.json') -Raw | ConvertFrom-Json
if ($info.sourceCommit -ne $SourceCommit -or $info.version -ne $version) { throw 'Portable source/version metadata mismatch.' }
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $folderName.zip" | Set-Content (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
Remove-Item $verification -Recurse -Force
Remove-Item $folder -Recurse -Force
Write-Host "PASS: extracted portable ZIP, version $version, source $SourceCommit, SHA256 $hash"
