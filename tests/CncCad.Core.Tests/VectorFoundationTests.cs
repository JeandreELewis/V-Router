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
    public void TextAlignmentMultilineLayoutAndMissingGlyphWarningsAreApplied()
    {
        var service=new SkiaTextOutlineService();var centered=service.Create(new("AA\nA?","Hershey Simplex","Regular",10,0,1.5,TextAlignment.Center,new(100,50)));var bounds=VectorPathGeometry.Bounds(centered.Outlines,Matrix2D.Identity);
        Assert.True(bounds.MinX<100&&bounds.MaxX>100);Assert.True(bounds.MinY<40);Assert.Contains(centered.Warnings,warning=>warning.Contains("?"));
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
    public void SvgPathResetsRelativePositionAndSmoothControlsAfterCloseOrLine()
    {
        const string svg="""<svg xmlns="http://www.w3.org/2000/svg" width="100mm" height="50mm" viewBox="0 0 100 50"><path d="M 10 10 L 20 10 Z m 5 0 l 5 0 M 0 0 L 10 0 S 20 10 30 0"/></svg>""";using var stream=new MemoryStream(Encoding.UTF8.GetBytes(svg));var result=new SvgVectorImporter().Import(new("path.svg",stream));var path=Assert.IsType<VectorPathEntity>(Assert.Single(result.Entities));
        Assert.Equal(new Point2D(15,10),path.Figures[1].Start);var cubic=Assert.IsType<CubicPathSegment>(path.Figures[2].Segments[1]);Assert.Equal(new Point2D(10,0),cubic.Control1);
    }
    [Fact]
    public void ImportReportsUnsupportedSvgLocationsAndAppliesDxfDrawingUnits()
    {
        const string svg="<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10mm\" height=\"10mm\">\n  <text x=\"1\" y=\"2\">Unsupported</text>\n  <line x1=\"0\" y1=\"0\" x2=\"10\" y2=\"0\"/>\n</svg>";using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(svg))){var result=new SvgVectorImporter().Import(new("unsupported.svg",stream));var unsupported=Assert.Single(result.UnsupportedDetails);Assert.Equal("text",unsupported.Element);Assert.Equal(2,unsupported.Line);}
        const string dxf="0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nCut\n10\n0\n20\n0\n11\n1\n21\n0\n0\nENDSEC\n0\nEOF\n";using var dxfStream=new MemoryStream(Encoding.ASCII.GetBytes(dxf));var imported=new DxfVectorImporter().Import(new("inch.dxf",dxfStream));Assert.Equal(ImportUnit.Inches,imported.DetectedUnit);Assert.Equal(25.4,imported.AppliedScaleToMillimetres,6);Assert.Equal(25.4,Assert.IsType<LineEntity>(Assert.Single(imported.Entities)).GetBounds().MaxX,6);
    }

    [Fact]
    public void SvgDefinitionsAndHiddenContentDoNotBecomeUnexpectedCutGeometry()
    {
        const string svg="""
        <svg xmlns="http://www.w3.org/2000/svg" width="100mm" height="50mm" viewBox="0 0 100 50">
          <defs><path id="template" d="M 0 0 L 90 0"/></defs>
          <g style="display:none"><line x1="0" y1="5" x2="90" y2="5"/></g>
          <line visibility="hidden" x1="0" y1="10" x2="90" y2="10"/>
          <line x1="10" y1="20" x2="30" y2="20"/>
          <use href="#template" x="5" y="10" transform="translate(2 3)"/>
        </svg>
        """;
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(svg));var result=new SvgVectorImporter().Import(new("visibility.svg",stream));Assert.Equal(2,result.Entities.Count);Assert.Empty(result.Unsupported);var world=result.Entities.Select(EntityGeometry.WorldPoints).OrderBy(points=>points[0].X).ToArray();Assert.Equal(new Point2D(7,37),world[0][0]);Assert.Equal(new Point2D(97,37),world[0][1]);Assert.Equal(20,world[1][0].DistanceTo(world[1][1]),6);
        using var hiddenStream=new MemoryStream(Encoding.UTF8.GetBytes(svg));var withHidden=new SvgVectorImporter().Import(new("visibility.svg",hiddenStream,IncludeHidden:true));Assert.Equal(4,withHidden.Entities.Count);Assert.Empty(withHidden.Unsupported);
    }

    [Fact]
    public void SvgViewBoxHonorsMeetNoneAlignmentPhysicalUnitsAndCssUserUnits()
    {
        const string meet="<svg xmlns='http://www.w3.org/2000/svg' width='200mm' height='100mm' viewBox='0 0 100 100'><line x1='0' y1='50' x2='100' y2='50'/></svg>";using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(meet))){var result=new SvgVectorImporter().Import(new("meet.svg",stream));var bounds=Assert.Single(result.Entities).GetBounds();Assert.Equal(50,bounds.MinX,6);Assert.Equal(150,bounds.MaxX,6);Assert.Equal(50,bounds.MinY,6);Assert.Equal(1,result.AppliedScaleToMillimetres,6);}
        const string none="<svg xmlns='http://www.w3.org/2000/svg' width='200mm' height='100mm' viewBox='0 0 100 100' preserveAspectRatio='none'><line x1='0' y1='50' x2='100' y2='50'/></svg>";using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(none))){var bounds=Assert.Single(new SvgVectorImporter().Import(new("none.svg",stream)).Entities).GetBounds();Assert.Equal(0,bounds.MinX,6);Assert.Equal(200,bounds.MaxX,6);Assert.Equal(50,bounds.MinY,6);}
        const string units="<svg xmlns='http://www.w3.org/2000/svg' width='2in' height='25.4mm' viewBox='0 0 2 1' preserveAspectRatio='none'><line x1='0' y1='0' x2='2' y2='1'/></svg>";using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(units))){var result=new SvgVectorImporter().Import(new("units.svg",stream,1,new(10,20)));var bounds=Assert.Single(result.Entities).GetBounds();Assert.Equal(10,bounds.MinX,6);Assert.Equal(60.8,bounds.MaxX,6);Assert.Equal(20,bounds.MinY,6);Assert.Equal(45.4,bounds.MaxY,6);}
        const string css="<svg xmlns='http://www.w3.org/2000/svg' width='25.4mm' height='25.4mm'><line x1='0' y1='0' x2='96' y2='0'/></svg>";using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(css))){var result=new SvgVectorImporter().Import(new("css.svg",stream));var bounds=Assert.Single(result.Entities).GetBounds();Assert.Equal(25.4,bounds.MaxX,6);Assert.Equal(25.4,bounds.MinY,6);Assert.Equal(25.4/96,result.AppliedScaleToMillimetres,8);}
    }

    [Fact]
    public void ImportersRejectUnsafePlacementAndSkipCollapsedGeometryWithDiagnostics()
    {
        const string svg="<svg xmlns='http://www.w3.org/2000/svg' width='20mm' height='10mm'><line x1='1' y1='1' x2='1' y2='1'/><circle cx='5' cy='5' r='0'/><line x1='0' y1='0' x2='10' y2='0'/></svg>";
        using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(svg))){var result=new SvgVectorImporter().Import(new("collapsed.svg",stream));Assert.Single(result.Entities);Assert.Contains(result.Warnings,item=>item.Code=="svg-degenerate");Assert.Equal(2,result.UnsupportedDetails.Count);}
        using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(svg)))Assert.Throws<ArgumentException>(()=>new SvgVectorImporter().Import(new("bad.svg",stream,double.NaN)));
        const string dxf="0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n4\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nCut\n10\n0\n20\n0\n11\n0\n21\n0\n0\nLINE\n8\nCut\n10\n0\n20\n0\n11\n10\n21\n0\n0\nENDSEC\n0\nEOF\n";
        using(var stream=new MemoryStream(Encoding.ASCII.GetBytes(dxf))){var result=new DxfVectorImporter().Import(new("collapsed.dxf",stream));Assert.Single(result.Entities);Assert.Contains(result.Warnings,item=>item.Code=="dxf-degenerate");Assert.Contains("Line",result.Unsupported);}
        using(var stream=new MemoryStream(Encoding.ASCII.GetBytes(dxf)))Assert.Throws<ArgumentException>(()=>new DxfVectorImporter().Import(new("bad.dxf",stream,1,new(double.PositiveInfinity,0))));
    }

    [Fact]
    public void DxfLayerPreservationCanBeDisabledWithoutLosingGeometry()
    {
        const string dxf="0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n4\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nCut\n10\n0\n20\n0\n11\n10\n21\n0\n0\nLINE\n8\nDrill\n10\n0\n20\n5\n11\n10\n21\n5\n0\nENDSEC\n0\nEOF\n";
        using var preservedStream=new MemoryStream(Encoding.ASCII.GetBytes(dxf));var preserved=new DxfVectorImporter().Import(new("layers.dxf",preservedStream));Assert.Equal(2,preserved.Layers.Count);Assert.Equal(2,preserved.Entities.Select(entity=>entity.LayerId).Distinct().Count());
        using var flatStream=new MemoryStream(Encoding.ASCII.GetBytes(dxf));var flat=new DxfVectorImporter().Import(new("layers.dxf",flatStream,PreserveLayers:false));var layer=Assert.Single(flat.Layers);Assert.Equal("layers Import",layer.Name);Assert.Equal(2,flat.Entities.Count);Assert.All(flat.Entities,entity=>Assert.Equal(layer.Id,entity.LayerId));
    }

    [Fact]
    public void DxfBlocksAndInsertsExpandWithLayerScaleRotationAndTranslation()
    {
        const string dxf="0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n4\n0\nENDSEC\n0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n8\n0\n2\nB1\n70\n0\n10\n0\n20\n0\n30\n0\n0\nLINE\n8\n0\n10\n0\n20\n0\n11\n10\n21\n0\n0\nENDBLK\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nINSERT\n8\nCut\n2\nB1\n10\n100\n20\n50\n30\n0\n41\n2\n42\n3\n50\n90\n0\nENDSEC\n0\nEOF\n";using var stream=new MemoryStream(Encoding.ASCII.GetBytes(dxf));var result=new DxfVectorImporter().Import(new("block.dxf",stream));Assert.True(result.Success,string.Join(", ",result.Unsupported));var line=Assert.IsType<LineEntity>(Assert.Single(result.Entities));var points=EntityGeometry.WorldPoints(line);Assert.Equal(100,points[0].X,7);Assert.Equal(50,points[0].Y,7);Assert.Equal(100,points[1].X,7);Assert.Equal(70,points[1].Y,7);Assert.Equal("Cut",Assert.Single(result.Layers).Name);Assert.Empty(result.Unsupported);
    }

    [Fact]
    public void DxfPolylineBulgePreservesANativeCircularArc()
    {
        const string dxf="0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n4\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nLWPOLYLINE\n8\nCut\n90\n2\n70\n0\n10\n0\n20\n0\n42\n1\n10\n10\n20\n0\n0\nENDSEC\n0\nEOF\n";using var stream=new MemoryStream(Encoding.ASCII.GetBytes(dxf));var result=new DxfVectorImporter().Import(new("bulge.dxf",stream));var path=Assert.IsType<VectorPathEntity>(Assert.Single(result.Entities));var figure=Assert.Single(path.Figures);var arc=Assert.IsType<ArcPathSegment>(Assert.Single(figure.Segments));Assert.Equal(new Point2D(5,0),arc.Center);Assert.Equal(180,arc.SweepAngleDegrees,8);Assert.Equal(new Point2D(10,0),arc.End);Assert.DoesNotContain(result.Approximations,item=>item.Code.Contains("bulge",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportResultSeparatesApproximationsFromUnsupportedContent()
    {
        const string svg="<svg xmlns='http://www.w3.org/2000/svg' width='20mm' height='20mm'><path d='M 2 10 A 8 5 20 0 1 18 10'/><text x='1' y='1'>x</text></svg>";using var stream=new MemoryStream(Encoding.UTF8.GetBytes(svg));var result=new SvgVectorImporter().Import(new("arc.svg",stream));Assert.Contains(result.Approximations,item=>item.Code=="svg-arc-cubic");Assert.Contains("text",result.Unsupported);Assert.Empty(result.Warnings);Assert.Empty(result.OtherWarnings);
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
    public void RepairMergesNearbyEndpointsAndPreservesEntityIdentityAndMetadata()
    {
        var layer=Guid.NewGuid();var group=Guid.NewGuid();var first=new PolylineEntity{LayerId=layer,MaterialSheetId=Guid.NewGuid(),GroupId=group,StrokeColor="#AA00FF",Points=[new(0,0),new(10,0)]};var second=new PolylineEntity{LayerId=layer,Points=[new(10.005,0),new(20,0)]};var analysis=new VectorRepairService().Analyze([first,second],new(.01,.001,MergeEndpoints:true));
        Assert.Contains(analysis.Issues,issue=>issue.Kind==RepairIssueKind.CoincidentEndpoint);var repaired=Assert.IsType<PolylineEntity>(analysis.Replacements.Single(entity=>entity.Id==first.Id));Assert.Equal(first.Id,repaired.Id);Assert.Equal(first.MaterialSheetId,repaired.MaterialSheetId);Assert.Equal(group,repaired.GroupId);Assert.Equal("#AA00FF",repaired.StrokeColor);Assert.Equal(10.0025,repaired.Points[^1].X,6);
    }

    [Fact]
    public void RepairUsesWorldMillimetresClosesGapsAndDetectsOverlaps()
    {
        var nearlyClosed=new PolylineEntity{Transform=Matrix2D.Scale(10,10),Points=[new(0,0),new(2,0),new(2,1),new(.0005,0)]};var overlap=new PolylineEntity{Points=[new(5,0),new(15,0)]};var baseLine=new PolylineEntity{Points=[new(0,0),new(10,0)]};
        var analysis=new VectorRepairService().Analyze([nearlyClosed,baseLine,overlap],new(.01,.01));var closed=Assert.IsType<PolylineEntity>(analysis.Replacements.Single(entity=>entity.Id==nearlyClosed.Id));Assert.True(closed.IsClosed);Assert.Equal(Matrix2D.Identity,closed.Transform);Assert.Equal(3,closed.Points.Count);Assert.Contains(analysis.Issues,issue=>issue.Kind==RepairIssueKind.Overlap&&issue.EntityIds.Contains(baseLine.Id)&&issue.EntityIds.Contains(overlap.Id));
    }

    [Fact]
    public void RepairRecognizesClosedDuplicatesIndependentOfStartAndDirection()
    {
        var first=new PolylineEntity{IsClosed=true,Points=[new(0,0),new(10,0),new(10,10),new(0,10)]};var second=new PolylineEntity{IsClosed=true,Points=[new(10,10),new(10,0),new(0,0),new(0,10)]};var analysis=new VectorRepairService().Analyze([first,second],new());Assert.Contains(analysis.Issues,issue=>issue.Kind==RepairIssueKind.DuplicateGeometry);Assert.Contains(second.Id,analysis.RemoveIds);
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
        var fillet=service.Fillet(corner,1,2);Assert.True(fillet.Success);var filletPath=Assert.IsType<VectorPathEntity>(fillet.Entities[0]);var arc=Assert.IsType<ArcPathSegment>(filletPath.Figures[0].Segments[1]);Assert.Equal(8,arc.Center.X,8);Assert.Equal(2,arc.Center.Y,8);Assert.Equal(90,arc.SweepAngleDegrees,8);Assert.Equal(10,arc.End.X,8);Assert.Equal(2,arc.End.Y,8);Assert.Equal(2,filletPath.Figures[0].Segments[0].End.DistanceTo(arc.Center),8);
    }

    [Fact]
    public void FilletUsesWorldMillimetresAndProducesAnExactTangentArc()
    {
        var source=new PolylineEntity{Id=Guid.NewGuid(),LayerId=Guid.NewGuid(),GroupId=Guid.NewGuid(),StrokeColor="#13579B",Transform=Matrix2D.Then(Matrix2D.Scale(2,.5),Matrix2D.Translation(20,30)),Points=[new(0,0),new(10,0),new(10,20)]};
        var result=new VectorTopologyService().Fillet(source,1,3);Assert.True(result.Success);var path=Assert.IsType<VectorPathEntity>(Assert.Single(result.Entities));Assert.Equal(source.Id,path.Id);Assert.Equal(source.LayerId,path.LayerId);Assert.Equal(source.GroupId,path.GroupId);Assert.Equal(source.StrokeColor,path.StrokeColor);Assert.Equal(Matrix2D.Identity,path.Transform);
        var figure=Assert.Single(path.Figures);var start=Assert.IsType<LinePathSegment>(figure.Segments[0]).End;var arc=Assert.IsType<ArcPathSegment>(figure.Segments[1]);Assert.Equal(3,start.DistanceTo(arc.Center),8);Assert.Equal(3,arc.End.DistanceTo(arc.Center),8);
        var incoming=start-figure.Start;var radiusAtStart=start-arc.Center;Assert.Equal(0,incoming.X*radiusAtStart.X+incoming.Y*radiusAtStart.Y,8);var outgoing=Assert.IsType<LinePathSegment>(figure.Segments[2]).End-arc.End;var radiusAtEnd=arc.End-arc.Center;Assert.Equal(0,outgoing.X*radiusAtEnd.X+outgoing.Y*radiusAtEnd.Y,8);
    }

    [Fact]
    public void ChamferDistanceIsMeasuredInWorldMillimetresUnderNonUniformTransform()
    {
        var source=new PolylineEntity{Transform=Matrix2D.Then(Matrix2D.Scale(2,.5),Matrix2D.Translation(20,30)),Points=[new(0,0),new(10,0),new(10,20)]};var result=new VectorTopologyService().Chamfer(source,1,3);var replacement=Assert.IsType<PolylineEntity>(Assert.Single(result.Entities));var world=replacement.Points.Select(replacement.Transform.Transform).ToArray();var originalCorner=source.Transform.Transform(source.Points[1]);Assert.Equal(3,world[1].DistanceTo(originalCorner),8);Assert.Equal(3,world[2].DistanceTo(originalCorner),8);Assert.All(world,point=>{Assert.True(double.IsFinite(point.X));Assert.True(double.IsFinite(point.Y));});
    }

    [Fact]
    public void ClosedPathFilletWrapsAcrossTheStartCornerWithoutBreakingClosure()
    {
        var source=new PolylineEntity{Points=[new(0,0),new(20,0),new(20,10),new(0,10)],IsClosed=true};var result=new VectorTopologyService().Fillet(source,0,2);var path=Assert.IsType<VectorPathEntity>(Assert.Single(result.Entities));var figure=Assert.Single(path.Figures);Assert.True(figure.IsClosed);var arc=Assert.IsType<ArcPathSegment>(figure.Segments[^1]);Assert.Equal(figure.Start,arc.End);var flattened=VectorPathGeometry.Flatten(path.Figures,.001);Assert.True(flattened[0].DistanceTo(flattened[^1])<1e-9);Assert.All(flattened,point=>{Assert.True(double.IsFinite(point.X));Assert.True(double.IsFinite(point.Y));});
    }

    [Fact]
    public void FilletPropertySweepProducesFiniteDeterministicTangentGeometry()
    {
        var random=new Random(1729);var service=new VectorTopologyService();for(var iteration=0;iteration<100;iteration++){var angle=(15+random.NextDouble()*150)*Math.PI/180;var firstLength=10+random.NextDouble()*90;var secondLength=10+random.NextDouble()*90;var tangentDistance=Math.Min(firstLength,secondLength)*(.1+random.NextDouble()*.35);var radius=tangentDistance*Math.Tan(angle/2);var source=new PolylineEntity{Points=[new(firstLength,0),new(0,0),new(secondLength*Math.Cos(angle),secondLength*Math.Sin(angle))]};var first=service.Fillet(source,1,radius);var second=service.Fillet(source,1,radius);Assert.True(first.Success);Assert.True(second.Success);var firstPath=Assert.IsType<VectorPathEntity>(Assert.Single(first.Entities));var secondPath=Assert.IsType<VectorPathEntity>(Assert.Single(second.Entities));var firstArc=Assert.IsType<ArcPathSegment>(firstPath.Figures[0].Segments[1]);var secondArc=Assert.IsType<ArcPathSegment>(secondPath.Figures[0].Segments[1]);Assert.Equal(firstArc,secondArc);var start=firstPath.Figures[0].Segments[0].End;Assert.Equal(radius,start.DistanceTo(firstArc.Center),7);Assert.Equal(radius,firstArc.End.DistanceTo(firstArc.Center),7);Assert.All(VectorPathGeometry.Flatten(firstPath.Figures,.005),point=>{Assert.True(double.IsFinite(point.X));Assert.True(double.IsFinite(point.Y));});}
    }
}
