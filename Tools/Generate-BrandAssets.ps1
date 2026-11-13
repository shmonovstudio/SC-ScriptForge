param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\Assets\Brand'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase, PresentationFramework
$brandOutput = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($brandOutput)
$brandCulture = [Globalization.CultureInfo]::InvariantCulture
$utf8 = [Text.UTF8Encoding]::new($false)

# The supplied SVG is the source of truth for the drawing, ICO and wordmark.
$markPath = Join-Path $brandOutput 'sc-scriptforge-mark.svg'
$xmlSettings = [Xml.XmlReaderSettings]::new()
$xmlSettings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
$xmlSettings.XmlResolver = $null
$xmlReader = [Xml.XmlReader]::Create($markPath, $xmlSettings)
$svg = [Xml.XmlDocument]::new()
try { $svg.Load($xmlReader) } finally { $xmlReader.Dispose() }
$viewBox = @($svg.DocumentElement.GetAttribute('viewBox').Split(' ', [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { [double]::Parse($_, $brandCulture) })
if ($viewBox.Count -ne 4 -or $viewBox[2] -le 0 -or $viewBox[3] -le 0) { throw 'The logo requires a valid viewBox.' }
$svgPaths = @($svg.GetElementsByTagName('path'))
if ($svgPaths.Count -eq 0) { throw 'The logo has no vector paths.' }

function Parse-Number([string]$value) {
    if ($value.EndsWith('%')) { return [double]::Parse($value.TrimEnd('%'), $brandCulture) / 100 }
    return [double]::Parse($value, $brandCulture)
}
# Fills are flat colors or url(#id) references to the SVG's linear/radial gradients.
function Resolve-Fill([string]$fill) {
    if ($fill -notmatch '^url\(#(.+)\)$') { return [Windows.Media.BrushConverter]::new().ConvertFromString($fill) }
    $id = $Matches[1]
    $gradient = @($svg.GetElementsByTagName('*') | Where-Object { $_.GetAttribute('id') -eq $id })[0]
    if ($null -eq $gradient) { throw "Missing gradient #$id." }
    $stops = [Windows.Media.GradientStopCollection]::new()
    foreach ($stop in $gradient.GetElementsByTagName('stop')) {
        $stops.Add([Windows.Media.GradientStop]::new([Windows.Media.ColorConverter]::ConvertFromString($stop.GetAttribute('stop-color')), (Parse-Number $stop.GetAttribute('offset'))))
    }
    if ($gradient.LocalName -eq 'linearGradient') {
        $brush = [Windows.Media.LinearGradientBrush]::new($stops)
        $brush.StartPoint = [Windows.Point]::new((Parse-Number $gradient.GetAttribute('x1')), (Parse-Number $gradient.GetAttribute('y1')))
        $brush.EndPoint = [Windows.Point]::new((Parse-Number $gradient.GetAttribute('x2')), (Parse-Number $gradient.GetAttribute('y2')))
        if ($gradient.GetAttribute('gradientUnits') -eq 'userSpaceOnUse') { $brush.MappingMode = [Windows.Media.BrushMappingMode]::Absolute }
        return $brush
    }
    $brush = [Windows.Media.RadialGradientBrush]::new($stops)
    $center = [Windows.Point]::new((Parse-Number $gradient.GetAttribute('cx')), (Parse-Number $gradient.GetAttribute('cy')))
    $brush.Center = $center; $brush.GradientOrigin = $center
    $brush.RadiusX = $brush.RadiusY = Parse-Number $gradient.GetAttribute('r')
    return $brush
}

# The SVG's full-bleed <rect> is the navy plate; the paths are the SC monogram drawn on it.
$plateRect = @($svg.GetElementsByTagName('rect'))[0]
$plateBrush = if ($plateRect) { Resolve-Fill $plateRect.GetAttribute('fill') } else { [Windows.Media.BrushConverter]::new().ConvertFromString('#09121F') }
$plateBrush.Freeze()
$drawingGroup = [Windows.Media.DrawingGroup]::new()
$drawingGroup.Transform = [Windows.Media.TranslateTransform]::new(-$viewBox[0], -$viewBox[1])
foreach ($path in $svgPaths) {
    $geometry = [Windows.Media.Geometry]::Parse($path.GetAttribute('d'))
    $drawingGroup.Children.Add([Windows.Media.GeometryDrawing]::new((Resolve-Fill $path.GetAttribute('fill')), $null, $geometry))
}
$drawingGroup.Freeze()

function Draw-Logo($drawing, [Windows.Rect]$rect) {
    $scale = [Math]::Min($rect.Width / $viewBox[2], $rect.Height / $viewBox[3])
    $x = $rect.X + ($rect.Width - $viewBox[2] * $scale) / 2
    $y = $rect.Y + ($rect.Height - $viewBox[3] * $scale) / 2
    $drawing.PushTransform([Windows.Media.TranslateTransform]::new($x, $y))
    $drawing.PushTransform([Windows.Media.ScaleTransform]::new($scale, $scale))
    $drawing.DrawDrawing($drawingGroup)
    $drawing.Pop(); $drawing.Pop()
}
# The rounded plate matches the in-app BrandMark tile; the SVG's own padding frames the monogram.
function Draw-Tile($drawing, [Windows.Rect]$rect, [double]$radius) {
    $drawing.DrawRoundedRectangle($plateBrush, $null, $rect, $radius, $radius)
    Draw-Logo $drawing $rect
}
function Encode-Png($visual, [int]$width, [int]$height) {
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($width, $height, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    try { $encoder.Save($stream); return ,$stream.ToArray() } finally { $stream.Dispose() }
}
function Render-Mark([int]$size) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    $drawing.PushTransform([Windows.Media.ScaleTransform]::new($size / 64.0, $size / 64.0))
    Draw-Tile $drawing ([Windows.Rect]::new(1, 1, 62, 62)) 14
    $drawing.Pop(); $drawing.Close()
    return ,(Encode-Png $visual $size $size)
}

$resourceGroup = [Windows.Media.DrawingGroup]::new()
$resourceGroup.Children.Add([Windows.Media.GeometryDrawing]::new($plateBrush, $null, [Windows.Media.RectangleGeometry]::new([Windows.Rect]::new($viewBox[0], $viewBox[1], $viewBox[2], $viewBox[3]))))
$resourceGroup.Children.Add($drawingGroup)
$resources = [Windows.ResourceDictionary]::new()
$resources.Add('BrandMark', [Windows.Media.DrawingImage]::new($resourceGroup))
$xamlSettings = [Xml.XmlWriterSettings]::new(); $xamlSettings.Indent = $true; $xamlSettings.OmitXmlDeclaration = $true; $xamlSettings.Encoding = $utf8
$xamlWriter = [Xml.XmlWriter]::Create((Join-Path $brandOutput 'BrandResources.xaml'), $xamlSettings)
try { [Windows.Markup.XamlWriter]::Save($resources, $xamlWriter) } finally { $xamlWriter.Dispose() }

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = @($sizes | ForEach-Object { ,(Render-Mark $_) })
$iconStream = [IO.File]::Create((Join-Path $brandOutput 'sc-scriptforge.ico'))
$iconWriter = [IO.BinaryWriter]::new($iconStream)
try {
    $iconWriter.Write([UInt16]0); $iconWriter.Write([UInt16]1); $iconWriter.Write([UInt16]$sizes.Count)
    $iconOffset = 6 + $sizes.Count * 16
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $iconWriter.Write([byte]$dimension); $iconWriter.Write([byte]$dimension)
        $iconWriter.Write([byte]0); $iconWriter.Write([byte]0)
        $iconWriter.Write([UInt16]1); $iconWriter.Write([UInt16]32)
        $iconWriter.Write([UInt32]$images[$i].Length); $iconWriter.Write([UInt32]$iconOffset)
        $iconOffset += $images[$i].Length
    }
    foreach ($brandImage in $images) { $iconWriter.Write([byte[]]$brandImage) }
} finally { $iconWriter.Dispose(); $iconStream.Dispose() }
[IO.File]::WriteAllBytes((Join-Path $brandOutput 'sc-scriptforge-app-icon-preview.png'), (Render-Mark 512))
$rawVisual = [Windows.Media.DrawingVisual]::new()
$rawDrawing = $rawVisual.RenderOpen()
$rawDrawing.DrawRectangle($plateBrush, $null, [Windows.Rect]::new(0, 0, 512, 512))
Draw-Logo $rawDrawing ([Windows.Rect]::new(0, 0, 512, 512))
$rawDrawing.Close()
[IO.File]::WriteAllBytes((Join-Path $brandOutput 'sc-scriptforge-mark-preview.png'), (Encode-Png $rawVisual 512 512))

$brandFontRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Assets\Fonts')).Replace('\', '/') + '/'
$brandFontUri = [Uri]::new($brandFontRoot)
$brandHeadingFont = [Windows.Media.FontFamily]::new($brandFontUri, './Manrope/#Manrope')
$brandBodyFont = [Windows.Media.FontFamily]::new($brandFontUri, './Onest/#Onest')
$typeface = [Windows.Media.Typeface]::new($brandHeadingFont, [Windows.FontStyles]::Normal, [Windows.FontWeights]::Bold, [Windows.FontStretches]::Normal)
$brandGlyph = $null
if (-not $typeface.TryGetGlyphTypeface([ref]$brandGlyph) -or $brandGlyph.FontUri.LocalPath -notlike '*manrope-bold.ttf') { throw 'The SVG wordmark requires the bundled Manrope Bold file.' }
$title = [Windows.Media.FormattedText]::new('SC ScriptForge', $brandCulture, [Windows.FlowDirection]::LeftToRight, $typeface, 31, [Windows.Media.Brushes]::Black, 1)
$titleGeometry = $title.BuildGeometry([Windows.Point]::new(82, 9)).GetOutlinedPathGeometry()
$titlePath = $titleGeometry.ToString($brandCulture) -replace '^F[01]\s*', ''
$subtitle = [Windows.Media.FormattedText]::new('POWERSHELL WORKSPACE', $brandCulture, [Windows.FlowDirection]::LeftToRight, [Windows.Media.Typeface]::new($brandBodyFont, [Windows.FontStyles]::Normal, [Windows.FontWeights]::Normal, [Windows.FontStretches]::Normal), 10.5, [Windows.Media.Brushes]::Black, 1)
$subtitleGeometry = $subtitle.BuildGeometry([Windows.Point]::new(84, 48)).GetOutlinedPathGeometry()
$subtitlePath = $subtitleGeometry.ToString($brandCulture) -replace '^F[01]\s*', ''
$lockupWidth = [Math]::Ceiling([Math]::Max($titleGeometry.Bounds.Right, $subtitleGeometry.Bounds.Right)) + 2
$markScale = 64 / [Math]::Max($viewBox[2], $viewBox[3])
$markDefs = @($svg.GetElementsByTagName('defs') | ForEach-Object { $_.OuterXml }) -join ''
$markBody = @($svgPaths | ForEach-Object { $_.OuterXml }) -join ''
$plateXml = '<rect x="' + $viewBox[0].ToString($brandCulture) + '" y="' + $viewBox[1].ToString($brandCulture) + '" width="' + $viewBox[2].ToString($brandCulture) + '" height="' + $viewBox[3].ToString($brandCulture) + '" rx="' + (14 / 64 * $viewBox[2]).ToString($brandCulture) + '" fill="' + $(if ($plateRect) { $plateRect.GetAttribute('fill') } else { '#09121F' }) + '"/>'
$markTransform = 'scale(' + $markScale.ToString($brandCulture) + ') translate(' + (-$viewBox[0]).ToString($brandCulture) + ' ' + (-$viewBox[1]).ToString($brandCulture) + ')'
$lockup = '<svg xmlns="http://www.w3.org/2000/svg" width="' + $lockupWidth + '" height="64" viewBox="0 0 ' + $lockupWidth + ' 64" fill="none" role="img" aria-label="SC ScriptForge"><title>SC ScriptForge</title>' + ($markDefs -replace ' xmlns="http://www.w3.org/2000/svg"', '') + '<g transform="' + $markTransform + '">' + $plateXml + ($markBody -replace ' xmlns="http://www.w3.org/2000/svg"', '') + '</g><path fill="#081D40" fill-rule="nonzero" d="' + $titlePath + '"/><path fill="#617B9C" fill-rule="nonzero" d="' + $subtitlePath + '"/></svg>'
[IO.File]::WriteAllText((Join-Path $brandOutput 'sc-scriptforge-logo.svg'), $lockup, $utf8)
$lockupVisual = [Windows.Media.DrawingVisual]::new()
$lockupDrawing = $lockupVisual.RenderOpen()
$lockupDrawing.PushTransform([Windows.Media.ScaleTransform]::new(2, 2))
Draw-Tile $lockupDrawing ([Windows.Rect]::new(0, 0, 64, 64)) 14
$lockupDrawing.DrawGeometry([Windows.Media.BrushConverter]::new().ConvertFromString('#081D40'), $null, $titleGeometry)
$lockupDrawing.DrawGeometry([Windows.Media.BrushConverter]::new().ConvertFromString('#617B9C'), $null, $subtitleGeometry)
$lockupDrawing.Pop(); $lockupDrawing.Close()
[IO.File]::WriteAllBytes((Join-Path $brandOutput 'sc-scriptforge-logo-preview.png'), (Encode-Png $lockupVisual ($lockupWidth * 2) 128))
Write-Output ('Brand assets saved to ' + $brandOutput)
