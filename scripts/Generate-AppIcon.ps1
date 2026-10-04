param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

$sourcePath = Join-Path $PSScriptRoot "..\src\PCInfoScreenStudio.App\Assets\PCInfoScreenStudio.png"
if (-not [System.IO.File]::Exists($sourcePath)) {
    throw "Application icon source was not found: $sourcePath"
}

$sourceUri = [System.Uri]::new([System.IO.Path]::GetFullPath($sourcePath))
$decoder = [System.Windows.Media.Imaging.PngBitmapDecoder]::new(
    $sourceUri,
    [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
    [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
$source = $decoder.Frames[0]

function New-IconPng([int]$Size) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    [System.Windows.Media.RenderOptions]::SetBitmapScalingMode(
        $visual,
        [System.Windows.Media.BitmapScalingMode]::HighQuality)
    $context = $visual.RenderOpen()

    try {
        $context.DrawImage(
            $source,
            [System.Windows.Rect]::new(0, 0, $Size, $Size))
    }
    finally {
        $context.Close()
    }

    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(
        $Size,
        $Size,
        96,
        96,
        [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)

    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = [System.IO.MemoryStream]::new()
    try {
        $encoder.Save($memory)
        return $memory.ToArray()
    }
    finally {
        $memory.Dispose()
    }
}

$directory = [System.IO.Path]::GetDirectoryName($OutputPath)
if ($directory) {
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = @()
foreach ($size in $sizes) {
    $images += ,@($size, (New-IconPng $size))
}

$stream = [System.IO.File]::Open(
    $OutputPath,
    [System.IO.FileMode]::Create,
    [System.IO.FileAccess]::Write,
    [System.IO.FileShare]::None)
$writer = [System.IO.BinaryWriter]::new($stream)

try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$images.Count)

    $offset = 6 + (16 * $images.Count)
    foreach ($item in $images) {
        $size = [int]$item[0]
        $bytes = [byte[]]$item[1]
        $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
        $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$bytes.Length)
        $writer.Write([UInt32]$offset)
        $offset += $bytes.Length
    }

    foreach ($item in $images) {
        $writer.Write([byte[]]$item[1])
    }
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

Write-Host "Generated application icon from $sourcePath"
