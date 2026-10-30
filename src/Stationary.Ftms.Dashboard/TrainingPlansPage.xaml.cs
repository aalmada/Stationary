using Stationary.Ftms.Dashboard.ViewModels;

namespace Stationary.Ftms.Dashboard;

public partial class TrainingPlansPage : ContentPage
{
    public TrainingPlansPage(DashboardViewModel dashboard)
    {
        InitializeComponent();
        BindingContext = dashboard.Training;
    }
}