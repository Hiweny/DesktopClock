using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace DesktopClock;

/// <summary>
/// 一条展示内容：诗句正文 + 署名。
/// </summary>
public sealed class Poem
{
    public string Text { get; init; } = string.Empty;
    public string Attribution { get; init; } = string.Empty;
    public bool IsOffline { get; init; }

    public static Poem Offline(DateTime now)
    {
        var greeting = Greeting(now.Hour);
        var (line, source) = LocalLines.Pick(now);
        return new Poem
        {
            Text = greeting,
            Attribution = line + " —— " + source,
            IsOffline = true
        };
    }

    private static string Greeting(int hour)
    {
        if (hour >= 5 && hour < 9) return "早上好，愿你今天万事顺遂";
        if (hour >= 9 && hour < 12) return "上午好，愿你诸事如意";
        if (hour >= 12 && hour < 14) return "中午好，记得好好吃饭";
        if (hour >= 14 && hour < 18) return "下午好，愿你元气满满";
        if (hour >= 18 && hour < 23) return "晚上好，愿你卸下一天疲惫";
        return "夜深了，早点歇息";
    }
}

internal static class LocalLines
{
    private static readonly (string Line, string Source)[] Lines =
    {
        ("行到水穷处，坐看云起时。", "王维《终南别业》"),
        ("山重水复疑无路，柳暗花明又一村。", "陆游《游山西村》"),
        ("不畏浮云遮望眼，自缘身在最高层。", "王安石《登飞来峰》"),
        ("海内存知己，天涯若比邻。", "王勃《送杜少府之任蜀州》"),
        ("长风破浪会有时，直挂云帆济沧海。", "李白《行路难》"),
        ("会当凌绝顶，一览众山小。", "杜甫《望岳》"),
        ("春风得意马蹄疾，一日看尽长安花。", "孟郊《登科后》"),
        ("但愿人长久，千里共婵娟。", "苏轼《水调歌头》"),
        ("沉舟侧畔千帆过，病树前头万木春。", "刘禹锡《酬乐天扬州初逢席上见赠》"),
        ("落霞与孤鹜齐飞，秋水共长天一色。", "王勃《滕王阁序》"),
        ("采菊东篱下，悠然见南山。", "陶渊明《饮酒》"),
        ("莫听穿林打叶声，何妨吟啸且徐行。", "苏轼《定风波》"),
        ("纸上得来终觉浅，绝知此事要躬行。", "陆游《冬夜读书示子聿》"),
        ("天生我材必有用，千金散尽还复来。", "李白《将进酒》"),
        ("一蓑烟雨任平生。", "苏轼《定风波》"),
        ("露从今夜白，月是故乡明。", "杜甫《月夜忆舍弟》")
    };

    public static (string Line, string Source) Pick(DateTime now)
    {
        // 以"每 10 分钟"为粒度变化，让离线时也不必频繁变动
        int idx = Math.Abs((int)(now.Ticks / TimeSpan.TicksPerMinute / 10)) % Lines.Length;
        return Lines[idx];
    }
}

/// <summary>
/// 古诗词一言服务：优先「一言(hitokoto)」，其次「今日诗词」，均失败则本地兜底。
/// </summary>
internal sealed class PoemService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("DesktopClock/1.0");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return c;
    }

    public async Task<Poem> FetchAsync()
    {
        var now = DateTime.Now;

        var one = await TryHitokotoAsync().ConfigureAwait(false);
        if (one != null) return one;

        var two = await TryJinrishiciAsync().ConfigureAwait(false);
        if (two != null) return two;

        return Poem.Offline(now);
    }

    private static async Task<Poem?> TryHitokotoAsync()
    {
        try
        {
            // c=i 表示"诗词"分类
            string url = "https://v1.hitokoto.cn/?c=i&encode=json";
            string body = await Http.GetStringAsync(url).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            string text = root.TryGetProperty("hitokoto", out var h) ? (h.GetString() ?? "") : "";
            string from = root.TryGetProperty("from", out var f) ? (f.GetString() ?? "") : "";
            string who = root.TryGetProperty("from_who", out var w) ? (w.GetString() ?? "") : "";
            if (string.IsNullOrWhiteSpace(text)) return null;

            string attribution = BuildAttribution(who, from);
            return new Poem { Text = text.Trim(), Attribution = attribution };
        }
        catch
        {
            return null;
        }
    }

    private static async Task<Poem?> TryJinrishiciAsync()
    {
        try
        {
            string url = "https://v2.jinrishici.com/one.json";
            string body = await Http.GetStringAsync(url).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("data", out var data)) return null;
            string text = data.TryGetProperty("content", out var c) ? (c.GetString() ?? "") : "";
            if (string.IsNullOrWhiteSpace(text)) return null;
            string author = "", title = "";
            if (data.TryGetProperty("origin", out var origin))
            {
                author = origin.TryGetProperty("author", out var a) ? (a.GetString() ?? "") : "";
                title = origin.TryGetProperty("title", out var t) ? (t.GetString() ?? "") : "";
            }
            return new Poem { Text = text.Trim(), Attribution = BuildAttribution(author, title) };
        }
        catch
        {
            return null;
        }
    }

    private static string BuildAttribution(string who, string from)
    {
        who = who?.Trim() ?? "";
        from = from?.Trim() ?? "";
        if (string.IsNullOrEmpty(from)) return who;
        if (string.IsNullOrEmpty(who)) return "《" + from + "》";
        return who + "《" + from + "》";
    }
}
