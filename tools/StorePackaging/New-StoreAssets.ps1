param(
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore,WindowsBase
$image = [System.Windows.Media.Imaging.BitmapFrame]::Create(
    [uri](Resolve-Path -LiteralPath $Source).Path,
    [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
    [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
if ($image.PixelWidth -ne 264 -or $image.PixelHeight -ne 264) {
    throw 'Expected the preserved 264x264 R-arrow symbol.'
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
foreach ($asset in @(
    @{ Name = 'StoreLogo.png'; Size = 50 },
    @{ Name = 'Square44x44Logo.png'; Size = 44 },
    @{ Name = 'Square150x150Logo.png'; Size = 150 }
)) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    [System.Windows.Media.RenderOptions]::SetBitmapScalingMode($visual, [System.Windows.Media.BitmapScalingMode]::HighQuality)
    $drawing = $visual.RenderOpen()
    try { $drawing.DrawImage($image, (New-Object System.Windows.Rect(0, 0, $asset.Size, $asset.Size))) }
    finally { $drawing.Close() }
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(
        $asset.Size, $asset.Size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create((Join-Path $OutputDirectory $asset.Name))
    try { $encoder.Save($stream) }
    finally { $stream.Dispose() }
}
