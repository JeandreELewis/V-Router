using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CncCad.Cam.Safety;
using CncCad.Cam.Tools;
using CncCad.Core.Documents;

namespace CncCad.Infrastructure.Persistence;

public sealed record LibraryPackage
{
    public int SchemaVersion { get; init; }=1;
    public IReadOnlyList<CncTool> Tools { get; init; }=[];
    public IReadOnlyList<MachineProfileDefinition> Machines { get; init; }=[];
    public IReadOnlyList<MaterialDefinition> Materials { get; init; }=[];
    public IReadOnlyList<ToolCuttingPreset> Presets { get; init; }=[];
}

public sealed record LibraryImportResult(int Tools,int Machines,int Materials,int Presets,IReadOnlyList<string> Warnings);

public sealed class LibraryExchangeService(IToolLibrary tools,IMachineProfileRepository machines,IMaterialLibrary materials,IToolCuttingPresetRepository presets)
{
    private static readonly JsonSerializerOptions JsonOptions=new(){WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase,Converters={new JsonStringEnumConverter()}};

    public async Task ExportJsonAsync(Stream destination,CancellationToken cancellationToken=default)
    {
        var package=new LibraryPackage{Tools=await tools.GetAllAsync(cancellationToken),Machines=await machines.GetAllAsync(cancellationToken),Materials=await materials.GetAllAsync(cancellationToken),Presets=await presets.GetAllAsync(cancellationToken)};
        await JsonSerializer.SerializeAsync(destination,package,JsonOptions,cancellationToken);
    }

    public async Task<LibraryImportResult> ImportJsonAsync(Stream source,CancellationToken cancellationToken=default)
    {
        var package=await JsonSerializer.DeserializeAsync<LibraryPackage>(source,JsonOptions,cancellationToken)??throw new InvalidDataException("Library package is empty.");
        if(package.SchemaVersion!=1)throw new NotSupportedException($"Library schema {package.SchemaVersion} is not supported.");
        var warnings=new List<string>();var toolCount=0;var machineCount=0;var materialCount=0;var presetCount=0;
        var knownTools=(await tools.GetAllAsync(cancellationToken)).Select(value=>value.Id).ToHashSet();var knownMachines=(await machines.GetAllAsync(cancellationToken)).Select(value=>value.Id).ToHashSet();var knownMaterials=(await materials.GetAllAsync(cancellationToken)).Select(value=>value.Id).ToHashSet();
        foreach(var tool in package.Tools){var errors=tool.Validate();if(errors.Count>0){warnings.Add($"Skipped tool '{tool.Name}': {string.Join(" ",errors)}");continue;}await tools.SaveAsync(tool,cancellationToken);knownTools.Add(tool.Id);toolCount++;}
        foreach(var machine in package.Machines){if(!machine.Snapshot().IsComplete){warnings.Add($"Skipped incomplete machine '{machine.Name}'.");continue;}await machines.SaveAsync(machine,cancellationToken);knownMachines.Add(machine.Id);machineCount++;}
        foreach(var material in package.Materials){if(string.IsNullOrWhiteSpace(material.Name)){warnings.Add("Skipped an unnamed material.");continue;}await materials.SaveAsync(material,cancellationToken);knownMaterials.Add(material.Id);materialCount++;}
        foreach(var preset in package.Presets){var errors=preset.Validate();if(errors.Count>0){warnings.Add($"Skipped cutting preset {preset.Id}: {string.Join(" ",errors)}");continue;}var missing=new List<string>();if(!knownTools.Contains(preset.ToolId))missing.Add("tool");if(!knownMachines.Contains(preset.MachineId))missing.Add("machine");if(!knownMaterials.Contains(preset.MaterialId))missing.Add("material");if(missing.Count>0){warnings.Add($"Skipped cutting preset {preset.Id}: missing {string.Join(", ",missing)} reference(s).");continue;}await presets.SaveAsync(preset,cancellationToken);presetCount++;}
        return new(toolCount,machineCount,materialCount,presetCount,warnings);
    }

