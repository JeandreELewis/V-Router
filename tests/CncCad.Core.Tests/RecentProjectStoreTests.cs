using CncCad.Infrastructure.Settings;

namespace CncCad.Core.Tests;

public class RecentProjectStoreTests
{
    [Fact]
    public async Task Add_DeduplicatesMovesToFrontAndLimitsHistory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cnccad-tests-{Guid.NewGuid():N}");
        try
        {
            var store = new JsonRecentProjectStore(Path.Combine(directory, "recent.json"));
            for (var i = 0; i < 12; i++) await store.AddAsync(Path.Combine(directory, $"job-{i}.cnccad"));
            await store.AddAsync(Path.Combine(directory, "job-5.cnccad"));
            var recent = await store.GetAsync();
            Assert.Equal(10, recent.Count);
            Assert.Equal("job-5", recent[0].Name);
            Assert.Single(recent, p => p.Name == "job-5");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
