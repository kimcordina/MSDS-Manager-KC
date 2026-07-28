using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using MSDSManager.Models;

namespace MSDSManager.Services;

public sealed class PackService
{
    private readonly SdsRepository _repository;

    public PackService(SdsRepository repository)
    {
        _repository = repository;
    }

    public IReadOnlyList<SavedPack> GetPacks() => _repository.GetPacks();

    public SavedPack CreatePack(string name, string? notes, IEnumerable<long> documentIds)
    {
        var id = _repository.CreatePack(name, notes, documentIds);
        return _repository.GetPacks().First(p => p.Id == id);
    }

    public void DeletePack(long packId) => _repository.DeletePack(packId);

    public IReadOnlyList<SdsDocument> ResolvePackDocuments(SavedPack pack) =>
        _repository.GetDocumentsByIds(pack.DocumentIds);
}

public sealed class ExportService
{
    public string CopyToFolder(IEnumerable<SdsDocument> documents, string destinationFolder)
    {
        Directory.CreateDirectory(destinationFolder);
        var copied = 0;
        foreach (var doc in documents)
        {
            if (!File.Exists(doc.FilePath))
                continue;

            var dest = Path.Combine(destinationFolder, doc.FileName);
            dest = EnsureUniquePath(dest);
            File.Copy(doc.FilePath, dest, overwrite: false);
            copied++;
        }

        return $"Copied {copied} file(s) to {destinationFolder}";
    }

    public string CreateZip(IEnumerable<SdsDocument> documents, string zipPath)
    {
        var dir = Path.GetDirectoryName(zipPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        if (File.Exists(zipPath))
            File.Delete(zipPath);

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var doc in documents)
        {
            if (!File.Exists(doc.FilePath))
                continue;

            var entryName = doc.FileName;
            var i = 1;
            while (!usedNames.Add(entryName))
            {
                entryName = $"{Path.GetFileNameWithoutExtension(doc.FileName)}_{i}{Path.GetExtension(doc.FileName)}";
                i++;
            }

            archive.CreateEntryFromFile(doc.FilePath, entryName, CompressionLevel.Optimal);
        }

        return zipPath;
    }

    public void OpenInExplorer(IEnumerable<SdsDocument> documents)
    {
        foreach (var doc in documents.Take(10))
        {
            if (!File.Exists(doc.FilePath))
                continue;

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{doc.FilePath}\"",
                UseShellExecute = true
            });
        }
    }

    public void OpenPdf(SdsDocument document)
    {
        if (!File.Exists(document.FilePath))
            throw new FileNotFoundException("PDF not found.", document.FilePath);

        Process.Start(new ProcessStartInfo
        {
            FileName = document.FilePath,
            UseShellExecute = true
        });
    }

    private static string EnsureUniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var i = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            i++;
        } while (File.Exists(candidate));

        return candidate;
    }
}
