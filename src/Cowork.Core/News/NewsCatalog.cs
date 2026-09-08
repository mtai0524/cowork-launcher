using Cowork.Core.Models;

namespace Cowork.Core.News;

/// <summary>
/// Danh sách nguồn dựng sẵn, cộng với cách gộp nguồn người dùng tự thêm.
///
/// Nghiêng hẳn về nguồn nước ngoài — đó là nơi tin AI và agent ra trước — nhưng vẫn giữ
/// vài báo công nghệ trong nước. Cân đối bao nhiêu là do <see cref="NewsDigest"/> quyết,
/// ở đây chỉ khai có gì.
/// </summary>
public static class NewsCatalog
{
    /// <summary>Nguồn dựng sẵn. Thứ tự không quan trọng: bảng tin sắp lại theo thời gian.</summary>
    public static IReadOnlyList<NewsSource> BuiltIn { get; } = new List<NewsSource>
    {
        // ---------- AI ----------
        new("openai", "OpenAI", "https://openai.com/news/rss.xml",
            NewsRegion.Global, new[] { NewsTopic.Ai, NewsTopic.Agents }),
        new("deepmind", "Google DeepMind", "https://deepmind.google/blog/rss.xml",
            NewsRegion.Global, new[] { NewsTopic.Ai }),
        new("huggingface", "Hugging Face", "https://huggingface.co/blog/feed.xml",
            NewsRegion.Global, new[] { NewsTopic.Ai }),
        new("techcrunch-ai", "TechCrunch AI", "https://techcrunch.com/category/artificial-intelligence/feed/",
            NewsRegion.Global, new[] { NewsTopic.Ai }),
        new("the-decoder", "The Decoder", "https://the-decoder.com/feed/",
            NewsRegion.Global, new[] { NewsTopic.Ai }),
        new("google-ai", "Google AI", "https://blog.google/technology/ai/rss/",
            NewsRegion.Global, new[] { NewsTopic.Ai }),
        new("mit-technology-review", "MIT Technology Review", "https://www.technologyreview.com/feed/",
            NewsRegion.Global, new[] { NewsTopic.Ai, NewsTopic.Technology }),
        new("arxiv-ai", "arXiv cs.AI", "https://export.arxiv.org/rss/cs.AI",
            NewsRegion.Global, new[] { NewsTopic.Ai }),

        // ---------- Agent ----------
        new("simon-willison", "Simon Willison", "https://simonwillison.net/atom/everything/",
            NewsRegion.Global, new[] { NewsTopic.Agents, NewsTopic.Ai, NewsTopic.Programming }),
        new("latent-space", "Latent Space", "https://www.latent.space/feed",
            NewsRegion.Global, new[] { NewsTopic.Agents, NewsTopic.Ai }),
        new("arxiv-agents", "arXiv cs.MA", "https://export.arxiv.org/rss/cs.MA",
            NewsRegion.Global, new[] { NewsTopic.Agents }),
        new("hn-agents", "Hacker News — AI agents", "https://hnrss.org/newest?q=%22AI+agent%22&points=20",
            NewsRegion.Global, new[] { NewsTopic.Agents }),

        // ---------- Công nghệ ----------
        new("hacker-news", "Hacker News", "https://hnrss.org/frontpage",
            NewsRegion.Global, new[] { NewsTopic.Technology, NewsTopic.Programming }),
        new("the-verge", "The Verge", "https://www.theverge.com/rss/index.xml",
            NewsRegion.Global, new[] { NewsTopic.Technology }),
        new("ars-technica", "Ars Technica", "https://feeds.arstechnica.com/arstechnica/index",
            NewsRegion.Global, new[] { NewsTopic.Technology }),
        new("the-register", "The Register", "https://www.theregister.com/headlines.atom",
            NewsRegion.Global, new[] { NewsTopic.Technology }),
        new("techcrunch", "TechCrunch", "https://techcrunch.com/feed/",
            NewsRegion.Global, new[] { NewsTopic.Technology, NewsTopic.Startups }),

        // ---------- Lập trình ----------
        new("pragmatic-engineer", "The Pragmatic Engineer", "https://blog.pragmaticengineer.com/rss/",
            NewsRegion.Global, new[] { NewsTopic.Programming }),
        new("dotnet-blog", ".NET Blog", "https://devblogs.microsoft.com/dotnet/feed/",
            NewsRegion.Global, new[] { NewsTopic.Programming }),
        new("martin-fowler", "martinfowler.com", "https://martinfowler.com/feed.atom",
            NewsRegion.Global, new[] { NewsTopic.Programming }),
        new("github-blog", "GitHub Blog", "https://github.blog/feed/",
            NewsRegion.Global, new[] { NewsTopic.Programming, NewsTopic.Agents }),

        // ---------- Khởi nghiệp ----------
        new("techcrunch-startups", "TechCrunch Startups", "https://techcrunch.com/category/startups/feed/",
            NewsRegion.Global, new[] { NewsTopic.Startups }),

        // ---------- An ninh ----------
        new("krebs-on-security", "Krebs on Security", "https://krebsonsecurity.com/feed/",
            NewsRegion.Global, new[] { NewsTopic.Security }),
        new("the-hacker-news", "The Hacker News", "https://feeds.feedburner.com/TheHackersNews",
            NewsRegion.Global, new[] { NewsTopic.Security }),
        new("dark-reading", "Dark Reading", "https://www.darkreading.com/rss.xml",
            NewsRegion.Global, new[] { NewsTopic.Security }),


        // ---------- Kho mã ----------
        // Hai cách tìm repo, cố ý để cạnh nhau: bảng xếp hạng thịnh hành cho việc *khám phá*
        // thứ chưa biết, và feed bản phát hành cho việc *theo* thứ đã biết.
        //
        // GitHub không phát RSS cho trang Trending; mshibanami.github.io dựng lại bảng đó
        // thành feed. Đây là nguồn duy nhất trong danh mục đi qua bên thứ ba, và nó chỉ
        // chuyển tiếp thứ vốn công khai, không cần tài khoản.
        new("github-trending", "GitHub Trending — hôm nay",
            "https://mshibanami.github.io/GitHubTrendingRSS/daily/all.xml",
            NewsRegion.Global, new[] { NewsTopic.Repos }),
        new("github-trending-week", "GitHub Trending — tuần này",
            "https://mshibanami.github.io/GitHubTrendingRSS/weekly/all.xml",
            NewsRegion.Global, new[] { NewsTopic.Repos }),
        new("github-trending-python", "GitHub Trending — Python",
            "https://mshibanami.github.io/GitHubTrendingRSS/daily/python.xml",
            NewsRegion.Global, new[] { NewsTopic.Repos }),
        new("github-trending-typescript", "GitHub Trending — TypeScript",
            "https://mshibanami.github.io/GitHubTrendingRSS/daily/typescript.xml",
            NewsRegion.Global, new[] { NewsTopic.Repos }),

        // Bản phát hành của repo. Dùng releases.atom chứ không dùng commits.atom: feed commit
        // hầu hết là "chore: …" của bot, còn một bản phát hành là thứ tác giả chủ động công bố.
        //
        // Chỉ chọn repo có ghi chú phát hành đọc được. Repo chạy tàu nightly hay alpha
        // (gemini-cli, llama.cpp) bị bỏ khỏi danh sách mặc định: mỗi ngày vài bản, tiêu đề là
        // số hiệu bản dựng và phần ghi chú rỗng — chúng lấp chỗ mà không nói được gì. Ai cần
        // theo đúng những repo đó thì thêm bằng feed riêng, địa chỉ là
        // https://github.com/<chủ>/<repo>/releases.atom.
        new("repo-claude-code", "anthropics/claude-code",
            "https://github.com/anthropics/claude-code/releases.atom",
            NewsRegion.Global, new[] { NewsTopic.Repos }),
        new("repo-claude-agent-sdk", "anthropics/claude-agent-sdk-python",
            "https://github.com/anthropics/claude-agent-sdk-python/releases.atom",
            NewsRegion.Global, new[] { NewsTopic.Repos }),
        new("repo-anthropic-sdk", "anthropics/anthropic-sdk-python",
            "https://github.com/anthropics/anthropic-sdk-python/releases.atom",
            NewsRegion.Global, new[] { NewsTopic.Repos }),
        new("repo-mcp-servers", "modelcontextprotocol/servers",
            "https://github.com/modelcontextprotocol/servers/releases.atom",
            NewsRegion.Global, new[] { NewsTopic.Repos }),
        new("repo-mcp-sdk", "modelcontextprotocol/python-sdk",
            "https://github.com/modelcontextprotocol/python-sdk/releases.atom",
            NewsRegion.Global, new[] { NewsTopic.Repos }),
        new("repo-codex", "openai/codex",
            "https://github.com/openai/codex/releases.atom",
            NewsRegion.Global, new[] { NewsTopic.Repos }),
        new("repo-langgraph", "langchain-ai/langgraph",
            "https://github.com/langchain-ai/langgraph/releases.atom",
            NewsRegion.Global, new[] { NewsTopic.Repos }),
        new("repo-browser-use", "browser-use/browser-use",
            "https://github.com/browser-use/browser-use/releases.atom",
            NewsRegion.Global, new[] { NewsTopic.Repos }),
        new("repo-litellm", "BerriAI/litellm",
            "https://github.com/BerriAI/litellm/releases.atom",
            NewsRegion.Global, new[] { NewsTopic.Repos }),
        new("repo-ollama", "ollama/ollama",
            "https://github.com/ollama/ollama/releases.atom",
            NewsRegion.Global, new[] { NewsTopic.Repos }),
        new("repo-vllm", "vllm-project/vllm",
            "https://github.com/vllm-project/vllm/releases.atom",
            NewsRegion.Global, new[] { NewsTopic.Repos }),
        new("repo-transformers", "huggingface/transformers",
            "https://github.com/huggingface/transformers/releases.atom",
            NewsRegion.Global, new[] { NewsTopic.Repos }),
        // ---------- Trong nước ----------
        // Báo trong nước không có feed riêng cho AI; hai nguồn lớn nhất đưa tin AI đủ đều
        // để khai thêm chủ đề Ai, nhờ vậy chọn mỗi AI vẫn còn phần tiếng Việt.
        new("vnexpress-so-hoa", "VnExpress Số hoá", "https://vnexpress.net/rss/so-hoa.rss",
            NewsRegion.Vietnam, new[] { NewsTopic.Technology, NewsTopic.Ai }),
        new("genk", "GenK", "https://genk.vn/rss/home.rss",
            NewsRegion.Vietnam, new[] { NewsTopic.Technology, NewsTopic.Ai }),
        new("tinhte", "Tinh tế", "https://tinhte.vn/rss",
            NewsRegion.Vietnam, new[] { NewsTopic.Technology }),
        new("vietnamnet-cong-nghe", "VietnamNet Công nghệ", "https://vietnamnet.vn/rss/cong-nghe.rss",
            NewsRegion.Vietnam, new[] { NewsTopic.Technology }),
        new("tuoitre-nhip-song-so", "Tuổi Trẻ Nhịp sống số", "https://tuoitre.vn/rss/nhip-song-so.rss",
            NewsRegion.Vietnam, new[] { NewsTopic.Technology }),
        new("cafebiz-cong-nghe", "CafeBiz Kinh doanh", "https://cafebiz.vn/rss/cong-nghe.rss",
            NewsRegion.Vietnam, new[] { NewsTopic.Startups, NewsTopic.Technology }),
    };

