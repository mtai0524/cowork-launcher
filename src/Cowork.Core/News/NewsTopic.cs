namespace Cowork.Core.News;

/// <summary>
/// Chủ đề tin. Người dùng chọn vài chủ đề, mỗi nguồn khai sẵn mình thuộc chủ đề nào —
/// nhờ vậy việc lọc là so khớp enum, không phải đoán qua tiêu đề.
/// </summary>
public enum NewsTopic
{
    /// <summary>Trí tuệ nhân tạo nói chung: mô hình, nghiên cứu, sản phẩm.</summary>
    Ai = 0,

    /// <summary>Agent: hệ tự hành, công cụ gọi mô hình, agent lập trình.</summary>
    Agents = 1,

    /// <summary>Công nghệ nói chung: thiết bị, nền tảng, chính sách.</summary>
    Technology = 2,

    /// <summary>Nghề lập trình: ngôn ngữ, kiến trúc, công cụ.</summary>
    Programming = 3,

    /// <summary>Khởi nghiệp và chuyện kinh doanh của ngành.</summary>
    Startups = 4,

    /// <summary>An ninh mạng: lỗ hổng, rò rỉ, tấn công.</summary>
    Security = 5,

    /// <summary>
    /// Kho mã đáng xem: repo đang thịnh hành trên GitHub, và bản phát hành mới của những
    /// repo AI/agent đáng theo. Tách riêng khỏi <see cref="Ai"/> vì đây là chuyện đi tìm
    /// công cụ để dùng, không phải đọc tin.
    /// </summary>
    Repos = 6,
}

/// <summary>
/// Nguồn tin ở đâu ra. Tách riêng khỏi chủ đề vì đây là trục thứ hai: người dùng muốn
/// đọc chủ yếu tin nước ngoài nhưng vẫn giữ một phần tin trong nước.
/// </summary>
public enum NewsRegion
{
    Global = 0,
    Vietnam = 1,
}
