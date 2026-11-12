using CncCad.Core.Geometry;

namespace CncCad.Core.Entities;

public enum VectorPathNodeKind { Anchor, QuadraticControl, CubicControl }

public sealed record VectorPathNode(int Index,Point2D Point,VectorPathNodeKind Kind,int FigureIndex,int SegmentIndex,int AnchorPosition,int ControlIndex);
public sealed record VectorPathNodeEdit(IReadOnlyList<VectorPathFigure> Figures,int SelectedNodeIndex);

public static class VectorPathNodes
{
    private const double Tolerance=GeometryTolerancePolicy.DefaultEndpointEqualityMm;

    public static IReadOnlyList<VectorPathNode> Describe(VectorPathEntity path)
    {
        var result=new List<VectorPathNode>();
        for(var figureIndex=0;figureIndex<path.Figures.Count;figureIndex++)
        {
            var figure=path.Figures[figureIndex];result.Add(new(result.Count,figure.Start,VectorPathNodeKind.Anchor,figureIndex,-1,0,0));
            for(var segmentIndex=0;segmentIndex<figure.Segments.Count;segmentIndex++)
            {
                switch(figure.Segments[segmentIndex])
                {
                    case QuadraticPathSegment quadratic:result.Add(new(result.Count,quadratic.Control,VectorPathNodeKind.QuadraticControl,figureIndex,segmentIndex,-1,1));break;
                    case CubicPathSegment cubic:result.Add(new(result.Count,cubic.Control1,VectorPathNodeKind.CubicControl,figureIndex,segmentIndex,-1,1));result.Add(new(result.Count,cubic.Control2,VectorPathNodeKind.CubicControl,figureIndex,segmentIndex,-1,2));break;
                }
                result.Add(new(result.Count,figure.Segments[segmentIndex].End,VectorPathNodeKind.Anchor,figureIndex,segmentIndex,segmentIndex+1,0));
            }
        }
        return result;
    }

    public static IReadOnlyList<Point2D> Points(VectorPathEntity path)=>Describe(path).Select(node=>node.Point).ToArray();
    public static Point2D Get(VectorPathEntity path,int index)=>Node(path,index).Point;
    public static bool CanMove(VectorPathEntity path,int index)
    {
        var node=Node(path,index);if(node.Kind!=VectorPathNodeKind.Anchor)return true;var figure=path.Figures[node.FigureIndex];var explicitClosure=figure.IsClosed&&figure.Segments.Count>0&&figure.Segments[^1].End.DistanceTo(figure.Start)<=Tolerance;
        var incoming=node.AnchorPosition>0?figure.Segments[node.AnchorPosition-1]:explicitClosure?figure.Segments[^1]:null;
        var outgoing=node.AnchorPosition<figure.Segments.Count?figure.Segments[node.AnchorPosition]:explicitClosure?figure.Segments[0]:null;
        return incoming is not ArcPathSegment&&outgoing is not ArcPathSegment;
    }
    public static bool CanInsertAfter(VectorPathEntity path,int index){var node=Node(path,index);if(node.Kind!=VectorPathNodeKind.Anchor)return false;var figure=path.Figures[node.FigureIndex];return node.AnchorPosition<figure.Segments.Count||figure.IsClosed&&figure.Segments.Count>0;}
    public static bool CanDelete(VectorPathEntity path,int index){var node=Node(path,index);if(node.Kind!=VectorPathNodeKind.Anchor)return false;var figure=path.Figures[node.FigureIndex];return AnchorCount(figure)>(figure.IsClosed?3:2);}
    public static bool CanMakeStart(VectorPathEntity path,int index){var node=Node(path,index);if(node.Kind!=VectorPathNodeKind.Anchor)return false;var figure=path.Figures[node.FigureIndex];return figure.IsClosed&&node.Point.DistanceTo(figure.Start)>Tolerance;}

