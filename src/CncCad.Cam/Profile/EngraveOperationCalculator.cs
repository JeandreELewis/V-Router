using CncCad.Cam.Operations;
using CncCad.Cam.Toolpaths;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Profile;

public sealed class CompositeOperationCalculator:ICamOperationCalculator
{
    private readonly IReadOnlyDictionary<Type,ICamOperationCalculator> calculators;
    public Type OperationType=>typeof(CamOperationDefinition);
    public CompositeOperationCalculator(params ICamOperationCalculator[] calculators)
    {
        this.calculators=calculators.ToDictionary(x=>x.OperationType);
    }
    public OperationCalculationResult Calculate(CamOperationDefinition operation,IReadOnlyDictionary<Guid,CadEntity> geometry)
    {
        var numericErrors=OperationNumerics.ValidateDefinition(operation);
        if(numericErrors.Count>0)return new(null,numericErrors);
        if(!calculators.TryGetValue(operation.GetType(),out var calculator))return OperationCalculationResult.Failed(new OperationValidationError("unsupported",$"{operation.GetType().Name} is not supported."));
        return OperationNumerics.ValidateResult(calculator.Calculate(operation,geometry));
    }
}
public sealed class EngraveOperationCalculator:ICamOperationCalculator
{
    public Type OperationType=>typeof(EngraveOperationDefinition);
    public OperationCalculationResult Calculate(CamOperationDefinition operation,IReadOnlyDictionary<Guid,CadEntity> geometry)
    {
        if(operation is not EngraveOperationDefinition engrave)return OperationCalculationResult.Failed(new OperationValidationError("unsupported","Operation is not engraving."));
        var numericErrors=OperationNumerics.ValidateDefinition(engrave);if(numericErrors.Count>0)return new(null,numericErrors);
        var errors=Validate(engrave,geometry);if(errors.Count>0)return new(null,errors);
        var paths=engrave.GeometryIds.SelectMany(id=>Paths(geometry[id]).Select(path=>path with{SourceId=id})).Where(path=>path.Segments.Count>0).ToList();
        var toolpath=new ToolpathOperation{SourceOperationId=engrave.Id,Name=engrave.Name,ToolId=engrave.Tool.SourceToolId,MaterialSheetId=engrave.MaterialSheetId,SourceGeometryIds=[..engrave.GeometryIds]};
        toolpath.Moves.Add(new ToolChange(engrave.Tool.ToolNumber,engrave.Tool.Name));toolpath.Moves.Add(new SpindleStart(engrave.SpindleRpm));
        var current=new Point2D(0,0);
        while(paths.Count>0)
        {
            var index=engrave.Direction==EngraveDirection.Optimize?Enumerable.Range(0,paths.Count).MinBy(i=>Math.Min(current.DistanceTo(paths[i].Start),current.DistanceTo(paths[i].End))):0;
            var path=paths[index];paths.RemoveAt(index);
            if(engrave.Direction==EngraveDirection.Reverse||engrave.Direction==EngraveDirection.Optimize&&current.DistanceTo(path.End)<current.DistanceTo(path.Start))path=path.Reverse();
            foreach(var depth in ProfileToolpathGenerator.CalculatePassDepths(engrave.StartDepthMm,engrave.CutDepthMm,engrave.PassDepthMm))
            {
                toolpath.Moves.Add(new RapidMove(path.Start,engrave.SafeZMm));toolpath.Moves.Add(new LinearMove(path.Start,-depth,engrave.PlungeRateMmPerMin));
                foreach(var segment in path.Segments)
                    toolpath.Moves.Add(segment is EngraveArc arc?new ArcMove(arc.End,arc.Center,-depth,engrave.FeedRateMmPerMin,arc.Clockwise):new LinearMove(segment.End,-depth,engrave.FeedRateMmPerMin));
                toolpath.Moves.Add(new RapidMove(path.End,engrave.SafeZMm));
            }
            current=path.End;
        }
        toolpath.Moves.Add(new SpindleStop());return OperationNumerics.ValidateResult(new(toolpath,[]));
    }
    private static List<OperationValidationError> Validate(EngraveOperationDefinition e,IReadOnlyDictionary<Guid,CadEntity> geometry){var errors=new List<OperationValidationError>();if(e.GeometryIds.Count==0)errors.Add(new("geometry","Select engraving vectors."));if(e.Tool.DiameterMm<=0)errors.Add(new("tool","Select a valid engraving tool."));if(e.CutDepthMm<=e.StartDepthMm||e.PassDepthMm<=0)errors.Add(new("depth","Engraving depth and pass depth are invalid."));if(e.FeedRateMmPerMin<=0||e.PlungeRateMmPerMin<=0||e.SafeZMm<=0)errors.Add(new("rates","Engraving rates and safe Z must be positive."));foreach(var id in e.GeometryIds){if(!geometry.TryGetValue(id,out var entity))errors.Add(new("missing",$"Referenced vector {id} is missing."));else if(Paths(entity).All(path=>path.Segments.Count==0))errors.Add(new("degenerate",$"{entity.GetType().Name} contains no machinable stroke."));}return errors;}
    private static IEnumerable<EngravePath> Paths(CadEntity entity)=>entity switch
    {
        ArcEntity arc when IsSimilarity(arc.Transform)=>[NativeArc(arc)],
        CircleEntity circle when IsSimilarity(circle.Transform)=>[NativeCircle(circle)],
        LineEntity line=>[FromPoints(EntityGeometry.WorldPoints(line),false)],
        PolylineEntity poly=>[FromPoints(EntityGeometry.WorldPoints(poly),poly.IsClosed)],
        RectangleEntity rectangle=>[FromPoints(EntityGeometry.WorldPoints(rectangle),true)],
        ArcEntity arc=>[FromPoints(EntityGeometry.WorldPoints(arc),false)],
        CircleEntity circle=>[FromPoints(EntityGeometry.WorldPoints(circle),true)],
        EllipseEntity ellipse=>[FromPoints(EntityGeometry.WorldPoints(ellipse),true)],
        VectorPathEntity path=>path.Figures.Select(figure=>FromFigure(figure,path.Transform)),
        TextEntity text=>text.Outlines.Select(figure=>FromFigure(figure,text.Transform)),
        _=>[]
    };
    private static EngravePath FromPoints(IReadOnlyList<Point2D> points,bool closed)
    {
        if(points.Count==0)return new(default,default,[]);var segments=points.Skip(1).Select(point=>(EngraveSegment)new EngraveLine(point)).ToList();if(closed&&points.Count>1&&points[0].DistanceTo(points[^1])>GeometryTolerancePolicy.DefaultEndpointEqualityMm)segments.Add(new EngraveLine(points[0]));return new(default,points[0],segments);
    }
    private static EngravePath FromFigure(VectorPathFigure figure,Matrix2D transform)
    {
        var start=transform.Transform(figure.Start);var segments=new List<EngraveSegment>();var current=figure.Start;
        foreach(var segment in figure.Segments)
        {
            if(segment is LinePathSegment line)segments.Add(new EngraveLine(transform.Transform(line.End)));
            else if(segment is ArcPathSegment arc&&IsSimilarity(transform))segments.AddRange(NativeArcSegments(current,arc.Center,arc.SweepAngleDegrees,arc.End,transform));
            else
            {
                var flattened=VectorPathGeometry.Flatten([new VectorPathFigure{Start=current,Segments=[segment]}],ClipperGeometryAdapter.CircleToleranceMm).Select(transform.Transform).ToArray();
                segments.AddRange(flattened.Skip(1).Select(point=>(EngraveSegment)new EngraveLine(point)));
            }
            current=segment.End;
        }
        if(figure.IsClosed&&segments.Count>0&&segments[^1].End.DistanceTo(start)>GeometryTolerancePolicy.DefaultEndpointEqualityMm)segments.Add(new EngraveLine(start));
        return new(default,start,segments);
    }
    private static EngravePath NativeArc(ArcEntity arc)
    {
        var startAngle=arc.StartAngleDegrees*Math.PI/180;var endAngle=(arc.StartAngleDegrees+arc.SweepAngleDegrees)*Math.PI/180;var start=new Point2D(arc.Center.X+arc.Radius*Math.Cos(startAngle),arc.Center.Y+arc.Radius*Math.Sin(startAngle));var end=new Point2D(arc.Center.X+arc.Radius*Math.Cos(endAngle),arc.Center.Y+arc.Radius*Math.Sin(endAngle));return new(default,arc.Transform.Transform(start),NativeArcSegments(start,arc.Center,arc.SweepAngleDegrees,end,arc.Transform));
    }
    private static EngravePath NativeCircle(CircleEntity circle)
    {
        var start=new Point2D(circle.Center.X+circle.Radius,circle.Center.Y);return new(default,circle.Transform.Transform(start),NativeArcSegments(start,circle.Center,360,start,circle.Transform));
    }
    private static List<EngraveSegment> NativeArcSegments(Point2D start,Point2D center,double sweepDegrees,Point2D exactEnd,Matrix2D transform)
    {
        var radius=start.DistanceTo(center);if(radius<=1e-12||Math.Abs(sweepDegrees)<=1e-12)return[new EngraveLine(transform.Transform(exactEnd))];var count=Math.Max(1,(int)Math.Ceiling(Math.Abs(sweepDegrees)/180));var step=sweepDegrees/count;var angle=Math.Atan2(start.Y-center.Y,start.X-center.X);var clockwise=(sweepDegrees<0)!=(Determinant(transform)<0);var result=new List<EngraveSegment>();for(var index=1;index<=count;index++){var next=index==count?exactEnd:new Point2D(center.X+radius*Math.Cos(angle+step*index*Math.PI/180),center.Y+radius*Math.Sin(angle+step*index*Math.PI/180));result.Add(new EngraveArc(transform.Transform(next),transform.Transform(center),clockwise));}return result;
    }
    private static bool IsSimilarity(Matrix2D transform)
    {
        var x=Math.Sqrt(transform.M11*transform.M11+transform.M12*transform.M12);var y=Math.Sqrt(transform.M21*transform.M21+transform.M22*transform.M22);var dot=transform.M11*transform.M21+transform.M12*transform.M22;return x>1e-12&&Math.Abs(x-y)<=1e-9*Math.Max(1,Math.Max(x,y))&&Math.Abs(dot)<=1e-9*Math.Max(1,x*y);
    }
    private static double Determinant(Matrix2D transform)=>transform.M11*transform.M22-transform.M12*transform.M21;
    private abstract record EngraveSegment(Point2D End);
    private sealed record EngraveLine(Point2D End):EngraveSegment(End);
    private sealed record EngraveArc(Point2D End,Point2D Center,bool Clockwise):EngraveSegment(End);
    private sealed record EngravePath(Guid SourceId,Point2D Start,List<EngraveSegment> Segments)
    {
        public Point2D End=>Segments.Count==0?Start:Segments[^1].End;
        public EngravePath Reverse()
        {
            var starts=new List<Point2D>{Start};starts.AddRange(Segments.Take(Math.Max(0,Segments.Count-1)).Select(segment=>segment.End));var reversed=new List<EngraveSegment>();for(var index=Segments.Count-1;index>=0;index--){var segment=Segments[index];reversed.Add(segment is EngraveArc arc?new EngraveArc(starts[index],arc.Center,!arc.Clockwise):new EngraveLine(starts[index]));}return new(SourceId,End,reversed);
        }
    }
}
