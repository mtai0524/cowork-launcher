using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Cowork.Core.Services;

namespace Cowork.App.Services;

/// <summary>
/// Chụp một cửa sổ bằng <c>PrintWindow</c> của user32.
///
/// Dùng <c>PrintWindow</c> chứ không phải BitBlt từ màn hình: BitBlt chỉ lấy được những gì
/// đang hiện, nên cửa sổ bị che hay thu nhỏ sẽ ra ảnh đen hoặc ảnh của cửa sổ nằm trên.
/// App chạy nền thường xuyên ở đúng tình trạng đó.
///
/// Cờ <c>PW_RENDERFULLCONTENT</c> cần cho cửa sổ vẽ bằng GPU (Chrome, Electron, WPF có
/// phần tăng tốc); thiếu nó thì phần thân cửa sổ ra một mảng đen.
/// </summary>
public sealed class GdiWindowCapture : IWindowCapture
{
    private const uint RenderFullContent = 0x00000002;

    public CapturedImage? Capture(nint windowHandle, int maxWidth)
    {
        if (windowHandle == 0 || !IsWindow(windowHandle) || !IsWindowVisible(windowHandle))
            return null;

        if (!GetWindowRect(windowHandle, out var rect))
            return null;

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        // Cửa sổ thu nhỏ xuống thanh tác vụ báo kích thước vô nghĩa (thường là 0 hoặc âm).
        if (width <= 0 || height <= 0)
            return null;

        try
        {
            using var shot = new Bitmap(width, height, PixelFormat.Format32bppArgb);

            using (var graphics = Graphics.FromImage(shot))
            {
                var deviceContext = graphics.GetHdc();
                try
                {
                    if (!PrintWindow(windowHandle, deviceContext, RenderFullContent))
                        return null;
                }
                finally
                {
                    graphics.ReleaseHdc(deviceContext);
                }
            }

            using var scaled = Downscale(shot, maxWidth);
            using var buffer = new MemoryStream();
            scaled.Save(buffer, ImageFormat.Png);

            return new CapturedImage(buffer.ToArray(), scaled.Width, scaled.Height);
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or InvalidOperationException or IOException)
        {
            // Cửa sổ đóng ngay giữa lúc chụp, hoặc hết bộ nhớ GDI. Không có ảnh thì thôi,
            // đây là tính năng xem cho biết, không đáng làm agent chết.
            return null;
        }
    }

    private static Bitmap Downscale(Bitmap source, int maxWidth)
    {
        if (source.Width <= maxWidth)
            return (Bitmap)source.Clone();

        var height = (int)Math.Round(source.Height * (double)maxWidth / source.Width);
        var target = new Bitmap(maxWidth, Math.Max(1, height), PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(target);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, 0, 0, target.Width, target.Height);

        return target;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(nint window, nint deviceContext, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
