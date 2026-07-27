using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace HorrorHarbor.App.Infrastructure;

internal static partial class WindowChromeService
{
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        bool useDarkChrome = !SystemParameters.HighContrast;
        int enabled = useDarkChrome ? 1 : 0;
        int result = DwmSetWindowAttribute(
            handle,
            DwmUseImmersiveDarkMode,
            ref enabled,
            sizeof(int));
        if (result != 0)
        {
            _ = DwmSetWindowAttribute(
                handle,
                DwmUseImmersiveDarkModeBefore20H1,
                ref enabled,
                sizeof(int));
        }

        if (!useDarkChrome)
        {
            return;
        }

        int caption = ToColorRef(Color.FromRgb(9, 11, 16));
        int border = ToColorRef(Color.FromRgb(47, 56, 72));
        int text = ToColorRef(Color.FromRgb(247, 248, 250));
        _ = DwmSetWindowAttribute(handle, DwmCaptionColor, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(handle, DwmBorderColor, ref border, sizeof(int));
        _ = DwmSetWindowAttribute(handle, DwmTextColor, ref text, sizeof(int));
    }

    private static int ToColorRef(Color color) =>
        color.R | (color.G << 8) | (color.B << 16);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
