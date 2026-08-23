using System.Text;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;
using CncCad.IO.Import;
using CncCad.Infrastructure.Fonts;
using CncCad.Core.Commands;
using CncCad.Core.Documents;

namespace CncCad.Core.Tests;

public class VectorFoundationTests
{
    [Fact]
    public void DesktopFontCatalog_ProvidesSystemAndCncCenterlineFonts()
    {
        var catalog=new DesktopFontCatalog();Assert.Contains(catalog.Fonts,x=>x.Descriptor.Kind==FontKind.SingleLine);Assert.Contains(catalog.Fonts,x=>!x.Descriptor.IsBundled);var system=catalog.Fonts.First(x=>!x.Descriptor.IsBundled);var result=new SkiaTextOutlineService().Create(new("A",system.Descriptor.Family,system.Descriptor.Style,12,0,1.2,TextAlignment.Left,new(0,0)));Assert.NotEmpty(result.Outlines);Assert.Equal(FontKind.Outline,result.Kind);
    }

    [Fact]
    public void HersheyText_ProducesOpenEngravingStrokes()
    {
        var result=new SkiaTextOutlineService().Create(new("ROUTER","Hershey Simplex","Regular",10,1,1.2,TextAlignment.Left,new(0,0)));Assert.Equal(FontKind.SingleLine,result.Kind);Assert.NotEmpty(result.Outlines);Assert.All(result.Outlines,x=>Assert.False(x.IsClosed));Assert.Equal(6,result.GlyphAdvancesMm!.Count);
    }
    [Fact]
    public void TextPathLayout_FitsNativeCurvesAndPreservesPortableSource()
    {
        var source=new[]{new VectorPathFigure{Start=new(0,0),IsClosed=true,Segments=[new CubicPathSegment(new(2,0),new(3,4),new(5,4)),new LinePathSegment(new(0,0))]}};var guide=Enumerable.Range(0,33).Select(i=>new Point2D(50+30*Math.Cos(i*Math.PI/32),50+30*Math.Sin(i*Math.PI/32))).ToArray();var placement=new TextPathPlacement{BaselineOffsetMm=3,FitMode=TextFitMode.Natural};var fitted=TextPathLayout.Fit(source,guide,placement);Assert.Single(fitted);Assert.IsType<CubicPathSegment>(fitted[0].Segments[0]);Assert.NotEqual(source[0].Start,fitted[0].Start);
    }

    [Fact]
    public void EntityClone_PreservesColorGroupAndEditableTextMetadata()
    {
        var group=Guid.NewGuid();var text=new TextEntity{LayerId=Guid.NewGuid(),StrokeColor="#FF00AA",GroupId=group,Text="CNC",FontKind=FontKind.Stencil,LayoutMode=TextLayoutMode.FollowPath,PathPlacement=new(){GuideEntityId=Guid.NewGuid(),BaselineOffsetMm=2},GlyphAdvancesMm=[4,5,6],SourceOutlines=[new(){Start=new(0,0)}],Outlines=[new(){Start=new(1,1)}]};var clone=Assert.IsType<TextEntity>(EntityGeometry.Clone(text));Assert.Equal(text.StrokeColor,clone.StrokeColor);Assert.Equal(group,clone.GroupId);Assert.Equal(FontKind.Stencil,clone.FontKind);Assert.Equal(3,clone.GlyphAdvancesMm.Count);Assert.NotSame(text.Outlines,clone.Outlines);
    }
    [Fact]
    public void TextAndAppearanceCommands_AreUndoable()
    {
        var document=new CadDocument();var text=new TextEntity{LayerId=document.ActiveLayerId,Text="Old",Outlines=[new(){Start=new(0,0)}]};document.Entities.Add(text);
        var edited=(TextEntity)EntityGeometry.Clone(text);edited.Text="New";edited.StrokeColor="#FF0000";edited.Outlines=[new(){Start=new(4,5)}];
        var edit=new EditTextEntityCommand(text,edited);edit.Execute();Assert.Equal("New",text.Text);edit.Undo();Assert.Equal("Old",text.Text);
        var color=new SetEntityColorCommand(document,[text.Id],"#00AAFF");color.Execute();Assert.Equal("#00AAFF",text.StrokeColor);color.Undo();Assert.Null(text.StrokeColor);
        var group=Guid.NewGuid();var grouping=new SetEntityGroupCommand(document,[text.Id],group);grouping.Execute();Assert.Equal(group,text.GroupId);grouping.Undo();Assert.Null(text.GroupId);
    }
    [Fact]
    public void CubicPath_HasExactExtremaAndToleranceControlledFlattening()
    {
        var path=new VectorPathEntity{Figures=[new(){Start=new(0,0),Segments=[new CubicPathSegment(new(0,10),new(10,10),new(10,0))]}]};
        var bounds=path.GetBounds();
        Assert.Equal(0,bounds.MinX,6);Assert.Equal(10,bounds.MaxX,6);Assert.Equal(7.5,bounds.MaxY,6);
        var coarse=VectorPathGeometry.Flatten(path.Figures,1);var fine=VectorPathGeometry.Flatten(path.Figures,.01);
        Assert.True(fine.Count>coarse.Count);
        var (left,right,mid)=VectorPathGeometry.Split(new(0,0),(CubicPathSegment)path.Figures[0].Segments[0],.5);
        Assert.Equal(mid,left.End);Assert.Equal(mid,new Point2D(5,7.5));Assert.Equal(new Point2D(10,0),right.End);
    }

