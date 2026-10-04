using Stationary.Ftms.Dashboard.ViewModels;

namespace Stationary.Ftms.Dashboard;

public partial class ResultsPage : ContentPage
{
    public ResultsPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}