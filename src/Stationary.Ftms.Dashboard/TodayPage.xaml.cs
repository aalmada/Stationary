using Stationary.Ftms.Dashboard.ViewModels;

namespace Stationary.Ftms.Dashboard;

public partial class TodayPage : ContentPage
{
    public TodayPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}