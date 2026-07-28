using System.Windows;
using MSDSManager.Services;
using MSDSManager.ViewModels;

namespace MSDSManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = AppSettings.Load();
        var dbPath = AppPaths.DatabasePath;
        var repository = new SdsRepository(dbPath);
        repository.Initialize();

        var indexer = new SdsIndexer(repository);
        var packService = new PackService(repository);
        var exportService = new ExportService();
        var outlook = new OutlookComService();
        var matcher = new RequestMatcher(repository);

        var mainVm = new MainViewModel(settings, repository, indexer, packService, exportService, outlook, matcher);
        var window = new MainWindow { DataContext = mainVm };
        window.Show();

        _ = mainVm.InitializeAsync();
    }
}
