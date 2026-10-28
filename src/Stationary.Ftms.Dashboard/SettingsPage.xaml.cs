using Stationary.Ftms.Dashboard.ViewModels;

namespace Stationary.Ftms.Dashboard;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}