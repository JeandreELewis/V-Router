using CncCad.Cam.Nesting;
using CncCad.Cam.Operations;
using CncCad.Cam.Profile;
using CncCad.Cam.Toolpaths;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Tests;

public class TabsAndNestingTests
{
    private static ToolSnapshot Tool=>new(){Name="6 mm",DiameterMm=6,FeedRateMmPerMin=1800,PlungeRateMmPerMin=600,StepDownMm=3};
    [Fact]
    public void RampedTabs_LiftOnlyPassesEnteringTabHeight()
    {
        var rectangle=new RectangleEntity{Width=100,Height=50};var operation=new ProfileOperationDefinition{GeometryIds=[rectangle.Id],Tool=Tool,Side=ProfileSide.OnVector,CutDepthMm=10,PassDepthMm=5,Tabs=new(){Enabled=true,AutomaticCount=2,WidthMm=8,RemainingHeightMm=3,RampLengthMm=4}};
        var result=new ProfileOperationCalculator().Calculate(operation,new Dictionary<Guid,CadEntity>{{rectangle.Id,rectangle}});Assert.True(result.Success);var cuts=result.Toolpath!.Moves.OfType<LinearMove>().ToArray();Assert.Contains(cuts,x=>Math.Abs(x.Z+5)<1e-6);Assert.Contains(cuts,x=>Math.Abs(x.Z+7)<1e-6);Assert.Contains(cuts,x=>x.Z < -7&&x.Z>=-10);
    }

    [Fact]
    public void ManualTabs_ValidateNormalizedPositionsAndSpacing()
    {
        var path=new[]{new Point2D(0,0),new Point2D(100,0),new Point2D(100,50),new Point2D(0,50)};var settings=new ProfileTabSettings{Enabled=true,PlacementMode=TabPlacementMode.Manual,WidthMm=8,RampLengthMm=4,RemainingHeightMm=3,ManualPlacements=[new(Guid.NewGuid(),.2),new(Guid.NewGuid(),.7)]};Assert.True(ProfileTabEngine.Validate(path,settings,18).Success);settings.ManualPlacements.Add(new(Guid.NewGuid(),1.2));Assert.False(ProfileTabEngine.Validate(path,settings,18).Success);
    }

    [Fact]
    public void ManualTabs_RejectRampsInsideCornerExclusion()
    {
        var path=new[]{new Point2D(0,0),new Point2D(100,0),new Point2D(100,50),new Point2D(0,50)};var settings=new ProfileTabSettings{Enabled=true,PlacementMode=TabPlacementMode.Manual,WidthMm=8,RampLengthMm=4,ExclusionDistanceMm=5,RemainingHeightMm=3,ManualPlacements=[new(Guid.NewGuid(),.01)]};var result=ProfileTabEngine.Validate(path,settings,18);Assert.Contains(result.Issues,x=>x.Code=="tab-corner");
    }

    [Fact]
    public void Nesting_IsDeterministicNonOverlappingAndUsesMultipleSheets()
    {
        var a=new RectangleEntity{Width=90,Height=90};var part=new NestPartDefinition{Name="Panel",OuterGeometryId=a.Id,Quantity=3};var job=new NestingJobDefinition{Parts=[part],SheetTemplate=new(){WidthMm=110,HeightMm=110,ThicknessMm=18,Quantity=3},PartSpacingMm=5,EdgeMarginMm=5};var optimizer=new DeterministicNestingOptimizer();var first=optimizer.Optimize(new(job,new Dictionary<Guid,CadEntity>{{a.Id,a}}));var second=optimizer.Optimize(new(job,new Dictionary<Guid,CadEntity>{{a.Id,a}}));Assert.Equal(3,first.Sheets.Count);Assert.Equal(first.Placements.Select(x=>(x.SheetId,x.Xmm,x.Ymm,x.RotationDegrees)),second.Placements.Select(x=>(x.SheetId,x.Xmm,x.Ymm,x.RotationDegrees)));Assert.Empty(first.UnplacedQuantities);
    }

    [Fact]
    public void Nesting_ReportsPartsThatCannotFit()
    {
        var a=new RectangleEntity{Width=200,Height=100};var part=new NestPartDefinition{OuterGeometryId=a.Id,Quantity=2};var job=new NestingJobDefinition{Parts=[part],SheetTemplate=new(){WidthMm=100,HeightMm=100,Quantity=1},EdgeMarginMm=5};var result=new DeterministicNestingOptimizer().Optimize(new(job,new Dictionary<Guid,CadEntity>{{a.Id,a}}));Assert.Equal(2,result.UnplacedQuantities[part.Id]);Assert.Empty(result.Placements);
    }

    [Fact]
    public void NestingHonorsPartAndSheetGrainWhenChoosingRotations()
    {
        var source=new RectangleEntity{Width=80,Height=30};
        var part=new NestPartDefinition{OuterGeometryId=source.Id,Quantity=1,Grain=NestGrainDirection.AlongY,AllowQuarterTurn=true};
        var job=new NestingJobDefinition{Parts=[part],SheetTemplate=new(){WidthMm=40,HeightMm=100,Quantity=1,Grain=NestGrainDirection.AlongX},EdgeMarginMm=2};
        var result=new DeterministicNestingOptimizer().Optimize(new(job,new Dictionary<Guid,CadEntity>{{source.Id,source}}));
        Assert.True(result.Success);Assert.Contains(Assert.Single(result.Placements).RotationDegrees,new[]{90,270});

        part.AllowQuarterTurn=false;
        var blocked=new DeterministicNestingOptimizer().Optimize(new(job,new Dictionary<Guid,CadEntity>{{source.Id,source}}));
        Assert.False(blocked.Success);Assert.Equal(1,blocked.UnplacedQuantities[part.Id]);
    }
}
