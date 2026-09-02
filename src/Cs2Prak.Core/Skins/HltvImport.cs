using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Cs2Prak.Core.Skins;

public static partial class HltvImport
{
    [GeneratedRegex(@"hltv\.org(/(?:[a-z]{2}/)?player/\d+[^\s""'<>]*)", RegexOptions.IgnoreCase)]
    private static partial Regex PlayerPath();

    [GeneratedRegex(@">\s*([^<>]+?)(?:'|&#x27;|&apos;)s\s+inventory")]
    private static partial Regex PlayerName();

    [GeneratedRegex("""<div class="skins-wrapper([^"]*)">\s*<a href="(/skins/[^"#]+)(#[^"]*)?" class="skin-top">(.*?)</a>""",
                    RegexOptions.Singleline)]
    private static partial Regex ItemBlock();

    [GeneratedRegex("""skin-title">([^<]*)<""")]
    private static partial Regex Title();

    [GeneratedRegex("""class="wear">([^<]*)<""")]
    private static partial Regex Wear();

    [GeneratedRegex("""skin-phase[^>]*>([^<]*)<""")]
    private static partial Regex Phase();

    [GeneratedRegex("class=\"skin-img\" src=\"([^\"]+)\"|src=\"([^\"]+)\" class=\"skin-img\"")]
    private static partial Regex Image();

    [GeneratedRegex("rarity-([a-z0-9-]+)")]
    private static partial Regex Rarity();

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex NotAlphanumeric();

    private static readonly Dictionary<string, double> WearFloat = new()
    {
        ["FN"] = 0.01, ["MW"] = 0.10, ["FT"] = 0.20, ["WW"] = 0.40, ["BS"] = 0.80,
    };

    private static readonly Dictionary<int, string> PhaseByPaint = new()
    {
        [415] = "Ruby", [416] = "Sapphire", [417] = "Black Pearl",
        [418] = "Phase 1", [419] = "Phase 2", [420] = "Phase 3", [421] = "Phase 4",
        [568] = "Emerald", [569] = "Phase 1", [570] = "Phase 2", [571] = "Phase 3", [572] = "Phase 4",
        [617] = "Black Pearl", [618] = "Phase 2", [619] = "Sapphire",
        [852] = "Phase 1", [853] = "Phase 2", [854] = "Phase 3", [855] = "Phase 4",
        [1119] = "Emerald", [1120] = "Phase 1", [1121] = "Phase 2", [1122] = "Phase 3", [1123] = "Phase 4",
    };

    private static readonly Dictionary<string, string> PhaseTag = new(StringComparer.OrdinalIgnoreCase)
    {
        ["R"] = "Ruby", ["S"] = "Sapphire", ["BP"] = "Black Pearl",
        ["E"] = "Emerald", ["EM"] = "Emerald",
        ["P1"] = "Phase 1", ["P2"] = "Phase 2", ["P3"] = "Phase 3", ["P4"] = "Phase 4",
    };

    public sealed record Result(bool Ok, string? Reason, string Player, int Count,
                                JsonArray Matched, JsonArray Unmatched);

    private static Result Failed(string reason) => new(false, reason, "", 0, [], []);

    public static Result FromUrl(string? url)
    {
        var match = PlayerPath().Match(url ?? "");
        if (!match.Success) return Failed("badurl");

        string document;
        try { document = Fetch("https://www.hltv.org" + match.Groups[1].Value); }
        catch (Exception) { return Failed("fetch"); }

        var (player, items) = Parse(document);
        if (items.Count == 0) return Failed("noskins");

        Catalogues.RefreshIfChanged();
        if (!Catalogues.Any) return Failed("nocatalog");

        var (matched, unmatched) = Match(items);
        return new Result(true, null, player, items.Count, matched, unmatched);
    }

