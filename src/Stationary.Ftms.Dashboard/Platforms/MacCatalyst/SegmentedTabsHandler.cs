using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

using UIKit;

namespace Stationary.Ftms.Dashboard;

public sealed class SegmentedTabsHandler : ViewHandler<SegmentedTabs, UISegmentedControl>
{
    public static readonly IPropertyMapper<SegmentedTabs, SegmentedTabsHandler> PropertyMapper =
        new PropertyMapper<SegmentedTabs, SegmentedTabsHandler>(ViewMapper)
        {
            [nameof(SegmentedTabs.Titles)] = MapTitles,
            [nameof(SegmentedTabs.SelectedIndex)] = MapSelectedIndex,
            [nameof(SegmentedTabs.SelectedColor)] = MapSelectedColor,
        };

    public SegmentedTabsHandler()
        : base(PropertyMapper)
    {
    }

    // UIView is enumerable over its subviews, so the analyzer requires a collection expression for construction.
    protected override UISegmentedControl CreatePlatformView() => [];

    protected override void ConnectHandler(UISegmentedControl platformView)
    {
        base.ConnectHandler(platformView);
        platformView.ValueChanged += OnValueChanged;
    }

    protected override void DisconnectHandler(UISegmentedControl platformView)
    {
        platformView.ValueChanged -= OnValueChanged;
        base.DisconnectHandler(platformView);
    }

    private static void MapTitles(SegmentedTabsHandler handler, SegmentedTabs tabs)
    {
        var control = handler.PlatformView;
        control.RemoveAllSegments();
        for (var index = 0; index < tabs.Titles.Count; index++)
        {
            control.InsertSegment(tabs.Titles[index], index, false);
        }

        MapSelectedIndex(handler, tabs);
        tabs.InvalidateMeasure();
    }

    private static void MapSelectedIndex(SegmentedTabsHandler handler, SegmentedTabs tabs) =>
        handler.PlatformView.SelectedSegment = tabs.SelectedIndex >= 0 && tabs.SelectedIndex < tabs.Titles.Count
            ? tabs.SelectedIndex
            : -1;

    private static void MapSelectedColor(SegmentedTabsHandler handler, SegmentedTabs tabs)
    {
        var control = handler.PlatformView;
        control.SelectedSegmentTintColor = tabs.SelectedColor?.ToPlatform();
        control.SetTitleTextAttributes(
            new UIStringAttributes { ForegroundColor = tabs.SelectedColor is null ? null : UIColor.White },
            UIControlState.Selected);
    }

    private void OnValueChanged(object? sender, EventArgs e)
    {
        if (VirtualView is not { } tabs || PlatformView.SelectedSegment == tabs.SelectedIndex)
        {
            return;
        }

        if (!tabs.RequestSelection((int)PlatformView.SelectedSegment))
        {
            MapSelectedIndex(this, tabs);
        }
    }
}