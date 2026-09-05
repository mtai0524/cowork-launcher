using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Cowork.Core.Services;

/// <summary>
/// Gửi Ctrl+C cho một tiến trình console. App console (node, python, .bat…) không có cửa sổ chính
/// nên <c>CloseMainWindow</c> vô dụng với chúng; Ctrl+C mới là tín hiệu "dừng lịch sự" mà chúng hiểu.
///
/// Cách làm: tạm nối Cowork vào console của tiến trình đích, phát Ctrl+C cho mọi tiến trình đang
/// dùng console đó (kể cả tiến trình con của nó), rồi tách ra. Hai điều dễ sai:
///
/// - Cowork cũng nhận đúng Ctrl+C mà nó phát, và handler mặc định của Windows là tắt tiến trình.
///   Cài handler riêng không ăn thua: AttachConsole/FreeConsole xoá sạch danh sách handler. Thứ
///   sống sót qua các bước đó là cờ "bỏ qua Ctrl+C" của tiến trình, nên dùng cờ này và cứ để bật.
/// - Nhưng cờ đó được tiến trình con kế thừa: con sinh ra lúc cờ đang bật sẽ điếc với Ctrl+C mãi
///   mãi (kể cả cờ Cowork thừa hưởng từ thứ đã mở nó). Vì vậy mọi tiến trình con phải khởi chạy
///   qua <see cref="StartChild"/>, nơi cờ được tắt ngay trước khi tạo tiến trình, dưới cùng một
///   khoá với việc gửi tín hiệu.
/// </summary>
internal static class ConsoleSignal
{
    private const uint CtrlCEvent = 0;
    private const uint AttachParentProcess = unchecked((uint)-1);
    private static readonly object Gate = new();

    /// <summary>Khởi chạy tiến trình con với Ctrl+C được bật, để sau này bấm Dừng nó còn nhận tín hiệu.</summary>
    public static bool StartChild(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!OperatingSystem.IsWindows())
            return process.Start();

        if (process.StartInfo.UseShellExecute)
        {
            // Chạy quyền admin đi qua ShellExecute và có thể đứng chờ người dùng ở hộp thoại UAC;
            // không giữ khoá trong lúc đó, nếu không lệnh Dừng app khác sẽ kẹt theo.
            SetConsoleCtrlHandler(IntPtr.Zero, false);
            return process.Start();
        }

        lock (Gate)
        {
            SetConsoleCtrlHandler(IntPtr.Zero, false);
            return process.Start();
        }
    }

    /// <summary>Gửi Ctrl+C tới console của tiến trình. Trả về false kèm lý do nếu không gửi được.</summary>
    public static bool TrySendCtrlC(int processId, out string? failure)
    {
        if (!OperatingSystem.IsWindows())
        {
            failure = "Chỉ hỗ trợ trên Windows.";
            return false;
        }

        return SendOnWindows(processId, out failure);
    }

    [SupportedOSPlatform("windows")]
    private static bool SendOnWindows(int processId, out string? failure)
    {
        failure = null;

        lock (Gate)
        {
            // Cowork.exe không có console; nhưng khi chạy trong test host thì có, và AttachConsole
            // từ chối khi đang nối với một console khác — tách ra trước, nối lại console cha sau.
            var hadConsole = GetConsoleWindow() != IntPtr.Zero;
            FreeConsole();

            if (!AttachConsole((uint)processId))
            {
                failure = Describe("AttachConsole");
                Detach(hadConsole);
                return false;
            }

            // Bật cờ bỏ qua trước khi phát, và không tắt lại ở đây: tín hiệu tới Cowork trễ vài mili
            // giây, có thể sau khi đã tách console. StartChild sẽ tắt cờ đúng lúc cần.
            SetConsoleCtrlHandler(IntPtr.Zero, true);

            try
            {
                if (GenerateConsoleCtrlEvent(CtrlCEvent, 0))
                    return true;

                failure = Describe("GenerateConsoleCtrlEvent");
                return false;
            }
            finally
            {
                Detach(hadConsole);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void Detach(bool reattachParent)
    {
        FreeConsole();
        if (reattachParent)
            AttachConsole(AttachParentProcess);
    }

    private static string Describe(string api) => $"{api} thất bại (mã lỗi Windows {Marshal.GetLastWin32Error()})";

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();
}