    public async Task ExportToolsCsvAsync(Stream destination,CancellationToken cancellationToken=default)
    {
        await using var writer=new StreamWriter(destination,new UTF8Encoding(false),leaveOpen:true);await writer.WriteLineAsync("Id,Name,ToolNumber,Type,DiameterMm,CuttingLengthMm,OverallLengthMm,ShankDiameterMm,StickOutMm,FluteCount,FeedRateMmPerMin,PlungeRateMmPerMin,SpindleRpm,StepDownMm,StepOverPercent,VBitAngleDegrees,TipDiameterMm,Manufacturer,ManufacturerPartNumber,Supplier,ProductUrl,Notes,ImageRelativePath");
        foreach(var tool in await tools.GetAllAsync(cancellationToken))await writer.WriteLineAsync(string.Join(',',Csv(tool.Id),Csv(tool.Name),Csv(tool.ToolNumber),Csv(tool.Type),Csv(tool.DiameterMm),Csv(tool.CuttingLengthMm),Csv(tool.OverallLengthMm),Csv(tool.ShankDiameterMm),Csv(tool.StickOutMm),Csv(tool.FluteCount),Csv(tool.FeedRateMmPerMin),Csv(tool.PlungeRateMmPerMin),Csv(tool.SpindleRpm),Csv(tool.StepDownMm),Csv(tool.StepOverPercent),Csv(tool.VBitAngleDegrees),Csv(tool.TipDiameterMm),Csv(tool.Manufacturer),Csv(tool.ManufacturerPartNumber),Csv(tool.Supplier),Csv(tool.ProductUrl),Csv(tool.Notes),Csv(tool.ImageRelativePath)));
        await writer.FlushAsync(cancellationToken);
    }

    public async Task<LibraryImportResult> ImportToolsCsvAsync(Stream source,CancellationToken cancellationToken=default)
    {
        using var reader=new StreamReader(source,Encoding.UTF8,detectEncodingFromByteOrderMarks:true,leaveOpen:true);var header=await ReadCsvRecordAsync(reader,cancellationToken)??throw new InvalidDataException("Tool CSV is empty.");var columns=ParseCsv(header).Select((name,index)=>(name,index)).ToDictionary(x=>x.name,x=>x.index,StringComparer.OrdinalIgnoreCase);var warnings=new List<string>();var count=0;var row=1;
        while(await ReadCsvRecordAsync(reader,cancellationToken) is{} line){row++;if(string.IsNullOrWhiteSpace(line))continue;try{var value=ParseCsv(line);string Get(string name)=>columns.TryGetValue(name,out var index)&&index<value.Count?value[index]:"";var tool=new CncTool{Id=Guid.TryParse(Get("Id"),out var id)?id:Guid.NewGuid(),Name=Get("Name"),ToolNumber=Int(Get("ToolNumber")),Type=Enum.TryParse<ToolType>(Get("Type"),true,out var type)?type:ToolType.Other,DiameterMm=Double(Get("DiameterMm")),CuttingLengthMm=NullableDouble(Get("CuttingLengthMm")),OverallLengthMm=NullableDouble(Get("OverallLengthMm")),ShankDiameterMm=NullableDouble(Get("ShankDiameterMm")),StickOutMm=NullableDouble(Get("StickOutMm")),FluteCount=Int(Get("FluteCount")),FeedRateMmPerMin=Double(Get("FeedRateMmPerMin")),PlungeRateMmPerMin=Double(Get("PlungeRateMmPerMin")),SpindleRpm=Int(Get("SpindleRpm"))??0,StepDownMm=Double(Get("StepDownMm")),StepOverPercent=Double(Get("StepOverPercent")),VBitAngleDegrees=NullableDouble(Get("VBitAngleDegrees")),TipDiameterMm=NullableDouble(Get("TipDiameterMm")),Manufacturer=NullIfEmpty(Get("Manufacturer")),ManufacturerPartNumber=NullIfEmpty(Get("ManufacturerPartNumber")),Supplier=NullIfEmpty(Get("Supplier")),ProductUrl=NullIfEmpty(Get("ProductUrl")),Notes=NullIfEmpty(Get("Notes")),ImageRelativePath=NullIfEmpty(Get("ImageRelativePath"))};var errors=tool.Validate();if(errors.Count>0)throw new InvalidDataException(string.Join(" ",errors));await tools.SaveAsync(tool,cancellationToken);count++;}catch(Exception ex)when(ex is FormatException or InvalidDataException){warnings.Add($"Record {row}: {ex.Message}");}}
        return new(count,0,0,0,warnings);
    }

