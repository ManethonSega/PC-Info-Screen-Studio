param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

$paths = @(
    @{
        Fill = "#6A1731"
        Data = "M 121,14 L 120,15 L 106,16 L 83,23 L 61,35 L 52,42 L 40,54 L 29,69 L 21,85 L 17,96 L 13,117 L 13,139 L 17,159 L 25,179 L 36,196 L 52,213 L 70,226 L 87,234 L 111,240 L 134,241 L 135,240 L 143,240 L 161,236 L 184,226 L 200,215 L 216,199 L 228,182 L 233,172 L 239,155 L 241,145 L 241,137 L 242,136 L 242,120 L 241,119 L 240,105 L 233,83 L 221,62 L 205,44 L 186,30 L 167,21 L 144,15 Z"
    },
    @{
        Fill = "#100612"
        Data = "M 55,47 L 54,55 L 53,56 L 53,64 L 52,65 L 52,85 L 53,86 L 53,95 L 54,96 L 54,101 L 55,102 L 57,114 L 51,126 L 48,135 L 48,138 L 41,147 L 39,151 L 39,154 L 43,158 L 48,169 L 52,175 L 61,184 L 67,188 L 83,195 L 108,210 L 117,213 L 130,214 L 131,213 L 141,212 L 154,206 L 168,197 L 189,187 L 201,177 L 209,165 L 211,159 L 216,154 L 216,152 L 212,144 L 207,138 L 204,126 L 200,119 L 200,117 L 198,115 L 198,110 L 200,105 L 201,94 L 202,93 L 202,83 L 203,82 L 202,57 L 201,56 L 201,51 L 199,46 L 196,47 L 178,61 L 155,83 L 154,82 L 146,81 L 145,80 L 140,80 L 139,79 L 117,79 L 116,80 L 111,80 L 110,81 L 106,81 L 99,83 L 73,58 L 57,46 Z"
    },
    @{
        Fill = "#F6E8CD"
        Data = "M 71,132 L 71,138 L 72,139 L 72,142 L 77,151 L 82,155 L 86,156 L 87,157 L 95,158 L 96,157 L 100,157 L 101,156 L 103,156 L 110,152 L 105,147 L 96,142 L 96,147 L 95,148 L 95,150 L 94,152 L 92,154 L 91,154 L 89,152 L 87,148 L 87,146 L 86,145 L 86,138 L 85,137 L 83,137 L 80,135 L 78,135 L 77,134 L 75,134 Z M 184,132 L 183,133 L 175,135 L 169,138 L 169,145 L 168,146 L 168,149 L 164,154 L 163,154 L 160,150 L 160,148 L 159,147 L 159,142 L 151,146 L 148,149 L 147,149 L 147,150 L 145,152 L 152,156 L 154,156 L 155,157 L 159,157 L 160,158 L 168,157 L 174,154 L 180,148 L 182,144 L 182,142 L 184,138 Z M 121,168 L 121,169 L 126,174 L 129,174 L 134,169 L 133,168 Z"
    }
)

function New-Brush([string]$Hex) {
    $color = [System.Windows.Media.ColorConverter]::ConvertFromString($Hex)
    $brush = [System.Windows.Media.SolidColorBrush]::new($color)
    $brush.Freeze()
    return $brush
}

function New-IconPng([int]$Size) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()

    try {
        $scale = $Size / 256.0
        $context.PushTransform([System.Windows.Media.ScaleTransform]::new($scale, $scale))

        foreach ($entry in $paths) {
            $geometry = [System.Windows.Media.Geometry]::Parse($entry.Data)
            $geometry.Freeze()
            $context.DrawGeometry((New-Brush $entry.Fill), $null, $geometry)
        }

        $context.Pop()
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

    $stream = [System.IO.MemoryStream]::new()
    try {
        $encoder.Save($stream)
        return $stream.ToArray()
    }
    finally {
        $stream.Dispose()
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
    # ICONDIR
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

Write-Host "Generated application icon: $OutputPath"
