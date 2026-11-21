# Genera l'icona di DockSpace: src/DockSpace/Assets/app.ico (più dimensioni) e una PNG grande di anteprima.
# Disegno: quadrato verde acqua con una piccola mappa a riquadri bianca; il riquadro chiaro è lo spazio che si libera.
# Stesso disegno di site/public/apps/dockspace/logo.svg e di src/DockSpace/Views/LogoMark.cs.
# Da rieseguire solo se cambia il logo.
param(
    [string]$Out = "$PSScriptRoot\..\src\DockSpace\Assets\app.ico",
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
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x0F, 0x9D, 0x8A))), $bg)

    # Nelle dimensioni piccole i riquadri si allineano ai pixel, così restano netti.
    function Snap([double]$v) { if ($size -le 32) { [math]::Round($v) } else { $v } }
    $white = [System.Drawing.Brushes]::White
    $soft = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(140, 255, 255, 255))
    foreach ($b in @(@(7, 7, 10, 18, $white), @(19, 7, 6, 10, $white), @(19, 19, 6, 6, $soft))) {
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $x = Snap ($b[0] * $s); $y = Snap ($b[1] * $s)
        $w = (Snap (($b[0] + $b[2]) * $s)) - $x; $h = (Snap (($b[1] + $b[3]) * $s)) - $y
        Add-RoundRect $p $x $y $w $h ([math]::Max(0.5, 1.6 * $s))
        $g.FillPath($b[4], $p)
    }
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
