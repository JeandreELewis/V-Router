using CncCad.Core.Documents;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.IO.Import;

public enum ImportUnit { Millimetres, Centimetres, Inches, Pixels, Unitless }
public sealed record VectorImportRequest(string FileName,Stream Content,double ScaleToMillimetres=1,Point2D Target=default,bool PreserveLayers=true,bool IncludeHidden=false);
public sealed record ImportWarning(string Code,string Message);
public sealed record VectorImportResult(IReadOnlyList<CadEntity> Entities,IReadOnlyList<CadLayer> Layers,BoundingBox Bounds,ImportUnit DetectedUnit,IReadOnlyList<ImportWarning> Warnings,IReadOnlyList<string> Unsupported)
{
    public bool Success=>Entities.Count>0;
}
public interface IVectorImportService
{
    bool CanImport(string extension);
    VectorImportResult Import(VectorImportRequest request);
}
public sealed class CompositeVectorImportService(IEnumerable<IVectorImportService> importers):IVectorImportService
{
    private readonly IVectorImportService[] _importers=[..importers];
    public bool CanImport(string extension)=>_importers.Any(x=>x.CanImport(extension));
    public VectorImportResult Import(VectorImportRequest request)=>_importers.FirstOrDefault(x=>x.CanImport(Path.GetExtension(request.FileName)))?.Import(request)??throw new NotSupportedException("Only SVG and DXF vector files are supported.");
}