    public async Task ExportMaterialsCsvAsync(Stream destination,CancellationToken cancellationToken=default)
    {
        await using var writer=new StreamWriter(destination,new UTF8Encoding(false),leaveOpen:true);await writer.WriteLineAsync("Id,Name,Category,Notes");
        foreach(var material in await materials.GetAllAsync(cancellationToken))await writer.WriteLineAsync(string.Join(',',Csv(material.Id),Csv(material.Name),Csv(material.Category),Csv(material.Notes)));
        await writer.FlushAsync(cancellationToken);
    }

    public async Task<LibraryImportResult> ImportMaterialsCsvAsync(Stream source,CancellationToken cancellationToken=default)
    {
        using var reader=new StreamReader(source,Encoding.UTF8,detectEncodingFromByteOrderMarks:true,leaveOpen:true);var columns=await CsvColumnsAsync(reader,"Material",cancellationToken);var warnings=new List<string>();var count=0;var row=1;
        while(await ReadCsvRecordAsync(reader,cancellationToken) is{} line){row++;if(string.IsNullOrWhiteSpace(line))continue;try{var value=ParseCsv(line);string Get(string name)=>GetColumn(columns,value,name);var material=new MaterialDefinition{Id=GuidValue(Get("Id")),Name=Get("Name"),Category=Get("Category"),Notes=NullIfEmpty(Get("Notes"))};if(string.IsNullOrWhiteSpace(material.Name))throw new InvalidDataException("Material name is required.");await materials.SaveAsync(material,cancellationToken);count++;}catch(Exception ex)when(ex is FormatException or InvalidDataException or ArgumentException){warnings.Add($"Record {row}: {ex.Message}");}}
        return new(0,0,count,0,warnings);
    }

    public async Task ExportMachinesCsvAsync(Stream destination,CancellationToken cancellationToken=default)
    {
        await using var writer=new StreamWriter(destination,new UTF8Encoding(false),leaveOpen:true);await writer.WriteLineAsync("Id,Name,MinimumXmm,MaximumXmm,MinimumYmm,MaximumYmm,MinimumZmm,MaximumZmm,RapidRateMmPerMin,ToolChangeSeconds,MaximumSpindleRpm,DefaultClearanceMm,HolderDiameterMm,HolderLengthMm,ToolChangeXmm,ToolChangeYmm,ParkXmm,ParkYmm,ParkEnabled,AllowedSpoilboardDepthMm,PostProcessorId,ControllerFamily,OutputUnit,SupportsArcs,ToolChangeMode");
        foreach(var machine in await machines.GetAllAsync(cancellationToken))await writer.WriteLineAsync(string.Join(',',Csv(machine.Id),Csv(machine.Name),Csv(machine.MinimumXmm),Csv(machine.MaximumXmm),Csv(machine.MinimumYmm),Csv(machine.MaximumYmm),Csv(machine.MinimumZmm),Csv(machine.MaximumZmm),Csv(machine.RapidRateMmPerMin),Csv(machine.ToolChangeSeconds),Csv(machine.MaximumSpindleRpm),Csv(machine.DefaultClearanceMm),Csv(machine.HolderDiameterMm),Csv(machine.HolderLengthMm),Csv(machine.ToolChangeXmm),Csv(machine.ToolChangeYmm),Csv(machine.ParkXmm),Csv(machine.ParkYmm),Csv(machine.ParkEnabled),Csv(machine.AllowedSpoilboardDepthMm),Csv(machine.PostProcessorId),Csv(machine.ControllerFamily),Csv(machine.OutputUnit),Csv(machine.SupportsArcs),Csv(machine.ToolChangeMode)));
        await writer.FlushAsync(cancellationToken);
    }

