# Genera l'icona di DockPost: src/DockPost/Assets/app.ico (più dimensioni) e, a richiesta, una PNG di anteprima.
# Disegno: quadrato arancio #F07A1A con una busta bianca.
# Stesso disegno di site/public/apps/dockpost/logo.svg e di src/DockPost/Views/LogoMark.cs.
# Da rieseguire solo se cambia il logo.
param(
    [string]$Out = "$PSScriptRoot\..\src\DockPost\Assets\app.ico",
    [string]$Preview = ""
)

Add-Type -AssemblyName System.Drawing

function Add-RoundRect($path, [single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $d = 2 * $r
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
}

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 32.0

    $bg = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-RoundRect $bg (1 * $s) (1 * $s) (30 * $s) (30 * $s) (7 * $s)
    $accent = [System.Drawing.Color]::FromArgb(255, 0xF0, 0x7A, 0x1A)
    $g.FillPath((New-Object System.Drawing.SolidBrush $accent), $bg)

    # Busta bianca con il lembo disegnato in arancio.
    $env = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-RoundRect $env (6 * $s) (9 * $s) (20 * $s) (14 * $s) ([math]::Max(0.6, 2 * $s))
    $g.FillPath([System.Drawing.Brushes]::White, $env)
    $pen = New-Object System.Drawing.Pen $accent, ([single][math]::Max(1.2, 1.8 * $s))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ([single](7.5 * $s)), ([single](10.5 * $s))),
        (New-Object System.Drawing.PointF ([single](16 * $s)), ([single](17 * $s))),
        (New-Object System.Drawing.PointF ([single](24.5 * $s)), ([single](10.5 * $s)))))
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

# Formato ICO: intestazione + una voce per immagine + i dati PNG.
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) { $images.Add([byte[]](New-IconPng $size)) }

$ico = New-Object System.IO.MemoryStream
$w = [System.IO.BinaryWriter]::new($ico)
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$images[$i].Length); $w.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
$target = [System.IO.Path]::GetFullPath($Out)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($target)) | Out-Null
[System.IO.File]::WriteAllBytes($target, $ico.ToArray())
Write-Host "Creato $target ($($ico.Length) byte, $($sizes.Count) dimensioni)"

if ($Preview) {
    # Anteprima: tutte le dimensioni affiancate su fondo chiaro e scuro.
    $sheet = New-Object System.Drawing.Bitmap 760, 300
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.Clear([System.Drawing.Color]::FromArgb(255, 246, 247, 249))
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 27, 28, 32))), 0, 150, 760, 150)
    foreach ($row in 0, 1) {
        $x = 12
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $img = [System.Drawing.Image]::FromStream((New-Object System.IO.MemoryStream (, $images[$i])))
            $dim = [math]::Min($sizes[$i], 128)
            $g.DrawImage($img, $x, 75 - $dim / 2 + 150 * $row, $dim, $dim)
            $x += $dim + 14
        }
    }
    $g.Dispose()
    $sheet.Save([System.IO.Path]::GetFullPath($Preview), [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "Anteprima $Preview"
}
