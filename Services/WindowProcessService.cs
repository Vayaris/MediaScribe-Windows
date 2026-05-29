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
        var parentMap = BuildParentProcessMap();
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
                    var captureProcessId = ResolveCaptureProcessId(process, parentMap);
                    results.Add(new ProcessAudioSource((int)processId, captureProcessId, title, process.ProcessName, ExtractIcon(process)));
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

    private static int ResolveCaptureProcessId(Process process, IReadOnlyDictionary<int, int> parentMap)
    {
        var selectedId = process.Id;
        var selectedName = process.ProcessName;
        var currentId = process.Id;
        var visited = new HashSet<int>();

        while (parentMap.TryGetValue(currentId, out var parentId) && parentId > 0 && visited.Add(parentId))
        {
            try
            {
                using var parent = Process.GetProcessById(parentId);
                if (!parent.ProcessName.Equals(selectedName, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                selectedId = parentId;
                currentId = parentId;
            }
            catch
            {
                break;
            }
        }

        return selectedId;
    }

    private static IReadOnlyDictionary<int, int> BuildParentProcessMap()
    {
        var map = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == INVALID_HANDLE_VALUE)
        {
            return map;
        }

        try
        {
            var entry = new PROCESSENTRY32
            {
                dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>(),
            };
            if (!Process32First(snapshot, ref entry))
            {
                return map;
            }

            do
            {
                map[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
            }
            while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return map;
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

    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
