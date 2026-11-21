using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace GarminMusicStudio.Services;

/// <summary>
/// Monitors for Windows Shell deletion confirmation dialogs on the user desktop
/// during batch or single delete operations, automatically clicking 'Yes'
/// after the user has already confirmed the operation in the application.
/// </summary>
public sealed class AutoConfirmWatcher : IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private const uint WM_COMMAND = 0x0111;
    private const uint BM_CLICK = 0x00F5;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const int VK_RETURN = 0x0D;
    private const int IDYES = 6;
    private const int IDOK = 1;

    private AutoConfirmWatcher()
    {
        Task.Run(async () =>
        {
            var sbClass = new StringBuilder(64);
            var sbTitle = new StringBuilder(256);

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    EnumWindows((hWnd, lParam) =>
                    {
                        if (!IsWindowVisible(hWnd)) return true;

                        sbClass.Clear();
                        GetClassName(hWnd, sbClass, 64);
                        var cls = sbClass.ToString();

                        if (cls == "#32770")
                        {
                            sbTitle.Clear();
                            GetWindowText(hWnd, sbTitle, 256);
                            var title = sbTitle.ToString();

                            if (title.Contains("刪除") || title.Contains("Delete") ||
                                title.Contains("確認") || title.Contains("Confirm") ||
                                title.Contains("檔案") || title.Contains("File"))
                            {
                                IntPtr btnYes = GetDlgItem(hWnd, IDYES);
                                if (btnYes != IntPtr.Zero)
                                {
                                    PostMessage(btnYes, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                                }
                                PostMessage(hWnd, WM_COMMAND, (IntPtr)IDYES, IntPtr.Zero);
                                PostMessage(hWnd, WM_COMMAND, (IntPtr)IDOK, IntPtr.Zero);
                                PostMessage(hWnd, WM_KEYDOWN, (IntPtr)VK_RETURN, IntPtr.Zero);
                                PostMessage(hWnd, WM_KEYUP, (IntPtr)VK_RETURN, IntPtr.Zero);
                            }
                        }
                        return true;
                    }, IntPtr.Zero);
                }
                catch { }

                try
                {
                    await Task.Delay(40, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, _cts.Token);
    }

    public static AutoConfirmWatcher Start()
    {
        return new AutoConfirmWatcher();
    }

    public void Dispose()
    {
        try
        {
            _cts.Cancel();
            _cts.Dispose();
        }
        catch { }
    }
}
