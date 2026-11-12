using CncCad.Cam.Operations;
using CncCad.Cam.Profile;
using CncCad.Cam.Toolpaths;
using CncCad.Cam.Tools;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Tests;

public class PersistentProfileTests
{
    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(90)]
    public void VCarve_UsesVariableDepthAndHonorsMaximum(double angle)
    {
        var rectangle=new RectangleEntity{Width=40,Height=20};var operation=new VCarveOperationDefinition{GeometryIds=[rectangle.Id],Tool=new(){Name=$"{angle} degree V-bit",Type=ToolType.VBit,DiameterMm=12,VBitAngleDegrees=angle,TipDiameterMm=.2},MaximumDepthMm=2,OffsetStepOverMm=.5,ClearFlatRegions=true,SafeZMm=5,FeedRateMmPerMin=900,PlungeRateMmPerMin=250};var result=new VCarveOperationCalculator().Calculate(operation,new Dictionary<Guid,CadEntity>{{rectangle.Id,rectangle}});Assert.True(result.Success,string.Join("; ",result.Errors.Select(x=>x.Message)));var depths=result.Toolpath!.Moves.OfType<LinearMove>().Select(x=>-x.Z).Where(x=>x>0).Distinct().Order().ToArray();Assert.NotEmpty(depths);Assert.True(depths[^1]<=2+1e-9);Assert.True(depths.Length>1);
    }
    [Fact]
    public void VCarve_RejectsSingleLineTextAndMissingToolAngle()
    {
        var text=new TextEntity{FontKind=FontKind.SingleLine,Outlines=[new(){Start=new(0,0),Segments=[new LinePathSegment(new(10,0))]}]};var operation=new VCarveOperationDefinition{GeometryIds=[text.Id],Tool=new(){Type=ToolType.VBit,DiameterMm=6},MaximumDepthMm=2,OffsetStepOverMm=.5};var result=new VCarveOperationCalculator().Calculate(operation,new Dictionary<Guid,CadEntity>{{text.Id,text}});Assert.False(result.Success);Assert.Contains(result.Errors,x=>x.Code=="angle");Assert.Contains(result.Errors,x=>x.Message.Contains("Single-line"));
    }
    [Fact]
    public void VCarve_MedialAxisDepthNeverExceedsBoundaryClearanceAndIsDeterministic()
    {
        var rectangle=new RectangleEntity{Width=40,Height=20};var operation=new VCarveOperationDefinition{GeometryIds=[rectangle.Id],Tool=new(){Name="90 degree",Type=ToolType.VBit,DiameterMm=12,VBitAngleDegrees=90,TipDiameterMm=.2},MaximumDepthMm=20,OffsetStepOverMm=.75,ClearFlatRegions=false,SafeZMm=5,FeedRateMmPerMin=900,PlungeRateMmPerMin=250};var geometry=new Dictionary<Guid,CadEntity>{{rectangle.Id,rectangle}};var first=new VCarveOperationCalculator().Calculate(operation,geometry);var second=new VCarveOperationCalculator().Calculate(operation,geometry);Assert.True(first.Success,string.Join("; ",first.Errors.Select(error=>error.Message)));Assert.Equal(first.Toolpath!.Moves,second.Toolpath!.Moves);var cuts=first.Toolpath.Moves.OfType<LinearMove>().ToArray();Assert.NotEmpty(cuts);Assert.Contains(cuts,move=>Math.Abs(move.End.Y-10)<.05&&move.End.X>10&&move.End.X<30);foreach(var move in cuts){var clearance=new[]{move.End.X,40-move.End.X,move.End.Y,20-move.End.Y}.Min();var effectiveRadius=.1+Math.Max(0,-move.Z);Assert.True(effectiveRadius<=clearance+1e-6,$"Effective radius {effectiveRadius} exceeded clearance {clearance} at {move.End}.");}
    }
    [Fact]
    public void VCarve_FlatRegionClearingAddsContainedMaximumDepthMotion()
    {
        var rectangle=new RectangleEntity{Width=40,Height=20};VCarveOperationDefinition Definition(bool clear)=>new(){GeometryIds=[rectangle.Id],Tool=new(){Name="90 degree",Type=ToolType.VBit,DiameterMm=12,VBitAngleDegrees=90,TipDiameterMm=.2},MaximumDepthMm=2,OffsetStepOverMm=1,ClearFlatRegions=clear,SafeZMm=5,FeedRateMmPerMin=900,PlungeRateMmPerMin=250};var geometry=new Dictionary<Guid,CadEntity>{{rectangle.Id,rectangle}};var medial=new VCarveOperationCalculator().Calculate(Definition(false),geometry);var clearing=new VCarveOperationCalculator().Calculate(Definition(true),geometry);Assert.True(medial.Success);Assert.True(clearing.Success);Assert.True(clearing.Toolpath!.Moves.Count>medial.Toolpath!.Moves.Count);var maximum=clearing.Toolpath.Moves.OfType<LinearMove>().Where(move=>Math.Abs(move.Z+2)<1e-9).ToArray();Assert.NotEmpty(maximum);foreach(var move in maximum){var clearance=new[]{move.End.X,40-move.End.X,move.End.Y,20-move.End.Y}.Min();Assert.True(clearance>=2.1-1e-6);}
    }
    [Fact]
    public void VCarve_EvenOddIslandKeepsEveryCutInMachinableMaterial()
    {
        var outer=new RectangleEntity{Width=60,Height=40};var island=new RectangleEntity{Origin=new(20,12),Width=20,Height=16};var operation=new VCarveOperationDefinition{GeometryIds=[outer.Id,island.Id],Tool=new(){Name="60 degree",Type=ToolType.VBit,DiameterMm=8,VBitAngleDegrees=60,TipDiameterMm=.2},MaximumDepthMm=3,OffsetStepOverMm=.8,ClearFlatRegions=true,SafeZMm=5,FeedRateMmPerMin=800,PlungeRateMmPerMin=200};var result=new VCarveOperationCalculator().Calculate(operation,new Dictionary<Guid,CadEntity>{{outer.Id,outer},{island.Id,island}});Assert.True(result.Success,string.Join("; ",result.Errors.Select(error=>error.Message)));var tangent=Math.Tan(Math.PI/6);foreach(var move in result.Toolpath!.Moves.OfType<LinearMove>()){var point=move.End;var inOuter=point.X>=0&&point.X<=60&&point.Y>=0&&point.Y<=40;var inIsland=point.X>20&&point.X<40&&point.Y>12&&point.Y<28;Assert.True(inOuter&&!inIsland,$"Cut point {point} left the even-odd region.");var dx=Math.Max(20-point.X,Math.Max(0,point.X-40));var dy=Math.Max(12-point.Y,Math.Max(0,point.Y-28));var islandClearance=Math.Sqrt(dx*dx+dy*dy);var clearance=Math.Min(new[]{point.X,60-point.X,point.Y,40-point.Y}.Min(),islandClearance);Assert.True(.1+Math.Max(0,-move.Z)*tangent<=clearance+.02);}
    }
    [Fact]
    public void Engraving_OpenCenterlines_GeneratesIndependentDepthPasses()
    {
        var line=new LineEntity{Start=new(0,0),End=new(20,0)};var path=new PolylineEntity{Points=[new(30,0),new(30,10),new(40,10)]};var operation=new EngraveOperationDefinition{Name="Lettering",GeometryIds=[line.Id,path.Id],Tool=new(){DiameterMm=1,Name="Engraver",FeedRateMmPerMin=800,PlungeRateMmPerMin=300,StepDownMm=.5},CutDepthMm=1,PassDepthMm=.5,SafeZMm=5,FeedRateMmPerMin=800,PlungeRateMmPerMin=300};var result=new EngraveOperationCalculator().Calculate(operation,new Dictionary<Guid,CadEntity>{{line.Id,line},{path.Id,path}});Assert.True(result.Success);Assert.Equal(8,result.Toolpath!.Moves.OfType<RapidMove>().Count());Assert.Contains(result.Toolpath.Moves.OfType<LinearMove>(),x=>Math.Abs(x.Z+.5)<1e-9);Assert.Contains(result.Toolpath.Moves.OfType<LinearMove>(),x=>Math.Abs(x.Z+1)<1e-9);
    }
    [Fact]
    public void OpenEngravingRepositionsAtClearanceBeforeEveryPlunge()
    {
        var line = new LineEntity { Start = new(10, 10), End = new(60, 30) };
        var operation = new EngraveOperationDefinition { GeometryIds = [line.Id], Tool = new() { DiameterMm = 1 },
            CutDepthMm = 3, PassDepthMm = 1, SafeZMm = 5, FeedRateMmPerMin = 800, PlungeRateMmPerMin = 200 };
        var result = new EngraveOperationCalculator().Calculate(operation, new Dictionary<Guid, CadEntity> { [line.Id] = line });
        Assert.True(result.Success);
        var moves = result.Toolpath!.Moves;
        var plunges = 0;
        for (var i = 1; i < moves.Count; i++)
        {
            if (moves[i] is not LinearMove { FeedRate: 200 } plunge) continue;
            var approach = Assert.IsType<RapidMove>(moves[i - 1]);
            Assert.Equal(plunge.End, approach.End);
            Assert.Equal(5, approach.Z);
            plunges++;
        }
        Assert.Equal(3, plunges);
    }
    private static ProfileOperationDefinition Operation(CadEntity entity, ProfileSide side = ProfileSide.Outside) => new()
    {
        GeometryIds = [entity.Id], Side = side, Tool = new ToolSnapshot { Name = "6mm", DiameterMm = 6 },
        CutDepthMm = 7, PassDepthMm = 3, SafeZMm = 5, FeedRateMmPerMin = 1200, PlungeRateMmPerMin = 300, SpindleRpm = 18000
    };
    private static OperationCalculationResult Calculate(ProfileOperationDefinition operation, params CadEntity[] entities) => new ProfileOperationCalculator().Calculate(operation, entities.ToDictionary(e => e.Id));

    [Theory]
    [InlineData(ProfileSide.Outside, -3, 103)]
    [InlineData(ProfileSide.Inside, 3, 97)]
    [InlineData(ProfileSide.OnVector, 0, 100)]
    public void Rectangle_UsesExactCutterCompensation(ProfileSide side, double minimum, double maximum)
    {
        var rectangle = new RectangleEntity { Width = 100, Height = 100 }; var result = Calculate(Operation(rectangle, side), rectangle);
        Assert.True(result.Success); var points = result.Toolpath!.Moves.OfType<LinearMove>().Select(m => m.End).ToArray();
        Assert.Equal(minimum, points.Min(p => p.X), 6); Assert.Equal(maximum, points.Max(p => p.X), 6);
    }

    [Fact] public void ClosedPolylineAndCircle_AreAccepted()
    {
        var polygon = new PolylineEntity { IsClosed = true, Points = [new(0,0), new(80,0), new(40,50)] };
        var circle = new CircleEntity { Center = new(150, 50), Radius = 25 };
        var operation = Operation(polygon); operation.GeometryIds.Add(circle.Id);
        var result = Calculate(operation, polygon, circle);
        Assert.True(result.Success); Assert.True(result.Toolpath!.Moves.OfType<LinearMove>().Count() > 30);
    }

    [Fact]
    public void TransformedRectanglePreservesWorldCompensationAndRequestedDirection()
    {
        var rectangle=new RectangleEntity{Width=40,Height=20,Transform=Matrix2D.Then(Matrix2D.Scale(-1.5,2),Matrix2D.Then(Matrix2D.Rotation(31),Matrix2D.Translation(80,45)))};
        var operation=Operation(rectangle,ProfileSide.Outside);operation.CutDepthMm=1;operation.PassDepthMm=1;
        var result=Calculate(operation,rectangle);
        Assert.True(result.Success,string.Join("; ",result.Errors.Select(error=>error.Message)));
        var source=rectangle.Corners.Select(rectangle.Transform.Transform).ToArray();
        var route=FirstCutRoute(result.Toolpath!,operation.FeedRateMmPerMin);
        Assert.True(route.Count>=4);
        foreach(var point in route)
        {
            var distance=Enumerable.Range(0,source.Length).Min(index=>DistanceToLine(point,source[index],source[(index+1)%source.Length]));
            Assert.Equal(operation.Tool.DiameterMm/2,distance,5);
        }
        Assert.True(SignedArea(route)<0,"Outside climb profiling must remain clockwise after reflection and rotation.");
    }

    [Fact]
    public void TransformedCurvesKeepTheWorldFlatteningToleranceAndRemainDeterministic()
    {
        var transform=Matrix2D.Then(Matrix2D.Scale(100,100),Matrix2D.Then(Matrix2D.Rotation(17),Matrix2D.Translation(250,140)));
        var circle=new CircleEntity{Radius=1,Transform=transform};
        var arcPath=new VectorPathEntity{Transform=transform,Figures=[new(){Start=new(1,0),IsClosed=true,Segments=[new ArcPathSegment(new(0,0),180,new(-1,0)),new ArcPathSegment(new(0,0),180,new(1,0))]}]};
        foreach(var entity in new CadEntity[]{circle,arcPath})
        foreach(var side in new[]{ProfileSide.Outside,ProfileSide.Inside,ProfileSide.OnVector})
        {
            var operation=Operation(entity,side);operation.CutDepthMm=1;operation.PassDepthMm=1;
            var first=Calculate(operation,entity);var second=Calculate(operation,entity);
            Assert.True(first.Success,string.Join("; ",first.Errors.Select(error=>error.Message)));
            Assert.Equal(first.Toolpath!.Moves,second.Toolpath!.Moves);
            var route=FirstCutRoute(first.Toolpath,operation.FeedRateMmPerMin);
            var center=transform.Transform(new(0,0));
            var expectedRadius=100+(side==ProfileSide.Outside?3:side==ProfileSide.Inside?-3:0);
            Assert.All(route,point=>Assert.InRange(point.DistanceTo(center),expectedRadius-.021,expectedRadius+.021));
            Assert.Equal(side==ProfileSide.Inside,SignedArea(route)>0);
            if(side!=ProfileSide.OnVector)continue;
            for(var index=0;index+1<route.Count;index++)
            {
                var midpoint=new Point2D((route[index].X+route[index+1].X)/2,(route[index].Y+route[index+1].Y)/2);
                Assert.InRange(100-midpoint.DistanceTo(center),0,.020001);
            }
        }
    }

    [Fact]
    public void TransformedPolylineSupportsEveryCompensationSideWithFiniteDeterministicMotion()
    {
        var polygon=new PolylineEntity{IsClosed=true,Points=[new(0,0),new(50,0),new(62,28),new(35,55),new(0,40)],Transform=Matrix2D.Then(Matrix2D.Scale(-1.4,.8),Matrix2D.Then(Matrix2D.Rotation(-23),Matrix2D.Translation(170,95)))};
        foreach(var side in new[]{ProfileSide.Outside,ProfileSide.Inside,ProfileSide.OnVector})
        {
            var operation=Operation(polygon,side);operation.CutDepthMm=1;operation.PassDepthMm=1;
            var first=Calculate(operation,polygon);var second=Calculate(operation,polygon);
            Assert.True(first.Success,string.Join("; ",first.Errors.Select(error=>error.Message)));
            Assert.Equal(first.Toolpath!.Moves,second.Toolpath!.Moves);
            var route=FirstCutRoute(first.Toolpath,operation.FeedRateMmPerMin);
            Assert.All(route,point=>{Assert.True(double.IsFinite(point.X));Assert.True(double.IsFinite(point.Y));});
            Assert.Equal(side==ProfileSide.Inside,SignedArea(route)>0);
        }
    }

    [Fact] public void FinalPass_IsExact_WhenStepDoesNotDivideDepth()
    {
        var rectangle = new RectangleEntity { Width = 100, Height = 100 }; var result = Calculate(Operation(rectangle), rectangle);
        var depths = result.Toolpath!.Moves.OfType<LinearMove>().Where(m => m.FeedRate == 300).Select(m => m.Z).ToArray();
        Assert.Equal([-3d, -6d, -7d], depths);
    }

    [Fact] public void InnerContour_IsCompletedBeforeOuterContour()
    {
        var outer = new RectangleEntity { Width = 200, Height = 100 }; var inner = new CircleEntity { Center = new(100,50), Radius = 20 };
        var operation = Operation(outer); operation.GeometryIds.Add(inner.Id); var result = Calculate(operation, outer, inner);
        var rapids = result.Toolpath!.Moves.OfType<RapidMove>().ToArray();
        Assert.InRange(rapids[0].End.X, 76.9, 123.1); Assert.Equal(4, rapids.Length); // entry/retract per contour
    }

    [Fact] public void InvalidAndCollapsedGeometry_ReturnStructuredErrors()
    {
        var open = new PolylineEntity { Points = [new(0,0), new(10,0), new(10,10)] }; var openResult = Calculate(Operation(open), open);
        Assert.Contains(openResult.Errors, e => e.Code == "geometry");
        var tiny = new RectangleEntity { Width = 4, Height = 4 }; var collapsed = Calculate(Operation(tiny, ProfileSide.Inside), tiny);
        Assert.Contains(collapsed.Errors, e => e.Code == "collapsed");
    }

    [Fact] public void ToolSnapshotAndDuplicate_AreIndependent()
    {
        var rectangle = new RectangleEntity { Width = 100, Height = 100 }; var original = Operation(rectangle); var copy = original.Duplicate();
        original.Tool = original.Tool with { DiameterMm = 12 }; original.GeometryIds.Clear();
        Assert.Equal(6, copy.Tool.DiameterMm); Assert.Single(copy.GeometryIds); Assert.NotEqual(original.Id, copy.Id);
    }

    [Fact] public void CamJob_MarksReferencedOperationsStaleOrInvalid()
    {
        var id = Guid.NewGuid(); var operation = new ProfileOperationDefinition { GeometryIds = [id], Status = OperationStatus.Current }; var job = new CamJob { Operations = [operation] };
        job.MarkGeometryChanged([id]); Assert.Equal(OperationStatus.Stale, operation.Status);
        job.MarkGeometryChanged([id], true); Assert.Equal(OperationStatus.Invalid, operation.Status); Assert.NotNull(operation.Error);
    }

    private static IReadOnlyList<Point2D> FirstCutRoute(ToolpathOperation toolpath,double feed)
    {
        var moves=toolpath.Moves;var rapidIndex=moves.FindIndex(move=>move is RapidMove);var rapid=Assert.IsType<RapidMove>(moves[rapidIndex]);var route=new List<Point2D>{rapid.End};
        for(var index=rapidIndex+1;index<moves.Count&&moves[index] is not RapidMove;index++)
            if(moves[index] is LinearMove linear&&Math.Abs(linear.FeedRate-feed)<1e-9)route.Add(linear.End);
        return route;
    }
    private static double DistanceToLine(Point2D point,Point2D start,Point2D end)
    {
        var segment=end-start;if(segment.Length<=1e-12)return point.DistanceTo(start);return Math.Abs(segment.X*(start.Y-point.Y)-(start.X-point.X)*segment.Y)/segment.Length;
    }
    private static double SignedArea(IReadOnlyList<Point2D> points)
    {
        var area=0d;for(var index=0;index<points.Count;index++){var next=points[(index+1)%points.Count];area+=points[index].X*next.Y-next.X*points[index].Y;}return area/2;
    }
}
