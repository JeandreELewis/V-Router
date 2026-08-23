using Avalonia.Controls;
using Avalonia.Interactivity;
using CncCad.App.ViewModels;
using CncCad.Core.Documents;

namespace CncCad.App.Views;

public partial class JobSetupWindow : Window
{
    private JobSetupViewModel ViewModel => (JobSetupViewModel)DataContext!;
    public JobSetupWindow() { InitializeComponent(); DataContext = new JobSetupViewModel(); }
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);
    private void Create_Click(object? sender, RoutedEventArgs e) { var settings = ViewModel.Build(); if (settings is not null) Close(settings); }
}
