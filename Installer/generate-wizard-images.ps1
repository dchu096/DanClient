param(
    [Parameter(Mandatory = $true)][string]$LogoPath,
    [Parameter(Mandatory = $true)][string]$OutputDir
)

Add-Type -AssemblyName System.Drawing

function New-WizardBitmap {
    param([int]$Width, [int]$Height, [bool]$Large)

    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    $bg = if ($Large) { [System.Drawing.Color]::FromArgb(255, 12, 18, 10) } else { [System.Drawing.Color]::FromArgb(255, 20, 28, 16) }
    $graphics.Clear($bg)

    if ($Large) {
        $footer = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 18, 26, 14))
        $graphics.FillRectangle($footer, 0, [int]($Height * 0.7), $Width, [int]($Height * 0.3))
        $footer.Dispose()

        $titleFont = New-Object System.Drawing.Font('Segoe UI', 16, [System.Drawing.FontStyle]::Bold)
        $subFont = New-Object System.Drawing.Font('Segoe UI', 9)
        $muted = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(180, 180, 180))
        $graphics.DrawString('DanClient', $titleFont, [System.Drawing.Brushes]::White, 14, 24)
        $graphics.DrawString('Minecraft Launcher', $subFont, $muted, 14, 52)
        $titleFont.Dispose()
        $subFont.Dispose()
        $muted.Dispose()
    }

    if (Test-Path $LogoPath) {
        $logo = [System.Drawing.Image]::FromFile($LogoPath)
        $size = if ($Large) { 72 } else { 44 }
        $x = [int](($Width - $size) / 2)
        $y = if ($Large) { [int]($Height * 0.38) } else { [int](($Height - $size) / 2) }
        $graphics.DrawImage($logo, $x, $y, $size, $size)
        $logo.Dispose()
    }

    $graphics.Dispose()
    return $bitmap
}

$OutputDir = $OutputDir.TrimEnd('\', '/')
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$largePath = Join-Path $OutputDir 'WizardLarge.bmp'
$smallPath = Join-Path $OutputDir 'WizardSmall.bmp'

$large = New-WizardBitmap -Width 164 -Height 314 -Large $true
$small = New-WizardBitmap -Width 55 -Height 55 -Large $false
$large.Save($largePath, [System.Drawing.Imaging.ImageFormat]::Bmp)
$small.Save($smallPath, [System.Drawing.Imaging.ImageFormat]::Bmp)
$large.Dispose()
$small.Dispose()

Write-Host "Generated installer wizard images in $OutputDir"
