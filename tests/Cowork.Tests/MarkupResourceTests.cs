using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Cowork.Tests;

/// <summary>
/// Đối chiếu mọi khoá tài nguyên dùng trong XAML với nơi khai báo chúng.
///
/// Trình biên dịch markup chỉ bắt lỗi cú pháp và lỗi kiểu. Khoá tài nguyên sai tên thì
/// nó cho qua: <c>StaticResource</c> hụt khoá làm cửa sổ ném ngoại lệ ngay lúc mở,
/// còn <c>DynamicResource</c> hụt khoá thì im lặng — điều khiển mất màu, chữ chìm vào nền.
/// Cả hai chỉ lộ ra khi có người mở đúng cửa sổ đó, nên phải chặn từ đây.
/// </summary>
public class MarkupResourceTests
{
    private const string MarkupPrefix = "Cowork.Tests.Markup.";
    private const string PalettePrefix = "Cowork.Tests.Palettes.";

    private static readonly Regex DynamicResource =
        new(@"\{DynamicResource (\w+)\}", RegexOptions.Compiled);

    // Bỏ qua dạng {StaticResource {x:Type ...}} — đó là style ngầm, không phải khoá đặt tên.
    private static readonly Regex StaticResource =
        new(@"\{StaticResource (\w+)\}", RegexOptions.Compiled);

    private static readonly Regex DeclaredKey =
        new(@"x:Key=""(\w+)""", RegexOptions.Compiled);

    [Fact]
    public void EveryDynamicResourceKey_ResolvesInEveryPalette()
    {
        var referenced = CollectKeys(DynamicResource);
        Assert.NotEmpty(referenced);

        var missing = new List<string>();
        foreach (var palette in Markup(PalettePrefix))
        {
            var defined = BrushKeys(palette.Value);
            missing.AddRange(referenced
                .Where(key => !defined.Contains(key))
                .Select(key => $"{palette.Key}: {key}"));
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryStaticResourceKey_IsDeclaredSomewhereInScope()
    {
        // Tài nguyên chung nhìn thấy được từ mọi cửa sổ, cộng thêm tài nguyên riêng của
        // chính file đó — đúng phạm vi mà WPF tra cứu lúc chạy.
        var shared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Markup(MarkupPrefix).Where(f => IsApplicationWide(f.Key)))
            shared.UnionWith(DeclaredKey.Matches(file.Value).Select(m => m.Groups[1].Value));

        foreach (var palette in Markup(PalettePrefix))
            shared.UnionWith(DeclaredKey.Matches(palette.Value).Select(m => m.Groups[1].Value));

        Assert.NotEmpty(shared);

        var missing = new List<string>();
        foreach (var file in Markup(MarkupPrefix))
        {
            var inScope = new HashSet<string>(shared, StringComparer.Ordinal);
            inScope.UnionWith(DeclaredKey.Matches(file.Value).Select(m => m.Groups[1].Value));

            missing.AddRange(StaticResource.Matches(file.Value)
                .Select(m => m.Groups[1].Value)
                .Where(key => !inScope.Contains(key))
                .Select(key => $"{file.Key}: {key}"));
        }

        Assert.Empty(missing);
    }

    /// <summary>App.xaml và Theme.xaml nạp vào tài nguyên cấp ứng dụng, các cửa sổ thì không.</summary>
    private static bool IsApplicationWide(string name)
        => name is "App" or "Theme";

    private static SortedSet<string> CollectKeys(Regex pattern)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Markup(MarkupPrefix))
            keys.UnionWith(pattern.Matches(file.Value).Select(m => m.Groups[1].Value));

        return keys;
    }

    private static HashSet<string> BrushKeys(string paletteMarkup)
    {
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        return XDocument.Parse(paletteMarkup).Root!.Elements()
            .Where(e => e.Name.LocalName == "SolidColorBrush")
            .Select(e => e.Attribute(xaml + "Key")!.Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Tên file (không đuôi) -> nội dung, đọc từ tài nguyên nhúng trong assembly test.</summary>
    private static IEnumerable<KeyValuePair<string, string>> Markup(string prefix)
    {
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);

            var shortName = name[prefix.Length..].Replace(".xaml", string.Empty, StringComparison.Ordinal);
            yield return new KeyValuePair<string, string>(shortName, reader.ReadToEnd());
        }
    }
}
