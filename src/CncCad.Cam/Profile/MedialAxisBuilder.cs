using CncCad.Core.Geometry;

namespace CncCad.Cam.Profile;

internal readonly record struct MedialAxisSegment(Point2D Start,Point2D End,double StartClearanceMm,double EndClearanceMm);

internal static class MedialAxisBuilder
{
    private readonly record struct Site(Point2D Point,int Feature);
    private readonly record struct Edge(int A,int B)
    {
        public static Edge Of(int a,int b)=>a<b?new(a,b):new(b,a);
    }
    private readonly record struct Triangle(int A,int B,int C)
    {
        public IEnumerable<Edge> Edges(){yield return Edge.Of(A,B);yield return Edge.Of(B,C);yield return Edge.Of(C,A);}
    }

    public static IReadOnlyList<MedialAxisSegment> Build(IReadOnlyList<IReadOnlyList<Point2D>> contours,double requestedSpacing)
    {
        if(contours.Count==0||!double.IsFinite(requestedSpacing)||requestedSpacing<=0)return[];var perimeter=contours.Sum(Perimeter);var spacing=Math.Max(requestedSpacing,perimeter/900);var sites=Sample(contours,spacing);if(sites.Count<3)return[];
        var points=sites.Select(site=>site.Point).ToList();var bounds=BoundingBox.FromPoints(points);var extent=Math.Max(bounds.Width,bounds.Height);if(extent<=GeometryTolerancePolicy.DefaultMinimumLengthMm)return[];var center=new Point2D((bounds.MinX+bounds.MaxX)/2,(bounds.MinY+bounds.MaxY)/2);points.Add(new(center.X-20*extent,center.Y-10*extent));points.Add(new(center.X,center.Y+20*extent));points.Add(new(center.X+20*extent,center.Y-10*extent));var superA=points.Count-3;var triangles=new List<Triangle>{new(superA,superA+1,superA+2)};
        for(var pointIndex=0;pointIndex<sites.Count;pointIndex++)
        {
            var bad=triangles.Where(triangle=>InCircumcircle(triangle,points,points[pointIndex])).ToArray();var boundary=bad.SelectMany(triangle=>triangle.Edges()).GroupBy(edge=>edge).Where(group=>group.Count()==1).Select(group=>group.Key).OrderBy(edge=>edge.A).ThenBy(edge=>edge.B).ToArray();foreach(var triangle in bad)triangles.Remove(triangle);foreach(var edge in boundary)if(Math.Abs(Cross(points[edge.A],points[edge.B],points[pointIndex]))>1e-10)triangles.Add(new(edge.A,edge.B,pointIndex));
        }
        triangles=triangles.Where(triangle=>triangle.A<sites.Count&&triangle.B<sites.Count&&triangle.C<sites.Count).ToList();var centers=new Point2D?[triangles.Count];for(var i=0;i<triangles.Count;i++)if(TryCircumcenter(triangles[i],points,out var circumcenter)&&Inside(contours,circumcenter))centers[i]=circumcenter;var owners=new Dictionary<Edge,List<int>>();for(var i=0;i<triangles.Count;i++)foreach(var edge in triangles[i].Edges()){if(!owners.TryGetValue(edge,out var list))owners[edge]=list=[];list.Add(i);}
        var result=new List<MedialAxisSegment>();foreach(var pair in owners.OrderBy(pair=>pair.Key.A).ThenBy(pair=>pair.Key.B))
        {
            if(pair.Value.Count!=2||sites[pair.Key.A].Feature==sites[pair.Key.B].Feature)continue;var first=centers[pair.Value[0]];var second=centers[pair.Value[1]];if(first is null||second is null||first.Value.DistanceTo(second.Value)<=1e-7)continue;var midpoint=Midpoint(first.Value,second.Value);if(!Inside(contours,midpoint))continue;var firstClearance=BoundaryDistance(contours,first.Value);var secondClearance=BoundaryDistance(contours,second.Value);if(firstClearance<=1e-7||secondClearance<=1e-7)continue;result.Add(new(first.Value,second.Value,firstClearance,secondClearance));
        }
        return result.OrderBy(segment=>Math.Min(segment.Start.X,segment.End.X)).ThenBy(segment=>Math.Min(segment.Start.Y,segment.End.Y)).ThenBy(segment=>Math.Max(segment.Start.X,segment.End.X)).ThenBy(segment=>Math.Max(segment.Start.Y,segment.End.Y)).ToArray();
    }

