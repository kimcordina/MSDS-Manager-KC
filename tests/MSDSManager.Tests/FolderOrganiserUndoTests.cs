using MSDSManager.Models;
using MSDSManager.Services;
using Xunit;

namespace MSDSManager.Tests;

public sealed class FolderOrganiserUndoTests
{
    [Fact]
    public void Undo_moves_file_back_to_source()
    {
        using var root = new TempFolder("msds-org-");
        var (repo, _, lifetime) = TestHelpers.CreateRepository();
        using (lifetime)
        {
            var sourceDir = Path.Combine(root.FullName, "Unsorted");
            var destDir = Path.Combine(root.FullName, "Kitchen", "Dishwashing");
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(destDir);

            var source = Path.Combine(sourceDir, "LUX5.pdf");
            TestHelpers.WriteMinimalPdf(source);

            var doc = TestHelpers.SeedDocument(
                repo,
                "Luxury Wash",
                "LUX5.pdf",
                filePath: source);

            var organiser = new FolderOrganiserService(repo);
            var result = organiser.ApplyApprovedMoves(root.FullName,
            [
                new FolderMoveSuggestion
                {
                    Document = doc,
                    CurrentFolder = "Unsorted",
                    SuggestedFolder = "Kitchen/Dishwashing",
                    Confidence = 90,
                    Reason = "test",
                    IsUncategorised = true
                }
            ]);

            Assert.Equal(1, result.Moved);
            Assert.False(File.Exists(source));
            Assert.True(File.Exists(result.Moves[0].DestinationPath));

            var undone = organiser.UndoMoves(result.Moves);
            Assert.Equal(1, undone.Moved);
            Assert.True(File.Exists(source));
            Assert.False(File.Exists(result.Moves[0].DestinationPath));
        }
    }

    [Fact]
    public void Undo_does_not_overwrite_if_source_already_exists()
    {
        using var root = new TempFolder("msds-org-");
        var (repo, _, lifetime) = TestHelpers.CreateRepository();
        using (lifetime)
        {
            var source = Path.Combine(root.FullName, "a.pdf");
            var dest = Path.Combine(root.FullName, "Kitchen", "a.pdf");
            TestHelpers.WriteMinimalPdf(source);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            TestHelpers.WriteMinimalPdf(dest);

            var organiser = new FolderOrganiserService(repo);
            var undone = organiser.UndoMoves(
            [
                new FolderMoveRecord
                {
                    SourcePath = source,
                    DestinationPath = dest,
                    FileName = "a.pdf",
                    ProductName = "A"
                }
            ]);

            Assert.Equal(0, undone.Moved);
            Assert.Equal(1, undone.Failed);
            Assert.True(File.Exists(source));
            Assert.True(File.Exists(dest));
        }
    }
}
