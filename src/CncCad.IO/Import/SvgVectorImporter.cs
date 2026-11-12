using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml;
using CncCad.Core.Documents;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.IO.Import;

public sealed class SvgVectorImporter:IVectorImportService
{
    public bool CanImport(string extension)=>extension.Equals(".svg",StringComparison.OrdinalIgnoreCase);
    public VectorImportResult Import(VectorImportRequest request)
    {
        var xml=XDocument.Load(request.Content,LoadOptions.SetLineInfo);var root=xml.Root??throw new InvalidDataException("SVG root is missing.");var warnings=new List<ImportWarning>();var approximations=new List<ImportWarning>();var unsupported=new HashSet<string>();var unsupportedDetails=new List<UnsupportedImportElement>();var entities=new List<CadEntity>();var layers=new List<CadLayer>();var defaultLayer=Layer(Path.GetFileNameWithoutExtension(request.FileName)+" Import");layers.Add(defaultLayer);
        var unit=DetectUnit((string?)root.Attribute("width"));var unitScale=unit switch{ImportUnit.Inches=>25.4,ImportUnit.Centimetres=>10,ImportUnit.Pixels=>25.4/96,_=>1};var viewBox=Numbers((string?)root.Attribute("viewBox"));var minX=viewBox.Count==4?viewBox[0]:0;var minY=viewBox.Count==4?viewBox[1]:0;var coordinateHeight=viewBox.Count==4?viewBox[3]:NumberValue((string?)root.Attribute("height"));var declaredWidth=NumberValue((string?)root.Attribute("width"));var viewScale=viewBox.Count==4&&viewBox[2]>0&&declaredWidth>0?declaredWidth/viewBox[2]:1;var scale=request.ScaleToMillimetres*unitScale*viewScale;
        Visit(root,defaultLayer.Id,Matrix2D.Identity);var svgToDocument=Matrix2D.Then(Matrix2D.Then(Matrix2D.Translation(-minX,-minY),Matrix2D.Scale(scale,-scale)),Matrix2D.Translation(request.Target.X,request.Target.Y+coordinateHeight*scale));foreach(var e in entities)e.Transform=Matrix2D.Then(e.Transform,svgToDocument);
        return new(entities,layers,entities.Count==0?new(0,0,0,0):BoundingBox.FromPoints(entities.SelectMany(e=>new[]{new Point2D(e.GetBounds().MinX,e.GetBounds().MinY),new Point2D(e.GetBounds().MaxX,e.GetBounds().MaxY)})),unit,warnings,[..unsupported],unsupportedDetails,scale,approximations);
        void Visit(XElement element,Guid layerId,Matrix2D inherited)
        {
            var display=(string?)element.Attribute("display");if(!request.IncludeHidden&&string.Equals(display,"none",StringComparison.OrdinalIgnoreCase))return;
            var name=element.Name.LocalName.ToLowerInvariant();var transform=Matrix2D.Then(ParseTransform((string?)element.Attribute("transform")),inherited);
            if(name=="g"&&request.PreserveLayers){var label=(string?)element.Attributes().FirstOrDefault(a=>a.Name.LocalName is "label" or "id");if(!string.IsNullOrWhiteSpace(label)){var layer=Layer(label);layers.Add(layer);layerId=layer.Id;}}
            CadEntity? entity=name switch
            {
                "line"=>new LineEntity{Start=P("x1","y1"),End=P("x2","y2")},
                "polyline"=>new PolylineEntity{Points=Points((string?)element.Attribute("points")),IsClosed=false},
                "polygon"=>new PolylineEntity{Points=Points((string?)element.Attribute("points")),IsClosed=true},
                "rect" when N("rx")<=0&&N("ry")<=0=>new RectangleEntity{Origin=P("x","y"),Width=N("width"),Height=N("height")},
                "rect"=>RoundedRectangle(P("x","y"),N("width"),N("height"),Math.Max(N("rx"),N("ry"))),
                "circle"=>new CircleEntity{Center=P("cx","cy"),Radius=N("r")},
                "ellipse"=>new EllipseEntity{Center=P("cx","cy"),RadiusX=N("rx"),RadiusY=N("ry")},
                "path"=>ParsePath((string?)element.Attribute("d"),warnings,approximations),
                _=>null
            };
            if(entity is not null){if(name=="rect"&&(N("rx")>0||N("ry")>0))AddApproximation("svg-rounded-rectangle-cubic","Rounded SVG rectangle corners were represented by cubic Bézier segments.");entity.LayerId=layerId;entity.Transform=transform;entities.Add(entity);}else if(name is not("svg" or "g" or "defs" or "title" or "desc")){unsupported.Add(name);var info=(IXmlLineInfo)element;unsupportedDetails.Add(new(name,info.HasLineInfo()?info.LineNumber:null,info.HasLineInfo()?info.LinePosition:null));}
            foreach(var child in element.Elements())Visit(child,layerId,transform);
            double N(string attribute)=>double.TryParse((string?)element.Attribute(attribute),NumberStyles.Float,CultureInfo.InvariantCulture,out var v)?v:0;
            Point2D P(string x,string y)=>new(N(x),N(y));
        }
        void AddApproximation(string code,string message){if(approximations.Any(item=>item.Code==code))return;approximations.Add(new(code,message));}
    }
    private static CadLayer Layer(string name)=>new(){Name=name};
    private static ImportUnit DetectUnit(string? value){var unit=Regex.Match(value??"",@"[a-zA-Z]+\s*$").Value.ToLowerInvariant();return unit switch{"in"=>ImportUnit.Inches,"px"=>ImportUnit.Pixels,"mm"=>ImportUnit.Millimetres,"cm"=>ImportUnit.Centimetres,"pt" or "pc"=>ImportUnit.Pixels,""=>ImportUnit.Unitless,_=>ImportUnit.Pixels};}
    private static VectorPathEntity RoundedRectangle(Point2D origin,double width,double height,double radius)
    {
        var r=Math.Clamp(radius,0,Math.Min(Math.Abs(width),Math.Abs(height))/2);var k=.5522847498307936;
        return new(){Figures=[new(){Start=new(origin.X+r,origin.Y),IsClosed=true,Segments=[new LinePathSegment(new(origin.X+width-r,origin.Y)),new CubicPathSegment(new(origin.X+width-r+k*r,origin.Y),new(origin.X+width,origin.Y+r-k*r),new(origin.X+width,origin.Y+r)),new LinePathSegment(new(origin.X+width,origin.Y+height-r)),new CubicPathSegment(new(origin.X+width,origin.Y+height-r+k*r),new(origin.X+width-r+k*r,origin.Y+height),new(origin.X+width-r,origin.Y+height)),new LinePathSegment(new(origin.X+r,origin.Y+height)),new CubicPathSegment(new(origin.X+r-k*r,origin.Y+height),new(origin.X,origin.Y+height-r+k*r),new(origin.X,origin.Y+height-r)),new LinePathSegment(new(origin.X,origin.Y+r)),new CubicPathSegment(new(origin.X,origin.Y+r-k*r),new(origin.X+r-k*r,origin.Y),new(origin.X+r,origin.Y))]}]};
    }
    private static List<Point2D> Points(string? value){var n=Numbers(value);var result=new List<Point2D>();for(var i=0;i+1<n.Count;i+=2)result.Add(new(n[i],n[i+1]));return result;}
    private static readonly Regex TokenRegex=new(@"[AaCcHhLlMmQqSsTtVvZz]|[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?",RegexOptions.Compiled);
    private static List<double> Numbers(string? value)=>Regex.Matches(value??"",@"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?").Select(x=>double.Parse(x.Value,CultureInfo.InvariantCulture)).ToList();
    private static double NumberValue(string? value)=>Numbers(value).FirstOrDefault();
    internal static VectorPathEntity ParsePath(string? data,List<ImportWarning>? warnings=null,List<ImportWarning>? approximations=null)
    {
        var tokens=TokenRegex.Matches(data??"").Select(x=>x.Value).ToArray();var path=new VectorPathEntity();VectorPathFigure? figure=null;var i=0;var command=' ';var current=new Point2D();var lastQuadratic=current;var lastCubic=current;var previous=' ';
        double D()=>double.Parse(tokens[i++],CultureInfo.InvariantCulture);Point2D Point(bool relative){var p=new Point2D(D(),D());return relative?current+(p-new Point2D()):p;}
        while(i<tokens.Length){if(char.IsLetter(tokens[i][0]))command=tokens[i++][0];if(command=='Z'||command=='z'){if(figure is not null){figure.IsClosed=true;current=figure.Start;}lastQuadratic=current;lastCubic=current;previous='Z';command=' ';continue;}var relative=char.IsLower(command);var upper=char.ToUpperInvariant(command);switch(upper)
        {case'M':current=Point(relative);figure=new(){Start=current};path.Figures.Add(figure);lastQuadratic=current;lastCubic=current;command=relative?'l':'L';break;case'L':var l=Point(relative);figure?.Segments.Add(new LinePathSegment(l));current=l;lastQuadratic=current;lastCubic=current;break;case'H':var h=D()+(relative?current.X:0);current=new(h,current.Y);figure?.Segments.Add(new LinePathSegment(current));lastQuadratic=current;lastCubic=current;break;case'V':var v=D()+(relative?current.Y:0);current=new(current.X,v);figure?.Segments.Add(new LinePathSegment(current));lastQuadratic=current;lastCubic=current;break;case'Q':var qc=Point(relative);var qe=Point(relative);figure?.Segments.Add(new QuadraticPathSegment(qc,qe));current=qe;lastQuadratic=qc;lastCubic=current;break;case'T':var tc=previous is 'Q' or 'T'?new Point2D(2*current.X-lastQuadratic.X,2*current.Y-lastQuadratic.Y):current;var te=Point(relative);figure?.Segments.Add(new QuadraticPathSegment(tc,te));current=te;lastQuadratic=tc;lastCubic=current;break;case'C':var c1=Point(relative);var c2=Point(relative);var ce=Point(relative);figure?.Segments.Add(new CubicPathSegment(c1,c2,ce));current=ce;lastCubic=c2;lastQuadratic=current;break;case'S':var sc1=previous is 'C' or 'S'?new Point2D(2*current.X-lastCubic.X,2*current.Y-lastCubic.Y):current;var sc2=Point(relative);var se=Point(relative);figure?.Segments.Add(new CubicPathSegment(sc1,sc2,se));current=se;lastCubic=sc2;lastQuadratic=current;break;case'A':var rx=D();var ry=D();var rotation=D();var large=D()!=0;var sweep=D()!=0;var ae=Point(relative);if(figure is not null){AddArc(figure,current,ae,rx,ry,rotation,large,sweep);var approximation=new ImportWarning("svg-arc-cubic","SVG elliptical arc segments were represented by cubic Bézier segments.");if(approximations?.Any(item=>item.Code==approximation.Code)!=true)approximations?.Add(approximation);}current=ae;lastQuadratic=current;lastCubic=current;break;default:throw new InvalidDataException($"Unsupported SVG path command '{command}'.");}previous=upper;}
        return path;
    }
    private static void AddArc(VectorPathFigure figure,Point2D start,Point2D end,double rx,double ry,double rotationDegrees,bool large,bool sweep)
    {
        rx=Math.Abs(rx);ry=Math.Abs(ry);if(rx<1e-12||ry<1e-12||start.DistanceTo(end)<1e-12){figure.Segments.Add(new LinePathSegment(end));return;}
        var phi=rotationDegrees*Math.PI/180;var cos=Math.Cos(phi);var sin=Math.Sin(phi);var dx=(start.X-end.X)/2;var dy=(start.Y-end.Y)/2;var x1=cos*dx+sin*dy;var y1=-sin*dx+cos*dy;var lambda=x1*x1/(rx*rx)+y1*y1/(ry*ry);if(lambda>1){var factor=Math.Sqrt(lambda);rx*=factor;ry*=factor;}
        var numerator=Math.Max(0,rx*rx*ry*ry-rx*rx*y1*y1-ry*ry*x1*x1);var denominator=rx*rx*y1*y1+ry*ry*x1*x1;var sign=large==sweep?-1:1;var coef=denominator<1e-20?0:sign*Math.Sqrt(numerator/denominator);var cx1=coef*(rx*y1/ry);var cy1=coef*(-ry*x1/rx);var cx=cos*cx1-sin*cy1+(start.X+end.X)/2;var cy=sin*cx1+cos*cy1+(start.Y+end.Y)/2;
        static double Angle(double ux,double uy,double vx,double vy)=>Math.Atan2(ux*vy-uy*vx,ux*vx+uy*vy);
        var ux=(x1-cx1)/rx;var uy=(y1-cy1)/ry;var vx=(-x1-cx1)/rx;var vy=(-y1-cy1)/ry;var theta=Math.Atan2(uy,ux);var delta=Angle(ux,uy,vx,vy);if(!sweep&&delta>0)delta-=2*Math.PI;if(sweep&&delta<0)delta+=2*Math.PI;var count=Math.Max(1,(int)Math.Ceiling(Math.Abs(delta)/(Math.PI/2)));var step=delta/count;
        Point2D Map(double x,double y)=>new(cx+rx*(cos*x-sin*y),cy+ry*(sin*x+cos*y));
        for(var n=0;n<count;n++){var a=theta+n*step;var b=a+step;var alpha=4d/3*Math.Tan((b-a)/4);var p1=Map(Math.Cos(a)-alpha*Math.Sin(a),Math.Sin(a)+alpha*Math.Cos(a));var p2=Map(Math.Cos(b)+alpha*Math.Sin(b),Math.Sin(b)-alpha*Math.Cos(b));var p3=Map(Math.Cos(b),Math.Sin(b));figure.Segments.Add(new CubicPathSegment(p1,p2,p3));}
    }
    private static Matrix2D ParseTransform(string? value){var result=Matrix2D.Identity;foreach(Match m in Regex.Matches(value??"",@"(matrix|translate|scale|rotate|skewX|skewY)\s*\(([^)]*)\)",RegexOptions.IgnoreCase)){var v=Numbers(m.Groups[2].Value);if(v.Count==0)continue;var next=m.Groups[1].Value.ToLowerInvariant() switch{"matrix" when v.Count>=6=>new(v[0],v[1],v[2],v[3],v[4],v[5]),"translate"=>Matrix2D.Translation(v[0],v.Count>1?v[1]:0),"scale"=>Matrix2D.Scale(v[0],v.Count>1?v[1]:v[0]),"rotate" when v.Count>=3=>Matrix2D.Around(new(v[1],v[2]),Matrix2D.Rotation(v[0])),"rotate"=>Matrix2D.Rotation(v[0]),"skewx"=>new(1,0,Math.Tan(v[0]*Math.PI/180),1,0,0),"skewy"=>new(1,Math.Tan(v[0]*Math.PI/180),0,1,0,0),_=>Matrix2D.Identity};result=Matrix2D.Then(result,next);}return result;}
}
