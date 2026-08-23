using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using CncCad.App.ViewModels;
using CncCad.App.Views;
using CncCad.Infrastructure.Logging;
using CncCad.Infrastructure.Persistence;
using CncCad.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;

namespace CncCad.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cnc-cad");
            Directory.CreateDirectory(dataDirectory);
            _ = LoggingSetup.Create(dataDirectory);
            var options = new DbContextOptionsBuilder<ToolLibraryDbContext>().UseSqlite($"Data Source={Path.Combine(dataDirectory, "cnc-cad.db")}").Options;
            var factory = new ToolLibraryContextFactory(options);
            var recentProjects = new JsonRecentProjectStore(Path.Combine(dataDirectory, "recent-projects.json"));
            var viewModel = new MainWindowViewModel(new SqliteToolLibrary(factory), recentProjects, new SqliteMachineProfileRepository(factory));
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };
            _ = InitializeWorkspaceAsync(factory, viewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task InitializeWorkspaceAsync(ToolLibraryContextFactory factory, MainWindowViewModel viewModel)
    {
        try
        {
            await ToolLibraryInitializer.InitializeAsync(factory);
            await viewModel.LoadAsync();
            viewModel.Message = "Ready — create or open a job";
        }
        catch (Exception exception)
        {
            viewModel.Message = $"Startup data could not be loaded: {exception.Message}";
        }
    }
}
