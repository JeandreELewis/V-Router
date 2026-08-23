using CncCad.Core.Geometry;

namespace CncCad.Core.Entities;

public sealed record TextOutlineRequest(string Text,string FontFamily,string FontStyle,double SizeMm,double LetterSpacingMm,double LineSpacing,TextAlignment Alignment,Point2D Origin);
public sealed record TextOutlineResult(IReadOnlyList<VectorPathFigure> Outlines,IReadOnlyList<string> Warnings);
public interface ITextOutlineService{TextOutlineResult Create(TextOutlineRequest request);}

// Deterministic portable fallback used when a platform font outline provider is unavailable.
// Its simple glyph cells deliberately remain project-owned vectors and never depend on a font at reopen time.
public sealed class PortableFallbackTextOutlineService:ITextOutlineService
{
    public TextOutlineResult Create(TextOutlineRequest request)
    {
        if(string.IsNullOrWhiteSpace(request.Text)||request.SizeMm<=0)throw new ArgumentException("Text and a positive size are required.");var figures=new List<VectorPathFigure>();var advance=request.SizeMm*.65+request.LetterSpacingMm;var x=request.Origin.X;var y=request.Origin.Y;
        foreach(var character in request.Text){if(character=='\n'){x=request.Origin.X;y-=request.SizeMm*request.LineSpacing;continue;}if(!char.IsWhiteSpace(character)){var width=Math.Max(request.SizeMm*.5,advance-request.LetterSpacingMm);figures.Add(new(){Start=new(x,y),IsClosed=true,Segments=[new LinePathSegment(new(x+width,y)),new LinePathSegment(new(x+width,y+request.SizeMm)),new LinePathSegment(new(x,y+request.SizeMm))]});}x+=advance;}
        return new(figures,["The requested platform font outline provider was unavailable; portable fallback outlines were generated. Edit the text after installing the font to regenerate glyph outlines."]);
    }
}