    public static void Set(VectorPathEntity path,int index,Point2D value)
    {
        if(!double.IsFinite(value.X)||!double.IsFinite(value.Y))throw new ArgumentException("Node coordinates must be finite.",nameof(value));var node=Node(path,index);if(!CanMove(path,index))throw new InvalidOperationException("Exact arc anchors cannot be moved because that would invalidate the circular arc.");var figure=path.Figures[node.FigureIndex];
        if(node.Kind==VectorPathNodeKind.Anchor)
        {
            if(node.AnchorPosition==0){var before=figure.Start;figure.Start=value;if(figure.IsClosed&&figure.Segments.Count>0&&figure.Segments[^1].End.DistanceTo(before)<=Tolerance)figure.Segments[^1].End=value;}
            else{var segment=figure.Segments[node.AnchorPosition-1];var before=segment.End;segment.End=value;if(figure.IsClosed&&node.AnchorPosition==figure.Segments.Count&&before.DistanceTo(figure.Start)<=Tolerance)figure.Start=value;}return;
        }
        switch(figure.Segments[node.SegmentIndex]){case QuadraticPathSegment quadratic:quadratic.Control=value;break;case CubicPathSegment cubic when node.ControlIndex==1:cubic.Control1=value;break;case CubicPathSegment cubic:cubic.Control2=value;break;default:throw new InvalidOperationException("The selected node is not an editable curve handle.");}
    }

    public static VectorPathNodeEdit InsertAfter(VectorPathEntity path,int index)
    {
        if(!CanInsertAfter(path,index))throw new InvalidOperationException("Select an anchor with a following segment to split.");var source=Node(path,index);var figures=VectorPathGeometry.Clone(path.Figures);var figure=figures[source.FigureIndex];int splitIndex;Point2D start;
        if(source.AnchorPosition<figure.Segments.Count){splitIndex=source.AnchorPosition;start=splitIndex==0?figure.Start:figure.Segments[splitIndex-1].End;}
        else if(figure.Segments[^1].End.DistanceTo(figure.Start)<=Tolerance){splitIndex=0;start=figure.Start;}
        else{var end=figure.Segments[^1].End;var midpoint=Midpoint(end,figure.Start);figure.Segments.Add(new LinePathSegment(midpoint));return Result(figures,source.FigureIndex,figure.Segments.Count);}
        var parts=Split(start,figure.Segments[splitIndex]);figure.Segments[splitIndex]=parts.First;figure.Segments.Insert(splitIndex+1,parts.Second);return Result(figures,source.FigureIndex,splitIndex+1);
    }

    public static VectorPathNodeEdit Delete(VectorPathEntity path,int index)
    {
        if(!CanDelete(path,index))throw new InvalidOperationException("The selected anchor cannot be removed without collapsing the path.");var source=Node(path,index);var figures=VectorPathGeometry.Clone(path.Figures);var figure=figures[source.FigureIndex];
        if(figure.IsClosed){RotateToStart(figure,source.AnchorPosition);if(figure.Segments[^1].End.DistanceTo(figure.Start)>Tolerance)figure.Segments.Add(new LinePathSegment(figure.Start));var nextStart=figure.Segments[0].End;var remaining=figure.Segments.Skip(1).Take(figure.Segments.Count-2).ToList();remaining.Add(new LinePathSegment(nextStart));figure.Start=nextStart;figure.Segments=remaining;return Result(figures,source.FigureIndex,0);}
        if(source.AnchorPosition==0){figure.Start=figure.Segments[0].End;figure.Segments.RemoveAt(0);return Result(figures,source.FigureIndex,0);}
        if(source.AnchorPosition==figure.Segments.Count){figure.Segments.RemoveAt(figure.Segments.Count-1);return Result(figures,source.FigureIndex,figure.Segments.Count);}
        var previous=source.AnchorPosition==1?figure.Start:figure.Segments[source.AnchorPosition-2].End;figure.Segments.RemoveAt(source.AnchorPosition-1);var outgoing=figure.Segments[source.AnchorPosition-1];if(outgoing is ArcPathSegment arc&&Math.Abs(previous.DistanceTo(arc.Center)-arc.End.DistanceTo(arc.Center))>Tolerance)figure.Segments[source.AnchorPosition-1]=new LinePathSegment(arc.End);return Result(figures,source.FigureIndex,source.AnchorPosition-1);
    }

