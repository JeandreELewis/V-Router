using CncCad.App.ViewModels;
using CncCad.Cam.Operations;
using CncCad.Cam.Nesting;
using CncCad.Cam.Tools;
using CncCad.Cam.Safety;
using CncCad.Core.Documents;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;
using CncCad.Infrastructure.Persistence;
using CncCad.Infrastructure.Settings;
using CncCad.IO.Projects;

namespace CncCad.Core.Tests;

public class GuidedWorkspaceTests
{
    [Fact] public void GuidedMode_IsTheDefaultAndAdvancedModeIsExplicit()
    {
        var vm=ViewModel();Assert.False(vm.IsAdvancedMode);vm.IsAdvancedMode=true;Assert.True(vm.IsAdvancedMode);
    }

    [Fact] public void Workflow_AdvancesFromSetupThroughPreview()
    {
        var vm = ViewModel(); Assert.StartsWith("1", vm.WorkflowStage);
        vm.CreateJob(new JobSettings { Name = "Flow", WidthMm = 600, HeightMm = 400, ThicknessMm = 18 }); Assert.StartsWith("2", vm.WorkflowStage);
        var rectangle = new RectangleEntity { LayerId = vm.Document.ActiveLayerId, Origin = new(20,20), Width = 200, Height = 100 }; vm.AddEntity(rectangle); Assert.StartsWith("2", vm.WorkflowStage);
        vm.SetWorkspaceFocus(WorkspaceFocus.Toolpaths);Assert.StartsWith("3",vm.WorkflowStage);vm.AddOperation(vm.CreateProfileDraft());vm.SetWorkspaceView(WorkspaceView.ToolpathPreview);Assert.StartsWith("4", vm.WorkflowStage); Assert.True(vm.HasOperations);
    }

    [Fact] public void ProfileReadiness_ExplainsSelectionCompatibility()
    {
        var vm = ViewModel(); vm.CreateJob(new JobSettings()); Assert.False(vm.CanCreateProfile); Assert.Contains("Select", vm.ProfileGuidance);
        var line = new LineEntity { LayerId = vm.Document.ActiveLayerId, Start = new(0,0), End = new(10,10) }; vm.AddEntity(line); Assert.False(vm.CanCreateProfile); Assert.Contains("unsupported", vm.ProfileGuidance);
        var rectangle = new RectangleEntity { LayerId = vm.Document.ActiveLayerId, Width = 100, Height = 50 }; vm.AddEntity(rectangle); Assert.True(vm.CanCreateProfile); Assert.Contains("ready", vm.ProfileGuidance);
        vm.Selection.Add(line.Id); vm.SelectionChanged(); Assert.False(vm.CanCreateProfile);
    }

    [Fact] public void CenterSelection_IsUndoable()
    {
        var vm = ViewModel(); vm.CreateJob(new JobSettings { WidthMm = 600, HeightMm = 400 }); var rectangle = new RectangleEntity { LayerId = vm.Document.ActiveLayerId, Origin = new(0,0), Width = 100, Height = 40 }; vm.AddEntity(rectangle);
        vm.CenterSelectionOnMaterial(); Assert.Equal(new Point2D(250,180), rectangle.Origin);
        vm.Undo(); Assert.Equal(new Point2D(0,0), rectangle.Origin);
    }

