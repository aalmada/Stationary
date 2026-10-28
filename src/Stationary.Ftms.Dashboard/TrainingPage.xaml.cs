using Stationary.Ftms.Dashboard.ViewModels;

namespace Stationary.Ftms.Dashboard;

public partial class TrainingPage : ContentPage
{
    public TrainingPage(DashboardViewModel dashboard)
    {
        InitializeComponent();
        BindingContext = dashboard.Training;
    }
}