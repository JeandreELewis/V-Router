using Avalonia.Controls;
using Avalonia.Interactivity;
using CncCad.Infrastructure.Settings;

namespace CncCad.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() : this(ApplicationPreferences.Defaults) { }
    public SettingsWindow(ApplicationPreferences preferences)
    {
        InitializeComponent();
        StartupPicker.ItemsSource = Enum.GetValues<StartupWorkspace>();
        AccentPicker.ItemsSource = Enum.GetValues<AccentPreference>();
        Apply(preferences);
    }

    private void Apply(ApplicationPreferences p)
    {
        StartupPicker.SelectedItem=p.StartupWorkspace; ShowGridCheck.IsChecked=p.ShowGrid; GridSpacingInput.Value=(decimal)p.GridSpacingMm;
        SnappingCheck.IsChecked=p.SnappingEnabled; SnapGridCheck.IsChecked=p.SnapToGrid; EndpointCheck.IsChecked=p.SnapToEndpoints;
        MidpointCheck.IsChecked=p.SnapToMidpoints; CenterCheck.IsChecked=p.SnapToCenters; ToolpathsCheck.IsChecked=p.ShowToolpaths;
        AccentPicker.SelectedItem=p.Accent; UnsavedCheck.IsChecked=p.ConfirmBeforeReplacingUnsavedJob;
    }

    private void Defaults_Click(object? sender, RoutedEventArgs e)=>Apply(ApplicationPreferences.Defaults);
    private void Cancel_Click(object? sender, RoutedEventArgs e)=>Close(null);
    private void Save_Click(object? sender, RoutedEventArgs e)=>Close(new ApplicationPreferences
    {
        StartupWorkspace=(StartupWorkspace)(StartupPicker.SelectedItem??StartupWorkspace.Design), ShowGrid=ShowGridCheck.IsChecked==true,
        GridSpacingMm=(double)(GridSpacingInput.Value??10), SnappingEnabled=SnappingCheck.IsChecked==true, SnapToGrid=SnapGridCheck.IsChecked==true,
        SnapToEndpoints=EndpointCheck.IsChecked==true, SnapToMidpoints=MidpointCheck.IsChecked==true, SnapToCenters=CenterCheck.IsChecked==true,
        ShowToolpaths=ToolpathsCheck.IsChecked==true, Accent=(AccentPreference)(AccentPicker.SelectedItem??AccentPreference.CncBlue),
        ConfirmBeforeReplacingUnsavedJob=UnsavedCheck.IsChecked==true
    });
}
