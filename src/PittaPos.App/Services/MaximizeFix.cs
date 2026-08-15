using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PittaPos.App.Services;

/// <summary>
/// Διορθώνει τη μεγιστοποίηση ενός παραθύρου χωρίς πλαίσιο (WindowStyle="None", βλ. MainWindow).
///
/// Το πρόβλημα: όταν το WPF μεγιστοποιεί τέτοιο παράθυρο, το κάνει όσο ΟΛΗ η οθόνη συν το πλάτος του
/// πλαισίου που δεν υπάρχει πια — μετρημένο σε αυτό το μηχάνημα: το παράθυρο έπιανε από -7 έως 1927 σε
/// οθόνη 1920. Αποτέλεσμα, 7 pixel σε κάθε πλευρά πέφτουν έξω από την οθόνη· τα κουμπιά του παραθύρου
/// πάνω δεξιά κόβονταν στη μέση και το ✕ ήταν μισό. Επίσης, χωρίς αυτό το μεγιστοποιημένο παράθυρο
/// σκεπάζει τη γραμμή εργασιών των Windows.
///
/// Η λύση: όταν τα Windows ρωτούν «πόσο μεγάλο θες να γίνεις;» (WM_GETMINMAXINFO), απαντάμε με το
/// ΩΦΕΛΙΜΟ εμβαδόν της οθόνης στην οποία βρίσκεται το παράθυρο — δηλαδή χωρίς τη γραμμή εργασιών.
/// Διαβάζεται η οθόνη κάθε φορά, άρα δουλεύει σωστά και με δεύτερη οθόνη ή διαφορετική ανάλυση.
/// </summary>
public static class MaximizeFix
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const int MonitorDefaultToNearest = 0x00000002;

    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                HwndSource.FromHwnd(handle)?.AddHook(Hook);
            }
            catch (Exception ex)
            {
                // Χωρίς τη διόρθωση το παράθυρο απλώς ξεχειλίζει λίγο — δεν αξίζει να ρίξει το ταμείο.
                AppLog.Write("window", $"Δεν μπήκε η διόρθωση μεγιστοποίησης: {ex.Message}");
            }
        };
    }

    private static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmGetMinMaxInfo)
            return IntPtr.Zero;

        try
        {
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
                return IntPtr.Zero;

            var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info))
                return IntPtr.Zero;

            var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            // Θέση και μέγεθος ΣΕ ΣΧΕΣΗ με την οθόνη του παραθύρου (η αριστερή/πάνω γωνία της οθόνης δεν
            // είναι πάντα 0,0 — σε δεύτερη οθόνη μπορεί να είναι και αρνητική).
            mmi.ptMaxPosition.x = info.rcWork.left - info.rcMonitor.left;
            mmi.ptMaxPosition.y = info.rcWork.top - info.rcMonitor.top;
            mmi.ptMaxSize.x = info.rcWork.right - info.rcWork.left;
            mmi.ptMaxSize.y = info.rcWork.bottom - info.rcWork.top;
            Marshal.StructureToPtr(mmi, lParam, true);
            handled = true;
        }
        catch (Exception)
        {
            // Ό,τι κι αν πάει στραβά εδώ, αφήνουμε τα Windows να αποφασίσουν μόνα τους.
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point2 { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point2 ptReserved;
        public Point2 ptMaxSize;
        public Point2 ptMaxPosition;
        public Point2 ptMinTrackSize;
        public Point2 ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect2 { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public Rect2 rcMonitor;
        public Rect2 rcWork;
        public int dwFlags;
    }
}
