using CncCad.Cam.Safety;
using CncCad.Cam.Tools;
using CncCad.Infrastructure.Persistence;

namespace CncCad.Core.Tests;

public sealed class LibraryExchangeTests
{
    [Fact]
    public async Task ToolCsvRoundTripPreservesEveryToolFieldIncludingMultilineNotes()
    {
        var sourceTool=new CncTool{Name="6 mm, finishing",ToolNumber=7,Type=ToolType.CompressionBit,DiameterMm=6,CuttingLengthMm=22,OverallLengthMm=60,ShankDiameterMm=6,StickOutMm=31,FluteCount=2,FeedRateMmPerMin=2345.6,PlungeRateMmPerMin=456.7,SpindleRpm=17500,StepDownMm=2.5,StepOverPercent=37.5,VBitAngleDegrees=null,TipDiameterMm=.1,Manufacturer="Router Tools",ManufacturerPartNumber="RT-6",Supplier="Local, supplier",ProductUrl="https://example.invalid/tool?id=7",Notes="First line\nSecond, quoted \"value\"",ImageRelativePath="tools/rt-6.png"};
        var source=new MemoryLibrary();await source.SaveAsync(sourceTool);var export=new LibraryExchangeService(source,source,source,source);await using var csv=new MemoryStream();await export.ExportToolsCsvAsync(csv);csv.Position=0;
        var destination=new MemoryLibrary();var result=await new LibraryExchangeService(destination,destination,destination,destination).ImportToolsCsvAsync(csv);
        Assert.Equal(1,result.Tools);Assert.Empty(result.Warnings);var restored=Assert.Single(await destination.GetAllAsync());
        Assert.Equal(sourceTool.Id,restored.Id);Assert.Equal(sourceTool.Name,restored.Name);Assert.Equal(sourceTool.CuttingLengthMm,restored.CuttingLengthMm);Assert.Equal(sourceTool.OverallLengthMm,restored.OverallLengthMm);Assert.Equal(sourceTool.ShankDiameterMm,restored.ShankDiameterMm);Assert.Equal(sourceTool.StickOutMm,restored.StickOutMm);Assert.Equal(sourceTool.FluteCount,restored.FluteCount);Assert.Equal(sourceTool.Manufacturer,restored.Manufacturer);Assert.Equal(sourceTool.ManufacturerPartNumber,restored.ManufacturerPartNumber);Assert.Equal(sourceTool.Supplier,restored.Supplier);Assert.Equal(sourceTool.ProductUrl,restored.ProductUrl);Assert.Equal(sourceTool.Notes,restored.Notes);Assert.Equal(sourceTool.ImageRelativePath,restored.ImageRelativePath);
    }

    [Fact]
    public async Task JsonImportRejectsDanglingPresetReferences()
    {
        var source=new MemoryLibrary();await source.SaveAsync(new ToolCuttingPreset{ToolId=Guid.NewGuid(),MachineId=Guid.NewGuid(),MaterialId=Guid.NewGuid(),FeedRateMmPerMin=1000,PlungeRateMmPerMin=300,SpindleRpm=18000,PassDepthMm=2,StepOverPercent=40});var exchange=new LibraryExchangeService(source,source,source,source);await using var json=new MemoryStream();await exchange.ExportJsonAsync(json);json.Position=0;
        var destination=new MemoryLibrary();var result=await new LibraryExchangeService(destination,destination,destination,destination).ImportJsonAsync(json);
        Assert.Equal(0,result.Presets);Assert.Contains(result.Warnings,message=>message.Contains("missing tool, machine, material",StringComparison.OrdinalIgnoreCase));Assert.Empty(await ((IToolCuttingPresetRepository)destination).GetAllAsync());
    }

    [Fact]
    public async Task MaterialMachineAndPresetCsvRoundTripEveryPersistedField()
    {
        var source=new MemoryLibrary();var tool=new CncTool{Name="Referenced tool",DiameterMm=6,CuttingLengthMm=20,OverallLengthMm=50,ShankDiameterMm=6,StickOutMm=30};await source.SaveAsync(tool);var machine=new MachineProfileDefinition{Name="Shop, router",MinimumXmm=-25,MaximumXmm=1250,MinimumYmm=-15,MaximumYmm=850,MinimumZmm=-180,MaximumZmm=75,RapidRateMmPerMin=7654.3,ToolChangeSeconds=42.5,MaximumSpindleRpm=30000,DefaultClearanceMm=8.5,HolderDiameterMm=44,HolderLengthMm=57,ToolChangeXmm=12,ToolChangeYmm=34,ParkXmm=100,ParkYmm=200,ParkEnabled=true,AllowedSpoilboardDepthMm=.75,PostProcessorId="linuxcnc",ControllerFamily=ControllerFamily.LinuxCnc,OutputUnit=CncCad.Core.Documents.MeasurementUnit.Inches,SupportsArcs=false,ToolChangeMode=MachineToolChangeMode.ManualPause};var material=new MaterialDefinition{Name="Birch, premium",Category="Sheet goods",Notes="Face A\nQuoted \"grade\""};var preset=new ToolCuttingPreset{ToolId=tool.Id,MachineId=machine.Id,MaterialId=material.Id,FeedRateMmPerMin=2345.67,PlungeRateMmPerMin=345.67,SpindleRpm=17654,PassDepthMm=2.75,StepOverPercent=37.25};await ((IMachineProfileRepository)source).SaveAsync(machine);await ((IMaterialLibrary)source).SaveAsync(material);await source.SaveAsync(preset);var exchange=new LibraryExchangeService(source,source,source,source);await using var machineCsv=new MemoryStream();await using var materialCsv=new MemoryStream();await using var presetCsv=new MemoryStream();await exchange.ExportMachinesCsvAsync(machineCsv);await exchange.ExportMaterialsCsvAsync(materialCsv);await exchange.ExportPresetsCsvAsync(presetCsv);
        var destination=new MemoryLibrary();await destination.SaveAsync(tool);var importer=new LibraryExchangeService(destination,destination,destination,destination);machineCsv.Position=0;materialCsv.Position=0;presetCsv.Position=0;var machineResult=await importer.ImportMachinesCsvAsync(machineCsv);var materialResult=await importer.ImportMaterialsCsvAsync(materialCsv);var presetResult=await importer.ImportPresetsCsvAsync(presetCsv);Assert.Equal(1,machineResult.Machines);Assert.Equal(1,materialResult.Materials);Assert.Equal(1,presetResult.Presets);Assert.Empty(machineResult.Warnings);Assert.Empty(materialResult.Warnings);Assert.Empty(presetResult.Warnings);Assert.Equivalent(machine,Assert.Single(await ((IMachineProfileRepository)destination).GetAllAsync()),strict:true);Assert.Equivalent(material,Assert.Single(await ((IMaterialLibrary)destination).GetAllAsync()),strict:true);Assert.Equivalent(preset,Assert.Single(await ((IToolCuttingPresetRepository)destination).GetAllAsync()),strict:true);
    }