    public static double BoundaryDistance(IEnumerable<IReadOnlyList<Point2D>> contours,Point2D point)=>contours.SelectMany(contour=>Enumerable.Range(0,contour.Count).Select(index=>DistanceToSegment(point,contour[index],contour[(index+1)%contour.Count]))).DefaultIfEmpty(0).Min();
    public static bool Inside(IEnumerable<IReadOnlyList<Point2D>> contours,Point2D point)=>contours.Count(contour=>ClipperGeometryAdapter.Contains(contour,point))%2==1;

    private static List<Site> Sample(IReadOnlyList<IReadOnlyList<Point2D>> contours,double spacing)
    {
        var result=new List<Site>();var feature=0;foreach(var contour in contours)for(var i=0;i<contour.Count;i++,feature++){var a=contour[i];var b=contour[(i+1)%contour.Count];var count=Math.Max(1,(int)Math.Ceiling(a.DistanceTo(b)/spacing));for(var n=0;n<count;n++){var t=(double)n/count;var point=new Point2D(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t);if(result.All(site=>site.Point.DistanceTo(point)>GeometryTolerancePolicy.DefaultTopologyMergeMm))result.Add(new(point,feature));}}return result.OrderBy(site=>site.Point.X).ThenBy(site=>site.Point.Y).ThenBy(site=>site.Feature).ToList();
    }
    private static double Perimeter(IReadOnlyList<Point2D> contour)=>Enumerable.Range(0,contour.Count).Sum(index=>contour[index].DistanceTo(contour[(index+1)%contour.Count]));
    private static bool InCircumcircle(Triangle triangle,IReadOnlyList<Point2D> points,Point2D point)=>TryCircumcenter(triangle,points,out var center)&&point.DistanceTo(center)<=points[triangle.A].DistanceTo(center)+GeometryTolerancePolicy.DefaultEndpointEqualityMm;
    private static bool TryCircumcenter(Triangle triangle,IReadOnlyList<Point2D> points,out Point2D center)
    {
        var a=points[triangle.A];var b=points[triangle.B];var c=points[triangle.C];var d=2*(a.X*(b.Y-c.Y)+b.X*(c.Y-a.Y)+c.X*(a.Y-b.Y));if(Math.Abs(d)<=1e-12){center=default;return false;}var aa=a.X*a.X+a.Y*a.Y;var bb=b.X*b.X+b.Y*b.Y;var cc=c.X*c.X+c.Y*c.Y;center=new((aa*(b.Y-c.Y)+bb*(c.Y-a.Y)+cc*(a.Y-b.Y))/d,(aa*(c.X-b.X)+bb*(a.X-c.X)+cc*(b.X-a.X))/d);return double.IsFinite(center.X)&&double.IsFinite(center.Y);
    }
    private static double Cross(Point2D a,Point2D b,Point2D c)=>(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
    private static Point2D Midpoint(Point2D a,Point2D b)=>new((a.X+b.X)/2,(a.Y+b.Y)/2);
    private static double DistanceToSegment(Point2D point,Point2D a,Point2D b){var direction=b-a;var lengthSquared=direction.X*direction.X+direction.Y*direction.Y;if(lengthSquared<=1e-20)return point.DistanceTo(a);var t=Math.Clamp(((point.X-a.X)*direction.X+(point.Y-a.Y)*direction.Y)/lengthSquared,0,1);return point.DistanceTo(new(a.X+direction.X*t,a.Y+direction.Y*t));}
}
