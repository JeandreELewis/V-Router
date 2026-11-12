using CncCad.Cam.Operations;
using CncCad.Cam.Profile;
using CncCad.Cam.Safety;
using CncCad.Cam.Tools;
using CncCad.Core.Documents;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;
using CncCad.Core.Preview;

namespace CncCad.IO.Projects;

/// <summary>A deterministic, production-shaped acceptance job that exercises all five 2.5D operation types.</summary>
public static class ModernDoorAcceptanceProject
{
    public static CncProject Create()
    {
        var boundaryLayer=Layer("Door Boundary",0,"#8DD5F2");
        var panelLayer=Layer("Recessed Panel",1,"#70E08D");
        var fluteLayer=Layer("Flutes",2,"#E5C56B");
        var hardwareLayer=Layer("Hardware",3,"#FF8C42");
        var letteringLayer=Layer("Lettering",4,"#B98CFF");
        var qaLayer=Layer("QA Geometry",5,"#7D8B94",false,true);

        var boundary=RoundedRectangle(boundaryLayer.Id,40,40,920,2220,24);
        var panel=RoundedRectangle(panelLayer.Id,430,300,470,1400,36);
        var handleIsland=RoundedRectangle(panelLayer.Id,750,500,110,1000,28);
        var flutes=Enumerable.Range(0,11).Select(i=>(CadEntity)new LineEntity
        {
            LayerId=fluteLayer.Id,Start=new(110+i*25,260),End=new(110+i*25,2040)
        }).ToList();
        var handleGroup=Guid.Parse("8b89ba91-8f67-4e02-bf0a-84cfd607c40c");
        var lowerBore=new DrillPointEntity{LayerId=hardwareLayer.Id,GroupId=handleGroup,Position=new(805,650)};
        var upperBore=new DrillPointEntity{LayerId=hardwareLayer.Id,GroupId=handleGroup,Position=new(805,1350)};
        var peephole=new CircleEntity{LayerId=hardwareLayer.Id,Center=new(805,1810),Radius=18};
        var accentArc=new ArcEntity{LayerId=hardwareLayer.Id,Center=new(805,1810),Radius=42,StartAngleDegrees=200,SweepAngleDegrees=140};
        var number=SevenSegmentText(letteringLayer.Id,"23",605,1930,105,18);
        var maker=MakerMark(letteringLayer.Id,90,115,42);

        var document=new CadDocument
        {
            Job=new JobSettings{Name="Test Door",WidthMm=1000,HeightMm=2300,ThicknessMm=50,Origin=XyOrigin.Centre,ZZero=ZZeroPosition.MaterialSurface,SafeZMm=8},
            Layers=[boundaryLayer,panelLayer,fluteLayer,hardwareLayer,letteringLayer,qaLayer],
            ActiveLayerId=boundaryLayer.Id,
            Entities=[boundary,panel,handleIsland,..flutes,lowerBore,upperBore,peephole,accentArc,number,maker]
        };

        var six=Tool("6mm End Mill",2,ToolType.EndMill,6,24,30,1800,600,18000,3);
        var twelve=Tool("12mm End Mill",4,ToolType.EndMill,12,48,60,2400,600,18000,5);
        var engraving=Tool("3.175mm Engraving Bit",6,ToolType.EngravingBit,3.175,12.7,20,1000,300,18000,1);
        var vbit=Tool("60 Degree V-Bit",7,ToolType.VBit,12,48,60,1200,350,18000,2,60,.2);
        var drill=Tool("5mm Drill",9,ToolType.Drill,5,20,25,500,300,18000,3);

        var profile=new ProfileOperationDefinition
        {
            Name="Shallow Perimeter Profile",GeometryIds=[boundary.Id,peephole.Id],Tool=six,Order=0,Side=ProfileSide.OnVector,Direction=CutDirection.Climb,
            StartDepthMm=0,CutDepthMm=3,PassDepthMm=1.5,AllowanceMm=0,StartPointFraction=.125,LeadInLengthMm=8,LeadOutLengthMm=5,RampLengthMm=0,LastPassDepthMm=.5,
            SafeZMm=8,FeedRateMmPerMin=1800,PlungeRateMmPerMin=500,SpindleRpm=18000,
            Tabs=new(){Enabled=false,AutomaticCount=4,WidthMm=10,RemainingHeightMm=1.5,RampLengthMm=5}
        };
        var pocket=new PocketOperationDefinition
        {
            Name="Asymmetric Recessed Panel",GeometryIds=[panel.Id,handleIsland.Id],Tool=twelve,Order=1,StartDepthMm=0,CutDepthMm=5,PassDepthMm=2.5,StepOverMm=4.8,
            AllowanceMm=.2,Direction=CutDirection.Climb,EntryMode=PocketEntryMode.LinearRamp,RampLengthMm=40,SafeZMm=8,FeedRateMmPerMin=2200,PlungeRateMmPerMin=550,SpindleRpm=18000
        };
        var drillOperation=new DrillOperationDefinition
        {
            Name="Pull Handle Pilot Bores",GeometryIds=[lowerBore.Id,upperBore.Id],Tool=drill,Order=2,Cycle=DrillCycle.Peck,Ordering=DrillOrdering.Optimized,
            CutDepthMm=18,PeckDepthMm=4.5,SafeZMm=8,RetractZMm=2,DwellSeconds=.2,PlungeRateMmPerMin=280,SpindleRpm=15000
        };
        var engrave=new EngraveOperationDefinition
        {
            Name="Flutes and VR Maker Mark",GeometryIds=[..flutes.Select(x=>x.Id),accentArc.Id,maker.Id],Tool=engraving,Order=3,Direction=EngraveDirection.Optimize,
            StartDepthMm=0,CutDepthMm=2,PassDepthMm=1,SafeZMm=8,FeedRateMmPerMin=1000,PlungeRateMmPerMin=280,SpindleRpm=18000
        };
        var carve=new VCarveOperationDefinition
        {
            Name="House Number 23",GeometryIds=[number.Id],Tool=vbit,Order=4,MaximumDepthMm=3,ClearFlatRegions=true,OffsetStepOverMm=.8,
            SafeZMm=8,FeedRateMmPerMin=1100,PlungeRateMmPerMin=320,SpindleRpm=18000
        };

        var machine=new MachineProfileDefinition
        {
            Name="1200 × 1200 CNC Router",MinimumXmm=-1200,MaximumXmm=1200,MinimumYmm=-1200,MaximumYmm=1200,MinimumZmm=-150,MaximumZmm=50,
            RapidRateMmPerMin=5000,MaximumSpindleRpm=24000,DefaultClearanceMm=8,HolderDiameterMm=35,HolderLengthMm=45,
            ParkXmm=0,ParkYmm=0,AllowedSpoilboardDepthMm=.5,PostProcessorId="uccnc-mm"
        };
        return new CncProject
        {
            Document=document,
            CamJob=new CamJob{Machine=machine.Snapshot(),Material=new(){Name="Hardwood",Category="Solid wood"},Operations=[profile,pocket,drillOperation,engrave,carve],SafetySetupIncomplete=false},
            PreviewSettings=ProjectPreviewSettings.NewWoodJobDefault
        };
    }