    [Fact]
    public async Task PresetCsvRejectsEveryDanglingReferenceBeforeSaving()
    {
        var source=new MemoryLibrary();await source.SaveAsync(new ToolCuttingPreset{ToolId=Guid.NewGuid(),MachineId=Guid.NewGuid(),MaterialId=Guid.NewGuid(),FeedRateMmPerMin=1000,PlungeRateMmPerMin=300,SpindleRpm=18000,PassDepthMm=2,StepOverPercent=40});await using var csv=new MemoryStream();await new LibraryExchangeService(source,source,source,source).ExportPresetsCsvAsync(csv);csv.Position=0;var destination=new MemoryLibrary();var result=await new LibraryExchangeService(destination,destination,destination,destination).ImportPresetsCsvAsync(csv);Assert.Equal(0,result.Presets);Assert.Contains(result.Warnings,message=>message.Contains("missing tool, machine, material",StringComparison.OrdinalIgnoreCase));Assert.Empty(await ((IToolCuttingPresetRepository)destination).GetAllAsync());
    }

    private sealed class MemoryLibrary:IToolLibrary,IMachineProfileRepository,IMaterialLibrary,IToolCuttingPresetRepository
    {
        private readonly Dictionary<Guid,CncTool> _tools=[];private readonly Dictionary<Guid,MachineProfileDefinition> _machines=[];private readonly Dictionary<Guid,MaterialDefinition> _materials=[];private readonly Dictionary<Guid,ToolCuttingPreset> _presets=[];
        Task<IReadOnlyList<CncTool>> IToolLibrary.GetAllAsync(CancellationToken cancellationToken)=>Task.FromResult<IReadOnlyList<CncTool>>(_tools.Values.ToArray());
        public Task<IReadOnlyList<CncTool>> GetAllAsync(CancellationToken cancellationToken=default)=>((IToolLibrary)this).GetAllAsync(cancellationToken);
        public Task SaveAsync(CncTool value,CancellationToken cancellationToken=default){_tools[value.Id]=value;return Task.CompletedTask;}public Task DeleteAsync(Guid id,CancellationToken cancellationToken=default){_tools.Remove(id);return Task.CompletedTask;}
        Task<IReadOnlyList<MachineProfileDefinition>> IMachineProfileRepository.GetAllAsync(CancellationToken cancellationToken)=>Task.FromResult<IReadOnlyList<MachineProfileDefinition>>(_machines.Values.ToArray());
        Task IMachineProfileRepository.SaveAsync(MachineProfileDefinition value,CancellationToken cancellationToken){_machines[value.Id]=value;return Task.CompletedTask;}Task IMachineProfileRepository.DeleteAsync(Guid id,CancellationToken cancellationToken){_machines.Remove(id);return Task.CompletedTask;}
        Task<IReadOnlyList<MaterialDefinition>> IMaterialLibrary.GetAllAsync(CancellationToken cancellationToken)=>Task.FromResult<IReadOnlyList<MaterialDefinition>>(_materials.Values.ToArray());
        Task IMaterialLibrary.SaveAsync(MaterialDefinition value,CancellationToken cancellationToken){_materials[value.Id]=value;return Task.CompletedTask;}Task IMaterialLibrary.DeleteAsync(Guid id,CancellationToken cancellationToken){_materials.Remove(id);return Task.CompletedTask;}
        Task<IReadOnlyList<ToolCuttingPreset>> IToolCuttingPresetRepository.GetAllAsync(CancellationToken cancellationToken)=>Task.FromResult<IReadOnlyList<ToolCuttingPreset>>(_presets.Values.ToArray());
        public Task SaveAsync(ToolCuttingPreset value,CancellationToken cancellationToken=default){_presets[value.Id]=value;return Task.CompletedTask;}Task IToolCuttingPresetRepository.SaveAsync(ToolCuttingPreset value,CancellationToken cancellationToken){_presets[value.Id]=value;return Task.CompletedTask;}Task IToolCuttingPresetRepository.DeleteAsync(Guid id,CancellationToken cancellationToken){_presets.Remove(id);return Task.CompletedTask;}
    }
}
