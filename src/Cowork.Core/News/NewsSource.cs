namespace Cowork.Core.News;

/// <summary>
/// Một nguồn tin: địa chỉ RSS/Atom cộng với chủ đề mà nó phục vụ.
///
/// Chủ đề gắn ở nguồn chứ không ở từng bài: đọc được chủ đề của một bài từ nội dung
/// đòi hỏi phân loại văn bản, còn "feed AI của TechCrunch chỉ đăng tin AI" thì đúng
/// gần như luôn và không cần chạy gì cả.
/// </summary>
/// <param name="Id">Mã ngắn, ổn định — dùng làm khoá bộ nhớ đệm và trong danh sách nguồn đã tắt.</param>
public sealed record NewsSource(
    string Id,
    string Name,
    string FeedUrl,
    NewsRegion Region,
    IReadOnlyList<NewsTopic> Topics)
{
    /// <summary>Nguồn này có phục vụ chủ đề nào trong số đang chọn không.</summary>
    public bool Covers(IReadOnlyCollection<NewsTopic> wanted)
    {
        foreach (var topic in Topics)
        {
            if (wanted.Contains(topic))
                return true;
        }

        return false;
    }
}
