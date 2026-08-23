using System.Collections;
using System.Reflection;
using CncCad.Core.Documents;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;
using IxMilia.Dxf;

namespace CncCad.IO.Import;

public sealed class DxfVectorImporter:IVectorImportService
{
    public bool CanImport(string extension)=>extension.Equals(".dxf",StringComparison.OrdinalIgnoreCase);
    public VectorImportResult Import(VectorImportRequest request)
    {
        var file=DxfFile.Load(request.Content);var layers=new Dictionary<string,CadLayer>(StringComparer.OrdinalIgnoreCase);var entities=new List<CadEntity>();var warnings=new List<ImportWarning>();var unsupported=new HashSet<string>();
        foreach(var source in file.Entities){var type=source.GetType().Name;var layerName=Get(source,"Layer")?.ToString()??Path.GetFileNameWithoutExtension(request.FileName)+" Import";if(!layers.TryGetValue(layerName,out var layer))layers[layerName]=layer=new(){Name=layerName};CadEntity? entity=type switch
        {
            "DxfLine"=>new LineEntity{Start=Point(Get(source,"P1")??Get(source,"Start")),End=Point(Get(source,"P2")??Get(source,"End"))},
            "DxfCircle"=>new CircleEntity{Center=Point(Get(source,"Center")),Radius=Number(Get(source,"Radius"))},
            "DxfArc"=>Arc(source),
            "DxfLwPolyline"=>PolylineWithBulges(source,"Vertices",v=>new(Number(Get(v,"X")),Number(Get(v,"Y")))),
            "DxfPolyline"=>PolylineWithBulges(source,"Vertices",v=>Point(Get(v,"Location")??v)),
            "DxfEllipse"=>Ellipse(source,warnings),
            "DxfSpline"=>Spline(source,warnings),
            _=>null
        };if(entity is null){unsupported.Add(type.Replace("Dxf",""));continue;}entity.LayerId=layer.Id;entity.Transform=Matrix2D.Then(Matrix2D.Scale(request.ScaleToMillimetres,request.ScaleToMillimetres),Matrix2D.Translation(request.Target.X,request.Target.Y));entities.Add(entity);}
        var bounds=entities.Count==0?new BoundingBox(0,0,0,0):BoundingBox.FromPoints(entities.SelectMany(x=>new[]{new Point2D(x.GetBounds().MinX,x.GetBounds().MinY),new Point2D(x.GetBounds().MaxX,x.GetBounds().MaxY)}));return new(entities,[..layers.Values],bounds,ImportUnit.Unitless,warnings,[..unsupported]);
    }
    private static ArcEntity Arc(object value){var start=Number(Get(value,"StartAngle"));var end=Number(Get(value,"EndAngle"));var sweep=(end-start+360)%360;if(sweep<1e-9)sweep=360;return new(){Center=Point(Get(value,"Center")),Radius=Number(Get(value,"Radius")),StartAngleDegrees=start,SweepAngleDegrees=sweep};}
    private static PolylineEntity Polyline(object value,string property,Func<object,Point2D> point){var items=((IEnumerable?)Get(value,property))?.Cast<object>().Select(point).ToList()??[];return new(){Points=items,IsClosed=Get(value,"IsClosed") as bool?==true};}
    private static PolylineEntity PolylineWithBulges(object value,string property,Func<object,Point2D> point)
    {
        var vertices=((IEnumerable?)Get(value,property))?.Cast<object>().ToArray()??[];if(!vertices.Any(v=>Math.Abs(Number(Get(v,"Bulge")))>1e-12))return Polyline(value,property,point);
        var method=value.GetType().GetMethod("AsSimpleEntities",BindingFlags.Public|BindingFlags.Instance);var simple=(method?.Invoke(value,null) as IEnumerable)?.Cast<object>().ToArray()??[];var points=new List<Point2D>();
        foreach(var item in simple){if(item.GetType().Name=="DxfLine"){var a=Point(Get(item,"P1")??Get(item,"Start"));var b=Point(Get(item,"P2")??Get(item,"End"));if(points.Count==0)points.Add(a);points.Add(b);}else if(item.GetType().Name=="DxfArc"){var arc=Arc(item);var start=GeometryAlgorithms.PointOnArc(arc,arc.StartAngleDegrees);if(points.Count==0)points.Add(start);for(var i=1;i<=16;i++)points.Add(GeometryAlgorithms.PointOnArc(arc,arc.StartAngleDegrees+arc.SweepAngleDegrees*i/16));}}
        return new(){Points=points,IsClosed=Get(value,"IsClosed") as bool?==true};
    }
    private static CadEntity Ellipse(object value,List<ImportWarning> warnings){var center=Point(Get(value,"Center"));var axis=Point(Get(value,"MajorAxis"));var rx=Math.Sqrt(axis.X*axis.X+axis.Y*axis.Y);var ratio=Number(Get(value,"MinorAxisRatio")??Get(value,"AxisRatio"));warnings.Add(new("dxf-ellipse-orientation","Rotated DXF ellipse orientation was flattened to an axis-aligned ellipse."));return new EllipseEntity{Center=center,RadiusX=rx,RadiusY=Math.Abs(rx*ratio)};}
    private static CadEntity Spline(object value,List<ImportWarning> warnings){var points=((IEnumerable?)Get(value,"ControlPoints"))?.Cast<object>().Select(Point).ToList()??[];warnings.Add(new("dxf-spline-control-polygon","DXF spline was imported from its control polygon."));return new PolylineEntity{Points=points};}
    private static object? Get(object? value,string name)=>value?.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.Instance|BindingFlags.IgnoreCase)?.GetValue(value);
    private static double Number(object? value)=>value is null?0:Convert.ToDouble(value,System.Globalization.CultureInfo.InvariantCulture);
    private static Point2D Point(object? value)=>new(Number(Get(value,"X")),Number(Get(value,"Y")));
}
