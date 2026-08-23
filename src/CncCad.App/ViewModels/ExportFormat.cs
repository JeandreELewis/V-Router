namespace CncCad.App.ViewModels;

public sealed record GCodeExportFormat(string Label,string Extension)
{
    public static IReadOnlyList<GCodeExportFormat> All { get; }=[new("UCCNC .nc",".nc"),new("UCCNC .txt",".txt"),new("UCCNC .tap",".tap")];
    public string NormalizePath(string path)=>Path.ChangeExtension(path,Extension);
    public override string ToString()=>Label;
}
