# Deterministic product-icon generator (night-design-2026-09-25 unit B1).
# Draws the pinned monitor/prompt pixel geometry with System.Drawing only and
# assembles two theme ICOs (light + dark), each carrying 16/20/24/32/48/256
# PNG images. Byte-deterministic: fixed 96-dpi resolution, no timestamps, no
# randomness. Run once; build.ps1 consumes the tracked assets and never
# regenerates them.
param(
    [string]$OutputDirectory =
        (Join-Path $PSScriptRoot '..\src\TerminalOrganizer.App\Assets')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Palette (design B1 §Icon concept). Hex without '#'.
$themes = @{
    'Light' = @{ Frame = '172033'; Neutral = 'D7E0EA'; Accent = '2563EB'; Prompt = 'FFFFFF' }
    'Dark'  = @{ Frame = 'F3F7FC'; Neutral = '334155'; Accent = '60A5FA'; Prompt = '07111F' }
}

# Pinned geometry per size (design B1 coordinate table). All bounds are
# inclusive integer pixels. Zones: ZXn = X range of zone n (rightmost is the
# accent zone); zones share ZoneTop..ZoneBottom. Stand/base in frame color.
$sizes = @(
    @{ Size = 16;  FrameL = 1;  FrameT = 1;  FrameR = 14;  FrameB = 12;  Stroke = 1;
       ZoneTop = 3;  ZoneBottom = 9;   ZX1 = @(3, 5);     ZX2 = @(7, 9);     ZX3 = @(11, 12);
       Stem = @(7, 8, 13, 13);       Base = @(5, 10, 14, 14);      PromptGlyph = '>' }
    @{ Size = 20;  FrameL = 1;  FrameT = 2;  FrameR = 18;  FrameB = 15;  Stroke = 1;
       ZoneTop = 4;  ZoneBottom = 12;  ZX1 = @(3, 6);     ZX2 = @(8, 11);    ZX3 = @(13, 16);
       Stem = @(9, 10, 16, 16);      Base = @(6, 13, 17, 17);     PromptGlyph = '>' }
    @{ Size = 24;  FrameL = 1;  FrameT = 2;  FrameR = 22;  FrameB = 18;  Stroke = 2;
       ZoneTop = 5;  ZoneBottom = 15;  ZX1 = @(4, 8);     ZX2 = @(10, 14);   ZX3 = @(16, 19);
       Stem = @(11, 12, 19, 20);    Base = @(7, 16, 21, 21);     PromptGlyph = '>_' }
    @{ Size = 32;  FrameL = 2;  FrameT = 3;  FrameR = 29;  FrameB = 23;  Stroke = 2;
       ZoneTop = 7;  ZoneBottom = 20;  ZX1 = @(5, 11);    ZX2 = @(13, 19);   ZX3 = @(21, 26);
       Stem = @(15, 16, 24, 27);    Base = @(10, 21, 28, 28);    PromptGlyph = '>_' }
    @{ Size = 48;  FrameL = 3;  FrameT = 4;  FrameR = 44;  FrameB = 35;  Stroke = 3;
       ZoneTop = 10; ZoneBottom = 30;  ZX1 = @(8, 17);    ZX2 = @(20, 29);   ZX3 = @(32, 39);
       Stem = @(23, 25, 36, 41);    Base = @(15, 33, 42, 44);    PromptGlyph = '>_' }
    @{ Size = 256; FrameL = 16; FrameT = 20; FrameR = 239; FrameB = 188; Stroke = 14;
       ZoneTop = 52; ZoneBottom = 160; ZX1 = @(42, 91);   ZX2 = @(104, 153); ZX3 = @(166, 215);
       Stem = @(116, 139, 189, 219); Base = @(82, 173, 220, 235); PromptGlyph = '>_' }
)

function ConvertTo-B1Color {
    param([string]$Hex)
    [System.Drawing.ColorTranslator]::FromHtml('#' + $Hex)
}

# Rounded-rectangle path; radius 0 degenerates to a plain rectangle.
function New-B1RoundedRectPath {
    param([float]$X, [float]$Y, [float]$Width, [float]$Height, [float]$Radius)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($Radius -le 0) {
        $path.AddRectangle((New-Object System.Drawing.RectangleF($X, $Y, $Width, $Height)))
        return $path
    }
    $d = 2 * $Radius
    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc($X + $Width - $d, $Y, $d, $d, 270, 90)
    $path.AddArc($X + $Width - $d, $Y + $Height - $d, $d, $d, 0, 90)
    $path.AddArc($X, $Y + $Height - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $path
}

function New-B1SizeBitmap {
    param([hashtable]$Spec, [hashtable]$Palette)
    $n = $Spec.Size
    $u = $Spec.Stroke
    $bmp = New-Object System.Drawing.Bitmap($n, $n, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
    $bmp.SetResolution(96.0, 96.0)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
        $g.Clear([System.Drawing.Color]::Transparent)

        $frameBrush = New-Object System.Drawing.SolidBrush ((ConvertTo-B1Color $Palette.Frame))

        # Frame (monitor bezel): outer region in frame color, inner region reset
        # to true transparency via SourceCopy. At 32+ the outer corners carry a
        # one-stroke-unit radius under AntiAlias; at 16/20/24 corners stay sharp.
        $outerW = $Spec.FrameR - $Spec.FrameL + 1
        $outerH = $Spec.FrameB - $Spec.FrameT + 1
        $radius = 0.0
        $smoothing = [System.Drawing.Drawing2D.SmoothingMode]::None
        if ($n -ge 32) {
            $radius = [float]$u
            $smoothing = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        }
        $g.SmoothingMode = $smoothing
        $outerPath = New-B1RoundedRectPath $Spec.FrameL $Spec.FrameT $outerW $outerH $radius
        try { $g.FillPath($frameBrush, $outerPath) } finally { $outerPath.Dispose() }

        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
        $innerX = $Spec.FrameL + $u
        $innerY = $Spec.FrameT + $u
        $innerW = $Spec.FrameR - $u - $innerX + 1
        $innerH = $Spec.FrameB - $u - $innerY + 1
        $transparentBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::Transparent)
        $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $g.FillRectangle($transparentBrush, $innerX, $innerY, $innerW, $innerH)
        $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
        $transparentBrush.Dispose()

        # Zones: two neutral panels then the rightmost accent panel (pixel-aligned).
        $neutralBrush = New-Object System.Drawing.SolidBrush ((ConvertTo-B1Color $Palette.Neutral))
        $accentBrush = New-Object System.Drawing.SolidBrush ((ConvertTo-B1Color $Palette.Accent))
        $zoneH = $Spec.ZoneBottom - $Spec.ZoneTop + 1
        foreach ($z in @($Spec.ZX1, $Spec.ZX2)) {
            $g.FillRectangle($neutralBrush, $z[0], $Spec.ZoneTop, ($z[1] - $z[0] + 1), $zoneH)
        }
        $g.FillRectangle($accentBrush, $Spec.ZX3[0], $Spec.ZoneTop, ($Spec.ZX3[1] - $Spec.ZX3[0] + 1), $zoneH)

        # Prompt mark, pixel-aligned at every size: a stroke-unit-thick chevron
        # anchored at the accent-zone left edge, plus the baseline underscore
        # (two stroke units long, one stroke unit thick) for the larger sizes.
        # Geometry per design: vertices (zoneLeft+u, midY-2u), (zoneLeft+3u,
        # midY), (zoneLeft+u, midY+2u); underscore starts one stroke unit right
        # of the chevron's bottom endpoint.
        $zoneLeft = $Spec.ZX3[0]
        $midY = [int](($Spec.ZoneTop + $Spec.ZoneBottom) / 2)
        $pen = New-Object System.Drawing.Pen ((ConvertTo-B1Color $Palette.Prompt), [float]$u)
        try {
            $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Flat
            $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Flat
            $g.DrawLine($pen, ($zoneLeft + $u), ($midY - 2 * $u), ($zoneLeft + 3 * $u), $midY)
            $g.DrawLine($pen, ($zoneLeft + 3 * $u), $midY, ($zoneLeft + $u), ($midY + 2 * $u))
            if ($Spec.PromptGlyph -eq '>_') {
                $promptBrush = New-Object System.Drawing.SolidBrush ((ConvertTo-B1Color $Palette.Prompt))
                $g.FillRectangle($promptBrush, ($zoneLeft + 2 * $u), ($midY + 2 * $u), (2 * $u), $u)
                $promptBrush.Dispose()
            }
        }
        finally { $pen.Dispose() }

        # Stand: stem below the frame, then the wider base row band.
        $g.FillRectangle($frameBrush, $Spec.Stem[0], $Spec.Stem[2], ($Spec.Stem[1] - $Spec.Stem[0] + 1), ($Spec.Stem[3] - $Spec.Stem[2] + 1))
        $g.FillRectangle($frameBrush, $Spec.Base[0], $Spec.Base[2], ($Spec.Base[1] - $Spec.Base[0] + 1), ($Spec.Base[3] - $Spec.Base[2] + 1))

        $neutralBrush.Dispose()
        $accentBrush.Dispose()
        $frameBrush.Dispose()
    }
    finally {
        $g.Dispose()
    }
    $bmp
}

function New-B1IconBytes {
    param([System.Drawing.Bitmap[]]$Bitmaps)
    # ICO assembly (design B1 §Generator): ICONDIR + six ICONDIRENTRY records,
    # PNG payloads appended in ascending-size order. Dimensions are captured
    # before encoding because each bitmap is disposed right after its PNG save.
    $headerSize = 6 + 16 * $Bitmaps.Count
    $offset = $headerSize
    $images = New-Object 'System.Collections.Generic.List[object]'
    foreach ($bmp in $Bitmaps) {
        $dim = $bmp.Width
        $ms = New-Object System.IO.MemoryStream
        try { $bmp.Save($ms, ([System.Drawing.Imaging.ImageFormat]::Png)) } finally { $bmp.Dispose() }
        $images.Add(@{ Dim = $dim; Png = $ms.ToArray() })
        $ms.Dispose()
    }
    $out = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($out)
    try {
        $bw.Write([UInt16]0)                    # reserved
        $bw.Write([UInt16]1)                    # type: icon
        $bw.Write([UInt16]$images.Count)        # image count
        for ($i = 0; $i -lt $images.Count; $i++) {
            $dim = if ($images[$i].Dim -ge 256) { 0 } else { $images[$i].Dim }
            $bw.Write([byte]$dim)               # width (0 = 256)
            $bw.Write([byte]$dim)               # height (0 = 256)
            $bw.Write([byte]0)                  # color count
            $bw.Write([byte]0)                  # reserved
            $bw.Write([UInt16]1)                # color planes
            $bw.Write([UInt16]32)               # bit count
            $bw.Write([UInt32]$images[$i].Png.Length)
            $bw.Write([UInt32]$offset)
            $offset += $images[$i].Png.Length
        }
        foreach ($image in $images) { $bw.Write($image.Png) }
        $bw.Flush()
        $out.ToArray()
    }
    finally {
        $bw.Dispose()
        $out.Dispose()
    }
}

if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
}