    [Fact]
    public void SvgImport_PreservesGroupsCurvesArcsAndMillimetres()
    {
        const string svg="""<svg xmlns="http://www.w3.org/2000/svg" width="100mm" height="50mm" viewBox="0 0 100 50"><g id="Cut"><path d="M 0 0 C 10 0 10 10 20 10 A 5 5 0 0 1 25 15 Z"/><rect x="30" y="5" width="20" height="10" rx="2"/></g></svg>""";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(svg));
        var result=new SvgVectorImporter().Import(new("part.svg",stream));
        Assert.Equal(ImportUnit.Millimetres,result.DetectedUnit);Assert.Equal(2,result.Entities.Count);Assert.Contains(result.Layers,x=>x.Name=="Cut");
        var path=Assert.IsType<VectorPathEntity>(result.Entities[0]);Assert.True(path.Figures[0].IsClosed);Assert.True(path.Figures[0].Segments.Count>=2);Assert.Empty(result.Warnings);
        Assert.True(result.Bounds.MinY>=0);Assert.True(result.Bounds.MaxY<=50.0001);
    }

    [Fact]
    public void RepairPreview_DetectsDuplicatesAndCleansTinyNodesWithoutMutation()
    {
        var layer=Guid.NewGuid();var a=new PolylineEntity{LayerId=layer,Points=[new(0,0),new(.001,0),new(10,0),new(10,10)],IsClosed=false};var duplicate=(PolylineEntity)EntityGeometry.Clone(a);
        var analysis=new VectorRepairService().Analyze([a,duplicate],new(.01,.01));
        Assert.Contains(analysis.Issues,x=>x.Kind==RepairIssueKind.DuplicateGeometry);Assert.Contains(analysis.Issues,x=>x.Kind==RepairIssueKind.TinySegment);
        Assert.Equal(4,a.Points.Count);Assert.NotEmpty(analysis.Replacements);Assert.NotEmpty(analysis.RemoveIds);
    }

    [Fact]
    public void PortableText_RoundTripsItsNativeOutlineGeometry()
    {
        var text=new TextEntity{Text="Router",FontFamily="Inter",SizeMm=12,LetterSpacingMm=.4,Outlines=[new(){Start=new(0,0),IsClosed=true,Segments=[new CubicPathSegment(new(5,0),new(5,10),new(0,10)),new LinePathSegment(new(0,0))]}]};
        var clone=Assert.IsType<TextEntity>(EntityGeometry.Clone(text));
        Assert.Equal("Router",clone.Text);Assert.Equal(.4,clone.LetterSpacingMm);Assert.IsType<CubicPathSegment>(clone.Outlines[0].Segments[0]);Assert.NotEqual(text.Id,clone.Id);
    }

    [Fact]
    public void BezierHandles_AreAddressableAndEditable()
    {
        var path=new VectorPathEntity{Figures=[new(){Start=new(0,0),Segments=[new CubicPathSegment(new(1,2),new(3,4),new(5,6))]}]};
        Assert.Equal(4,VectorPathNodes.Points(path).Count);VectorPathNodes.Set(path,2,new(30,40));
        Assert.Equal(new Point2D(30,40),((CubicPathSegment)path.Figures[0].Segments[0]).Control2);
    }

    [Fact]
    public void TrimAndCornerTools_ReturnNewTopologyAndLeaveSourceUntouched()
    {
        var target=new LineEntity{Start=new(0,5),End=new(30,5)};var cutters=new CadEntity[]{new LineEntity{Start=new(10,0),End=new(10,10)},new LineEntity{Start=new(20,0),End=new(20,10)}};var service=new VectorTopologyService();
        var trim=service.Trim(target,cutters,new(15,5),.001);Assert.True(trim.Success);Assert.Equal(2,trim.Entities.Count);Assert.Equal(new Point2D(0,5),target.Start);Assert.Equal(new Point2D(30,5),target.End);
        var corner=new PolylineEntity{Points=[new(0,0),new(10,0),new(10,10)]};var chamfer=service.Chamfer(corner,1,2);Assert.True(chamfer.Success);Assert.Equal(4,Assert.IsType<PolylineEntity>(chamfer.Entities[0]).Points.Count);
        var fillet=service.Fillet(corner,1,2);Assert.True(fillet.Success);Assert.Contains(Assert.IsType<VectorPathEntity>(fillet.Entities[0]).Figures[0].Segments,x=>x is QuadraticPathSegment);
    }
}
