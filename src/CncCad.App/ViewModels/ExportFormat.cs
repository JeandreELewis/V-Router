namespace CncCad.App.ViewModels;

public sealed record GCodeExportFormat(string Label,string Extension)
{
    public static IReadOnlyList<GCodeExportFormat> All { get; }=[new("G-code .nc",".nc"),new("G-code .txt",".txt"),new("G-code .tap",".tap")];
    public string NormalizePath(string path)=>NormalizePath(path,Extension);
    public static string NormalizePath(string path,string extension)
    {
        if(string.IsNullOrWhiteSpace(path))throw new ArgumentException("Choose an export filename.",nameof(path));
        extension=extension.StartsWith('.')?extension:"."+extension;
        var trimmedPath=path.Trim();
        var directory=Path.GetDirectoryName(trimmedPath);
        var fileName=Path.GetFileName(trimmedPath);
        var hadExpectedExtension=false;
        while(fileName.EndsWith(extension,StringComparison.OrdinalIgnoreCase))
        {
            fileName=fileName[..^extension.Length];
            hadExpectedExtension=true;
        }
        if(string.IsNullOrWhiteSpace(fileName))throw new ArgumentException("Choose an export filename.",nameof(path));
        var normalized=hadExpectedExtension?fileName+extension:Path.ChangeExtension(fileName,extension);
        return string.IsNullOrEmpty(directory)?normalized:Path.Combine(directory,normalized);
    }
    public static string SafeFileStem(string? value)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['<','>',':','"','/','\\','|','?','*']).ToHashSet();
        var safe = new string((value ?? string.Empty).Select(character => char.IsControl(character) || invalid.Contains(character) ? '_' : character).ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(safe) ? "v-router-program" : safe;
    }
    public override string ToString()=>Label;
}

public enum ToolpathExportScope { SelectedToolpath, VisibleCombined, VisibleSeparateFiles }
public sealed record GeneratedToolpathFile(string FileStem,string Extension,string Text,int WarningCount);
