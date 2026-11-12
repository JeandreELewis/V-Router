using CncCad.App.Services;

namespace CncCad.App.HeadlessTests;

public sealed class AtomicExportBatchWriterTests
{
    [Fact]
    public async Task DuplicateDestinationsAreRejectedBeforeAnyFileIsWritten()
    {
        var directory=Path.Combine(Path.GetTempPath(),$"v-router-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path=Path.Combine(directory,"job.nc");
            await Assert.ThrowsAsync<InvalidOperationException>(()=>AtomicExportBatchWriter.WriteAsync([new(path,"one"),new(path.ToUpperInvariant(),"two")],TestContext.Current.CancellationToken));
            Assert.Empty(Directory.EnumerateFiles(directory));
        }
        finally{Directory.Delete(directory,true);}
    }

    [Fact]
    public async Task CompleteBatchIsStagedAndCommittedTogether()
    {
        var directory=Path.Combine(Path.GetTempPath(),$"v-router-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var first=Path.Combine(directory,"one.nc");var second=Path.Combine(directory,"two.nc");
            await AtomicExportBatchWriter.WriteAsync([new(first,"first"),new(second,"second")],TestContext.Current.CancellationToken);
            Assert.Equal("first",await File.ReadAllTextAsync(first,TestContext.Current.CancellationToken));Assert.Equal("second",await File.ReadAllTextAsync(second,TestContext.Current.CancellationToken));
            Assert.DoesNotContain(Directory.EnumerateFiles(directory),path=>path.EndsWith(".tmp",StringComparison.OrdinalIgnoreCase)||path.EndsWith(".bak",StringComparison.OrdinalIgnoreCase));
        }
        finally{Directory.Delete(directory,true);}
    }
}
