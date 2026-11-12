using CncCad.Cam.Tools;
using CncCad.Cam.Safety;
using Microsoft.EntityFrameworkCore;

namespace CncCad.Infrastructure.Persistence;

public sealed class ToolLibraryDbContext(DbContextOptions<ToolLibraryDbContext> options) : DbContext(options)
{
    public DbSet<CncTool> Tools => Set<CncTool>();
    public DbSet<MachineProfileDefinition> Machines => Set<MachineProfileDefinition>();
    public DbSet<MaterialDefinition> Materials => Set<MaterialDefinition>();
    public DbSet<ToolCuttingPreset> ToolCuttingPresets => Set<ToolCuttingPreset>();
}

public sealed class ToolLibraryContextFactory(DbContextOptions<ToolLibraryDbContext> options) : IDbContextFactory<ToolLibraryDbContext>
{
    public ToolLibraryDbContext CreateDbContext() => new(options);
}

public interface IToolLibrary
{
    Task<IReadOnlyList<CncTool>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CncTool tool, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IMachineProfileRepository
{
    Task<IReadOnlyList<MachineProfileDefinition>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(MachineProfileDefinition machine, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IMaterialLibrary
{
    Task<IReadOnlyList<MaterialDefinition>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(MaterialDefinition material, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IToolCuttingPresetRepository
{
    Task<IReadOnlyList<ToolCuttingPreset>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ToolCuttingPreset preset, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class SqliteMaterialLibrary(IDbContextFactory<ToolLibraryDbContext> factory):IMaterialLibrary
{
    public async Task<IReadOnlyList<MaterialDefinition>> GetAllAsync(CancellationToken cancellationToken=default){await using var db=await factory.CreateDbContextAsync(cancellationToken);return await db.Materials.AsNoTracking().OrderBy(x=>x.Category).ThenBy(x=>x.Name).ToListAsync(cancellationToken);}
    public async Task SaveAsync(MaterialDefinition material,CancellationToken cancellationToken=default){if(string.IsNullOrWhiteSpace(material.Name))throw new ArgumentException("Material name is required.");await using var db=await factory.CreateDbContextAsync(cancellationToken);db.Update(material);await db.SaveChangesAsync(cancellationToken);}
    public async Task DeleteAsync(Guid id,CancellationToken cancellationToken=default){await using var db=await factory.CreateDbContextAsync(cancellationToken);await db.Materials.Where(x=>x.Id==id).ExecuteDeleteAsync(cancellationToken);}
}

public sealed class SqliteToolCuttingPresetRepository(IDbContextFactory<ToolLibraryDbContext> factory):IToolCuttingPresetRepository
{
    public async Task<IReadOnlyList<ToolCuttingPreset>> GetAllAsync(CancellationToken cancellationToken=default){await using var db=await factory.CreateDbContextAsync(cancellationToken);return await db.ToolCuttingPresets.AsNoTracking().ToListAsync(cancellationToken);}
    public async Task SaveAsync(ToolCuttingPreset preset,CancellationToken cancellationToken=default){var errors=preset.Validate();if(errors.Count>0)throw new ArgumentException(string.Join(" ",errors));await using var db=await factory.CreateDbContextAsync(cancellationToken);db.Update(preset);await db.SaveChangesAsync(cancellationToken);}
    public async Task DeleteAsync(Guid id,CancellationToken cancellationToken=default){await using var db=await factory.CreateDbContextAsync(cancellationToken);await db.ToolCuttingPresets.Where(x=>x.Id==id).ExecuteDeleteAsync(cancellationToken);}
}

public sealed class SqliteMachineProfileRepository(IDbContextFactory<ToolLibraryDbContext> factory) : IMachineProfileRepository
{
    public async Task<IReadOnlyList<MachineProfileDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Machines.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }
    public async Task SaveAsync(MachineProfileDefinition machine, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken); db.Update(machine); await db.SaveChangesAsync(cancellationToken);
    }
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken); await db.Machines.Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}

public sealed class SqliteToolLibrary(IDbContextFactory<ToolLibraryDbContext> factory) : IToolLibrary
{
    public async Task<IReadOnlyList<CncTool>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Tools.AsNoTracking().OrderBy(t => t.ToolNumber).ThenBy(t => t.Name).ToListAsync(cancellationToken);
    }
    public async Task SaveAsync(CncTool tool, CancellationToken cancellationToken = default)
    {
        var errors = tool.Validate();
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.Update(tool);
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await db.Tools.Where(t => t.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}

public static class ToolLibraryInitializer
{
    public static async Task InitializeAsync(IDbContextFactory<ToolLibraryDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Tools ADD COLUMN StickOutMm REAL NULL;"); } catch (Microsoft.Data.Sqlite.SqliteException exception) when (exception.SqliteErrorCode == 1 && exception.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Machines ADD COLUMN PostProcessorId TEXT NOT NULL DEFAULT 'uccnc-mm';"); } catch (Microsoft.Data.Sqlite.SqliteException exception) when (exception.SqliteErrorCode == 1 && exception.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { }
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS Machines (
              Id TEXT NOT NULL PRIMARY KEY, Name TEXT NOT NULL, MinimumXmm REAL NOT NULL, MaximumXmm REAL NOT NULL,
              MinimumYmm REAL NOT NULL, MaximumYmm REAL NOT NULL, MinimumZmm REAL NOT NULL, MaximumZmm REAL NOT NULL,
              RapidRateMmPerMin REAL NOT NULL, MaximumSpindleRpm INTEGER NOT NULL, DefaultClearanceMm REAL NOT NULL,
              HolderDiameterMm REAL NOT NULL, HolderLengthMm REAL NOT NULL, ToolChangeXmm REAL NOT NULL, ToolChangeYmm REAL NOT NULL,
              ParkXmm REAL NOT NULL, ParkYmm REAL NOT NULL, AllowedSpoilboardDepthMm REAL NOT NULL
            );
            """);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS Materials (
              Id TEXT NOT NULL PRIMARY KEY, Name TEXT NOT NULL, Category TEXT NOT NULL, Notes TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS ToolCuttingPresets (
              Id TEXT NOT NULL PRIMARY KEY, ToolId TEXT NOT NULL, MachineId TEXT NOT NULL, MaterialId TEXT NOT NULL,
              FeedRateMmPerMin REAL NOT NULL, PlungeRateMmPerMin REAL NOT NULL, SpindleRpm INTEGER NOT NULL,
              PassDepthMm REAL NOT NULL, StepOverPercent REAL NOT NULL
            );
            """);
        if (!await db.Tools.AnyAsync()) db.Tools.AddRange(SampleTools());
        else
        {
            foreach (var tool in await db.Tools.Where(x => x.Name.Contains("(Sample)") && (x.CuttingLengthMm == null || x.OverallLengthMm == null || x.ShankDiameterMm == null || x.StickOutMm == null)).ToListAsync())
            {
                tool.CuttingLengthMm ??= Math.Max(12, tool.DiameterMm * 4); tool.OverallLengthMm ??= 50; tool.ShankDiameterMm ??= tool.DiameterMm; tool.StickOutMm ??= Math.Max(20, tool.DiameterMm * 5);
            }
        }
        if (!await db.Machines.AnyAsync()) db.Machines.Add(new MachineProfileDefinition { Name = "1200 × 1200 CNC Router", ParkXmm = 0, ParkYmm = 0 });
        if (!await db.Materials.AnyAsync()) db.Materials.AddRange(
            new MaterialDefinition{Name="MDF",Category="Wood composite"},
            new MaterialDefinition{Name="Plywood",Category="Wood composite"},
            new MaterialDefinition{Name="Hardwood",Category="Timber"});
        await db.SaveChangesAsync();
    }
    private static IEnumerable<CncTool> SampleTools() =>
    [
        Tool(1, "3mm End Mill", ToolType.EndMill, 3, 1200, 400, 3), Tool(2, "6mm End Mill", ToolType.EndMill, 6, 1800, 600, 3),
        Tool(3, "6mm Compression Bit", ToolType.CompressionBit, 6, 2400, 700, 6), Tool(4, "12mm End Mill", ToolType.EndMill, 12, 2400, 600, 5),
        Tool(5, "6mm Ball Nose", ToolType.BallNose, 6, 1600, 500, 2), Tool(6, "3.175mm Engraving Bit", ToolType.EngravingBit, 3.175, 1000, 300, 1),
        Tool(7, "60 Degree V-Bit", ToolType.VBit, 12, 1200, 350, 2, 60), Tool(8, "90 Degree V-Bit", ToolType.VBit, 12, 1200, 350, 2, 90),
        Tool(9, "5mm Drill", ToolType.Drill, 5, 500, 300, 3), Tool(10, "25mm Surfacing Bit", ToolType.SurfacingBit, 25, 2500, 500, 1)
    ];
    private static CncTool Tool(int number, string name, ToolType type, double diameter, double feed, double plunge, double stepDown, double? angle = null) =>
        new() { ToolNumber = number, Name = $"{name} (Sample)", Type = type, DiameterMm = diameter, CuttingLengthMm = Math.Max(12, diameter * 4), OverallLengthMm = 50, ShankDiameterMm = diameter, StickOutMm = Math.Max(20, diameter * 5), FeedRateMmPerMin = feed, PlungeRateMmPerMin = plunge, StepDownMm = stepDown, SpindleRpm = 18000, StepOverPercent = 40, VBitAngleDegrees = angle };
}
