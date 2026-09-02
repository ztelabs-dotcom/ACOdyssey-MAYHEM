Add-Type -AssemblyName System.Drawing

$src = Join-Path $PSScriptRoot '..\src\ACOdysseyUMM\Assets\IconHelmetSource.png'
$outIco = Join-Path $PSScriptRoot '..\src\ACOdysseyUMM\Assets\Mayhem.ico'
$outPng = Join-Path $PSScriptRoot '..\src\ACOdysseyUMM\Assets\MayhemIconPreview.png'

$img = [System.Drawing.Bitmap]::FromFile($src)
try {
    $sizes = @(256, 128, 64, 48, 32, 16)
    $pngs = New-Object 'System.Collections.Generic.List[byte[]]'

    foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.Clear([System.Drawing.Color]::Transparent)
            $g.DrawImage($img, (New-Object System.Drawing.Rectangle(0, 0, $s, $s)))
        }
        finally {
            $g.Dispose()
        }

        if ($s -eq 256) {
            $bmp.Save($outPng, [System.Drawing.Imaging.ImageFormat]::Png)
        }

        $ms = New-Object System.IO.MemoryStream
        try {
            $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $pngs.Add($ms.ToArray())
        }
        finally {
            $ms.Dispose()
            $bmp.Dispose()
        }
    }

    $fs = [System.IO.File]::Open($outIco, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
    $bw = New-Object System.IO.BinaryWriter($fs)
    try {
        $bw.Write([UInt16]0)
        $bw.Write([UInt16]1)
        $bw.Write([UInt16]$sizes.Count)
        $offset = 6 + (16 * $sizes.Count)

        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $s = $sizes[$i]
            $data = $pngs[$i]
            $wh = if ($s -eq 256) { 0 } else { $s }
            $bw.Write([Byte]$wh)
            $bw.Write([Byte]$wh)
            $bw.Write([Byte]0)
            $bw.Write([Byte]0)
            $bw.Write([UInt16]1)
            $bw.Write([UInt16]32)
            $bw.Write([UInt32]$data.Length)
            $bw.Write([UInt32]$offset)
            $offset += $data.Length
        }

        foreach ($data in $pngs) {
            $bw.Write($data)
        }
    }
    finally {
        $bw.Dispose()
        $fs.Dispose()
    }
}
finally {
    $img.Dispose()
}

Write-Output "ICON=$outIco"
Write-Output "PREVIEW=$outPng"
