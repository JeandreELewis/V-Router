using CncCad.Cam.Operations;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Toolpaths;

public enum MotionEventKind { Rapid, Linear, Arc, Dwell, ToolChange, SpindleStart, SpindleStop }
public sealed record MotionEvaluationSettings(double RapidRateMmPerMin,double ToolChangeSeconds=0);
public sealed record MotionTraceEvent(Guid OperationId,string OperationName,int MoveIndex,MotionEventKind Kind,Point2D Start,double StartZ,Point2D End,double EndZ,TimeSpan StartTime,TimeSpan Duration,ToolpathMove Source,Point2D? ArcCenter=null,bool Clockwise=false)
{
    public TimeSpan EndTime=>StartTime+Duration;
    public double PathLengthMm=>Kind switch
    {
        MotionEventKind.Arc when ArcCenter is{} center=>ArcLength(Start,StartZ,End,EndZ,center,Clockwise),
        MotionEventKind.Rapid or MotionEventKind.Linear=>Distance3D(Start,StartZ,End,EndZ),
        _=>0
    };
    public Point2D PointAt(double progress)
    {
        progress=Math.Clamp(progress,0,1);
        if(Kind!=MotionEventKind.Arc||ArcCenter is not{} center)return new(Start.X+(End.X-Start.X)*progress,Start.Y+(End.Y-Start.Y)*progress);
        var startAngle=Math.Atan2(Start.Y-center.Y,Start.X-center.X);var sweep=ArcSweep(Start,End,center,Clockwise);var angle=startAngle+sweep*progress;var radius=Start.DistanceTo(center);return new(center.X+radius*Math.Cos(angle),center.Y+radius*Math.Sin(angle));
    }
    public double ZAt(double progress)=>StartZ+(EndZ-StartZ)*Math.Clamp(progress,0,1);
    internal static double ArcSweep(Point2D start,Point2D end,Point2D center,bool clockwise){var a=Math.Atan2(start.Y-center.Y,start.X-center.X);var b=Math.Atan2(end.Y-center.Y,end.X-center.X);var sweep=b-a;if(clockwise&&sweep>=0)sweep-=Math.PI*2;else if(!clockwise&&sweep<=0)sweep+=Math.PI*2;return sweep;}
    private static double ArcLength(Point2D start,double startZ,Point2D end,double endZ,Point2D center,bool clockwise){var planar=Math.Abs(ArcSweep(start,end,center,clockwise))*start.DistanceTo(center);return Math.Sqrt(planar*planar+Math.Pow(endZ-startZ,2));}
    private static double Distance3D(Point2D a,double az,Point2D b,double bz)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2)+Math.Pow(az-bz,2));
}

public sealed record MotionEvaluationResult(IReadOnlyList<MotionTraceEvent> Events,IReadOnlyList<OperationValidationError> Diagnostics)
{
    public bool Success=>Diagnostics.All(diagnostic=>diagnostic.Severity!=OperationDiagnosticSeverity.Error);
    public TimeSpan TotalTime=>Events.Count==0?TimeSpan.Zero:Events[^1].EndTime;
}

public static class MotionEvaluator
{
    public static MotionEvaluationResult Evaluate(ToolpathProgram program,MotionEvaluationSettings settings)
    {
        var diagnostics=new List<OperationValidationError>();
        if(!double.IsFinite(settings.RapidRateMmPerMin)||settings.RapidRateMmPerMin<=0)diagnostics.Add(new("rapid-rate","Machine rapid rate must be positive and finite."));
        if(!double.IsFinite(settings.ToolChangeSeconds)||settings.ToolChangeSeconds<0)diagnostics.Add(new("tool-change-time","Tool-change time must be finite and non-negative."));
        foreach(var operation in program.Operations)diagnostics.AddRange(OperationNumerics.ValidateMoves(operation));
        if(diagnostics.Count>0)return new([],diagnostics);

        var firstPositioningMove=program.Operations.SelectMany(operation=>operation.Moves).FirstOrDefault(move=>move is RapidMove or LinearMove or ArcMove);
        var events=new List<MotionTraceEvent>();var point=new Point2D(0,0);var z=firstPositioningMove is RapidMove firstRapid?firstRapid.Z:0d;var cursor=TimeSpan.Zero;var positioned=false;
        foreach(var operation in program.Operations)
        for(var moveIndex=0;moveIndex<operation.Moves.Count;moveIndex++)
        {
            var move=operation.Moves[moveIndex];MotionTraceEvent? trace=move switch
            {
                RapidMove rapid=>Create(operation,moveIndex,MotionEventKind.Rapid,point,z,rapid.End,rapid.Z,RateDuration(Distance3D(point,z,rapid.End,rapid.Z),settings.RapidRateMmPerMin),cursor,move),
                LinearMove linear=>Create(operation,moveIndex,MotionEventKind.Linear,point,z,linear.End,linear.Z,RateDuration(Distance3D(point,z,linear.End,linear.Z),linear.FeedRate),cursor,move),
                ArcMove arc when !positioned=>null,
                ArcMove arc=>Create(operation,moveIndex,MotionEventKind.Arc,point,z,arc.End,arc.Z,RateDuration(ArcDistance(point,z,arc),arc.FeedRate),cursor,move,arc.Center,arc.Clockwise),
                DwellMove dwell=>Create(operation,moveIndex,MotionEventKind.Dwell,point,z,point,z,TimeSpan.FromSeconds(dwell.Seconds),cursor,move),
                ToolChange=>Create(operation,moveIndex,MotionEventKind.ToolChange,point,z,point,z,TimeSpan.FromSeconds(settings.ToolChangeSeconds),cursor,move),
                SpindleStart=>Create(operation,moveIndex,MotionEventKind.SpindleStart,point,z,point,z,TimeSpan.Zero,cursor,move),
                SpindleStop=>Create(operation,moveIndex,MotionEventKind.SpindleStop,point,z,point,z,TimeSpan.Zero,cursor,move),
                _=>null
            };
            if(move is ArcMove&&!positioned){diagnostics.Add(new("arc-start",$"{operation.Name}: arc move {moveIndex+1} has no preceding positioned move."));continue;}
            if(trace is null)continue;events.Add(trace);cursor=trace.EndTime;
            if(move is RapidMove or LinearMove or ArcMove){point=trace.End;z=trace.EndZ;positioned=true;}
        }
        return new(events,diagnostics);
    }

    private static MotionTraceEvent Create(ToolpathOperation operation,int moveIndex,MotionEventKind kind,Point2D start,double startZ,Point2D end,double endZ,TimeSpan duration,TimeSpan cursor,ToolpathMove source,Point2D? center=null,bool clockwise=false)=>new(operation.SourceOperationId,operation.Name,moveIndex,kind,start,startZ,end,endZ,cursor,duration,source,center,clockwise);
    private static TimeSpan RateDuration(double distance,double rate)=>TimeSpan.FromMinutes(distance/rate);
    private static double ArcDistance(Point2D start,double startZ,ArcMove arc){var planar=Math.Abs(MotionTraceEvent.ArcSweep(start,arc.End,arc.Center,arc.Clockwise))*start.DistanceTo(arc.Center);return Math.Sqrt(planar*planar+Math.Pow(arc.Z-startZ,2));}
    private static double Distance3D(Point2D a,double az,Point2D b,double bz)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2)+Math.Pow(az-bz,2));
}
