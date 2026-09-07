using MSDSManager.Models;
using MSDSManager.Services;

namespace MSDSManager.Tests;

internal sealed class TempFolder : IDisposable
{
    public DirectoryInfo Info { get; }
    public string FullName => Info.FullName;

    public TempFolder(string prefix)
    {
        Info = Directory.CreateTempSubdirectory(prefix);
    }

    public void Dispose()
    {
        try
        {
            if (Info.Exists)
                Info.Delete(recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}

internal static class TestHelpers
{
    public static (SdsRepository Repo, string DbPath, TempFolder Lifetime) CreateRepository()
    {
        var dir = new TempFolder("msds-tests-");
        var dbPath = Path.Combine(dir.FullName, "sds-library.db");
        var repo = new SdsRepository(dbPath);
        repo.Initialize();
        return (repo, dbPath, dir);
    }

    public static SdsDocument SeedDocument(
        SdsRepository repo,
        string productName,
        string fileName,
        string? filePath = null,
        DocumentStatus status = DocumentStatus.Current,
        DateTime? revisionDate = null,
        string? version = null)
    {
        var path = filePath ?? Path.Combine(Path.GetTempPath(), fileName);
        var doc = new SdsDocument
        {
            FilePath = path,
            FileName = fileName,
            RelativePath = fileName,
            Category = "Kitchen",
            ProductName = productName,
            Version = version,
            RevisionDate = revisionDate ?? new DateTime(2024, 3, 1),
            IndexedAt = DateTime.UtcNow,
            FileLastWriteUtc = DateTime.UtcNow,
            FileSizeBytes = 100,
            Status = status
        };
        repo.UpsertDocument(doc);
        return repo.GetAllDocuments().First(d =>
            string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));
    }

    public static void WriteMinimalPdf(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllBytes(path,
            "%PDF-1.1\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"u8.ToArray());
    }
}
