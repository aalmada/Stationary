using System.Diagnostics;
using System.Runtime.InteropServices;

using Foundation;

using ObjCRuntime;

namespace Stationary.Ftms.Dashboard;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeRect(double X, double Y, double Width, double Height);

// Mac Catalyst hides AppKit, but the AppKit objects exist in the process. Reach the few NSScreen/NSWindow members
// the ride scene needs through the Objective-C runtime; every call degrades to a no-op when a selector is missing.
internal static partial class AppKitInterop
{
    private const string ObjCLibrary = "/usr/lib/libobjc.dylib";
    private const string IOKitLibrary = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const uint AssertionLevelOn = 255;

    public const string ScreenParametersChangedNotification = "NSApplicationDidChangeScreenParametersNotification";

    public static int ScreenCount => Screens()?.Count is { } count ? (int)count : 1;

    // Idle display sleep and idle system sleep are separate power assertions; a ride needs both so the computer
    // neither dims nor sleeps. Both are released together when the returned handle is disposed.
    public static IDisposable PreventSleep(string reason)
    {
        var assertions = new System.Reactive.Disposables.CompositeDisposable();
        foreach (var type in (ReadOnlySpan<string>)["PreventUserIdleDisplaySleep", "PreventUserIdleSystemSleep"])
        {
            using var assertionType = new NSString(type);
            using var name = new NSString(reason);
            if (IOPMAssertionCreateWithName(assertionType.Handle, AssertionLevelOn, name.Handle, out var assertion) != 0)
            {
                Trace.TraceWarning("macOS refused the {0} power assertion.", type);
                continue;
            }

            assertions.Add(System.Reactive.Disposables.Disposable.Create(() => IOPMAssertionRelease(assertion)));
        }

        return assertions;
    }

    // Moves the AppKit window with the given title to a screen other than the one showing another app window and makes
    // it fill that screen. Returns false when there is no such window or no other screen yet.
    public static bool MoveToSecondaryScreen(string title)
    {
        try
        {
            var windows = Windows();
            var screens = Screens();
            if (windows is null || screens is null || screens.Count < 2)
            {
                return false;
            }

            var window = FindWindow(windows, title);
            if (window is null)
            {
                return false;
            }

            var mainScreen = FindOtherVisibleWindow(windows, title)?.ValueForKey(new NSString("screen"));
            NSObject? target = null;
            for (nuint index = 0; index < screens.Count; index++)
            {
                var screen = screens.GetItem<NSObject>(index);
                if (screen is not null && (mainScreen is null ? index > 0 : !screen.IsEqual(mainScreen)))
                {
                    target = screen;
                    break;
                }
            }

            if (target?.ValueForKey(new NSString("visibleFrame")) is not NSValue frameValue)
            {
                return false;
            }

            var frame = frameValue.CGRectValue;
            SendRect(window.Handle, Selector.GetHandle("setFrame:display:"), new NativeRect(frame.X, frame.Y, frame.Width, frame.Height), 1);
            if (!IsFullScreen(window))
            {
                SendObject(window.Handle, Selector.GetHandle("toggleFullScreen:"), IntPtr.Zero);
            }

            return true;
        }
        catch (Exception exception) when (exception is ObjCException or InvalidCastException or MarshalDirectiveException)
        {
            Trace.TraceWarning("Moving the ride scene window failed: {0}", exception);
            return false;
        }
    }

    private static bool IsFullScreen(NSObject window)
    {
        const ulong fullScreenMask = 1 << 14;
        return window.ValueForKey(new NSString("styleMask")) is NSNumber mask && (mask.UInt64Value & fullScreenMask) != 0;
    }

    private static NSObject? FindWindow(NSArray windows, string title)
    {
        for (nuint index = 0; index < windows.Count; index++)
        {
            var window = windows.GetItem<NSObject>(index);
            if (window?.ValueForKey(new NSString("title")) is NSString windowTitle && windowTitle.ToString() == title)
            {
                return window;
            }
        }

        return null;
    }

    private static NSObject? FindOtherVisibleWindow(NSArray windows, string title)
    {
        for (nuint index = 0; index < windows.Count; index++)
        {
            var window = windows.GetItem<NSObject>(index);
            if (window?.ValueForKey(new NSString("title")) is NSString windowTitle
                && windowTitle.ToString() != title
                && window.ValueForKey(new NSString("visible")) is NSNumber { BoolValue: true })
            {
                return window;
            }
        }

        return null;
    }

    private static NSArray? Windows()
    {
        var application = Send(Class.GetHandle("NSApplication"), Selector.GetHandle("sharedApplication"));
        return application == IntPtr.Zero
            ? null
            : Runtime.GetNSObject(application)?.ValueForKey(new NSString("windows")) as NSArray;
    }

    private static NSArray? Screens()
    {
        var screenClass = Class.GetHandle("NSScreen");
        return screenClass == IntPtr.Zero ? null : Runtime.GetNSObject<NSArray>(Send(screenClass, Selector.GetHandle("screens")));
    }

    [LibraryImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static partial IntPtr Send(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void SendObject(IntPtr receiver, IntPtr selector, IntPtr argument);

    [LibraryImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void SendRect(IntPtr receiver, IntPtr selector, NativeRect rect, byte display);

    [LibraryImport(IOKitLibrary)]
    private static partial int IOPMAssertionCreateWithName(IntPtr assertionType, uint level, IntPtr name, out uint assertionId);

    [LibraryImport(IOKitLibrary)]
    private static partial int IOPMAssertionRelease(uint assertionId);
}