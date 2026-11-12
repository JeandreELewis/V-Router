using CncCad.Infrastructure.Settings;

namespace CncCad.Core.Tests;

public sealed class ApplicationPreferencesTests
{
    [Fact]
    public async Task Preferences_RoundTrip_And_Reset_Without_ProjectData()
    {
        var directory=Path.Combine(Path.GetTempPath(),$"cnccad-settings-{Guid.NewGuid():N}");
        try
        {
            var path=Path.Combine(directory,"preferences.json");var store=new JsonApplicationPreferencesStore(path);
            var expected=new ApplicationPreferences{StartupWorkspace=StartupWorkspace.Toolpaths,LastWorkspaceMode=WorkspaceMode.Toolpaths,ViewportLayout=ViewportLayout.VerticalSplit,ShowGrid=false,GridSpacingMm=2.5,SnappingEnabled=false,SnapToEndpoints=false,Accent=AccentPreference.System};
            await store.SaveAsync(expected);var actual=await store.LoadAsync();
            Assert.Equal(expected,actual);await store.ResetAsync();Assert.Equal(ApplicationPreferences.Defaults,await store.LoadAsync());
        }
        finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
    }

    [Fact]
    public async Task Invalid_Settings_Fall_Back_And_Grid_Is_Normalized()
    {
        var directory=Path.Combine(Path.GetTempPath(),$"cnccad-settings-{Guid.NewGuid():N}");Directory.CreateDirectory(directory);var path=Path.Combine(directory,"preferences.json");
        try
        {
            await File.WriteAllTextAsync(path,"not json");var store=new JsonApplicationPreferencesStore(path);Assert.Equal(ApplicationPreferences.Defaults,await store.LoadAsync());
            await store.SaveAsync(new ApplicationPreferences{GridSpacingMm=5000});Assert.Equal(1000,(await store.LoadAsync()).GridSpacingMm);
        }
        finally { Directory.Delete(directory,true); }
    }

    [Fact]
    public async Task RecentProjects_CanBeCleared()
    {
        var directory=Path.Combine(Path.GetTempPath(),$"cnccad-recent-{Guid.NewGuid():N}");var store=new JsonRecentProjectStore(Path.Combine(directory,"recent.json"));
        try { await store.AddAsync(Path.Combine(directory,"one.cnccad"));await store.ClearAsync();Assert.Empty(await store.GetAsync()); }
        finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
    }
}
