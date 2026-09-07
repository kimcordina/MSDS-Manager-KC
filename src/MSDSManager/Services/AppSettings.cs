using System.IO;
using System.Text.Json;
using MSDSManager.Models;

namespace MSDSManager.Services;

public static class AppPaths
{
    public static string AppDataDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MSDSManagerKC");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string DatabasePath => Path.Combine(AppDataDirectory, "sds-library.db");
    public static string SettingsPath => Path.Combine(AppDataDirectory, "settings.json");
}

public sealed class AppSettings
{
    public string? LibraryRootPath { get; set; }
    public string? LastExportFolder { get; set; }
    public int ReviewAfterMonths { get; set; } = 36;
    public int VerificationReminderMonths { get; set; } = 12;
    public string? ReplyTemplate { get; set; }
    public string? DefaultSupplierEmail { get; set; }
    public List<FolderMoveRecord> LastFolderMoves { get; set; } = [];


    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsPath))
                return new AppSettings();

            var json = File.ReadAllText(AppPaths.SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            settings.LastFolderMoves ??= [];
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(AppPaths.SettingsPath, json);
    }
}
