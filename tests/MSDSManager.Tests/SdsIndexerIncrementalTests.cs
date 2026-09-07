using MSDSManager.Services;
using Xunit;

namespace MSDSManager.Tests;

public sealed class SdsIndexerIncrementalTests
{
    [Fact]
    public async Task Incremental_index_skips_unchanged_files()
    {
        using var library = new TempFolder("msds-lib-");
        var (repo, _, lifetime) = TestHelpers.CreateRepository();
        using (lifetime)
        {
            var pdf = Path.Combine(library.FullName, "Kitchen", "LUX5.pdf");
            TestHelpers.WriteMinimalPdf(pdf);

            var indexer = new SdsIndexer(repo);
            var first = await indexer.IndexLibraryAsync(library.FullName, reviewAfterMonths: 36, forceFull: false);
            Assert.Equal(1, first.NewFiles);
            Assert.Equal(0, first.UnchangedFiles);

            var second = await indexer.IndexLibraryAsync(library.FullName, reviewAfterMonths: 36, forceFull: false);
            Assert.Equal(0, second.NewFiles);
            Assert.Equal(0, second.ChangedFiles);
            Assert.Equal(1, second.UnchangedFiles);

            File.AppendAllText(pdf, " ");
            var third = await indexer.IndexLibraryAsync(library.FullName, reviewAfterMonths: 36, forceFull: false);
            Assert.Equal(1, third.ChangedFiles);
        }
    }

    [Fact]
    public async Task DetectChanges_reports_new_and_missing()
    {
        using var library = new TempFolder("msds-lib-");
        var (repo, _, lifetime) = TestHelpers.CreateRepository();
        using (lifetime)
        {
            var pdf = Path.Combine(library.FullName, "LUX5.pdf");
            TestHelpers.WriteMinimalPdf(pdf);
            var indexer = new SdsIndexer(repo);
            await indexer.IndexLibraryAsync(library.FullName, 36);

            var extra = Path.Combine(library.FullName, "NEW.pdf");
            TestHelpers.WriteMinimalPdf(extra);
            File.Delete(pdf);

            var changes = indexer.DetectChanges(library.FullName);
            Assert.True(changes.HasChanges);
            Assert.Equal(1, changes.NewCount);
            Assert.Equal(1, changes.MissingCount);
        }
    }
}
