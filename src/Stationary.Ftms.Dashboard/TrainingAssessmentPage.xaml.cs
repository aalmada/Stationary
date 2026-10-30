using Stationary.Ftms.Dashboard.ViewModels;

namespace Stationary.Ftms.Dashboard;

public partial class TrainingAssessmentPage : ContentPage
{
    public TrainingAssessmentPage(DashboardViewModel dashboard)
    {
        InitializeComponent();
        BindingContext = dashboard.Training;
    }
}