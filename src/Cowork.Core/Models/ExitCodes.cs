using System.Globalization;

namespace Cowork.Core.Models;

/// <summary>
/// Danh sách mã thoát được coi là thành công. Mặc định chỉ có 0, nhưng nhiều công cụ dùng mã
/// khác 0 để báo "xong việc" — robocopy trả 1 khi đã sao chép được file — nếu cứ coi khác 0 là
/// lỗi thì lịch sử ghi sai và khay hệ thống báo lỗi giả.
/// </summary>
public static class ExitCodes
{
    public const int Default = 0;

    private static readonly char[] Separators = { ',', ';', ' ', '\t' };

    /// <summary>Phân tích chuỗi người dùng gõ ("0, 1"); bỏ qua phần không phải số. Rỗng ⇒ chỉ có 0.</summary>
    public static List<int> Parse(string? text)
    {
        var result = new List<int>();
        if (!string.IsNullOrWhiteSpace(text))
        {
            foreach (var token in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
                    result.Add(code);
            }
        }

        return Normalize(result);
    }

    public static string Format(IEnumerable<int>? codes) => string.Join(", ", Normalize(codes));

    /// <summary>Loại trùng và sắp xếp; null hoặc rỗng ⇒ chỉ có 0. Dùng khi nạp workspace cũ hoặc file sửa tay.</summary>
    public static List<int> Normalize(IEnumerable<int>? codes)
    {
        var list = codes?.Distinct().OrderBy(c => c).ToList() ?? new List<int>();
        if (list.Count == 0)
            list.Add(Default);

        return list;
    }

    /// <summary>Mã thoát có nằm trong danh sách thành công không. Không đọc được mã thì tính là lỗi.</summary>
    public static bool IsSuccess(IEnumerable<int>? successCodes, int? exitCode)
        => exitCode is { } code && Normalize(successCodes).Contains(code);
}