    public async Task<LibraryImportResult> ImportMachinesCsvAsync(Stream source,CancellationToken cancellationToken=default)
    {
        using var reader=new StreamReader(source,Encoding.UTF8,detectEncodingFromByteOrderMarks:true,leaveOpen:true);var columns=await CsvColumnsAsync(reader,"Machine",cancellationToken);var warnings=new List<string>();var count=0;var row=1;
        while(await ReadCsvRecordAsync(reader,cancellationToken) is{} line){row++;if(string.IsNullOrWhiteSpace(line))continue;try{var value=ParseCsv(line);string Get(string name)=>GetColumn(columns,value,name);var machine=new MachineProfileDefinition{Id=GuidValue(Get("Id")),Name=Get("Name"),MinimumXmm=Double(Get("MinimumXmm")),MaximumXmm=Double(Get("MaximumXmm")),MinimumYmm=Double(Get("MinimumYmm")),MaximumYmm=Double(Get("MaximumYmm")),MinimumZmm=Double(Get("MinimumZmm")),MaximumZmm=Double(Get("MaximumZmm")),RapidRateMmPerMin=Double(Get("RapidRateMmPerMin")),ToolChangeSeconds=Double(Get("ToolChangeSeconds")),MaximumSpindleRpm=Int(Get("MaximumSpindleRpm"))??0,DefaultClearanceMm=Double(Get("DefaultClearanceMm")),HolderDiameterMm=Double(Get("HolderDiameterMm")),HolderLengthMm=Double(Get("HolderLengthMm")),ToolChangeXmm=Double(Get("ToolChangeXmm")),ToolChangeYmm=Double(Get("ToolChangeYmm")),ParkXmm=Double(Get("ParkXmm")),ParkYmm=Double(Get("ParkYmm")),ParkEnabled=Bool(Get("ParkEnabled")),AllowedSpoilboardDepthMm=Double(Get("AllowedSpoilboardDepthMm")),PostProcessorId=Get("PostProcessorId"),ControllerFamily=EnumValue<ControllerFamily>(Get("ControllerFamily")),OutputUnit=EnumValue<MeasurementUnit>(Get("OutputUnit")),SupportsArcs=Bool(Get("SupportsArcs")),ToolChangeMode=EnumValue<MachineToolChangeMode>(Get("ToolChangeMode"))};if(!machine.Snapshot().IsComplete)throw new InvalidDataException("Machine safety data is incomplete or invalid.");await machines.SaveAsync(machine,cancellationToken);count++;}catch(Exception ex)when(ex is FormatException or InvalidDataException or ArgumentException){warnings.Add($"Record {row}: {ex.Message}");}}
        return new(0,count,0,0,warnings);
    }

    public async Task ExportPresetsCsvAsync(Stream destination,CancellationToken cancellationToken=default)
    {
        await using var writer=new StreamWriter(destination,new UTF8Encoding(false),leaveOpen:true);await writer.WriteLineAsync("Id,ToolId,MachineId,MaterialId,FeedRateMmPerMin,PlungeRateMmPerMin,SpindleRpm,PassDepthMm,StepOverPercent");
        foreach(var preset in await presets.GetAllAsync(cancellationToken))await writer.WriteLineAsync(string.Join(',',Csv(preset.Id),Csv(preset.ToolId),Csv(preset.MachineId),Csv(preset.MaterialId),Csv(preset.FeedRateMmPerMin),Csv(preset.PlungeRateMmPerMin),Csv(preset.SpindleRpm),Csv(preset.PassDepthMm),Csv(preset.StepOverPercent)));
        await writer.FlushAsync(cancellationToken);
    }

    public async Task<LibraryImportResult> ImportPresetsCsvAsync(Stream source,CancellationToken cancellationToken=default)
    {
        using var reader=new StreamReader(source,Encoding.UTF8,detectEncodingFromByteOrderMarks:true,leaveOpen:true);var columns=await CsvColumnsAsync(reader,"Preset",cancellationToken);var warnings=new List<string>();var count=0;var row=1;var knownTools=(await tools.GetAllAsync(cancellationToken)).Select(value=>value.Id).ToHashSet();var knownMachines=(await machines.GetAllAsync(cancellationToken)).Select(value=>value.Id).ToHashSet();var knownMaterials=(await materials.GetAllAsync(cancellationToken)).Select(value=>value.Id).ToHashSet();
        while(await ReadCsvRecordAsync(reader,cancellationToken) is{} line){row++;if(string.IsNullOrWhiteSpace(line))continue;try{var value=ParseCsv(line);string Get(string name)=>GetColumn(columns,value,name);var preset=new ToolCuttingPreset{Id=GuidValue(Get("Id")),ToolId=GuidValue(Get("ToolId")),MachineId=GuidValue(Get("MachineId")),MaterialId=GuidValue(Get("MaterialId")),FeedRateMmPerMin=Double(Get("FeedRateMmPerMin")),PlungeRateMmPerMin=Double(Get("PlungeRateMmPerMin")),SpindleRpm=Int(Get("SpindleRpm"))??0,PassDepthMm=Double(Get("PassDepthMm")),StepOverPercent=Double(Get("StepOverPercent"))};var errors=preset.Validate();if(errors.Count>0)throw new InvalidDataException(string.Join(" ",errors));var missing=new List<string>();if(!knownTools.Contains(preset.ToolId))missing.Add("tool");if(!knownMachines.Contains(preset.MachineId))missing.Add("machine");if(!knownMaterials.Contains(preset.MaterialId))missing.Add("material");if(missing.Count>0)throw new InvalidDataException($"Missing {string.Join(", ",missing)} reference(s).");await presets.SaveAsync(preset,cancellationToken);count++;}catch(Exception ex)when(ex is FormatException or InvalidDataException or ArgumentException){warnings.Add($"Record {row}: {ex.Message}");}}
        return new(0,0,0,count,warnings);
    }

