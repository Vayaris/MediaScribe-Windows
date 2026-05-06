using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediaScribeRecorder.Models;

namespace MediaScribeRecorder.Services;

public sealed class WindowProcessService
{
    public IReadOnlyList<ProcessAudioSource> GetVisibleProcessWindows()
    {
        var results = new List<ProcessAudioSource>();
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd) || GetWindowTextLength(hWnd) == 0)
            {
                return true;
            }

            var builder = new StringBuilder(512);
            _ = GetWindowText(hWnd, builder, builder.Capacity);
            var title = builder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            GetWindowThreadProcessId(hWnd, out var processId);
            try
            {
                using var process = Process.GetProcessById((int)processId);
                if (!string.IsNullOrWhiteSpace(process.ProcessName))
                {
                    results.Add(new ProcessAudioSource((int)processId, title, process.ProcessName, ExtractIcon(process)));
                }
            }
            catch
            {
                // Processes can exit while the window list is being built.
            }

            return true;
        }, IntPtr.Zero);

        return results
            .GroupBy(item => item.ProcessId)
            .Select(group => group.First())
            .OrderBy(item => item.ProcessName)
            .ThenBy(item => item.Title)
            .ToList();
    }

    private static ImageSource? ExtractIcon(Process process)
    {
        try
        {
            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            var image = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(18, 18));
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
