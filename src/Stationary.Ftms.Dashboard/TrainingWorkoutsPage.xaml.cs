using Stationary.Ftms.Dashboard.ViewModels;

namespace Stationary.Ftms.Dashboard;

public partial class TrainingWorkoutsPage : ContentPage
{
    public TrainingWorkoutsPage(DashboardViewModel dashboard)
    {
        InitializeComponent();
        BindingContext = dashboard.Training;
    }
}