    public static VectorPathNodeEdit MakeStart(VectorPathEntity path,int index)
    {
        if(!CanMakeStart(path,index))throw new InvalidOperationException("Select another anchor on a closed vector path.");var source=Node(path,index);var figures=VectorPathGeometry.Clone(path.Figures);RotateToStart(figures[source.FigureIndex],source.AnchorPosition);return Result(figures,source.FigureIndex,0);
    }

    private static VectorPathNode Node(VectorPathEntity path,int index)=>Describe(path).ElementAtOrDefault(index)??throw new ArgumentOutOfRangeException(nameof(index));
    private static int AnchorCount(VectorPathFigure figure){var count=figure.Segments.Count+1;if(figure.IsClosed&&figure.Segments.Count>0&&figure.Segments[^1].End.DistanceTo(figure.Start)<=Tolerance)count--;return count;}
    private static VectorPathNodeEdit Result(IReadOnlyList<VectorPathFigure> figures,int figureIndex,int anchorPosition){var wrapper=new VectorPathEntity{Figures=VectorPathGeometry.Clone(figures)};var selected=Describe(wrapper).Single(node=>node.FigureIndex==figureIndex&&node.Kind==VectorPathNodeKind.Anchor&&node.AnchorPosition==anchorPosition);return new(VectorPathGeometry.Clone(figures),selected.Index);}
    private static (VectorPathSegment First,VectorPathSegment Second) Split(Point2D start,VectorPathSegment segment)
    {
        switch(segment)
        {
            case LinePathSegment line:{var midpoint=Midpoint(start,line.End);return(new LinePathSegment(midpoint),new LinePathSegment(line.End));}
            case QuadraticPathSegment quadratic:{var firstControl=Midpoint(start,quadratic.Control);var secondControl=Midpoint(quadratic.Control,quadratic.End);var midpoint=Midpoint(firstControl,secondControl);return(new QuadraticPathSegment(firstControl,midpoint),new QuadraticPathSegment(secondControl,quadratic.End));}
            case CubicPathSegment cubic:{var split=VectorPathGeometry.Split(start,cubic,.5);return(split.Left,split.Right);}
            case ArcPathSegment arc:{var radius=start.DistanceTo(arc.Center);var startAngle=Math.Atan2(start.Y-arc.Center.Y,start.X-arc.Center.X);var halfSweep=arc.SweepAngleDegrees/2;var angle=startAngle+halfSweep*Math.PI/180;var midpoint=new Point2D(arc.Center.X+radius*Math.Cos(angle),arc.Center.Y+radius*Math.Sin(angle));return(new ArcPathSegment(arc.Center,halfSweep,midpoint),new ArcPathSegment(arc.Center,arc.SweepAngleDegrees-halfSweep,arc.End));}
            default:throw new NotSupportedException($"Unsupported vector segment {segment.GetType().Name}.");
        }
    }
    private static void RotateToStart(VectorPathFigure figure,int anchorPosition){if(!figure.IsClosed)throw new InvalidOperationException("Only a closed figure can change its start node.");if(anchorPosition==0)return;if(figure.Segments[^1].End.DistanceTo(figure.Start)>Tolerance)figure.Segments.Add(new LinePathSegment(figure.Start));if(anchorPosition>=figure.Segments.Count)return;var start=figure.Segments[anchorPosition-1].End;figure.Segments=[..figure.Segments.Skip(anchorPosition),..figure.Segments.Take(anchorPosition)];figure.Start=start;}
    private static Point2D Midpoint(Point2D first,Point2D second)=>new((first.X+second.X)/2,(first.Y+second.Y)/2);
}
