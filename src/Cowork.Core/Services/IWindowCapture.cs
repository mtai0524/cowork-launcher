namespace Cowork.Core.Services;

/// <summary>Một tấm ảnh đã mã hoá PNG, kèm kích thước thật sau khi thu nhỏ.</summary>
public sealed record CapturedImage(byte[] Png, int Width, int Height);

/// <summary>
/// Chụp ảnh một cửa sổ. Phần cài đặt dùng API riêng của Windows nên nằm ở
/// <c>Cowork.App</c>; ở đây chỉ có giao diện để tầng Core không kéo theo
/// <c>System.Drawing</c> lẫn <c>user32</c> — hub chạy trên Linux cũng tham chiếu tầng này.
/// </summary>
public interface IWindowCapture
{
    /// <summary>
    /// Chụp cửa sổ <paramref name="windowHandle"/>, thu nhỏ về tối đa
    /// <paramref name="maxWidth"/> điểm ảnh chiều ngang. Trả <c>null</c> khi không chụp được.
    /// </summary>
    CapturedImage? Capture(nint windowHandle, int maxWidth);
}