    private static CadLayer Layer(string name,int order,string color,bool visible=true,bool locked=false)=>new(){Name=name,Order=order,Color=color,IsVisible=visible,IsLocked=locked};

    private static ToolSnapshot Tool(string name,int number,ToolType type,double diameter,double cuttingLength,double stickOut,double feed,double plunge,int rpm,double stepDown,double? angle=null,double? tip=null)=>new()
    {
        SourceToolId=StableGuid($"{number}:{name}"),Name=name,ToolNumber=number,Type=type,DiameterMm=diameter,CuttingLengthMm=cuttingLength,OverallLengthMm=stickOut+20,
        ShankDiameterMm=diameter,StickOutMm=stickOut,FeedRateMmPerMin=feed,PlungeRateMmPerMin=plunge,SpindleRpm=rpm,StepDownMm=stepDown,VBitAngleDegrees=angle,TipDiameterMm=tip
    };

    private static Guid StableGuid(string value)
    {
        var bytes=System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(value));return new Guid(bytes);
    }

    private static PolylineEntity RoundedRectangle(Guid layer,double x,double y,double width,double height,double radius)
    {
        radius=Math.Clamp(radius,0,Math.Min(width,height)/2);var points=new List<Point2D>();
        AddQuarter(points,x+width-radius,y+height-radius,radius,0);AddQuarter(points,x+radius,y+height-radius,radius,90);
        AddQuarter(points,x+radius,y+radius,radius,180);AddQuarter(points,x+width-radius,y+radius,radius,270);
        return new(){LayerId=layer,Points=points,IsClosed=true};
    }

    private static void AddQuarter(List<Point2D> points,double cx,double cy,double radius,double startDegrees)
    {
        for(var i=0;i<=4;i++){var angle=(startDegrees+i*22.5)*Math.PI/180;points.Add(new(cx+radius*Math.Cos(angle),cy+radius*Math.Sin(angle)));}
    }

    private static TextEntity SevenSegmentText(Guid layer,string value,double x,double y,double height,double stroke)
    {
        var figures=new List<VectorPathFigure>();var width=height*.58;var advance=width+stroke*1.4;
        for(var index=0;index<value.Length;index++)
        {
            var ox=x+index*advance;foreach(var segment in Segments(value[index]))
            {
                var (sx,sy,sw,sh)=segment switch
                {
                    'a'=>(ox+stroke,y+height-stroke,width-2*stroke,stroke),'b'=>(ox+width-stroke,y+height/2,stroke,height/2-stroke),
                    'c'=>(ox+width-stroke,y+stroke,stroke,height/2-stroke),'d'=>(ox+stroke,y,width-2*stroke,stroke),
                    'e'=>(ox,y+stroke,stroke,height/2-stroke),'f'=>(ox,y+height/2,stroke,height/2-stroke),
                    _=>(ox+stroke,y+height/2-stroke/2,width-2*stroke,stroke)
                };figures.Add(RectangleFigure(sx,sy,sw,sh));
            }
        }
        return new(){LayerId=layer,Text=value,FontFamily="V-Router Seven Segment",FontStyle="Regular",SizeMm=height,Alignment=TextAlignment.Left,FontKind=FontKind.Stencil,SourceOutlines=VectorPathGeometry.Clone(figures),Outlines=figures,GlyphAdvancesMm=[..value.Select(_=>advance)]};
    }

    private static string Segments(char value)=>value switch{'2'=>"abged",'3'=>"abgcd",_=>"abcdefg"};
    private static VectorPathFigure RectangleFigure(double x,double y,double width,double height)=>new(){Start=new(x,y),IsClosed=true,Segments=[new LinePathSegment(new(x+width,y)),new LinePathSegment(new(x+width,y+height)),new LinePathSegment(new(x,y+height))]};

    private static TextEntity MakerMark(Guid layer,double x,double y,double height)
    {
        var width=height*.65;var gap=height*.2;
        var figures=new List<VectorPathFigure>
        {
            new(){Start=new(x,y+height),Segments=[new LinePathSegment(new(x+width/2,y)),new LinePathSegment(new(x+width,y+height))]},
            new(){Start=new(x+width+gap,y),Segments=[new LinePathSegment(new(x+width+gap,y+height))]},
            new(){Start=new(x+width+gap,y+height),Segments=[new CubicPathSegment(new(x+width*2.1+gap,y+height),new(x+width*2.1+gap,y+height/2),new(x+width+gap,y+height/2))]},
            new(){Start=new(x+width+gap,y+height/2),Segments=[new LinePathSegment(new(x+width*2+gap,y))]}
        };
        return new(){LayerId=layer,Text="VR",FontFamily="V-Router Single Line",FontKind=FontKind.SingleLine,SizeMm=height,SourceOutlines=VectorPathGeometry.Clone(figures),Outlines=figures,GlyphAdvancesMm=[width+gap,width]};
    }
}