    public static IReadOnlyList<T> Search<T>(IEnumerable<T> source,string? query,Func<T,string> text)=>string.IsNullOrWhiteSpace(query)?source.ToArray():source.Where(x=>text(x).Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray();
    private static string Csv(object? value){var text=value switch{null=>"",IFormattable formattable=>formattable.ToString(null,CultureInfo.InvariantCulture)??"",_=>value.ToString()??""};return text.IndexOfAny([',','"','\r','\n'])>=0?$"\"{text.Replace("\"","\"\"")}\"":text;}
    private static async Task<string?> ReadCsvRecordAsync(StreamReader reader,CancellationToken cancellationToken){var first=await reader.ReadLineAsync(cancellationToken);if(first is null)return null;var record=new StringBuilder(first);while(HasOpenQuote(record)){var next=await reader.ReadLineAsync(cancellationToken)??throw new InvalidDataException("Unterminated quoted CSV record.");record.Append('\n').Append(next);}return record.ToString();}
    private static bool HasOpenQuote(StringBuilder text){var quoted=false;for(var index=0;index<text.Length;index++)if(text[index]=='"'){if(quoted&&index+1<text.Length&&text[index+1]=='"')index++;else quoted=!quoted;}return quoted;}
    private static List<string> ParseCsv(string line){var result=new List<string>();var value=new StringBuilder();var quoted=false;for(var i=0;i<line.Length;i++){var c=line[i];if(c=='"'){if(quoted&&i+1<line.Length&&line[i+1]=='"'){value.Append('"');i++;}else quoted=!quoted;}else if(c==','&&!quoted){result.Add(value.ToString());value.Clear();}else value.Append(c);}if(quoted)throw new InvalidDataException("Unterminated quoted field.");result.Add(value.ToString());return result;}
    private static async Task<Dictionary<string,int>> CsvColumnsAsync(StreamReader reader,string kind,CancellationToken cancellationToken){var header=await ReadCsvRecordAsync(reader,cancellationToken)??throw new InvalidDataException($"{kind} CSV is empty.");return ParseCsv(header).Select((name,index)=>(name,index)).ToDictionary(value=>value.name,value=>value.index,StringComparer.OrdinalIgnoreCase);}
    private static string GetColumn(IReadOnlyDictionary<string,int> columns,IReadOnlyList<string> values,string name)=>columns.TryGetValue(name,out var index)&&index<values.Count?values[index]:"";
    private static Guid GuidValue(string value)=>Guid.TryParse(value,out var parsed)?parsed:throw new FormatException($"'{value}' is not a valid identifier.");
    private static T EnumValue<T>(string value)where T:struct,Enum=>Enum.TryParse<T>(value,true,out var parsed)&&Enum.IsDefined(parsed)?parsed:throw new FormatException($"'{value}' is not a valid {typeof(T).Name} value.");
    private static bool Bool(string value)=>bool.TryParse(value,out var parsed)?parsed:value switch{"1"=>true,"0"=>false,_=>throw new FormatException($"'{value}' is not a valid Boolean value.")};
    private static int? Int(string value)=>string.IsNullOrWhiteSpace(value)?null:int.Parse(value,CultureInfo.InvariantCulture);
    private static double Double(string value)=>double.Parse(value,CultureInfo.InvariantCulture);
    private static double? NullableDouble(string value)=>string.IsNullOrWhiteSpace(value)?null:Double(value);
    private static string? NullIfEmpty(string value)=>string.IsNullOrEmpty(value)?null:value;
}