    /// <summary>
    /// Những nguồn thật sự phải tải cho một thiết lập: dựng sẵn cộng nguồn tự thêm, bỏ nguồn
    /// đã tắt, và bỏ luôn nguồn không phục vụ chủ đề nào đang chọn — không tải cái sẽ không hiện.
    /// </summary>
    public static IReadOnlyList<NewsSource> Resolve(NewsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var wanted = settings.Topics.Count > 0
            ? settings.Topics.ToHashSet()
            : new HashSet<NewsTopic>();

        if (wanted.Count == 0)
            return Array.Empty<NewsSource>();

        var disabled = settings.DisabledSourceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return All(settings)
            .Where(source => !disabled.Contains(source.Id))
            .Where(source => settings.IncludeVietnam || source.Region != NewsRegion.Vietnam)
            .Where(source => source.Covers(wanted))
            .ToList();
    }

    /// <summary>Mọi nguồn người dùng thấy được trong bảng chọn, kể cả nguồn đang tắt.</summary>
    public static IReadOnlyList<NewsSource> All(NewsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var known = BuiltIn.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var custom = settings.CustomSources
            .Select(c => c.ToSource())
            .Where(s => s is not null)
            .Select(s => s!)
            .Where(s => known.Add(s.Id));

        return BuiltIn.Concat(custom).ToList();
    }
}
