using CncCad.App;
using CncCad.App.ViewModels;

namespace CncCad.Core.Tests;

public sealed class CommandHelpTests
{
    [Fact]
    public void HelpCatalog_HasUniqueStableKeysAndActionableContent()
    {
        Assert.Equal(CommandHelpCatalog.All.Count,CommandHelpCatalog.All.Select(x=>x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(CommandHelpCatalog.All,item=>{Assert.False(string.IsNullOrWhiteSpace(item.Title));Assert.False(string.IsNullOrWhiteSpace(item.Summary));Assert.False(string.IsNullOrWhiteSpace(item.HowTo));});
    }

    [Fact]
    public void EveryDrawingPaletteToolHasRegisteredDetailedHelp()
    {
        Assert.All(ToolPaletteItem.DefaultDrawingTools,item=>Assert.Equal(item.HelpKey,CommandHelpCatalog.GetRequired(item.HelpKey).Id));
    }

    [Theory]
    [InlineData("edit.copy")]
    [InlineData("edit.paste")]
    [InlineData("toolpath.profile")]
    [InlineData("toolpath.pocket")]
    [InlineData("toolpath.drill")]
    [InlineData("toolpath.engrave")]
    [InlineData("toolpath.vcarve")]
    [InlineData("toolpath.preflight")]
    [InlineData("toolpath.export")]
    public void PrerequisiteCommands_ExplainWhyTheyCanBeUnavailable(string key)
    {
        Assert.False(string.IsNullOrWhiteSpace(CommandHelpCatalog.GetRequired(key).Requirement));
    }
}