    [Fact] public void WorldTransformsRejectUnsafeNumericsPreserveNativeGeometryAndInvalidateCam()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=200,HeightMm=150,ThicknessMm=18});var rectangle=new RectangleEntity{LayerId=vm.Document.ActiveLayerId,Origin=new(10,20),Width=40,Height=20};vm.Document.Entities.Add(rectangle);vm.Selection.Set(rectangle.Id);vm.SelectionChanged();var operation=vm.CreateProfileDraft();vm.AddOperation(operation);Assert.Equal(OperationStatus.Current,operation.Status);Assert.False(vm.CanUndo);
        vm.PivotX=30;vm.PivotY=30;vm.TransformAngle=0;vm.RotateSelection();Assert.False(vm.CanUndo);Assert.Equal(OperationStatus.Current,operation.Status);
        vm.TransformAngle=double.NaN;Assert.Throws<InvalidOperationException>(vm.RotateSelection);Assert.False(vm.CanUndo);Assert.Equal(Matrix2D.Identity,rectangle.Transform);
        vm.TransformAngle=90;vm.RotateSelection();Assert.True(vm.CanUndo);Assert.IsType<RectangleEntity>(rectangle);AssertBounds(new(20,10,40,50),rectangle.GetBounds());Assert.Equal(OperationStatus.Stale,operation.Status);vm.Undo();AssertBounds(new(10,20,50,40),rectangle.GetBounds());
        vm.Selection.Set(rectangle.Id);vm.SelectionChanged();vm.PivotX=30;vm.PivotY=30;vm.ScaleX=2;vm.ScaleY=.5;vm.ScaleSelection();AssertBounds(new(-10,25,70,35),rectangle.GetBounds());vm.Undo();
        vm.Selection.Set(rectangle.Id);vm.SelectionChanged();vm.PivotX=30;vm.PivotY=30;vm.MirrorSelection(true);Assert.Equal(new Point2D(50,20),EntityGeometry.WorldPoints(rectangle)[0]);vm.Undo();vm.Selection.Set(rectangle.Id);vm.SelectionChanged();vm.MirrorSelection(false);Assert.Equal(new Point2D(10,40),EntityGeometry.WorldPoints(rectangle)[0]);vm.Undo();
        vm.Selection.Set(rectangle.Id);vm.SelectionChanged();vm.SelectionX=60;vm.SelectionY=70;vm.ApplySelectionPosition();AssertBounds(new(60,70,100,90),rectangle.GetBounds());vm.Undo();AssertBounds(new(10,20,50,40),rectangle.GetBounds());
        vm.Selection.Set(rectangle.Id);vm.SelectionChanged();vm.SelectionX=double.PositiveInfinity;Assert.Throws<InvalidOperationException>(vm.ApplySelectionPosition);AssertBounds(new(10,20,50,40),rectangle.GetBounds());
        static void AssertBounds(BoundingBox expected,BoundingBox actual){Assert.Equal(expected.MinX,actual.MinX,8);Assert.Equal(expected.MinY,actual.MinY,8);Assert.Equal(expected.MaxX,actual.MaxX,8);Assert.Equal(expected.MaxY,actual.MaxY,8);}
    }

    [Fact] public void AlignAndDistributeUseDeterministicEqualGapsAndDoNotRecordNoOps()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=200,HeightMm=100});var first=new RectangleEntity{LayerId=vm.Document.ActiveLayerId,Origin=new(0,5),Width=10,Height=10};var middle=new RectangleEntity{LayerId=vm.Document.ActiveLayerId,Origin=new(20,20),Width=30,Height=15};var last=new RectangleEntity{LayerId=vm.Document.ActiveLayerId,Origin=new(100,40),Width=20,Height=20};vm.Document.Entities.AddRange([middle,last,first]);vm.Selection.Set([last.Id,first.Id,middle.Id]);vm.SelectionChanged();Assert.False(vm.CanUndo);
        vm.DistributeSelection(true);Assert.Equal(40,middle.Origin.X,8);Assert.Equal(30,middle.GetBounds().MinX-first.GetBounds().MaxX,8);Assert.Equal(30,last.GetBounds().MinX-middle.GetBounds().MaxX,8);vm.Undo();Assert.Equal(20,middle.Origin.X,8);
        vm.Selection.Set([last.Id,first.Id,middle.Id]);vm.SelectionChanged();vm.AlignSelection("Right");Assert.Equal(120,first.GetBounds().MaxX,8);Assert.Equal(120,middle.GetBounds().MaxX,8);Assert.Equal(120,last.GetBounds().MaxX,8);vm.Undo();
        vm.Selection.Set([last.Id,first.Id,middle.Id]);vm.SelectionChanged();Assert.Throws<InvalidOperationException>(()=>vm.AlignSelection("Diagonal"));Assert.False(vm.CanUndo);
    }

    [Fact] public void JoinChainsBothEndsInAnySelectionOrderAndIsUndoable()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings());var group=Guid.NewGuid();var middle=new LineEntity{LayerId=vm.Document.ActiveLayerId,Start=new(10,0),End=new(20,0),GroupId=group,StrokeColor="#FF0000"};var tail=new LineEntity{LayerId=vm.Document.ActiveLayerId,Start=new(20,0),End=new(30,0)};var head=new LineEntity{LayerId=vm.Document.ActiveLayerId,Start=new(0,0),End=new(10,0)};vm.AddEntity(middle);vm.AddEntity(tail);vm.AddEntity(head);vm.Selection.Set([middle.Id,tail.Id,head.Id]);vm.SelectionChanged();vm.JoinSelection();
        var joined=Assert.IsType<PolylineEntity>(Assert.Single(vm.Document.Entities));Assert.Equal(middle.Id,joined.Id);Assert.Equal([new Point2D(0,0),new Point2D(10,0),new Point2D(20,0),new Point2D(30,0)],joined.Points);Assert.Equal(group,joined.GroupId);Assert.Equal("#FF0000",joined.StrokeColor);vm.Undo();Assert.Equal(3,vm.Document.Entities.Count);
    }

    [Fact] public void ChamferAndExactFilletAreUndoableAndInvalidateDependentToolpaths()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=100,HeightMm=100,ThicknessMm=12});var contour=new PolylineEntity{LayerId=vm.Document.ActiveLayerId,Points=[new(10,10),new(70,10),new(70,60),new(10,60)],IsClosed=true};vm.AddEntity(contour);var operation=vm.CreateProfileDraft();vm.AddOperation(operation);Assert.Equal(OperationStatus.Current,operation.Status);Assert.True(vm.CanUseChamferFillet);
        vm.CornerIndex=1;vm.ChamferDistance=4;vm.ChamferSelection();var chamfered=Assert.IsType<PolylineEntity>(Assert.Single(vm.Document.Entities));Assert.Equal(5,chamfered.Points.Count);Assert.Equal(OperationStatus.Stale,operation.Status);vm.Undo();Assert.IsType<PolylineEntity>(Assert.Single(vm.Document.Entities));
        vm.Selection.Set(contour.Id);vm.SelectionChanged();vm.FilletRadius=4;vm.FilletSelection();var filleted=Assert.IsType<VectorPathEntity>(Assert.Single(vm.Document.Entities));Assert.Contains(filleted.Figures[0].Segments,segment=>segment is ArcPathSegment);Assert.Equal(OperationStatus.Stale,operation.Status);vm.Undo();Assert.IsType<PolylineEntity>(Assert.Single(vm.Document.Entities));
    }

    [Fact] public void SignedOpenOffsetIsAvailableAndUndoableForAnOpenVector()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=100,HeightMm=100});var source=new PolylineEntity{LayerId=vm.Document.ActiveLayerId,Points=[new(10,10),new(50,10),new(50,40)]};vm.AddEntity(source);Assert.True(vm.CanOffsetSelection);vm.OffsetDistance=3;vm.OffsetJoin=CncCad.Cam.Profile.CadOffsetJoin.Miter;vm.OffsetSelection();var offset=Assert.IsType<PolylineEntity>(vm.Document.Entities.Single(entity=>entity.Id!=source.Id));Assert.Equal([new Point2D(10,13),new Point2D(47,13),new Point2D(47,40)],offset.Points);vm.Undo();Assert.Single(vm.Document.Entities);Assert.Same(source,vm.Document.Entities[0]);
    }

    [Fact] public void ExactAndParametricConstructionIsValidatedUndoableAndNumericPolylineCancellationDoesNotMutate()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=200,HeightMm=150});
        vm.ActivePaletteItem=vm.CreationTools.Single(item=>item.Tool==CncCad.Rendering.Canvas.CanvasTool.Line);vm.Point1X=5;vm.Point1Y=7;vm.Point2X=25;vm.Point2Y=17;vm.CreateExact();var line=Assert.IsType<LineEntity>(Assert.Single(vm.Document.Entities));Assert.Equal(new Point2D(5,7),line.Start);vm.Undo();Assert.Empty(vm.Document.Entities);
        vm.Point2X=double.NaN;Assert.Throws<InvalidOperationException>(vm.CreateExact);Assert.Empty(vm.Document.Entities);
        vm.ActivePaletteItem=vm.CreationTools.Single(item=>item.Tool==CncCad.Rendering.Canvas.CanvasTool.Polyline);vm.Point1X=0;vm.Point1Y=0;vm.Point2X=10;vm.Point2Y=0;vm.AddNumericPolylineNode();vm.CancelNumericPolyline();Assert.Empty(vm.Document.Entities);Assert.False(vm.CanUndo);
        vm.AddNumericPolylineNode();Assert.Throws<InvalidOperationException>(()=>vm.FinishNumericPolyline(true));Assert.Empty(vm.Document.Entities);vm.CancelNumericPolyline();
        vm.Point1X=30;vm.Point1Y=40;vm.ShapeWidth=50;vm.ShapeHeight=20;vm.CornerRadius=0;vm.CreateRoundedRectangle();var rounded=Assert.IsType<PolylineEntity>(Assert.Single(vm.Document.Entities));Assert.Equal(4,rounded.Points.Count);vm.Undo();Assert.Empty(vm.Document.Entities);
        vm.CreateSlot();Assert.All(Assert.IsType<PolylineEntity>(Assert.Single(vm.Document.Entities)).Points,point=>{Assert.True(double.IsFinite(point.X));Assert.True(double.IsFinite(point.Y));});vm.Undo();
        vm.ShapeSides=6;vm.ShapeOuterRadius=15;vm.CreatePolygon();Assert.Equal(6,Assert.IsType<PolylineEntity>(Assert.Single(vm.Document.Entities)).Points.Count);vm.Undo();
        vm.ShapeInnerRadius=7;vm.CreateStar();Assert.Equal(12,Assert.IsType<PolylineEntity>(Assert.Single(vm.Document.Entities)).Points.Count);vm.Undo();Assert.Empty(vm.Document.Entities);
        vm.ShapeSides=10001;Assert.Throws<ArgumentException>(vm.CreatePolygon);Assert.Empty(vm.Document.Entities);
    }

    [Fact] public void DuplicateAndPasteRemapGroupsAndCopiedTextGuideLinksWithoutLosingMetadata()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=200,HeightMm=150});var group=Guid.NewGuid();var sheet=Guid.NewGuid();
        var guide=new PolylineEntity{LayerId=vm.Document.ActiveLayerId,MaterialSheetId=sheet,GroupId=group,StrokeColor="#123456",IsVisible=false,IsLocked=true,Points=[new(10,10),new(80,10),new(80,50)]};
        var text=new TextEntity{LayerId=vm.Document.ActiveLayerId,MaterialSheetId=sheet,GroupId=group,StrokeColor="#ABCDEF",Transform=Matrix2D.Rotation(12),Text="Linked",LayoutMode=TextLayoutMode.FollowPath,PathPlacement=new(){GuideEntityId=guide.Id,GuideSnapshot=[..guide.Points]},SourceOutlines=[new(){Start=new(0,0),Segments=[new LinePathSegment(new(5,0))]}],Outlines=[new(){Start=new(10,10),Segments=[new LinePathSegment(new(15,10))]}]};
        vm.AddEntity(guide);vm.AddEntity(text);vm.Selection.Set([guide.Id,text.Id]);vm.SelectionChanged();vm.DuplicateSelection();
        var duplicates=vm.Document.Entities.Where(entity=>vm.Selection.Contains(entity.Id)).ToArray();var duplicateGuide=Assert.IsType<PolylineEntity>(duplicates.Single(entity=>entity is PolylineEntity));var duplicateText=Assert.IsType<TextEntity>(duplicates.Single(entity=>entity is TextEntity));
        Assert.NotEqual(group,duplicateGuide.GroupId);Assert.Equal(duplicateGuide.GroupId,duplicateText.GroupId);Assert.Equal(duplicateGuide.Id,duplicateText.PathPlacement.GuideEntityId);Assert.Equal(sheet,duplicateGuide.MaterialSheetId);Assert.Equal("#123456",duplicateGuide.StrokeColor);Assert.False(duplicateGuide.IsVisible);Assert.True(duplicateGuide.IsLocked);Assert.Equal(new Point2D(20,20),duplicateGuide.Points[0]);
        vm.Undo();Assert.DoesNotContain(vm.Document.Entities,entity=>entity.Id==duplicateGuide.Id);vm.Redo();Assert.Contains(vm.Document.Entities,entity=>entity.Id==duplicateGuide.Id);

        vm.Selection.Set([guide.Id,text.Id]);vm.SelectionChanged();vm.CopySelection();vm.Paste();var pasted=vm.Document.Entities.Where(entity=>vm.Selection.Contains(entity.Id)).ToArray();var pastedGuide=Assert.IsType<PolylineEntity>(pasted.Single(entity=>entity is PolylineEntity));var pastedText=Assert.IsType<TextEntity>(pasted.Single(entity=>entity is TextEntity));
        Assert.Equal(pastedGuide.Id,pastedText.PathPlacement.GuideEntityId);Assert.NotEqual(group,pastedGuide.GroupId);Assert.Equal(pastedGuide.GroupId,pastedText.GroupId);Assert.Equal(new Point2D(20,20),pastedGuide.Points[0]);
        vm.Paste();var secondGuide=Assert.IsType<PolylineEntity>(vm.Document.Entities.Single(entity=>vm.Selection.Contains(entity.Id)&&entity is PolylineEntity));Assert.Equal(new Point2D(30,30),secondGuide.Points[0]);Assert.NotEqual(pastedGuide.GroupId,secondGuide.GroupId);
    }

    [Fact] public void DuplicateCoversEveryNativeEntityWithoutFlatteningOrMutatingCamReferences()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=300,HeightMm=200,ThicknessMm=18});var group=Guid.NewGuid();var sheet=Guid.NewGuid();var layer=vm.Document.ActiveLayerId;CadEntity[] originals=
        [
            new LineEntity{Start=new(1,2),End=new(11,12)},new PolylineEntity{Points=[new(20,5),new(30,15),new(40,5)],IsClosed=true},new RectangleEntity{Origin=new(50,10),Width=20,Height=15},new CircleEntity{Center=new(90,20),Radius=8},new ArcEntity{Center=new(120,20),Radius=9,StartAngleDegrees=15,SweepAngleDegrees=220},new EllipseEntity{Center=new(150,20),RadiusX=12,RadiusY=6},new DrillPointEntity{Position=new(180,20)},new VectorPathEntity{Figures=[new(){Start=new(200,10),Segments=[new CubicPathSegment(new(205,25),new(215,-5),new(220,10)),new ArcPathSegment(new(220,20),90,new(230,20))]}]},new TextEntity{Text="Native",FontStyle="Bold",Transform=Matrix2D.Rotation(5),SourceOutlines=[new(){Start=new(240,10),Segments=[new QuadraticPathSegment(new(245,25),new(250,10))]}],Outlines=[new(){Start=new(240,10),Segments=[new QuadraticPathSegment(new(245,25),new(250,10))]}],Glyphs=[new(){Cluster="N",Order=0,AdvanceMm=10,SourceFigures=[new(){Start=new(240,10)}],PlacedFigures=[new(){Start=new(240,10)}]}]}
        ];
        foreach(var entity in originals){entity.LayerId=layer;entity.MaterialSheetId=sheet;entity.GroupId=group;entity.StrokeColor="#345678";entity.Transform=Matrix2D.Then(entity.Transform,Matrix2D.Translation(2,3));vm.Document.Entities.Add(entity);}var referenced=originals.OfType<RectangleEntity>().Single();var operation=new ProfileOperationDefinition{GeometryIds=[referenced.Id],Status=OperationStatus.Current};vm.CamJob.Operations.Add(operation);vm.Selection.Set(originals.Select(entity=>entity.Id));vm.SelectionChanged();vm.DuplicateSelection();var clones=vm.Document.Entities.Skip(originals.Length).ToArray();Assert.Equal(originals.Length,clones.Length);Assert.Equal(OperationStatus.Current,operation.Status);var clonedGroup=Assert.Single(clones.Select(entity=>entity.GroupId).Distinct());Assert.NotEqual(group,clonedGroup);
        foreach(var pair in originals.Zip(clones)){Assert.Equal(pair.First.GetType(),pair.Second.GetType());Assert.NotEqual(pair.First.Id,pair.Second.Id);Assert.Equal(layer,pair.Second.LayerId);Assert.Equal(sheet,pair.Second.MaterialSheetId);Assert.Equal("#345678",pair.Second.StrokeColor);var before=pair.First.GetBounds();var after=pair.Second.GetBounds();Assert.Equal(before.MinX+10,after.MinX,6);Assert.Equal(before.MinY+10,after.MinY,6);Assert.Equal(before.MaxX+10,after.MaxX,6);Assert.Equal(before.MaxY+10,after.MaxY,6);}
        var originalPath=originals.OfType<VectorPathEntity>().Single();var clonedPath=clones.OfType<VectorPathEntity>().Single();Assert.IsType<CubicPathSegment>(clonedPath.Figures[0].Segments[0]);Assert.IsType<ArcPathSegment>(clonedPath.Figures[0].Segments[1]);clonedPath.Figures[0].Segments[0].End=new(999,999);Assert.NotEqual(clonedPath.Figures[0].Segments[0].End,originalPath.Figures[0].Segments[0].End);var originalText=originals.OfType<TextEntity>().Single();var clonedText=clones.OfType<TextEntity>().Single();clonedText.Glyphs[0].PlacedFigures[0].Start=new(777,777);Assert.NotEqual(clonedText.Glyphs[0].PlacedFigures[0].Start,originalText.Glyphs[0].PlacedFigures[0].Start);
        vm.Undo();Assert.Equal(originals.Length,vm.Document.Entities.Count);vm.Redo();Assert.Equal(originals.Length*2,vm.Document.Entities.Count);Assert.Equal(clones.Select(entity=>entity.Id),vm.Document.Entities.Skip(originals.Length).Select(entity=>entity.Id));
    }

    [Fact] public void DeleteDetachesLinkedTextAsOneTransactionInvalidatesItsCamAndHonorsLocks()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=120,HeightMm=80,ThicknessMm=12});var guide=new PolylineEntity{LayerId=vm.Document.ActiveLayerId,Points=[new(10,20),new(100,20)]};var text=new TextEntity{LayerId=vm.Document.ActiveLayerId,Text="Linked",LayoutMode=TextLayoutMode.FollowPath,PathPlacement=new(){GuideEntityId=guide.Id,GuideSnapshot=[..guide.Points]},SourceOutlines=[new(){Start=new(0,0),Segments=[new LinePathSegment(new(5,0))]}],Outlines=[new(){Start=new(10,20),Segments=[new LinePathSegment(new(15,20))]}]};vm.Document.Entities.AddRange([guide,text]);var operation=new EngraveOperationDefinition{GeometryIds=[text.Id],Status=OperationStatus.Current};vm.CamJob.Operations.Add(operation);vm.Selection.Set(guide.Id);vm.SelectionChanged();vm.DeleteSelection();Assert.DoesNotContain(guide,vm.Document.Entities);Assert.Null(text.PathPlacement.GuideEntityId);Assert.Equal(TextLayoutMode.Straight,text.LayoutMode);Assert.Equal(OperationStatus.Stale,operation.Status);vm.Undo();Assert.Contains(guide,vm.Document.Entities);Assert.Equal(guide.Id,text.PathPlacement.GuideEntityId);Assert.Equal(TextLayoutMode.FollowPath,text.LayoutMode);Assert.False(vm.CanUndo);
        text.IsLocked=true;vm.Selection.Set(guide.Id);vm.SelectionChanged();Assert.Throws<InvalidOperationException>(vm.DeleteSelection);Assert.Contains(guide,vm.Document.Entities);Assert.Equal(guide.Id,text.PathPlacement.GuideEntityId);text.IsLocked=false;guide.IsLocked=true;vm.Selection.Set(guide.Id);vm.SelectionChanged();Assert.Throws<InvalidOperationException>(vm.DeleteSelection);Assert.Contains(guide,vm.Document.Entities);
    }

    [Fact] public void GroupAndUngroupAreSingleUndoableTransactionsAndRejectLockedSelections()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings());var first=new LineEntity{LayerId=vm.Document.ActiveLayerId,End=new(10,0)};var second=new CircleEntity{LayerId=vm.Document.ActiveLayerId,Center=new(20,20),Radius=5};vm.Document.Entities.AddRange([first,second]);var operation=new EngraveOperationDefinition{GeometryIds=[first.Id],Status=OperationStatus.Current};vm.CamJob.Operations.Add(operation);vm.Selection.Set([first.Id,second.Id]);vm.SelectionChanged();vm.GroupSelection();var group=Assert.IsType<Guid>(first.GroupId);Assert.Equal(group,second.GroupId);Assert.Equal(OperationStatus.Stale,operation.Status);vm.Undo();Assert.Null(first.GroupId);Assert.Null(second.GroupId);Assert.False(vm.CanUndo);vm.Redo();Assert.Equal(group,first.GroupId);vm.Selection.Set([first.Id,second.Id]);vm.SelectionChanged();vm.UngroupSelection();Assert.Null(first.GroupId);Assert.Null(second.GroupId);vm.Undo();Assert.Equal(group,first.GroupId);
        vm.Undo();first.IsLocked=true;vm.Selection.Set([first.Id,second.Id]);vm.SelectionChanged();Assert.Throws<InvalidOperationException>(vm.GroupSelection);Assert.Null(first.GroupId);Assert.Null(second.GroupId);
    }

    [Fact] public void PocketHelixIsExposedOnlyThroughTheContainmentCheckedCalculator()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=120,HeightMm=100,ThicknessMm=12});var boundary=new RectangleEntity{LayerId=vm.Document.ActiveLayerId,Origin=new(10,10),Width=80,Height=60};vm.AddEntity(boundary);Assert.Contains(PocketEntryMode.Helix,vm.PocketEntryModes);var pocket=vm.CreatePocketDraft();pocket.EntryMode=PocketEntryMode.Helix;pocket.RampLengthMm=10;pocket.CutDepthMm=2;pocket.PassDepthMm=2;vm.AddOperation(pocket);Assert.Equal(OperationStatus.Current,pocket.Status);Assert.NotEmpty(pocket.CalculatedToolpath!.Moves.OfType<CncCad.Cam.Toolpaths.ArcMove>());
    }

    [Fact] public void VCarveUnlockRequiresClosedOutlinesAndAValidVBit()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=120,HeightMm=100,ThicknessMm=12});var outline=new RectangleEntity{LayerId=vm.Document.ActiveLayerId,Origin=new(10,10),Width=80,Height=60};vm.AddEntity(outline);Assert.False(vm.CanCreateVCarve);vm.SelectedTool=new(){Name="60 degree V-bit",Type=ToolType.VBit,DiameterMm=8,VBitAngleDegrees=60,TipDiameterMm=.2,FeedRateMmPerMin=800,PlungeRateMmPerMin=200,SpindleRpm=16000,StepDownMm=2};Assert.True(vm.CanCreateVCarve);var carve=vm.CreateVCarveDraft();vm.AddOperation(carve);Assert.Equal(OperationStatus.Current,carve.Status);Assert.NotEmpty(carve.CalculatedToolpath!.Moves);var open=new LineEntity{LayerId=vm.Document.ActiveLayerId,Start=new(0,0),End=new(10,0)};vm.AddEntity(open);Assert.False(vm.CanCreateVCarve);Assert.Contains("closed",vm.VCarveGuidance,StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public void PolylineNodeSelectionInsertionDeletionAndStartChangesAreTruthfulAndUndoable()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=100,HeightMm=100,ThicknessMm=12});var path=new PolylineEntity{LayerId=vm.Document.ActiveLayerId,Transform=Matrix2D.Translation(5,7),Points=[new(0,0),new(20,0),new(20,10),new(0,10)],IsClosed=true};vm.AddEntity(path);vm.SelectNode(path,1,path.Transform.Transform(path.Points[1]));Assert.True(vm.HasActiveNode);Assert.True(vm.CanInsertNode);Assert.True(vm.CanDeleteNode);Assert.True(vm.CanMakeStartNode);vm.InsertNode();Assert.Equal(5,path.Points.Count);Assert.Equal(new Point2D(20,5),path.Points[2]);vm.Undo();Assert.Equal(4,path.Points.Count);
        vm.Selection.Set(path.Id);vm.SelectionChanged();vm.SelectNode(path,2,path.Transform.Transform(path.Points[2]));vm.MakeStartNode();Assert.Equal(new Point2D(20,10),path.Points[0]);vm.Undo();Assert.Equal(new Point2D(0,0),path.Points[0]);vm.Selection.Set(path.Id);vm.SelectionChanged();vm.SelectNode(path,1,path.Transform.Transform(path.Points[1]));vm.DeleteNode();Assert.Equal(3,path.Points.Count);Assert.False(vm.HasActiveNode);Assert.False(vm.CanDeleteNode);vm.Undo();Assert.Equal(4,path.Points.Count);
    }

    [Fact] public void BezierHandlePositionIsEditableWhileUnsupportedTopologyActionsStayDisabled()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings());var path=new VectorPathEntity{LayerId=vm.Document.ActiveLayerId,Figures=[new(){Start=new(0,0),Segments=[new CubicPathSegment(new(5,10),new(15,10),new(20,0))]}]};vm.AddEntity(path);vm.SelectNode(path,1,new(5,10));Assert.True(vm.HasActiveNode);Assert.False(vm.CanInsertNode);Assert.False(vm.CanDeleteNode);Assert.False(vm.CanMakeStartNode);vm.NodeX=6;vm.NodeY=12;vm.ApplyNodePosition();Assert.Equal(new Point2D(6,12),Assert.IsType<CubicPathSegment>(path.Figures[0].Segments[0]).Control1);vm.Undo();Assert.Equal(new Point2D(5,10),Assert.IsType<CubicPathSegment>(path.Figures[0].Segments[0]).Control1);
    }

    [Fact] public void OpenPathFinalAnchorTruthfullyDisablesInsertion()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings());var polyline=new PolylineEntity{LayerId=vm.Document.ActiveLayerId,Points=[new(0,0),new(10,0),new(20,0)]};vm.AddEntity(polyline);vm.SelectNode(polyline,2,new(20,0));Assert.False(vm.CanInsertNode);Assert.True(vm.CanDeleteNode);Assert.Throws<InvalidOperationException>(vm.InsertNode);
        var vector=new VectorPathEntity{LayerId=vm.Document.ActiveLayerId,Figures=[new(){Start=new(0,0),Segments=[new LinePathSegment(new(10,0)),new LinePathSegment(new(20,0))]}]};vm.AddEntity(vector);vm.SelectNode(vector,2,new(20,0));Assert.False(vm.CanInsertNode);Assert.True(vm.CanDeleteNode);Assert.Throws<InvalidOperationException>(vm.InsertNode);
    }

    [Fact] public void LockedGeometryTruthfullyDisablesEveryNodeMutation()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings());var path=new PolylineEntity{LayerId=vm.Document.ActiveLayerId,IsLocked=true,IsClosed=true,Points=[new(0,0),new(20,0),new(20,20),new(0,20)]};vm.AddEntity(path);vm.SelectNode(path,1,new(20,0));Assert.True(vm.HasActiveNode);Assert.False(vm.CanApplyNodePosition);Assert.False(vm.CanInsertNode);Assert.False(vm.CanDeleteNode);Assert.False(vm.CanMakeStartNode);Assert.Throws<InvalidOperationException>(()=>vm.MoveNode(path,1,new(21,0)));Assert.Equal(new Point2D(20,0),path.Points[1]);
    }

    [Fact] public void ExactArcVectorNodesSplitRotateDeleteUndoAndInvalidateDependentCam()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=100,HeightMm=100,ThicknessMm=12});var path=new VectorPathEntity{LayerId=vm.Document.ActiveLayerId,Transform=Matrix2D.Translation(30,30),Figures=[new(){Start=new(10,0),IsClosed=true,Segments=[new ArcPathSegment(new(0,0),90,new(0,10)),new ArcPathSegment(new(0,0),90,new(-10,0)),new ArcPathSegment(new(0,0),90,new(0,-10)),new ArcPathSegment(new(0,0),90,new(10,0))]}]};vm.AddEntity(path);var operation=new ProfileOperationDefinition{GeometryIds=[path.Id],Status=OperationStatus.Current};vm.CamJob.Operations.Add(operation);
        vm.SelectNode(path,0,path.Transform.Transform(path.Figures[0].Start));Assert.True(vm.CanInsertNode);Assert.True(vm.CanDeleteNode);Assert.False(vm.CanMakeStartNode);vm.InsertNode();Assert.Equal(5,path.Figures[0].Segments.Count);Assert.All(path.Figures[0].Segments.Take(2),segment=>Assert.IsType<ArcPathSegment>(segment));Assert.Equal(OperationStatus.Stale,operation.Status);vm.Undo();Assert.Equal(4,path.Figures[0].Segments.Count);
        vm.SelectNode(path,1,path.Transform.Transform(path.Figures[0].Segments[0].End));Assert.True(vm.CanMakeStartNode);vm.MakeStartNode();Assert.Equal(new Point2D(0,10),path.Figures[0].Start);Assert.All(path.Figures[0].Segments,segment=>Assert.IsType<ArcPathSegment>(segment));vm.Undo();Assert.Equal(new Point2D(10,0),path.Figures[0].Start);
        vm.SelectNode(path,1,path.Transform.Transform(path.Figures[0].Segments[0].End));vm.DeleteNode();Assert.Equal(3,path.Figures[0].Segments.Count);Assert.False(vm.HasActiveNode);vm.Undo();Assert.Equal(4,path.Figures[0].Segments.Count);
    }

    [Fact] public void RepairApplyIsEnabledOnlyForCurrentUnambiguousPreviewAndIsUndoable()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings());var path=new PolylineEntity{LayerId=vm.Document.ActiveLayerId,Points=[new(0,0),new(.001,0),new(10,0)]};vm.AddEntity(path);Assert.False(vm.CanApplyRepair);vm.MinimumSegment=.01;vm.AnalyzeRepair();Assert.True(vm.CanApplyRepair);vm.ApplyRepair();Assert.False(vm.CanApplyRepair);Assert.Equal(2,Assert.IsType<PolylineEntity>(Assert.Single(vm.Document.Entities)).Points.Count);vm.Undo();Assert.Equal(3,Assert.IsType<PolylineEntity>(Assert.Single(vm.Document.Entities)).Points.Count);
        vm.Selection.Set(path.Id);vm.SelectionChanged();vm.AnalyzeRepair();Assert.True(vm.CanApplyRepair);vm.NudgeSelection(1,0);Assert.False(vm.CanApplyRepair);Assert.Empty(vm.RepairIssues);
    }

    [Fact] public void SelfIntersectionRepairInstallsStableSimpleContoursAndSupportsUndoRedo()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings());var bowtie=new PolylineEntity{LayerId=vm.Document.ActiveLayerId,GroupId=Guid.NewGuid(),StrokeColor="#2468AC",IsClosed=true,Points=[new(0,0),new(20,20),new(0,20),new(20,0)]};vm.AddEntity(bowtie);var operation=new ProfileOperationDefinition{GeometryIds=[bowtie.Id],Status=OperationStatus.Current};vm.CamJob.Operations.Add(operation);vm.Selection.Set(bowtie.Id);vm.SelectionChanged();vm.RepairTolerance=.001;vm.MinimumSegment=.001;vm.AnalyzeRepair();Assert.True(vm.CanApplyRepair);Assert.Contains(vm.RepairIssues,issue=>issue.Kind==RepairIssueKind.SelfIntersection);vm.ApplyRepair();Assert.Equal(2,vm.Document.Entities.Count);Assert.All(vm.Document.Entities,entity=>{var contour=Assert.IsType<PolylineEntity>(entity);Assert.True(contour.IsClosed);Assert.Equal(bowtie.GroupId,contour.GroupId);Assert.Equal(bowtie.StrokeColor,contour.StrokeColor);});Assert.Equal(OperationStatus.Stale,operation.Status);vm.Undo();Assert.Same(bowtie,Assert.Single(vm.Document.Entities));vm.Redo();Assert.Equal(2,vm.Document.Entities.Count);
    }

    [Fact] public void RouterCornerReliefIsTruthfullyEligibleUndoableAndInvalidatesCam()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=100,HeightMm=100,ThicknessMm=18});var path=new PolylineEntity{LayerId=vm.Document.ActiveLayerId,Points=[new(0,0),new(60,0),new(60,40),new(30,40),new(30,20),new(0,20)],IsClosed=true,GroupId=Guid.NewGuid(),StrokeColor="#12AB34"};vm.AddEntity(path);var operation=new ProfileOperationDefinition{GeometryIds=[path.Id],Status=OperationStatus.Current};vm.CamJob.Operations.Add(operation);vm.CornerIndex=4;vm.CornerReliefToolDiameter=6;Assert.True(vm.CanAddCornerRelief);vm.AddCornerRelief(CornerReliefStyle.Dogbone);var relieved=Assert.IsType<PolylineEntity>(Assert.Single(vm.Document.Entities));Assert.Equal(path.Id,relieved.Id);Assert.Equal(path.GroupId,relieved.GroupId);Assert.Equal(path.StrokeColor,relieved.StrokeColor);Assert.True(relieved.Points.Count>path.Points.Count);Assert.Equal(OperationStatus.Stale,operation.Status);vm.Undo();Assert.Same(path,Assert.Single(vm.Document.Entities));vm.Selection.Set(path.Id);vm.SelectionChanged();vm.CornerIndex=1;Assert.False(vm.CanAddCornerRelief);Assert.Contains("outside corner",vm.CornerReliefGuidance);Assert.Throws<InvalidOperationException>(()=>vm.AddCornerRelief(CornerReliefStyle.TBone));
    }

    [Fact] public void LayerCreationFlagsOrderingAndEntityMovesAreUndoable()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings());var original=vm.Document.ActiveLayerId;var entity=new LineEntity{LayerId=original,End=new(10,0)};vm.AddEntity(entity);vm.AddLayer();var added=vm.Document.ActiveLayerId;Assert.NotEqual(original,added);vm.MoveSelectionToLayer();Assert.Equal(added,entity.LayerId);vm.Undo();Assert.Equal(original,entity.LayerId);vm.ToggleLayerVisibility();Assert.False(vm.SelectedLayer!.IsVisible);vm.Undo();Assert.True(vm.Document.Layers.Single(layer=>layer.Id==added).IsVisible);vm.Undo();Assert.DoesNotContain(vm.Document.Layers,layer=>layer.Id==added);vm.Redo();Assert.Contains(vm.Document.Layers,layer=>layer.Id==added);vm.SelectedLayer=vm.Document.Layers.Single(layer=>layer.Id==added);vm.LayerNameDraft="Cut vectors";vm.LayerColorDraft="#123456";vm.CommitLayerProperties();Assert.Equal("Cut vectors",vm.SelectedLayer.Name);vm.Undo();Assert.NotEqual("Cut vectors",vm.Document.Layers.Single(layer=>layer.Id==added).Name);
    }

    [Fact] public void ActiveLayerAndJobSetupChangesAreSingleUndoableTransactionsThatInvalidateAllDerivedWork()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{Name="Before",WidthMm=100,HeightMm=80,ThicknessMm=12,SafeZMm=4});var originalLayer=vm.Document.ActiveLayerId;vm.AddLayer();var secondLayer=vm.Document.ActiveLayerId;vm.Undo();Assert.Equal(originalLayer,vm.Document.ActiveLayerId);vm.Redo();Assert.Equal(secondLayer,vm.Document.ActiveLayerId);
        vm.SelectedLayer=vm.Document.Layers.Single(layer=>layer.Id==originalLayer);vm.ActivateLayer();Assert.Equal(originalLayer,vm.Document.ActiveLayerId);vm.Undo();Assert.Equal(secondLayer,vm.Document.ActiveLayerId);vm.Redo();Assert.Equal(originalLayer,vm.Document.ActiveLayerId);

        var geometry=new RectangleEntity{LayerId=originalLayer,Origin=new(5,5),Width=20,Height=10};vm.Document.Entities.Add(geometry);var profile=new ProfileOperationDefinition{GeometryIds=[geometry.Id],Status=OperationStatus.Current,CalculatedToolpath=new()};var surface=new SurfaceOperationDefinition{Status=OperationStatus.Current,CalculatedToolpath=new()};vm.CamJob.Operations.AddRange([profile,surface]);vm.NestingJob.AcceptedLayout=new NestingResult();vm.NestingJob.Status=NestingLayoutStatus.Current;
        var changed=new JobSettings{Name="After",WidthMm=200,HeightMm=150,ThicknessMm=18,SafeZMm=7,Origin=XyOrigin.Centre,OriginOffsetXmm=2,OriginOffsetYmm=-3,ZZero=ZZeroPosition.MachineBed};vm.UpdateJob(changed);
        Assert.Equal("After",vm.Document.Job.Name);Assert.Equal("After",vm.JobTitle);Assert.Equal("200 × 150 × 18 mm",vm.MaterialSummary);Assert.All(vm.CamJob.Operations,operation=>Assert.Equal(OperationStatus.Stale,operation.Status));Assert.All(vm.CamJob.Operations,operation=>Assert.Null(operation.CalculatedToolpath));Assert.Equal(200,vm.NestingJob.SheetTemplate.WidthMm);Assert.Null(vm.NestingJob.AcceptedLayout);Assert.Equal(NestingLayoutStatus.Stale,vm.NestingJob.Status);
        vm.Undo();Assert.Equal("Before",vm.Document.Job.Name);Assert.Equal(100,vm.Document.Job.WidthMm);Assert.Equal("Before",vm.JobTitle);Assert.Equal(100,vm.NestingJob.SheetTemplate.WidthMm);Assert.All(vm.CamJob.Operations,operation=>Assert.Equal(OperationStatus.Stale,operation.Status));vm.Redo();Assert.Equal("After",vm.Document.Job.Name);Assert.Equal(200,vm.Document.Job.WidthMm);
    }

    [Fact] public void InvalidOrUnchangedJobSetupCannotMutateTheDocumentOrHistory()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{Name="Safe",WidthMm=100,HeightMm=80,ThicknessMm=12,SafeZMm=5});vm.UpdateJob(new JobSettings{Name="Safe",WidthMm=100,HeightMm=80,ThicknessMm=12,SafeZMm=5});Assert.False(vm.CanUndo);Assert.Throws<ArgumentException>(()=>vm.UpdateJob(new JobSettings{Name="Bad",WidthMm=double.NaN,HeightMm=80,ThicknessMm=12,SafeZMm=5}));Assert.Equal("Safe",vm.Document.Job.Name);Assert.Equal(100,vm.Document.Job.WidthMm);Assert.False(vm.CanUndo);
    }

    [Fact] public void PathTextEditsAndVectorConversionPreserveRigidGlyphGroupsAndMetadata()
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent(),textOutlineService:new PortableFallbackTextOutlineService());vm.CreateJob(new JobSettings{WidthMm=120,HeightMm=100,ThicknessMm=12});var guide=new PolylineEntity{LayerId=vm.Document.ActiveLayerId,Points=[new(10,20),new(70,20),new(70,80)]};vm.Document.Entities.Add(guide);vm.Selection.Set(guide.Id);vm.SelectionChanged();vm.TextValue="AB";vm.TextSize=10;vm.TextPathStart=.5;vm.CreateText();var text=Assert.IsType<TextEntity>(vm.Document.Entities.Single(entity=>entity is TextEntity));Assert.Equal(TextLayoutMode.FollowPath,text.LayoutMode);Assert.Equal(2,text.Glyphs.Count);Assert.All(text.Glyphs,glyph=>Assert.Single(glyph.PlacedFigures));
        vm.TextValue="ABC";vm.UpdateSelectedText();Assert.Equal(3,text.Glyphs.Count);vm.Undo();Assert.Equal("AB",text.Text);Assert.Equal(2,text.Glyphs.Count);text.Transform=Matrix2D.Then(Matrix2D.Rotation(12),Matrix2D.Translation(3,4));text.StrokeColor="#234567";text.MaterialSheetId=Guid.NewGuid();var operation=new EngraveOperationDefinition{GeometryIds=[text.Id],Status=OperationStatus.Current};vm.CamJob.Operations.Add(operation);vm.Selection.Set(text.Id);vm.SelectionChanged();vm.ConvertSelectedTextToVectors();var vectors=vm.Document.Entities.OfType<VectorPathEntity>().ToArray();Assert.Equal(2,vectors.Length);Assert.Single(vectors.Select(vector=>vector.GroupId).Distinct());Assert.All(vectors,vector=>{Assert.Equal(text.Transform,vector.Transform);Assert.Equal(text.StrokeColor,vector.StrokeColor);Assert.Equal(text.MaterialSheetId,vector.MaterialSheetId);Assert.Single(vector.Figures);});Assert.Equal(OperationStatus.Invalid,operation.Status);vm.Undo();Assert.Contains(text,vm.Document.Entities);Assert.DoesNotContain(vm.Document.Entities,entity=>entity is VectorPathEntity);
    }

    [Fact] public void PathTextConvenienceCommandsAreUndoableAndInvalidateDependentCam()
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent(),textOutlineService:new PortableFallbackTextOutlineService());vm.CreateJob(new JobSettings{WidthMm=120,HeightMm=100,ThicknessMm=12});var guide=new PolylineEntity{LayerId=vm.Document.ActiveLayerId,Points=[new(10,20),new(100,20)]};vm.Document.Entities.Add(guide);vm.Selection.Set(guide.Id);vm.SelectionChanged();vm.TextValue="ABC";vm.TextPathStart=.2;vm.TextLetterSpacing=2;vm.CreateText();var text=Assert.IsType<TextEntity>(vm.Document.Entities.Single(entity=>entity is TextEntity));var operation=new EngraveOperationDefinition{GeometryIds=[text.Id],Status=OperationStatus.Current,CalculatedToolpath=new()};vm.CamJob.Operations.Add(operation);
        vm.CenterTextOnPath();Assert.Equal(.5,text.PathPlacement.StartOffset);Assert.Equal(OperationStatus.Stale,operation.Status);vm.Undo();Assert.Equal(.2,text.PathPlacement.StartOffset);
        vm.Selection.Set(text.Id);vm.SelectionChanged();vm.ReverseTextPath();Assert.True(text.PathPlacement.ReverseDirection);vm.Undo();Assert.False(text.PathPlacement.ReverseDirection);
        vm.Selection.Set(text.Id);vm.SelectionChanged();vm.FlipTextPath();Assert.True(text.PathPlacement.FlipSide);vm.Undo();Assert.False(text.PathPlacement.FlipSide);
        vm.Selection.Set(text.Id);vm.SelectionChanged();Assert.Equal(2,vm.TextLetterSpacing);vm.ResetTextSpacing();Assert.Equal(0,text.LetterSpacingMm);Assert.Equal(TextFitMode.Natural,text.PathPlacement.FitMode);vm.Undo();Assert.Equal(2,text.LetterSpacingMm);
    }

    [Fact] public void VectorAppearanceCommandsValidateUndoAndInvalidateDependentCam()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings());var line=new LineEntity{LayerId=vm.Document.ActiveLayerId,Start=new(0,0),End=new(20,0)};vm.AddEntity(line);var operation=new EngraveOperationDefinition{GeometryIds=[line.Id],Status=OperationStatus.Current,CalculatedToolpath=new()};vm.CamJob.Operations.Add(operation);vm.VectorColor="#12ab34";vm.ApplyVectorColor();Assert.Equal("#12AB34",line.StrokeColor);Assert.Equal(OperationStatus.Stale,operation.Status);vm.Undo();Assert.Null(line.StrokeColor);
        vm.Selection.Set(line.Id);vm.SelectionChanged();vm.VectorColor="not-a-colour";Assert.Throws<InvalidOperationException>(vm.ApplyVectorColor);Assert.Null(line.StrokeColor);vm.VectorColor="#ABCDEF";vm.ApplyVectorColor();vm.PickPrimaryColor();Assert.Equal("#ABCDEF",vm.VectorColor);vm.UseLayerColor();Assert.Null(line.StrokeColor);vm.Undo();Assert.Equal("#ABCDEF",line.StrokeColor);
    }

    [Fact] public void SurfaceDraftUsesOneSelectedClosedRegionOrDefaultsToWholeStock()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=200,HeightMm=120,ThicknessMm=18});vm.SelectedTool=new(){Name="Facing cutter",Type=ToolType.SurfacingBit,DiameterMm=20,StepDownMm=1,StepOverPercent=40,FeedRateMmPerMin=1800,PlungeRateMmPerMin=300,SpindleRpm=16000};var whole=vm.CreateSurfaceDraft();Assert.Empty(whole.GeometryIds);Assert.Equal(200,whole.WidthMm);Assert.Equal(120,whole.HeightMm);var region=new PolylineEntity{LayerId=vm.Document.ActiveLayerId,Transform=Matrix2D.Translation(7,9),IsClosed=true,Points=[new(0,0),new(60,0),new(60,20),new(20,20),new(20,50),new(0,50)]};vm.Document.Entities.Add(region);vm.Selection.Set(region.Id);vm.SelectionChanged();var selected=vm.CreateSurfaceDraft();Assert.Equal([region.Id],selected.GeometryIds);Assert.Equal(7,selected.OriginXmm);Assert.Equal(9,selected.OriginYmm);Assert.Equal(60,selected.WidthMm);Assert.Equal(50,selected.HeightMm);var copy=selected.Duplicate();Assert.Equal(selected.GeometryIds,copy.GeometryIds);var open=new LineEntity{LayerId=vm.Document.ActiveLayerId,End=new(10,0)};vm.Document.Entities.Add(open);vm.Selection.Set(open.Id);vm.SelectionChanged();Assert.Throws<InvalidOperationException>(vm.CreateSurfaceDraft);
    }

    [Fact] public void ReacceptingANestReplacesTheInstalledLayoutInsteadOfDuplicatingIt()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=120,HeightMm=120,ThicknessMm=18});
        var source=new RectangleEntity{LayerId=vm.Document.ActiveLayerId,Origin=new(5,5),Width=40,Height=30};vm.AddEntity(source);vm.NestQuantity=2;vm.AddNestPartsFromSelection();
        var first=vm.OptimizeNest();Assert.True(first.Success);Assert.True(vm.CanAcceptNest);vm.AcceptNest();Assert.False(vm.CanAcceptNest);
        var firstIds=vm.Document.Entities.Where(entity=>entity.MaterialSheetId is not null).Select(entity=>entity.Id).ToHashSet();Assert.Equal(2,firstIds.Count);
        var second=vm.OptimizeNest();Assert.True(second.Success);vm.AcceptNest();
        var installed=vm.Document.Entities.Where(entity=>entity.MaterialSheetId is not null).ToArray();var secondIds=installed.Select(entity=>entity.Id).ToHashSet();Assert.Equal(2,installed.Length);Assert.DoesNotContain(installed,entity=>firstIds.Contains(entity.Id));Assert.Single(vm.Document.Layers,layer=>layer.Name.StartsWith("Sheet ",StringComparison.Ordinal));
        vm.Undo();Assert.Equal(firstIds,vm.Document.Entities.Where(entity=>entity.MaterialSheetId is not null).Select(entity=>entity.Id).ToHashSet());Assert.Equal(CncCad.Cam.Nesting.NestingLayoutStatus.Current,vm.NestingJob.Status);
        vm.Redo();Assert.Equal(secondIds,vm.Document.Entities.Where(entity=>entity.MaterialSheetId is not null).Select(entity=>entity.Id).ToHashSet());Assert.Equal(CncCad.Cam.Nesting.NestingLayoutStatus.Current,vm.NestingJob.Status);
    }

    [Fact] public void NestPartDefinitionEditsAreUndoableAndCannotOrphanAnAcceptedLayout()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=120,HeightMm=120,ThicknessMm=18});var source=new RectangleEntity{LayerId=vm.Document.ActiveLayerId,Origin=new(5,5),Width=40,Height=30};vm.AddEntity(source);vm.NestQuantity=3;vm.AddNestPartsFromSelection();var part=Assert.Single(vm.NestingJob.Parts);Assert.Equal(3,part.Quantity);
        vm.Undo();Assert.Empty(vm.NestingJob.Parts);vm.Redo();Assert.Same(part,Assert.Single(vm.NestingJob.Parts));vm.SelectedNestPart=part;vm.RemoveSelectedNestPart();Assert.Empty(vm.NestingJob.Parts);vm.Undo();Assert.Same(part,Assert.Single(vm.NestingJob.Parts));
        var result=vm.OptimizeNest();Assert.True(result.Success);vm.AcceptNest();Assert.Contains(vm.Document.Entities,entity=>entity.MaterialSheetId is not null);var entityCount=vm.Document.Entities.Count;vm.Selection.Set(source.Id);vm.SelectionChanged();var error=Assert.Throws<InvalidOperationException>(vm.AddNestPartsFromSelection);Assert.Contains("Undo the accepted nest",error.Message);Assert.Equal(entityCount,vm.Document.Entities.Count);Assert.NotNull(vm.NestingJob.AcceptedLayout);
    }

    [Fact] public void FocusAndViewChoices_AreExplicitState()
    {
        var vm = ViewModel(); Assert.Equal(WorkspaceFocus.Design, vm.WorkspaceFocus); vm.SetWorkspaceFocus(WorkspaceFocus.Toolpaths); vm.SetWorkspaceView(WorkspaceView.ToolpathPreview);
        Assert.Equal(WorkspaceFocus.Toolpaths, vm.WorkspaceFocus); Assert.Equal(WorkspaceView.ToolpathPreview, vm.ActiveWorkspaceView);
    }
    [Fact] public void ModeAndFourViewportLayouts_AreExplicitState()
    {
        var vm=ViewModel();Assert.Equal(WorkspaceMode.Startup,vm.WorkspaceMode);vm.CreateJob(new JobSettings());Assert.Equal(WorkspaceMode.Design,vm.WorkspaceMode);vm.SetWorkspaceMode(WorkspaceMode.Toolpaths);Assert.Equal(WorkspaceFocus.Toolpaths,vm.WorkspaceFocus);foreach(var layout in Enum.GetValues<ViewportLayout>()){vm.SetViewportLayout(layout);Assert.Equal(layout,vm.ViewportLayout);Assert.Equal(layout!=ViewportLayout.Design2D,vm.IsToolpathPreview);}
    }
    [Theory]
    [InlineData(1100, ResponsiveLayoutState.Compact)]
    [InlineData(1280, ResponsiveLayoutState.Compact)]
    [InlineData(1359, ResponsiveLayoutState.Compact)]
    [InlineData(1360, ResponsiveLayoutState.Regular)]
    [InlineData(1920, ResponsiveLayoutState.Regular)]
    public void ResponsiveLayout_UsesTheDocumentedBreakpoint(double width,ResponsiveLayoutState expected)
    {
        var vm=ViewModel();vm.SetResponsiveLayout(width);Assert.Equal(expected,vm.ResponsiveLayout);Assert.Equal(expected,vm.Shell.ResponsiveLayout);
    }
    [Theory]
    [InlineData(1100, WorkspaceMode.Design, 884)]
    [InlineData(1100, WorkspaceMode.Toolpaths, 796)]
    [InlineData(1280, WorkspaceMode.Toolpaths, 976)]
    [InlineData(1440, WorkspaceMode.Design, 1224)]
    [InlineData(1920, WorkspaceMode.Design, 1704)]
    public void ShellWidths_KeepTheCanvasAboveItsMinimum(double width,WorkspaceMode mode,double expected)
    {
        var actual=WorkspaceLayoutMetrics.CanvasWidth(width,mode);Assert.Equal(expected,actual);Assert.True(actual>=WorkspaceLayoutMetrics.MinimumCanvasWidth);
    }
    [Fact] public void FocusedTaskPages_ReturnToTheirCompactPalettes()
    {
        var vm=ViewModel();vm.OpenDesignTask(DesignTaskPage.Layers);Assert.True(vm.IsDesignLayersTask);Assert.False(vm.IsDesignPalette);vm.CloseDesignTask();Assert.True(vm.IsDesignPalette);
        vm.OpenToolpathTask(ToolpathTaskPage.ToolLibrary);Assert.True(vm.IsToolLibraryTask);Assert.False(vm.IsToolpathPalette);vm.CloseToolpathTask();Assert.True(vm.IsToolpathPalette);
    }
    [Fact] public void InlineToolpathEditor_AppliesAndCancelsWithoutMutatingTarget()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=200,HeightMm=100,ThicknessMm=12});var rectangle=new RectangleEntity{LayerId=vm.Document.ActiveLayerId,Width=80,Height=40};vm.AddEntity(rectangle);var draft=vm.CreateProfileDraft();vm.BeginOperationEditor(draft,true);Assert.True(vm.IsOperationEditorOpen);vm.ApplyOperationEditor();var saved=Assert.IsType<ProfileOperationDefinition>(Assert.Single(vm.Operations));vm.BeginOperationEditor(saved,false);Assert.NotSame(saved,vm.OperationEditorDraft);((ProfileOperationDefinition)vm.OperationEditorDraft!).CutDepthMm=5;vm.CancelOperationEditor();Assert.Equal(12,saved.CutDepthMm);
    }
    [Fact] public void Switching_To_Design_Hides_Playback_Ui_But_Preserves_The_Session()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=100,HeightMm=100,ThicknessMm=18});var shape=new RectangleEntity{LayerId=vm.Document.ActiveLayerId,Width=50,Height=50};vm.AddEntity(shape);vm.AddOperation(vm.CreateProfileDraft());vm.LoadAllPlayback();vm.SetWorkspaceView(WorkspaceView.ToolpathPreview);
        var segmentCount=vm.Playback.Frame.Segments.Count;Assert.True(vm.ShowPlaybackControls);vm.SetWorkspaceView(WorkspaceView.Design);Assert.False(vm.ShowPlaybackControls);Assert.Equal(segmentCount,vm.Playback.Frame.Segments.Count);Assert.StartsWith("2",vm.WorkflowStage);vm.SetWorkspaceView(WorkspaceView.ToolpathPreview);Assert.True(vm.ShowPlaybackControls);Assert.Equal(segmentCount,vm.Playback.Frame.Segments.Count);
    }
    [Fact] public void PlaybackSelectedAndAll_UseTheRequestedOperationsInVisibleOrder()
    {
        var vm = ViewModel(); vm.CreateJob(new JobSettings { WidthMm = 600, HeightMm = 400, ThicknessMm = 18 });
        var firstShape = new RectangleEntity { LayerId=vm.Document.ActiveLayerId, Width=100, Height=100 }; vm.AddEntity(firstShape); var first=vm.CreateProfileDraft(); first.Name="First"; vm.AddOperation(first);
        var secondShape = new CircleEntity { LayerId=vm.Document.ActiveLayerId, Center=new(200,100), Radius=30 }; vm.AddEntity(secondShape); var second=vm.CreateProfileDraft(); second.Name="Second"; vm.AddOperation(second);
        vm.SelectedOperation=second; vm.LoadSelectedPlayback(); Assert.All(vm.Playback.Frame.Segments, segment => Assert.Equal(second.Id, segment.OperationId));
        first.IsEnabled=false; vm.LoadAllPlayback(); Assert.All(vm.Playback.Frame.Segments, segment => Assert.Equal(second.Id, segment.OperationId)); Assert.StartsWith("All enabled", vm.PlaybackSessionLabel);
    }
    [Fact] public async Task Saved_Job_Reopens_With_Its_Toolpaths_Ready()
    {
        var path=Path.Combine(Path.GetTempPath(),$"cnccad-{Guid.NewGuid():N}.cnccad");
        try
        {
            var source=ViewModel();source.CreateJob(new JobSettings{Name="Persistent CAM",WidthMm=600,HeightMm=400,ThicknessMm=18});var rectangle=new RectangleEntity{LayerId=source.Document.ActiveLayerId,Width=100,Height=60};source.AddEntity(rectangle);var operation=source.CreateProfileDraft();operation.Name="Saved outline";source.AddOperation(operation);await source.SaveProjectAsync(path);
            var reopened=ViewModel();await reopened.OpenProjectAsync(path);var saved=Assert.Single(reopened.Operations);Assert.Equal("Saved outline",saved.Name);Assert.Equal(rectangle.Id,Assert.Single(saved.GeometryIds));Assert.Equal(OperationStatus.Current,saved.Status);Assert.NotNull(saved.CalculatedToolpath);Assert.True(reopened.HasOperations);
        }
        finally{if(File.Exists(path))File.Delete(path);}
    }
    [Fact] public async Task Save_Normalizes_Project_Extension_And_Remains_Successful_When_Recent_List_Fails()
    {
        var basePath=Path.Combine(Path.GetTempPath(),$"cnccad-{Guid.NewGuid():N}.wrong");var expected=Path.ChangeExtension(basePath,".cnccad");
        try
        {
            var vm=new MainWindowViewModel(new MemoryTools(),new FailingRecent());vm.CreateJob(new JobSettings{Name="Reliable save"});await vm.SaveProjectAsync(basePath);
            Assert.Equal(Path.GetFullPath(expected),vm.ProjectPath);Assert.True(File.Exists(expected));Assert.False(vm.IsDirty);Assert.Contains("recent projects",vm.Message,StringComparison.OrdinalIgnoreCase);
        }
        finally{if(File.Exists(expected))File.Delete(expected);}
    }
    [Theory]
    [InlineData("sample.cnccad", "sample.cnccad")]
    [InlineData("sample.cnccad.cnccad", "sample.cnccad")]
    [InlineData("sample.CNCCAD.CNCCAD", "sample.cnccad")]
    [InlineData("sample.wrong", "sample.cnccad")]
    public void Project_Path_Normalization_Produces_Exactly_One_Extension(string input,string expected)
    {
        var directory=Path.Combine(Path.GetTempPath(),"cnccad-extension-tests");
        var actual=MainWindowViewModel.NormalizeProjectPath(Path.Combine(directory,input));
        Assert.Equal(Path.Combine(directory,expected),actual);
    }

    [Theory]
    [InlineData("program.nc.nc", ".nc", "program.nc")]
    [InlineData("program.txt.txt", ".txt", "program.txt")]
    public void Export_Path_Normalization_Produces_Exactly_One_Extension(string input,string extension,string expected)
    {
        var directory=Path.Combine(Path.GetTempPath(),"cnccad-extension-tests");
        var actual=GCodeExportFormat.NormalizePath(Path.Combine(directory,input),extension);
        Assert.Equal(Path.Combine(directory,expected),actual);
    }
    [Fact] public async Task Failed_Open_Does_Not_Clear_The_Current_Playback()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=100,HeightMm=100,ThicknessMm=18});var shape=new RectangleEntity{LayerId=vm.Document.ActiveLayerId,Width=50,Height=50};vm.AddEntity(shape);vm.AddOperation(vm.CreateProfileDraft());vm.LoadAllPlayback();var segmentCount=vm.Playback.Frame.Segments.Count;
        var missing=Path.Combine(Path.GetTempPath(),$"missing-{Guid.NewGuid():N}.cnccad");await Assert.ThrowsAsync<FileNotFoundException>(()=>vm.OpenProjectAsync(missing));Assert.Equal(segmentCount,vm.Playback.Frame.Segments.Count);
    }

    [Fact] public async Task ModernDoor_ReopensAndBlocksCombinedMultiToolOutputEvenForAutomaticRouter()
    {
        var path=Path.Combine(Path.GetTempPath(),$"modern-door-{Guid.NewGuid():N}.cnccad");
        try
        {
            await using(var stream=File.Create(path))await new JsonProjectSerializer().SaveAsync(ModernDoorAcceptanceProject.Create(),stream);
            var vm=ViewModel();await vm.OpenProjectAsync(path);vm.SelectedOperation=vm.Operations[0];vm.CamJob.Machine=vm.CamJob.Machine! with{ToolChangeMode=MachineToolChangeMode.Automatic};
            var selected=vm.GenerateExportPrograms(ToolpathExportScope.SelectedToolpath);var blocked=Assert.Throws<InvalidOperationException>(()=>vm.GenerateExportPrograms(ToolpathExportScope.VisibleCombined));var separate=vm.GenerateExportPrograms(ToolpathExportScope.VisibleSeparateFiles);
            Assert.Contains("separate files",blocked.Message,StringComparison.OrdinalIgnoreCase);Assert.Single(selected);Assert.Equal(5,separate.Count);Assert.All([..selected,..separate],file=>{Assert.Equal(".tap",file.Extension);Assert.Contains("M30",file.Text);});
        }
        finally{if(File.Exists(path))File.Delete(path);}
    }

    [Fact] public async Task LibraryCrudSearchAndPresetSnapshotsAreCompleteAndReferentiallySafe()
    {
        var libraries=new MemoryLibraries();var vm=new MainWindowViewModel(libraries,new MemoryRecent(),libraries,materialLibrary:libraries,presetRepository:libraries);await vm.LoadAsync();await vm.AddToolAsync();vm.SelectedTool!.Name="6 mm finishing";vm.SelectedTool.FeedRateMmPerMin=1800;vm.SelectedTool.PlungeRateMmPerMin=450;vm.SelectedTool.SpindleRpm=17000;vm.SelectedTool.StepDownMm=3;vm.SelectedTool.StepOverPercent=40;await vm.SaveSelectedToolAsync();vm.AddMachineProfile();vm.SelectedMachine!.Name="Shop Router";await vm.SaveSelectedMachineAsync();await vm.AddMaterialAsync();vm.SelectedMaterial!.Name="Birch plywood";vm.SelectedMaterial.Category="Sheet goods";await vm.SaveSelectedMaterialAsync();await vm.AddPresetAsync();vm.SelectedToolPreset!.FeedRateMmPerMin=2345;vm.SelectedToolPreset.PlungeRateMmPerMin=345;vm.SelectedToolPreset.SpindleRpm=17600;vm.SelectedToolPreset.PassDepthMm=2.25;vm.SelectedToolPreset.StepOverPercent=31;await vm.SaveSelectedPresetAsync();
        vm.ToolSearch="finishing";Assert.Single(vm.FilteredTools);vm.MachineSearch="shop";Assert.Single(vm.FilteredMachines);vm.MaterialSearch="plywood";Assert.Single(vm.FilteredMaterials);vm.PresetSearch="birch";Assert.Single(vm.FilteredToolPresets);vm.CreateJob(new JobSettings{WidthMm=100,HeightMm=80,ThicknessMm=12});var rectangle=new RectangleEntity{LayerId=vm.Document.ActiveLayerId,Width=60,Height=40};vm.AddEntity(rectangle);var pocket=vm.CreatePocketDraft();Assert.Equal(2345,pocket.Tool.FeedRateMmPerMin);Assert.Equal(345,pocket.Tool.PlungeRateMmPerMin);Assert.Equal(17600,pocket.Tool.SpindleRpm);Assert.Equal(2.25,pocket.PassDepthMm);Assert.Equal(1.86,pocket.StepOverMm,6);vm.SelectedToolPreset.FeedRateMmPerMin=999;Assert.Equal(2345,pocket.Tool.FeedRateMmPerMin);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>vm.DeleteSelectedToolAsync());await Assert.ThrowsAsync<InvalidOperationException>(()=>vm.DeleteSelectedMachineAsync());await Assert.ThrowsAsync<InvalidOperationException>(()=>vm.DeleteSelectedMaterialAsync());await vm.DeleteSelectedPresetAsync();await vm.DeleteSelectedToolAsync();await vm.DeleteSelectedMachineAsync();await vm.DeleteSelectedMaterialAsync();Assert.Empty(libraries.Tools);Assert.Empty(libraries.Machines);Assert.Empty(libraries.Materials);Assert.Empty(libraries.Presets);
    }

    private static MainWindowViewModel ViewModel()
    {
        var vm = new MainWindowViewModel(new MemoryTools(), new MemoryRecent()); vm.SelectedTool = new CncTool { Name = "6mm End Mill", DiameterMm = 6, StepDownMm = 3, CuttingLengthMm=30, OverallLengthMm=50, ShankDiameterMm=6, StickOutMm=35 };vm.Machines.Add(new MachineProfileDefinition());vm.SelectedMachine=vm.Machines[0]; return vm;
    }
    private sealed class MemoryTools : IToolLibrary
    {
        public Task<IReadOnlyList<CncTool>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CncTool>>([]);
        public Task SaveAsync(CncTool tool, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class MemoryRecent : IRecentProjectStore
    {
        public Task<IReadOnlyList<RecentProject>> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RecentProject>>([]);
        public Task AddAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class MemoryLibraries:IToolLibrary,IMachineProfileRepository,IMaterialLibrary,IToolCuttingPresetRepository
    {
        public Dictionary<Guid,CncTool> Tools{get;}=[];public Dictionary<Guid,MachineProfileDefinition> Machines{get;}=[];public Dictionary<Guid,MaterialDefinition> Materials{get;}=[];public Dictionary<Guid,ToolCuttingPreset> Presets{get;}=[];
        Task<IReadOnlyList<CncTool>> IToolLibrary.GetAllAsync(CancellationToken cancellationToken)=>Task.FromResult<IReadOnlyList<CncTool>>(Tools.Values.ToArray());Task IToolLibrary.SaveAsync(CncTool value,CancellationToken cancellationToken){Tools[value.Id]=value;return Task.CompletedTask;}Task IToolLibrary.DeleteAsync(Guid id,CancellationToken cancellationToken){Tools.Remove(id);return Task.CompletedTask;}
        Task<IReadOnlyList<MachineProfileDefinition>> IMachineProfileRepository.GetAllAsync(CancellationToken cancellationToken)=>Task.FromResult<IReadOnlyList<MachineProfileDefinition>>(Machines.Values.ToArray());Task IMachineProfileRepository.SaveAsync(MachineProfileDefinition value,CancellationToken cancellationToken){Machines[value.Id]=value;return Task.CompletedTask;}Task IMachineProfileRepository.DeleteAsync(Guid id,CancellationToken cancellationToken){Machines.Remove(id);return Task.CompletedTask;}
        Task<IReadOnlyList<MaterialDefinition>> IMaterialLibrary.GetAllAsync(CancellationToken cancellationToken)=>Task.FromResult<IReadOnlyList<MaterialDefinition>>(Materials.Values.ToArray());Task IMaterialLibrary.SaveAsync(MaterialDefinition value,CancellationToken cancellationToken){Materials[value.Id]=value;return Task.CompletedTask;}Task IMaterialLibrary.DeleteAsync(Guid id,CancellationToken cancellationToken){Materials.Remove(id);return Task.CompletedTask;}
        Task<IReadOnlyList<ToolCuttingPreset>> IToolCuttingPresetRepository.GetAllAsync(CancellationToken cancellationToken)=>Task.FromResult<IReadOnlyList<ToolCuttingPreset>>(Presets.Values.ToArray());Task IToolCuttingPresetRepository.SaveAsync(ToolCuttingPreset value,CancellationToken cancellationToken){Presets[value.Id]=value;return Task.CompletedTask;}Task IToolCuttingPresetRepository.DeleteAsync(Guid id,CancellationToken cancellationToken){Presets.Remove(id);return Task.CompletedTask;}
    }
    private sealed class FailingRecent : IRecentProjectStore
    {
        public Task<IReadOnlyList<RecentProject>> GetAsync(CancellationToken cancellationToken = default)=>throw new IOException("Settings unavailable");
        public Task AddAsync(string path,CancellationToken cancellationToken=default)=>throw new IOException("Settings unavailable");
        public Task RemoveAsync(string path,CancellationToken cancellationToken=default)=>Task.CompletedTask;
    }
}
