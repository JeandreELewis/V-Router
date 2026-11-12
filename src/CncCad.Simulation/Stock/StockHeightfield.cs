using CncCad.Cam.Operations;
using CncCad.Cam.Toolpaths;
using CncCad.Cam.Tools;
using CncCad.Core.Geometry;

namespace CncCad.Simulation.Stock;

public sealed class StockHeightfield
{
    private readonly float[] _heights;
    internal StockHeightfield(double widthMm,double heightMm,double thicknessMm,double cellSizeMm,int columns,int rows)
    { WidthMm=widthMm;HeightMm=heightMm;ThicknessMm=thicknessMm;CellSizeMm=cellSizeMm;Columns=columns;Rows=rows;_heights=new float[columns*rows]; }
    public double WidthMm { get; }
    public double HeightMm { get; }
    public double ThicknessMm { get; }
    public double CellSizeMm { get; }
    public int Columns { get; }
    public int Rows { get; }
    public double this[int column,int row]=>_heights[row*Columns+column];
    internal void Remove(int column,int row,double z)=>_heights[row*Columns+column]=(float)Math.Max(-ThicknessMm,Math.Min(_heights[row*Columns+column],z));
}

public interface IStockSimulationService
{
    StockHeightfield Simulate(double widthMm,double heightMm,double thicknessMm,IEnumerable<CamOperationDefinition> operations);
}

public sealed class CpuStockSimulationService : IStockSimulationService
{
    public StockHeightfield Simulate(double widthMm,double heightMm,double thicknessMm,IEnumerable<CamOperationDefinition> operations)
    {
        if(widthMm<=0||heightMm<=0||thicknessMm<=0)throw new ArgumentOutOfRangeException(nameof(widthMm));
        var active=operations.Where(x=>x.IsEnabled&&x.CalculatedToolpath is not null).ToArray();
        var smallest=active.Select(x=>x.Tool.DiameterMm).Where(x=>x>0).DefaultIfEmpty(8).Min();
        var cell=Math.Clamp(smallest/8,.1,1);cell=Math.Max(cell,Math.Max(widthMm/2048d,heightMm/2048d));
        var columns=Math.Max(1,(int)Math.Ceiling(widthMm/cell));var rows=Math.Max(1,(int)Math.Ceiling(heightMm/cell));
        var field=new StockHeightfield(widthMm,heightMm,thicknessMm,cell,columns,rows);
        foreach(var operation in active)Apply(field,operation);
        return field;
    }

    private static void Apply(StockHeightfield field,CamOperationDefinition definition)
    {
        var point=new Point2D(0,0);var z=0d;
        foreach(var move in definition.CalculatedToolpath!.Moves)
        {
            switch(move)
            {
                case RapidMove rapid:point=rapid.End;z=rapid.Z;break;
                case LinearMove linear:CutLine(field,definition.Tool,point,z,linear.End,linear.Z);point=linear.End;z=linear.Z;break;
                case ArcMove arc:CutArc(field,definition.Tool,point,z,arc);point=arc.End;z=arc.Z;break;
            }
        }
    }
    private static void CutLine(StockHeightfield field,ToolSnapshot tool,Point2D start,double startZ,Point2D end,double endZ)
    {
        var distance=Math.Sqrt(Math.Pow(end.X-start.X,2)+Math.Pow(end.Y-start.Y,2)+Math.Pow(endZ-startZ,2));var count=Math.Max(1,(int)Math.Ceiling(distance/(field.CellSizeMm/2)));
        for(var i=0;i<=count;i++){var t=i/(double)count;Sweep(field,tool,new(start.X+(end.X-start.X)*t,start.Y+(end.Y-start.Y)*t),startZ+(endZ-startZ)*t);}
    }
    private static void CutArc(StockHeightfield field,ToolSnapshot tool,Point2D start,double startZ,ArcMove arc)
    {
        var radius=start.DistanceTo(arc.Center);var a=Math.Atan2(start.Y-arc.Center.Y,start.X-arc.Center.X);var b=Math.Atan2(arc.End.Y-arc.Center.Y,arc.End.X-arc.Center.X);var sweep=b-a;if(arc.Clockwise&&sweep>=0)sweep-=Math.PI*2;else if(!arc.Clockwise&&sweep<=0)sweep+=Math.PI*2;var count=Math.Max(8,(int)Math.Ceiling(Math.Abs(sweep)*radius/(field.CellSizeMm/2)));
        for(var i=0;i<=count;i++){var t=i/(double)count;var angle=a+sweep*t;Sweep(field,tool,new(arc.Center.X+radius*Math.Cos(angle),arc.Center.Y+radius*Math.Sin(angle)),startZ+(arc.Z-startZ)*t);}
    }
    private static void Sweep(StockHeightfield field,ToolSnapshot tool,Point2D center,double tipZ)
    {
        if(tipZ>=0)return;var radius=tool.DiameterMm/2;var minX=Math.Max(0,(int)Math.Floor((center.X-radius)/field.CellSizeMm));var maxX=Math.Min(field.Columns-1,(int)Math.Ceiling((center.X+radius)/field.CellSizeMm));var minY=Math.Max(0,(int)Math.Floor((center.Y-radius)/field.CellSizeMm));var maxY=Math.Min(field.Rows-1,(int)Math.Ceiling((center.Y+radius)/field.CellSizeMm));
        var vbit=tool.Type==ToolType.VBit&&tool.VBitAngleDegrees is >0;var tangent=vbit?Math.Tan(tool.VBitAngleDegrees!.Value*Math.PI/360):0;
        for(var row=minY;row<=maxY;row++)for(var column=minX;column<=maxX;column++)
        {
            var x=(column+.5)*field.CellSizeMm;var y=(row+.5)*field.CellSizeMm;var radial=Math.Sqrt(Math.Pow(x-center.X,2)+Math.Pow(y-center.Y,2));if(radial>radius)continue;
            var surface=vbit?tipZ+Math.Max(0,radial-(tool.TipDiameterMm??0)/2)/tangent:tipZ;if(surface<0)field.Remove(column,row,surface);
        }
    }
}
