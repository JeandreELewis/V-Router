using System.Text;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;
using SkiaSharp;

namespace CncCad.Infrastructure.Fonts;

public sealed class DesktopFontCatalog:IFontCatalog
{
    private readonly List<FontCatalogEntry> _fonts;
    public DesktopFontCatalog()
    {
        _fonts=SKFontManager.Default.FontFamilies.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).Select(x=>new FontCatalogEntry(new(x,"Regular",FontKind.Outline,"System",false),"System font license")).ToList();
        foreach(var name in new[]{"Hershey Simplex","Hershey Duplex","Hershey Triplex","Hershey Script","Hershey Gothic","Hershey Greek","Hershey Cyrillic"})_fonts.Insert(0,new(new(name,"Regular",FontKind.SingleLine,"Bundled Hershey",true,name.Contains("Greek"),name.Contains("Greek"),name.Contains("Cyrillic")),"Hershey"));
    }
    public IReadOnlyList<FontCatalogEntry> Fonts=>_fonts;
    public FontCatalogEntry? Find(string family,string style)=>_fonts.FirstOrDefault(x=>x.Descriptor.Family.Equals(family,StringComparison.OrdinalIgnoreCase));
}

public sealed class SkiaTextOutlineService:ITextOutlineService
{
    public TextOutlineResult Create(TextOutlineRequest request)
    {
        if(string.IsNullOrWhiteSpace(request.Text)||request.SizeMm<=0)throw new ArgumentException("Text and a positive size are required.");
        if(request.FontFamily.StartsWith("Hershey",StringComparison.OrdinalIgnoreCase))return HersheyStrokeService.Create(request);
        var requestedStyle=ResolveStyle(request.FontStyle);using var typeface=SKTypeface.FromFamilyName(request.FontFamily,requestedStyle)??throw new InvalidOperationException($"Font '{request.FontFamily}' is not installed.");using var font=new SKFont(typeface,(float)request.SizeMm);var figures=new List<VectorPathFigure>();var advances=new List<double>();var generated=new List<GeneratedGlyph>();var warnings=new List<string>();var order=0;var lines=request.Text.Replace("\r\n","\n").Replace('\r','\n').Split('\n');
        for(var lineIndex=0;lineIndex<lines.Length;lineIndex++)
        {
            var measured=lines[lineIndex].EnumerateRunes().Select(rune=>{var glyphs=font.GetGlyphs(rune.ToString());var glyph=glyphs.Length==0?(ushort)0:glyphs[0];var width=glyph==0?0:font.GetGlyphWidths([glyph])[0];return(Rune:rune,Glyph:glyph,Width:width,Advance:(double)width+request.LetterSpacingMm);}).ToArray();var lineWidth=measured.Length==0?0:measured.Sum(item=>item.Advance)-request.LetterSpacingMm;var x=(float)(request.Origin.X-(request.Alignment switch{TextAlignment.Center=>lineWidth/2,TextAlignment.Right=>lineWidth,_=>0}));var y=(float)(request.Origin.Y-lineIndex*request.SizeMm*request.LineSpacing);
            foreach(var item in measured)
            {
                using var path=item.Glyph==0?null:font.GetGlyphPath(item.Glyph);if(path is null&&Rune.IsWhiteSpace(item.Rune)){advances.Add(item.Advance);generated.Add(new(item.Rune.ToString(),item.Advance,[],order++));x+=(float)item.Advance;continue;}if(item.Glyph==0||path is null){warnings.Add($"Font '{request.FontFamily}' has no outline for '{item.Rune}'.");x+=(float)item.Advance;continue;}advances.Add(item.Advance);var glyphFigures=Convert(path,x,y).ToList();figures.AddRange(glyphFigures);generated.Add(new(item.Rune.ToString(),item.Advance,glyphFigures,order++));x+=(float)item.Advance;
            }
        }
        if(figures.Count==0)throw new InvalidOperationException($"Font '{request.FontFamily}' produced no vector outlines for this text.");return new(figures,warnings.Distinct().ToArray(),advances,FontKind.Outline,generated);
    }
    private static SKFontStyle ResolveStyle(string style){var bold=style.Contains("bold",StringComparison.OrdinalIgnoreCase);var italic=style.Contains("italic",StringComparison.OrdinalIgnoreCase)||style.Contains("oblique",StringComparison.OrdinalIgnoreCase);return bold&&italic?SKFontStyle.BoldItalic:bold?SKFontStyle.Bold:italic?SKFontStyle.Italic:SKFontStyle.Normal;}
    private static IEnumerable<VectorPathFigure> Convert(SKPath path,float x,float y)
    {
        var output=new List<VectorPathFigure>();VectorPathFigure? figure=null;var points=new SKPoint[4];using var iterator=path.CreateRawIterator();while(true){var verb=iterator.Next(points);if(verb==SKPathVerb.Done)break;Point2D P(SKPoint p)=>new(x+p.X,y-p.Y);switch(verb){case SKPathVerb.Move:if(figure is not null)output.Add(figure);figure=new(){Start=P(points[0])};break;case SKPathVerb.Line:figure?.Segments.Add(new LinePathSegment(P(points[1])));break;case SKPathVerb.Quad:figure?.Segments.Add(new QuadraticPathSegment(P(points[1]),P(points[2])));break;case SKPathVerb.Cubic:figure?.Segments.Add(new CubicPathSegment(P(points[1]),P(points[2]),P(points[3])));break;case SKPathVerb.Close:if(figure is not null)figure.IsClosed=true;break;}}if(figure is not null)output.Add(figure);return output;
    }
}

