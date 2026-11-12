using CncCad.Cam.Profile;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Nesting;

public sealed class DeterministicNestingOptimizer:INestingOptimizer
{
    public NestingResult Optimize(NestingRequest request)
    {
        var d=request.Definition;Validate(d);var items=d.Parts.SelectMany(p=>Enumerable.Range(0,p.Quantity).Select(i=>CreateItem(p,i,request.Geometry))).ToArray();var orderings=new[]{items.OrderByDescending(x=>x.Area),items.OrderByDescending(x=>Math.Max(x.Bounds.Width,x.Bounds.Height)),items.OrderByDescending(x=>x.Bounds.Width+x.Bounds.Height)};NestingResult? best=null;
        foreach(var ordering in orderings){var candidate=Pack(ordering.ToArray(),d);if(best is null||Compare(candidate.Score,best.Score)<0)best=candidate;}return best!;
    }
    private static NestingResult Pack(Item[] items,NestingJobDefinition definition)
    {
        var result=new NestingResult{Seed=definition.Seed};var sheets=new List<SheetState>();foreach(var item in items){Placement? selected=null;var candidates=sheets.AsEnumerable();if(sheets.Count<definition.SheetTemplate.Quantity)candidates=candidates.Append(new SheetState(CreateSheet(definition,sheets.Count)));foreach(var sheet in candidates){foreach(var rotation in AllowedRotations(item.Part,sheet.Definition)){var contour=Rotate(item.Contour,rotation);var b=BoundingBox.FromPoints(contour);var xs=new[]{definition.EdgeMarginMm}.Concat(sheet.Placements.Select(x=>x.Bounds.MaxX+definition.PartSpacingMm)).Distinct().Order();var ys=new[]{definition.EdgeMarginMm}.Concat(sheet.Placements.Select(x=>x.Bounds.MaxY+definition.PartSpacingMm)).Distinct().Order();foreach(var y in ys)foreach(var x in xs){var moved=Translate(contour,x-b.MinX,y-b.MinY);var bounds=BoundingBox.FromPoints(moved);if(bounds.MaxX>sheet.Definition.WidthMm-definition.EdgeMarginMm+GeometryTolerancePolicy.DefaultEndpointEqualityMm||bounds.MaxY>sheet.Definition.HeightMm-definition.EdgeMarginMm+GeometryTolerancePolicy.DefaultEndpointEqualityMm)continue;if(sheet.Placements.Any(p=>ClipperGeometryAdapter.Overlaps(moved,p.Contour,definition.PartSpacingMm)))continue;selected=new(sheet,item,moved,bounds,rotation,x-b.MinX,y-b.MinY);break;}if(selected is not null)break;}if(selected is not null){if(!sheets.Contains(selected.Sheet))sheets.Add(selected.Sheet);selected.Sheet.Placements.Add(selected);result.Placements.Add(new(){PartId=item.Part.Id,SheetId=selected.Sheet.Definition.Id,CopyIndex=item.CopyIndex,Xmm=selected.X,Ymm=selected.Y,RotationDegrees=selected.Rotation});break;}}
            if(selected is null)result.UnplacedQuantities[item.Part.Id]=result.UnplacedQuantities.GetValueOrDefault(item.Part.Id)+1;}
        result.Sheets.AddRange(sheets.Select(x=>x.Definition));var areas=items.GroupBy(x=>x.Part.Id).ToDictionary(x=>x.Key,x=>x.First().Area);var used=result.Placements.Sum(x=>areas[x.PartId]);var sheetArea=result.Sheets.Sum(x=>x.WidthMm*x.HeightMm);result.Score=new(result.Sheets.Count,Math.Max(0,sheetArea-used),RapidEstimate(result.Placements));if(result.UnplacedQuantities.Count>0)result.Warnings.Add("Some requested parts do not fit within the available sheet quantity.");return result;
    }
    private static Item CreateItem(NestPartDefinition part,int copy,IReadOnlyDictionary<Guid,CadEntity> geometry){if(!geometry.TryGetValue(part.OuterGeometryId,out var entity))throw new InvalidOperationException($"Missing geometry for {part.Name}.");var contour=ClipperGeometryAdapter.ToContour(entity);return new(part,copy,contour,BoundingBox.FromPoints(contour),Math.Abs(ClipperGeometryAdapter.SignedArea(contour)));}
    private static MaterialSheetDefinition CreateSheet(NestingJobDefinition d,int index)=>new(){Id=DeterministicGuid(d.Seed,index),Name=$"Sheet {index+1}",WidthMm=d.SheetTemplate.WidthMm,HeightMm=d.SheetTemplate.HeightMm,ThicknessMm=d.SheetTemplate.ThicknessMm,Grain=d.SheetTemplate.Grain};
    private static Guid DeterministicGuid(int seed,int index){Span<byte> bytes=stackalloc byte[16];BitConverter.TryWriteBytes(bytes,seed);BitConverter.TryWriteBytes(bytes[4..],index);bytes[8]=0x4E;bytes[9]=0x53;return new Guid(bytes);}
    private static IReadOnlyList<Point2D> Rotate(IReadOnlyList<Point2D> source,int degrees){var b=BoundingBox.FromPoints(source);var center=new Point2D((b.MinX+b.MaxX)/2,(b.MinY+b.MaxY)/2);var matrix=Matrix2D.Around(center,Matrix2D.Rotation(degrees));return source.Select(matrix.Transform).ToArray();}
    private static IReadOnlyList<Point2D> Translate(IReadOnlyList<Point2D> source,double x,double y)=>source.Select(p=>new Point2D(p.X+x,p.Y+y)).ToArray();
    private static int Compare(NestingScore a,NestingScore b){var sheets=a.SheetCount.CompareTo(b.SheetCount);if(sheets!=0)return sheets;var waste=a.WasteAreaMm2.CompareTo(b.WasteAreaMm2);return waste!=0?waste:a.EstimatedRapidDistanceMm.CompareTo(b.EstimatedRapidDistanceMm);}
    private static double RapidEstimate(IReadOnlyList<NestedPartPlacement> placements){var distance=0d;var current=new Point2D();foreach(var p in placements){var next=new Point2D(p.Xmm,p.Ymm);distance+=current.DistanceTo(next);current=next;}return distance;}
    private static IReadOnlyList<int> AllowedRotations(NestPartDefinition part,MaterialSheetDefinition sheet)
    {
        if(part.Grain==NestGrainDirection.None||sheet.Grain==NestGrainDirection.None)return part.AllowQuarterTurn?[0,90,180,270]:[0,180];
        if(part.Grain==sheet.Grain)return [0,180];
        return part.AllowQuarterTurn?[90,270]:[];
    }
    private static void Validate(NestingJobDefinition d){if(!double.IsFinite(d.SheetTemplate.WidthMm)||!double.IsFinite(d.SheetTemplate.HeightMm)||!double.IsFinite(d.SheetTemplate.ThicknessMm)||!double.IsFinite(d.PartSpacingMm)||!double.IsFinite(d.EdgeMarginMm))throw new ArgumentException("Sheet dimensions, spacing and margin must be finite.");if(d.SheetTemplate.WidthMm<=0||d.SheetTemplate.HeightMm<=0||d.SheetTemplate.ThicknessMm<=0||d.SheetTemplate.Quantity<=0)throw new ArgumentException("Sheet dimensions, thickness and quantity must be positive.");if(d.PartSpacingMm<0||d.EdgeMarginMm<0)throw new ArgumentException("Spacing and margin cannot be negative.");if(d.Parts.Count==0||d.Parts.Any(x=>x.Quantity<=0))throw new ArgumentException("Add parts with positive quantities before nesting.");}
    private sealed record Item(NestPartDefinition Part,int CopyIndex,IReadOnlyList<Point2D> Contour,BoundingBox Bounds,double Area);
    private sealed class SheetState(MaterialSheetDefinition definition){public MaterialSheetDefinition Definition{get;}=definition;public List<Placement> Placements{get;}=[];}
    private sealed record Placement(SheetState Sheet,Item Item,IReadOnlyList<Point2D> Contour,BoundingBox Bounds,int Rotation,double X,double Y);
}
