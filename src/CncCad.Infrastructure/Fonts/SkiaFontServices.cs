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
        using var typeface=SKTypeface.FromFamilyName(request.FontFamily)??throw new InvalidOperationException($"Font '{request.FontFamily}' is not installed.");using var font=new SKFont(typeface,(float)request.SizeMm);var figures=new List<VectorPathFigure>();var advances=new List<double>();var generated=new List<GeneratedGlyph>();var x=(float)request.Origin.X;var y=(float)request.Origin.Y;var order=0;
        foreach(var rune in request.Text.EnumerateRunes())
        {
            if(rune.Value=='\n'){x=(float)request.Origin.X;y-=(float)(request.SizeMm*request.LineSpacing);continue;}var glyphs=font.GetGlyphs(rune.ToString());if(glyphs.Length==0)continue;var glyph=glyphs[0];using var path=font.GetGlyphPath(glyph);var width=font.GetGlyphWidths([glyph])[0];var advance=width+request.LetterSpacingMm;advances.Add(advance);var glyphFigures=path is null?[]:Convert(path,x,y).ToList();figures.AddRange(glyphFigures);generated.Add(new(rune.ToString(),advance,glyphFigures,order++));x+=width+(float)request.LetterSpacingMm;
        }
        if(figures.Count==0)throw new InvalidOperationException($"Font '{request.FontFamily}' produced no vector outlines for this text.");return new(figures,[],advances,FontKind.Outline,generated);
    }
    private static IEnumerable<VectorPathFigure> Convert(SKPath path,float x,float y)
    {
        var output=new List<VectorPathFigure>();VectorPathFigure? figure=null;var points=new SKPoint[4];using var iterator=path.CreateRawIterator();while(true){var verb=iterator.Next(points);if(verb==SKPathVerb.Done)break;Point2D P(SKPoint p)=>new(x+p.X,y-p.Y);switch(verb){case SKPathVerb.Move:if(figure is not null)output.Add(figure);figure=new(){Start=P(points[0])};break;case SKPathVerb.Line:figure?.Segments.Add(new LinePathSegment(P(points[1])));break;case SKPathVerb.Quad:figure?.Segments.Add(new QuadraticPathSegment(P(points[1]),P(points[2])));break;case SKPathVerb.Cubic:figure?.Segments.Add(new CubicPathSegment(P(points[1]),P(points[2]),P(points[3])));break;case SKPathVerb.Close:if(figure is not null)figure.IsClosed=true;break;}}if(figure is not null)output.Add(figure);return output;
    }
}

internal static class HersheyStrokeService
{
    public static TextOutlineResult Create(TextOutlineRequest request)
    {
        var figures=new List<VectorPathFigure>();var advances=new List<double>();var generated=new List<GeneratedGlyph>();var x=request.Origin.X;var size=request.SizeMm;var order=0;foreach(var c in request.Text.ToUpperInvariant()){if(c=='\n'){x=request.Origin.X;continue;}var strokes=Glyph(c);var glyphFigures=new List<VectorPathFigure>();foreach(var stroke in strokes){if(stroke.Length<2)continue;glyphFigures.Add(new(){Start=new(x+stroke[0].X*size,request.Origin.Y+stroke[0].Y*size),Segments=stroke.Skip(1).Select(p=>(VectorPathSegment)new LinePathSegment(new(x+p.X*size,request.Origin.Y+p.Y*size))).ToList()});}figures.AddRange(glyphFigures);var advance=size*.72+request.LetterSpacingMm;advances.Add(advance);generated.Add(new(c.ToString(),advance,glyphFigures,order++));x+=advance;}return new(figures,[],advances,FontKind.SingleLine,generated);
    }
    private static Point2D[][] Glyph(char c)=>c switch
    {
        'A'=>[[new(0,0),new(.3,1),new(.6,0)],[new(.12,.42),new(.48,.42)]],
        'E'=>[[new(.6,1),new(0,1),new(0,0),new(.6,0)],[new(0,.5),new(.48,.5)]],
        'H'=>[[new(0,0),new(0,1)],[new(.6,0),new(.6,1)],[new(0,.5),new(.6,.5)]],
        'I'=>[[new(.3,0),new(.3,1)],[new(.05,1),new(.55,1)],[new(.05,0),new(.55,0)]],
        'L'=>[[new(0,1),new(0,0),new(.6,0)]],
        'N'=>[[new(0,0),new(0,1),new(.6,0),new(.6,1)]],
        'O'=>[[new(.1,0),new(0,.2),new(0,.8),new(.1,1),new(.5,1),new(.6,.8),new(.6,.2),new(.5,0),new(.1,0)]],
        'R'=>[[new(0,0),new(0,1),new(.45,1),new(.6,.85),new(.6,.62),new(.45,.5),new(0,.5)],[new(.35,.5),new(.65,0)]],
        'S'=>[[new(.6,.85),new(.45,1),new(.15,1),new(0,.82),new(.6,.18),new(.45,0),new(.15,0),new(0,.15)]],
        'T'=>[[new(0,1),new(.6,1)],[new(.3,1),new(.3,0)]],
        'U'=>[[new(0,1),new(0,.2),new(.15,0),new(.45,0),new(.6,.2),new(.6,1)]],
        'V'=>[[new(0,1),new(.3,0),new(.6,1)]],
        'X'=>[[new(0,1),new(.6,0)],[new(.6,1),new(0,0)]],
        'Y'=>[[new(0,1),new(.3,.5),new(.6,1)],[new(.3,.5),new(.3,0)]],
        ' '=>[],
        _=>[[new(0,0),new(0,1),new(.6,1),new(.6,0),new(0,0)]]
    };
}
