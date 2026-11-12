using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using CncCad.App.ViewModels;
using CncCad.App.Views;
using CncCad.Infrastructure.Fonts;
using CncCad.Infrastructure.Logging;
using CncCad.Infrastructure.Persistence;
using CncCad.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using CncCad.PostProcessors.GCode;
using CncCad.PostProcessors.Uccnc;
using Serilog;

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
            var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "v-router");
            Directory.CreateDirectory(dataDirectory);
            LoggingSetup.InitializeLocalCrashLogging(dataDirectory);
            var options = new DbContextOptionsBuilder<ToolLibraryDbContext>().UseSqlite($"Data Source={Path.Combine(dataDirectory, "v-router.db")}").Options;
            var factory = new ToolLibraryContextFactory(options);
            var recentProjects = new JsonRecentProjectStore(Path.Combine(dataDirectory, "recent-projects.json"));
            var preferences = new JsonApplicationPreferencesStore(Path.Combine(dataDirectory, "preferences.json"));
            var recovery = new CncCad.IO.Projects.JsonRecoveryStore(Path.Combine(dataDirectory, "recovery"));
            var postProcessors = CreatePostProcessorRegistry(dataDirectory);
            var viewModel = new MainWindowViewModel(new SqliteToolLibrary(factory), recentProjects, new SqliteMachineProfileRepository(factory), new SkiaTextOutlineService(), new DesktopFontCatalog(), recovery, new SqliteMaterialLibrary(factory), new SqliteToolCuttingPresetRepository(factory), postProcessors);
            desktop.MainWindow = new MainWindow(preferences)
            {
                DataContext = viewModel,
            };
            _ = InitializeWorkspaceAsync(factory, viewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IPostProcessorRegistry CreatePostProcessorRegistry(string dataDirectory)
    {
        var definitions = CertifiedPostDefinitions.All.ToList();
        var community = CommunityPostDefinitionLoader.LoadWithDiagnostics(Path.Combine(dataDirectory, "posts"));
        foreach (var error in community.Errors) Log.Warning("Community post was skipped: {Reason}", error);
        foreach (var definition in community.Definitions)
        {
            if (definitions.Any(existing => existing.Id.Equals(definition.Id, StringComparison.OrdinalIgnoreCase)))
            {
                Log.Warning("Community post {PostId} was skipped because its ID is already registered", definition.Id);
                continue;
            }
            definitions.Add(definition);
        }
        return new PostProcessorRegistry(definitions.Select(definition => new DeclarativeGCodePostProcessor(definition)));
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
