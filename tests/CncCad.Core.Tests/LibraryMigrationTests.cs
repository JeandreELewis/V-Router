using CncCad.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CncCad.Core.Tests;

public sealed class LibraryMigrationTests
{
    [Fact]
    public async Task ExistingLibraryAddsMachineSafetyColumnsAndRecordsVersionedMigrations()
    {
        var path=Path.Combine(Path.GetTempPath(),$"v-router-library-{Guid.NewGuid():N}.db");
        try
        {
            var options=new DbContextOptionsBuilder<ToolLibraryDbContext>().UseSqlite($"Data Source={path}").Options;
            await using(var setup=new ToolLibraryDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                await setup.Database.ExecuteSqlRawAsync("ALTER TABLE Machines DROP COLUMN ParkEnabled;");
                await setup.Database.ExecuteSqlRawAsync("ALTER TABLE Machines DROP COLUMN ToolChangeSeconds;");
            }

            var factory=new ToolLibraryContextFactory(options);
            await ToolLibraryInitializer.InitializeAsync(factory);
            await using(var verify=new ToolLibraryDbContext(options))
            {
                var columns=await verify.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Machines');").ToListAsync();
                Assert.Contains("ParkEnabled",columns);
                Assert.Contains("ToolChangeSeconds",columns);
                var versions=await verify.Database.SqlQueryRaw<int>("SELECT Version AS Value FROM __VRouterLibraryMigrations ORDER BY Version;").ToListAsync();
                Assert.Equal(Enumerable.Range(1,8),versions);
                var machine=Assert.Single(await verify.Machines.AsNoTracking().ToListAsync());
                Assert.False(machine.ParkEnabled);
                Assert.Equal(30,machine.ToolChangeSeconds);
            }
        }
        finally
        {
            if(File.Exists(path))File.Delete(path);
        }
    }
}
