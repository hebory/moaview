$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$assetDirectory = Join-Path $PSScriptRoot '../src/MoaView/Assets/Brand'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null

# Two stacked files and an eye, drawn on a 256-by-256 canvas.
$artwork = @'
<Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Width="256" Height="256">
    <Rectangle Canvas.Left="8" Canvas.Top="8" Width="240" Height="240" RadiusX="55" RadiusY="55">
        <Rectangle.Fill>
            <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
                <GradientStop Color="#385FE9" Offset="0" />
                <GradientStop Color="#17245C" Offset="1" />
            </LinearGradientBrush>
        </Rectangle.Fill>
    </Rectangle>
    <Path Fill="#75E7D5" Data="M 89,40 H 169 L 208,79 V 157 Q 208,173 192,173 H 89 Q 73,173 73,157 V 56 Q 73,40 89,40 Z" />
    <Path Fill="#FAFCFF" Data="M 63,68 H 146 L 184,106 V 199 Q 184,215 168,215 H 63 Q 47,215 47,199 V 84 Q 47,68 63,68 Z" />
    <Path Fill="#B9C8FB" Data="M 146,68 L 184,106 H 157 Q 146,106 146,95 Z" />
    <Path Fill="#263C96" Data="M 65,149 Q 115,91 166,149 Q 115,207 65,149 Z" />
    <Ellipse Canvas.Left="100" Canvas.Top="133" Width="32" Height="32" Fill="#FAFCFF" />
    <Ellipse Canvas.Left="109" Canvas.Top="142" Width="14" Height="14" Fill="#263C96" />
</Canvas>
'@

$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$frames = foreach ($size in $sizes) {
    $canvas = [Windows.Markup.XamlReader]::Parse($artwork)
    $canvas.LayoutTransform = [Windows.Media.ScaleTransform]::new($size / 256.0, $size / 256.0)
    $canvas.Measure([Windows.Size]::new($size, $size))
    $canvas.Arrange([Windows.Rect]::new(0, 0, $size, $size))
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($canvas)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    try {
        $encoder.Save($stream)
        [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
    } finally { $stream.Dispose() }
}

$iconPath = Join-Path $assetDirectory 'MoaView.ico'
$writer = [IO.BinaryWriter]::new([IO.File]::Create($iconPath))
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        # Zero in the one-byte width and height fields denotes 256 pixels.
        $dimension = [byte]($frame.Size % 256)
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose() }

[IO.File]::WriteAllBytes((Join-Path $assetDirectory 'MoaView.png'), $frames[-1].Bytes)
