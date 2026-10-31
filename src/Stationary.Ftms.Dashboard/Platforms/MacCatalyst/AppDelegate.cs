using System.Diagnostics;
using System.Reflection;

using Foundation;

using UIKit;

namespace Stationary.Ftms.Dashboard;

[Register("AppDelegate")]
public sealed class AppDelegate : MauiUIApplicationDelegate
{
    private static readonly PropertyInfo? MenuBuilderProperty =
        typeof(MauiUIApplicationDelegate).GetProperty("MenuBuilder", BindingFlags.Instance | BindingFlags.NonPublic);

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override void BuildMenu(IUIMenuBuilder builder)
    {
        base.BuildMenu(builder);
        if (builder.System != UIMenuSystem.MainSystem)
        {
            return;
        }

        if (!HasActiveWindowScene())
        {
            BuildPageMenus(builder);
        }

        // Keep only menus that apply to a trainer dashboard: Edit serves text fields and Window is a macOS convention.
        foreach (var menu in (ReadOnlySpan<UIMenuIdentifier>)[UIMenuIdentifier.File, UIMenuIdentifier.Format, UIMenuIdentifier.View, UIMenuIdentifier.Help])
        {
            builder.RemoveMenu(menu.GetConstant()!);
        }
    }

    private static bool HasActiveWindowScene() =>
        UIApplication.SharedApplication.ConnectedScenes.ToArray()
            .OfType<UIWindowScene>()
            .Any(static scene => scene.TraitCollection.ActiveAppearance == UIUserInterfaceActiveAppearance.Active);

    // MAUI only adds page menus when a window scene reports an active appearance, which Mac Catalyst does not report
    // while building the main menu. Apply the window's menu bar the same way MAUI does, with its builder in scope.
    private void BuildPageMenus(IUIMenuBuilder builder)
    {
        var handler = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler;
        if (handler is null || MenuBuilderProperty is null)
        {
            return;
        }

        try
        {
            MenuBuilderProperty.SetValue(this, builder);
            handler.UpdateValue(nameof(IMenuBarElement.MenuBar));
        }
        catch (Exception exception) when (exception is TargetInvocationException or InvalidOperationException)
        {
            Trace.TraceError("Building the app menus failed: {0}", exception);
        }
        finally
        {
            MenuBuilderProperty.SetValue(this, null);
        }
    }
}