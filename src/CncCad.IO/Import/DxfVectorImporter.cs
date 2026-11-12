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
        var file=DxfFile.Load(request.Content);var layers=new Dictionary<string,CadLayer>(StringComparer.OrdinalIgnoreCase);var entities=new List<CadEntity>();var warnings=new List<ImportWarning>();var approximations=new List<ImportWarning>();var unsupported=new HashSet<string>();var unsupportedDetails=new List<UnsupportedImportElement>();var (unit,unitScale)=Units(file.Header.DefaultDrawingUnits.ToString(),warnings);var scale=request.ScaleToMillimetres*unitScale;var fallbackLayer=Path.GetFileNameWithoutExtension(request.FileName)+" Import";var blocks=Blocks(file).Where(block=>!string.IsNullOrWhiteSpace(BlockName(block))).GroupBy(BlockName,StringComparer.OrdinalIgnoreCase).ToDictionary(group=>group.Key,group=>group.First(),StringComparer.OrdinalIgnoreCase);
        foreach(var source in file.Entities)Expand(source,Matrix2D.Identity,null,0);var fileTransform=Matrix2D.Then(Matrix2D.Scale(scale,scale),Matrix2D.Translation(request.Target.X,request.Target.Y));foreach(var entity in entities)entity.Transform=Matrix2D.Then(entity.Transform,fileTransform);var bounds=entities.Count==0?new BoundingBox(0,0,0,0):BoundingBox.FromPoints(entities.SelectMany(x=>new[]{new Point2D(x.GetBounds().MinX,x.GetBounds().MinY),new Point2D(x.GetBounds().MaxX,x.GetBounds().MaxY)}));return new(entities,[..layers.Values],bounds,unit,warnings,[..unsupported],unsupportedDetails,scale,approximations);

        void Expand(object source,Matrix2D inherited,string? inheritedLayer,int depth)
        {
            var type=source.GetType().Name;if(depth>32){AddUnsupported("Insert recursion");return;}if(type=="DxfInsert")
            {
                var name=Get(source,"Name")?.ToString()??Get(source,"BlockName")?.ToString()??Get(Get(source,"Block"),"Name")?.ToString()??"";if(!blocks.TryGetValue(name,out var block)){AddUnsupported($"Insert:{name}");return;}var insertLayer=LayerName(source,inheritedLayer??fallbackLayer);var location=Point(Get(source,"Location")??Get(source,"InsertionPoint"));var rotation=Number(Get(source,"Rotation")??Get(source,"RotationAngle"));var scaleX=NonZero(Number(Get(source,"XScaleFactor")??Get(source,"XScale")??1));var scaleY=NonZero(Number(Get(source,"YScaleFactor")??Get(source,"YScale")??1));var rows=Math.Max(1,(int)Number(Get(source,"RowCount")??1));var columns=Math.Max(1,(int)Number(Get(source,"ColumnCount")??1));var rowSpacing=Number(Get(source,"RowSpacing"));var columnSpacing=Number(Get(source,"ColumnSpacing"));var basePoint=Point(Get(block,"BasePoint")??Get(block,"Base")??Get(block,"Location"));if(rows>1||columns>1)AddApproximation("dxf-insert-array","DXF insert row/column arrays were expanded into individual transformed block instances.");for(var row=0;row<rows;row++)for(var column=0;column<columns;column++){var transform=Matrix2D.Then(Matrix2D.Then(Matrix2D.Then(Matrix2D.Translation(-basePoint.X,-basePoint.Y),Matrix2D.Translation(column*columnSpacing,row*rowSpacing)),Matrix2D.Scale(scaleX,scaleY)),Matrix2D.Then(Matrix2D.Rotation(rotation),Matrix2D.Translation(location.X,location.Y)));var combined=Matrix2D.Then(transform,inherited);foreach(var child in BlockEntities(block))Expand(child,combined,insertLayer,depth+1);}return;
            }
            var entity=ConvertEntity(source,warnings,approximations);if(entity is null){AddUnsupported(type.Replace("Dxf",""));return;}var layerName=LayerName(source,inheritedLayer??fallbackLayer);if(!layers.TryGetValue(layerName,out var layer))layers[layerName]=layer=new(){Name=layerName};entity.LayerId=layer.Id;entity.Transform=Matrix2D.Then(entity.Transform,inherited);entities.Add(entity);
        }
        void AddUnsupported(string name){unsupported.Add(name);unsupportedDetails.Add(new(name));}
        void AddApproximation(string code,string message){if(approximations.Any(item=>item.Code==code))return;approximations.Add(new(code,message));}
    }
    private static ArcEntity Arc(object value){var start=Number(Get(value,"StartAngle"));var end=Number(Get(value,"EndAngle"));var sweep=(end-start+360)%360;if(sweep<1e-9)sweep=360;return new(){Center=Point(Get(value,"Center")),Radius=Number(Get(value,"Radius")),StartAngleDegrees=start,SweepAngleDegrees=sweep};}
    private static PolylineEntity Polyline(object value,string property,Func<object,Point2D> point){var items=((IEnumerable?)Get(value,property))?.Cast<object>().Select(point).ToList()??[];return new(){Points=items,IsClosed=Get(value,"IsClosed") as bool?==true};}
    private static CadEntity? ConvertEntity(object source,List<ImportWarning> warnings,List<ImportWarning> approximations)=>source.GetType().Name switch
    {
        "DxfLine"=>new LineEntity{Start=Point(Get(source,"P1")??Get(source,"Start")),End=Point(Get(source,"P2")??Get(source,"End"))},
        "DxfCircle"=>new CircleEntity{Center=Point(Get(source,"Center")),Radius=Number(Get(source,"Radius"))},
        "DxfArc"=>Arc(source),
        "DxfLwPolyline"=>PolylineWithBulges(source,"Vertices",vertex=>new(Number(Get(vertex,"X")),Number(Get(vertex,"Y"))),warnings,approximations),
        "DxfPolyline"=>PolylineWithBulges(source,"Vertices",vertex=>Point(Get(vertex,"Location")??vertex),warnings,approximations),
        "DxfEllipse"=>Ellipse(source,warnings),
        "DxfSpline"=>Spline(source,warnings,approximations),
        _=>null
    };
    private static CadEntity PolylineWithBulges(object value,string property,Func<object,Point2D> point,List<ImportWarning> warnings,List<ImportWarning> approximations)
    {
        var vertices=((IEnumerable?)Get(value,property))?.Cast<object>().ToArray()??[];if(!vertices.Any(v=>Math.Abs(Number(Get(v,"Bulge")))>1e-12))return Polyline(value,property,point);
        var points=vertices.Select(point).ToArray();var closed=Get(value,"IsClosed") as bool?==true;if(points.Length<2)return new PolylineEntity{Points=[..points],IsClosed=closed};var figure=new VectorPathFigure{Start=points[0],IsClosed=closed};var count=closed?points.Length:points.Length-1;for(var index=0;index<count;index++){var start=points[index];var end=points[(index+1)%points.Length];var bulge=Number(Get(vertices[index],"Bulge"));if(Math.Abs(bulge)<=1e-12){figure.Segments.Add(new LinePathSegment(end));continue;}var chord=end-start;var length=chord.Length;if(length<=1e-12){figure.Segments.Add(new LinePathSegment(end));continue;}var midpoint=new Point2D((start.X+end.X)/2,(start.Y+end.Y)/2);var centerDistance=length*(1-bulge*bulge)/(4*bulge);var center=new Point2D(midpoint.X-chord.Y/length*centerDistance,midpoint.Y+chord.X/length*centerDistance);var sweep=4*Math.Atan(bulge)*180/Math.PI;figure.Segments.Add(new ArcPathSegment(center,sweep,end));}return new VectorPathEntity{Figures=[figure]};
    }
    private static CadEntity Ellipse(object value,List<ImportWarning> warnings){var center=Point(Get(value,"Center"));var axis=Point(Get(value,"MajorAxis"));var rx=Math.Sqrt(axis.X*axis.X+axis.Y*axis.Y);var ratio=Number(Get(value,"MinorAxisRatio")??Get(value,"AxisRatio"));var angle=Math.Atan2(axis.Y,axis.X)*180/Math.PI;return new EllipseEntity{Center=center,RadiusX=rx,RadiusY=Math.Abs(rx*ratio),Transform=Matrix2D.Around(center,Matrix2D.Rotation(angle))};}
    private static CadEntity Spline(object value,List<ImportWarning> warnings,List<ImportWarning> approximations){var points=((IEnumerable?)Get(value,"ControlPoints"))?.Cast<object>().Select(Point).ToList()??[];AddApproximation(warnings,approximations,"dxf-spline-control-polygon","DXF spline was imported from its control polygon.");return new PolylineEntity{Points=points};}
    private static void AddApproximation(List<ImportWarning> warnings,List<ImportWarning> approximations,string code,string message){if(approximations.Any(item=>item.Code==code))return;approximations.Add(new(code,message));}
    private static IEnumerable<object> Blocks(object file){var value=Get(file,"Blocks")??Get(Get(file,"BlocksSection"),"Blocks");return(value as IEnumerable)?.Cast<object>()??[];}
    private static IEnumerable<object> BlockEntities(object block)=>(Get(block,"Entities") as IEnumerable)?.Cast<object>()??[];
    private static string BlockName(object block)=>Get(block,"Name")?.ToString()??Get(block,"BlockName")?.ToString()??"";
    private static string LayerName(object source,string fallback){var value=Get(source,"Layer")?.ToString();return string.IsNullOrWhiteSpace(value)||value=="0"?fallback:value;}
    private static double NonZero(double value)=>Math.Abs(value)<1e-12?1:value;
    private static object? Get(object? value,string name)=>value?.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.Instance|BindingFlags.IgnoreCase)?.GetValue(value);
    private static double Number(object? value)=>value is null?0:Convert.ToDouble(value,System.Globalization.CultureInfo.InvariantCulture);
    private static Point2D Point(object? value)=>new(Number(Get(value,"X")),Number(Get(value,"Y")));
    private static (ImportUnit Unit,double Scale) Units(string? value,List<ImportWarning> warnings)
    {
        var unit=value??"Unitless";if(unit.Contains("Millimeter",StringComparison.OrdinalIgnoreCase))return(ImportUnit.Millimetres,1);if(unit.Contains("Centimeter",StringComparison.OrdinalIgnoreCase))return(ImportUnit.Centimetres,10);if(unit.Contains("Inch",StringComparison.OrdinalIgnoreCase))return(ImportUnit.Inches,25.4);if(unit.Equals("Meters",StringComparison.OrdinalIgnoreCase)||unit.Equals("Meter",StringComparison.OrdinalIgnoreCase))return(ImportUnit.Unitless,1000);if(!unit.Contains("Unitless",StringComparison.OrdinalIgnoreCase)&&!unit.Contains("NoUnits",StringComparison.OrdinalIgnoreCase))warnings.Add(new("dxf-units",$"DXF unit '{unit}' is not directly supported; values were treated as millimetres."));return(ImportUnit.Unitless,1);
    }
}