internal static class HersheyStrokeService
{
    public static TextOutlineResult Create(TextOutlineRequest request)
    {
        var figures=new List<VectorPathFigure>();var advances=new List<double>();var generated=new List<GeneratedGlyph>();var warnings=new List<string>();var size=request.SizeMm;var advance=size*.72+request.LetterSpacingMm;var order=0;var lines=request.Text.ToUpperInvariant().Replace("\r\n","\n").Replace('\r','\n').Split('\n');for(var lineIndex=0;lineIndex<lines.Length;lineIndex++){var lineWidth=Math.Max(0,lines[lineIndex].Length*advance-request.LetterSpacingMm);var x=request.Origin.X-(request.Alignment switch{TextAlignment.Center=>lineWidth/2,TextAlignment.Right=>lineWidth,_=>0});var y=request.Origin.Y-lineIndex*size*request.LineSpacing;foreach(var c in lines[lineIndex]){var strokes=Glyph(c);if(strokes is null){warnings.Add($"Hershey font has no glyph for '{c}'; a visible placeholder was inserted.");strokes=[[new(0,0),new(0,1),new(.6,1),new(.6,0),new(0,0)]];}var glyphFigures=new List<VectorPathFigure>();foreach(var stroke in strokes){if(stroke.Length<2)continue;glyphFigures.Add(new(){Start=new(x+stroke[0].X*size,y+stroke[0].Y*size),Segments=stroke.Skip(1).Select(p=>(VectorPathSegment)new LinePathSegment(new(x+p.X*size,y+p.Y*size))).ToList()});}figures.AddRange(glyphFigures);advances.Add(advance);generated.Add(new(c.ToString(),advance,glyphFigures,order++));x+=advance;}}return new(figures,warnings.Distinct().ToArray(),advances,FontKind.SingleLine,generated);
    }
    private static Point2D[][]? Glyph(char c)=>c switch
    {
        'A'=>[[new(0,0),new(.3,1),new(.6,0)],[new(.12,.42),new(.48,.42)]],
        'B'=>[[new(0,0),new(0,1),new(.42,1),new(.6,.86),new(.6,.62),new(.42,.5),new(0,.5)],[new(.42,.5),new(.6,.38),new(.6,.14),new(.42,0),new(0,0)]],
        'C'=>[[new(.6,.85),new(.45,1),new(.15,1),new(0,.82),new(0,.18),new(.15,0),new(.45,0),new(.6,.15)]],
        'D'=>[[new(0,0),new(0,1),new(.38,1),new(.6,.78),new(.6,.22),new(.38,0),new(0,0)]],
        'E'=>[[new(.6,1),new(0,1),new(0,0),new(.6,0)],[new(0,.5),new(.48,.5)]],
        'F'=>[[new(0,0),new(0,1),new(.6,1)],[new(0,.5),new(.48,.5)]],
        'G'=>[[new(.6,.82),new(.45,1),new(.15,1),new(0,.82),new(0,.18),new(.15,0),new(.48,0),new(.6,.15),new(.6,.45),new(.35,.45)]],
        'H'=>[[new(0,0),new(0,1)],[new(.6,0),new(.6,1)],[new(0,.5),new(.6,.5)]],
        'I'=>[[new(.3,0),new(.3,1)],[new(.05,1),new(.55,1)],[new(.05,0),new(.55,0)]],
        'J'=>[[new(.05,1),new(.6,1),new(.6,.18),new(.45,0),new(.15,0),new(0,.18)]],
        'K'=>[[new(0,0),new(0,1)],[new(.6,1),new(0,.45),new(.65,0)]],
        'L'=>[[new(0,1),new(0,0),new(.6,0)]],
        'M'=>[[new(0,0),new(0,1),new(.3,.45),new(.6,1),new(.6,0)]],
        'N'=>[[new(0,0),new(0,1),new(.6,0),new(.6,1)]],
        'O'=>[[new(.1,0),new(0,.2),new(0,.8),new(.1,1),new(.5,1),new(.6,.8),new(.6,.2),new(.5,0),new(.1,0)]],
        'P'=>[[new(0,0),new(0,1),new(.45,1),new(.6,.85),new(.6,.65),new(.45,.5),new(0,.5)]],
        'Q'=>[[new(.1,0),new(0,.2),new(0,.8),new(.1,1),new(.5,1),new(.6,.8),new(.6,.2),new(.5,0),new(.1,0)],[new(.38,.25),new(.68,-.08)]],
        'R'=>[[new(0,0),new(0,1),new(.45,1),new(.6,.85),new(.6,.62),new(.45,.5),new(0,.5)],[new(.35,.5),new(.65,0)]],
        'S'=>[[new(.6,.85),new(.45,1),new(.15,1),new(0,.82),new(.6,.18),new(.45,0),new(.15,0),new(0,.15)]],
        'T'=>[[new(0,1),new(.6,1)],[new(.3,1),new(.3,0)]],
        'U'=>[[new(0,1),new(0,.2),new(.15,0),new(.45,0),new(.6,.2),new(.6,1)]],
        'V'=>[[new(0,1),new(.3,0),new(.6,1)]],
        'W'=>[[new(0,1),new(.12,0),new(.3,.55),new(.48,0),new(.6,1)]],
        'X'=>[[new(0,1),new(.6,0)],[new(.6,1),new(0,0)]],
        'Y'=>[[new(0,1),new(.3,.5),new(.6,1)],[new(.3,.5),new(.3,0)]],
        'Z'=>[[new(0,1),new(.6,1),new(0,0),new(.6,0)]],
        '0'=>[[new(.1,0),new(0,.2),new(0,.8),new(.1,1),new(.5,1),new(.6,.8),new(.6,.2),new(.5,0),new(.1,0)],[new(.1,.15),new(.5,.85)]],
        '1'=>[[new(.12,.78),new(.3,1),new(.3,0)],[new(.08,0),new(.55,0)]],
        '2'=>[[new(0,.78),new(.15,1),new(.45,1),new(.6,.8),new(.6,.62),new(0,0),new(.6,0)]],
        '3'=>[[new(0,.85),new(.15,1),new(.45,1),new(.6,.82),new(.45,.52),new(.6,.2),new(.45,0),new(.15,0),new(0,.15)]],
        '4'=>[[new(.48,0),new(.48,1),new(0,.32),new(.62,.32)]],
        '5'=>[[new(.6,1),new(0,1),new(0,.52),new(.45,.52),new(.6,.35),new(.6,.15),new(.45,0),new(.15,0),new(0,.15)]],
        '6'=>[[new(.58,.82),new(.45,1),new(.15,1),new(0,.78),new(0,.2),new(.15,0),new(.45,0),new(.6,.2),new(.6,.4),new(.45,.55),new(0,.55)]],
        '7'=>[[new(0,1),new(.6,1),new(.12,0)]],
        '8'=>[[new(.15,.5),new(0,.68),new(0,.84),new(.15,1),new(.45,1),new(.6,.84),new(.6,.68),new(.45,.5),new(.15,.5),new(0,.32),new(0,.16),new(.15,0),new(.45,0),new(.6,.16),new(.6,.32),new(.45,.5)]],
        '9'=>[[new(.6,.45),new(.15,.45),new(0,.6),new(0,.8),new(.15,1),new(.45,1),new(.6,.8),new(.6,.2),new(.45,0),new(.15,0),new(.02,.16)]],
        '.'=>[[new(.28,0),new(.32,0)]],
        '-'=>[[new(.08,.5),new(.52,.5)]],
        '/'=>[[new(0,0),new(.6,1)]],
        ' '=>[],
        _=>null
    };
}
