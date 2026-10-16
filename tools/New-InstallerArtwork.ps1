[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
[void][IO.Directory]::CreateDirectory($OutputDirectory)
# Reuse approved transparent brand art. This only composes wizard BMP resources.
$logo = [Drawing.Image]::FromFile((Join-Path $repoRoot 'assets\brand\previews\temhalo-logo-horizontal-color.png'))
$symbol = [Drawing.Image]::FromFile((Join-Path $repoRoot 'assets\brand\icons\app\temhalo-256.png'))
try {
    foreach ($kind in 'header','welcome') {
        $width = if ($kind -eq 'header') { 150 } else { 164 }
        $height = if ($kind -eq 'header') { 57 } else { 314 }
        $bitmap = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([Drawing.ColorTranslator]::FromHtml('#0D1218'))
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
            if ($kind -eq 'header') {
                $logoWidth = 132; $logoHeight = [int]($logoWidth * $logo.Height / $logo.Width)
                $graphics.DrawImage($logo, 8, [int](($height - $logoHeight) / 2), $logoWidth, $logoHeight)
            }
            else {
                $graphics.DrawImage($symbol, 34, 62, 96, 96)
                $logoWidth = 132; $logoHeight = [int]($logoWidth * $logo.Height / $logo.Width)
                $graphics.DrawImage($logo, 16, 180, $logoWidth, $logoHeight)
                $pen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#7CE8CD'), 2)
                try { $graphics.DrawLine($pen, 60, 244, 104, 244) } finally { $pen.Dispose() }
            }
            $bitmap.Save((Join-Path $OutputDirectory ($kind + '.bmp')), [Drawing.Imaging.ImageFormat]::Bmp)
        }
        finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
}
finally { $logo.Dispose(); $symbol.Dispose() }
