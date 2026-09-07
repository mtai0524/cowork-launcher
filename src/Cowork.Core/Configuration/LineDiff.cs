namespace Cowork.Core.Configuration;

public enum DiffKind
{
    Unchanged = 0,
    Removed = 1,
    Added = 2,
}

/// <summary>
/// Một dòng trong bảng so sánh. <see cref="LeftNumber"/> và <see cref="RightNumber"/> là số dòng
/// ở mỗi bên (null nghĩa là bên đó không có dòng này).
/// </summary>
public sealed record DiffLine(DiffKind Kind, string Text, int? LeftNumber, int? RightNumber);

public sealed record DiffResult(IReadOnlyList<DiffLine> Lines, int Added, int Removed, bool Truncated)
{
    public bool HasChanges => Added > 0 || Removed > 0;
}

/// <summary>
/// So sánh hai văn bản theo dòng. Hàm thuần, không đụng đĩa — nhờ vậy kiểm thử được đầy đủ và dùng
/// lại được cho mọi cặp text (bản đang sửa với bản trên đĩa, bản sao lưu với bản hiện tại…).
///
/// Cách làm: cắt phần đầu và phần đuôi giống nhau trước, rồi chạy LCS trên khúc giữa. Với file cấu
/// hình — sửa vài dòng giữa hàng nghìn dòng — bước cắt này giải quyết gần hết, nên bảng quy hoạch
/// động hiếm khi lớn.
/// </summary>
public static class LineDiff
{
    /// <summary>
    /// Trần cho khúc giữa. Vượt qua thì trả về "cả khối bị thay" thay vì dựng bảng hàng trăm triệu ô;
    /// so hai file khác hẳn nhau không đáng để treo giao diện vài giây.
    /// </summary>
    public const int MaxBlockLines = 2000;

    public static DiffResult Compare(string? left, string? right)
    {
        var leftLines = SplitLines(left);
        var rightLines = SplitLines(right);

        var lines = new List<DiffLine>();
        var added = 0;
        var removed = 0;

        // Phần đầu giống nhau.
        var prefix = 0;
        while (prefix < leftLines.Length && prefix < rightLines.Length
               && string.Equals(leftLines[prefix], rightLines[prefix], StringComparison.Ordinal))
        {
            prefix++;
        }

        // Phần đuôi giống nhau, không lấn vào phần đầu đã cắt.
        var suffix = 0;
        while (suffix < leftLines.Length - prefix && suffix < rightLines.Length - prefix
               && string.Equals(leftLines[^(suffix + 1)], rightLines[^(suffix + 1)], StringComparison.Ordinal))
        {
            suffix++;
        }

        for (var i = 0; i < prefix; i++)
            lines.Add(new DiffLine(DiffKind.Unchanged, leftLines[i], i + 1, i + 1));

        var leftBlock = leftLines[prefix..(leftLines.Length - suffix)];
        var rightBlock = rightLines[prefix..(rightLines.Length - suffix)];

        var truncated = leftBlock.Length > MaxBlockLines || rightBlock.Length > MaxBlockLines;
        if (truncated)
        {
            // Quá lớn để dò từng dòng: coi cả khối là "bỏ hết bên trái, thêm hết bên phải".
            for (var i = 0; i < leftBlock.Length; i++)
                lines.Add(new DiffLine(DiffKind.Removed, leftBlock[i], prefix + i + 1, null));
            for (var i = 0; i < rightBlock.Length; i++)
                lines.Add(new DiffLine(DiffKind.Added, rightBlock[i], null, prefix + i + 1));

            removed = leftBlock.Length;
            added = rightBlock.Length;
        }
        else
        {
            DiffBlock(leftBlock, rightBlock, prefix, lines, ref added, ref removed);
        }

        for (var i = 0; i < suffix; i++)
        {
            var leftIndex = leftLines.Length - suffix + i;
            var rightIndex = rightLines.Length - suffix + i;
            lines.Add(new DiffLine(DiffKind.Unchanged, leftLines[leftIndex], leftIndex + 1, rightIndex + 1));
        }

        return new DiffResult(lines, added, removed, truncated);
    }

    /// <summary>Chỉ những dòng đã đổi, kèm vài dòng ngữ cảnh quanh mỗi cụm.</summary>
    public static DiffResult OnlyChanges(DiffResult full, int context = 3)
    {
        ArgumentNullException.ThrowIfNull(full);

        var keep = new bool[full.Lines.Count];
        for (var i = 0; i < full.Lines.Count; i++)
        {
            if (full.Lines[i].Kind == DiffKind.Unchanged)
                continue;

            for (var j = Math.Max(0, i - context); j <= Math.Min(full.Lines.Count - 1, i + context); j++)
                keep[j] = true;
        }

        var lines = full.Lines.Where((_, i) => keep[i]).ToList();
        return full with { Lines = lines };
    }

    private static void DiffBlock(
        string[] left, string[] right, int offset, List<DiffLine> lines, ref int added, ref int removed)
    {
        // lcs[i, j] = độ dài dãy con chung dài nhất của left[i..] và right[j..].
        var lcs = new int[left.Length + 1, right.Length + 1];
        for (var i = left.Length - 1; i >= 0; i--)
        {
            for (var j = right.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = string.Equals(left[i], right[j], StringComparison.Ordinal)
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        int x = 0, y = 0;
        while (x < left.Length && y < right.Length)
        {
            if (string.Equals(left[x], right[y], StringComparison.Ordinal))
            {
                lines.Add(new DiffLine(DiffKind.Unchanged, left[x], offset + x + 1, offset + y + 1));
                x++;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                lines.Add(new DiffLine(DiffKind.Removed, left[x], offset + x + 1, null));
                removed++;
                x++;
            }
            else
            {
                lines.Add(new DiffLine(DiffKind.Added, right[y], null, offset + y + 1));
                added++;
                y++;
            }
        }

        for (; x < left.Length; x++)
        {
            lines.Add(new DiffLine(DiffKind.Removed, left[x], offset + x + 1, null));
            removed++;
        }

        for (; y < right.Length; y++)
        {
            lines.Add(new DiffLine(DiffKind.Added, right[y], null, offset + y + 1));
            added++;
        }
    }

    /// <summary>
    /// Tách dòng cho cả CRLF, LF và CR. Văn bản rỗng cho về mảng rỗng — không phải một dòng trắng,
    /// nếu không file rỗng với file có một dòng trắng trông giống hệt nhau.
    /// </summary>
    private static string[] SplitLines(string? text)
        => string.IsNullOrEmpty(text)
            ? Array.Empty<string>()
            : text.ReplaceLineEndings("\n").Split('\n');
}