    private static string Fetch(string pageUrl)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var headers = http.DefaultRequestHeaders;
        headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                                  + "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120 Safari/537.36");
        headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,"
                              + "image/avif,image/webp,*/*;q=0.8");
        headers.Add("Accept-Language", "en-US,en;q=0.9");
        headers.Add("Sec-Fetch-Mode", "navigate");
        headers.Add("Sec-Fetch-Site", "none");
        headers.Add("Upgrade-Insecure-Requests", "1");

        var response = http.GetAsync(pageUrl).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    private sealed record ScrapedItem(string Category, string WeaponSlug, string Title,
                                      string Wear, string Phase, string Image, string Rarity);

    private static (string Player, List<ScrapedItem> Items) Parse(string document)
    {
        var start = document.IndexOf("id=\"skins-loadout\"", StringComparison.Ordinal);
        if (start < 0) return ("", []);

        var section = document[start..Math.Min(document.Length, start + 80000)];

        var name = PlayerName().Match(section);
        var player = name.Success ? WebUtility.HtmlDecode(name.Groups[1].Value.Trim()) : "";

        var items = new List<ScrapedItem>();
        foreach (Match block in ItemBlock().Matches(section))
        {
            var wrapperClass = block.Groups[1].Value;
            var href = block.Groups[2].Value;
            var inner = block.Groups[4].Value;

            var parts = href.Trim('/').Split('/');
            if (parts.Length < 4) continue;

            var image = Image().Match(inner);
            items.Add(new ScrapedItem(
                Category: parts[1],
                WeaponSlug: parts[2],
                Title: Group(Title(), inner) is { } t ? WebUtility.HtmlDecode(t.Trim()) : "",
                Wear: (Group(Wear(), inner) ?? "").Trim().ToUpperInvariant(),
                Phase: (Group(Phase(), inner) ?? "").Trim(),
                Image: image.Success
                    ? (image.Groups[1].Success ? image.Groups[1].Value : image.Groups[2].Value)
                    : "",
                Rarity: Group(Rarity(), wrapperClass) ?? ""));
        }
        return (player, items);
    }

    private static string? Group(Regex regex, string input)
    {
        var m = regex.Match(input);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string Normalise(string? s) =>
        NotAlphanumeric().Replace((s ?? "").Replace("★", "").ToLowerInvariant(), "");

    private static int PaintId(JsonNode? node)
    {
        if (node is not JsonValue v) return 0;
        if (v.TryGetValue(out long l)) return (int)l;
        if (v.TryGetValue(out string? s) && int.TryParse(s, out var parsed)) return parsed;
        return 0;
    }

    private static (JsonArray Matched, JsonArray Unmatched) Match(List<ScrapedItem> items)
    {
        var skinIndex = new Dictionary<(string, string), List<(string Weapon, JsonObject Skin)>>();
        foreach (var (weaponName, bucket) in Catalogues.SkinsByWeapon)
        {
            if (bucket is not JsonArray list) continue;
            foreach (var node in list)
            {
                if (node is not JsonObject skin) continue;
                if (SplitPaintName(skin) is not { } key) continue;
                if (!skinIndex.TryGetValue(key, out var candidates))
                    skinIndex[key] = candidates = [];
                candidates.Add((weaponName, skin));
            }
        }

        var gloveIndex = new Dictionary<(string, string), List<JsonObject>>();
        foreach (var node in Catalogues.Gloves)
        {
            if (node is not JsonObject glove) continue;
            if (SplitPaintName(glove) is not { } key) continue;
            if (!gloveIndex.TryGetValue(key, out var candidates))
                gloveIndex[key] = candidates = [];
            candidates.Add(glove);
        }

        var matched = new JsonArray();
        var unmatched = new JsonArray();
        var seen = new HashSet<(string, int, int)>();
        var slotsTaken = new HashSet<string>();

        void Add(ScrapedItem item, string kind, int defindex, int paintId, string name,
                 string image, double wearFloat, string phaseLabel, string? weaponName)
        {
            if (!seen.Add((kind, defindex, paintId))) return;

            var slot = kind switch { "knife" => "knife", "glove" => "glove", _ => defindex.ToString() };
            var entry = new JsonObject
            {
                ["kind"] = kind,
                ["weapon_defindex"] = defindex,
                ["paint_id"] = paintId,
                ["name"] = name,
                ["image"] = image,
                ["wear"] = item.Wear,
                ["wear_float"] = wearFloat,
                ["phase"] = phaseLabel,
                ["default_sel"] = slotsTaken.Add(slot),
                ["rarity"] = item.Rarity,
            };
            if (weaponName is not null) entry["weapon_name"] = weaponName;
            matched.Add(entry);
        }

        foreach (var item in items)
        {
            var key = (Normalise(item.WeaponSlug), Normalise(item.Title));
            var wearFloat = WearFloat.GetValueOrDefault(item.Wear, 0.06);

            if (item.Category == "gloves")
            {
                if (gloveIndex.TryGetValue(key, out var gloves) && gloves.Count > 0)
                {
                    var glove = gloves[0];
                    Add(item, "glove",
                        (int)(glove["weapon_defindex"]?.GetValue<long>() ?? 0),
                        PaintId(glove["paint"]),
                        CleanName(glove),
                        item.Image.Length > 0 ? item.Image : Str(glove["image"]),
                        wearFloat, "", null);
                    continue;
                }
            }
            else if (skinIndex.TryGetValue(key, out var candidates) && candidates.Count > 0)
            {
                var (weapon, skin) = candidates[0];

                var hasPhase = PhaseTag.TryGetValue(item.Phase, out var wanted);
                var phaseLabel = "";
                if (hasPhase && candidates.Count > 1)
                {
                    foreach (var (otherWeapon, otherSkin) in candidates)
                    {
                        if (PhaseByPaint.GetValueOrDefault(PaintId(otherSkin["paint"])) != wanted) continue;
                        (weapon, skin) = (otherWeapon, otherSkin);
                        phaseLabel = wanted ?? "";
                        break;
                    }
                }
                if (phaseLabel.Length == 0)
                    phaseLabel = PhaseByPaint.GetValueOrDefault(PaintId(skin["paint"]), "");

                var isKnife = weapon.StartsWith("weapon_knife", StringComparison.Ordinal)
                              || weapon == "weapon_bayonet";

                Add(item, isKnife ? "knife" : "skin",
                    (int)(skin["weapon_defindex"]?.GetValue<long>() ?? 0),
                    PaintId(skin["paint"]),
                    CleanName(skin),
                    item.Image.Length > 0 ? item.Image : Str(skin["image"]),
                    wearFloat, phaseLabel, weapon);
                continue;
            }

            var label = (Titleise(item.WeaponSlug) + " | " + item.Title).Trim(' ', '|');
            unmatched.Add(new JsonObject
            {
                ["name"] = label,
                ["wear"] = item.Wear,
                ["image"] = item.Image,
            });
        }

        return (matched, unmatched);
    }

    private static (string, string)? SplitPaintName(JsonObject entry)
    {
        var paintName = Str(entry["paint_name"]);
        var bar = paintName.IndexOf('|');
        if (bar < 0) return null;
        return (Normalise(paintName[..bar]), Normalise(paintName[(bar + 1)..]));
    }

    private static string CleanName(JsonObject entry) =>
        Str(entry["paint_name"]).Replace("★", "").Trim();

    private static string Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue(out string? s) ? s : "";

    private static string Titleise(string slug)
    {
        var words = slug.Replace('-', ' ').Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            if (words[i].Length == 0) continue;
            words[i] = char.ToUpperInvariant(words[i][0]) + words[i][1..].ToLowerInvariant();
        }
        return string.Join(' ', words);
    }
}