$results = @()
foreach ($themeName in @('Light', 'Dark')) {
    $palette = $themes[$themeName]
    $bitmaps = @($sizes | ForEach-Object { New-B1SizeBitmap $_ $palette })
    $bytes = New-B1IconBytes $bitmaps
    $fileName = if ($themeName -eq 'Dark') { 'TerminalOrganizer.Dark.ico' } else { 'TerminalOrganizer.ico' }
    $path = Join-Path $OutputDirectory $fileName
    [IO.File]::WriteAllBytes($path, $bytes)

    # Reopen with System.Drawing.Icon and assert the 256x256 image. Framework
    # limitation (measured on .NET 4.8): the Icon size-matching constructors
    # cannot address a width-byte-0 (256) ICONDIRENTRY — new Icon(stream,
    # Size(256,256)) returns the largest sub-256 image — so the reopen asserts
    # the file parses under System.Drawing.Icon, and the 256x256 assert decodes
    # the declared-256 entry's PNG payload.
    $fs = [IO.File]::OpenRead($path)
    try {
        $reopened = New-Object System.Drawing.Icon($fs)
        try {
            $null = $reopened.Handle   # parsing must yield a live icon handle
            if ($reopened.Width -le 0 -or $reopened.Height -le 0) {
                throw ("generated icon reopened with no size: {0}x{1}" -f $reopened.Width, $reopened.Height)
            }
        }
        finally { $reopened.Dispose() }
    }
    finally { $fs.Dispose() }

    $fileBytes = [IO.File]::ReadAllBytes($path)
    $entryCount = [BitConverter]::ToUInt16($fileBytes, 4)
    $lastBase = 6 + 16 * ($entryCount - 1)
    $payloadSize = [BitConverter]::ToUInt32($fileBytes, $lastBase + 8)
    $payloadOffset = [BitConverter]::ToUInt32($fileBytes, $lastBase + 12)
    $payload = New-Object byte[] $payloadSize
    [Array]::Copy($fileBytes, $payloadOffset, $payload, 0, $payloadSize)
    $payloadStream = New-Object System.IO.MemoryStream (,$payload)
    try {
        $decoded = New-Object System.Drawing.Bitmap($payloadStream)
        try {
            if ($decoded.Width -ne 256 -or $decoded.Height -ne 256) {
                throw ("generated icon's declared-256 entry decodes to {0}x{1}" -f $decoded.Width, $decoded.Height)
            }
        }
        finally { $decoded.Dispose() }
    }
    finally { $payloadStream.Dispose() }

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '') }
    finally { $sha.Dispose() }
    $results += ('{0} {1} bytes sha256={2}' -f $fileName, $bytes.Length, $hash)
}

$results | ForEach-Object { Write-Output $_ }
exit 0
