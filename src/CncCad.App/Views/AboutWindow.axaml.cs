using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Reflection;

namespace CncCad.App.Views;
public partial class AboutWindow : Window
{
    public AboutWindow(){InitializeComponent();var assembly=Assembly.GetExecutingAssembly().GetName();VersionText.Text=$"Version {assembly.Version?.ToString(3)??"development"}";RuntimeText.Text=$".NET {Environment.Version} · Avalonia 12";}
    private void Close_Click(object? sender,RoutedEventArgs e)=>Close();
}
