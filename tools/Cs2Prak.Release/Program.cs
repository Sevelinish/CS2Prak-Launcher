using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

const string BundleName = "update.zip";

var flags = args.Where(a => a.StartsWith("--", StringComparison.Ordinal))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

if (positional.Length == 0 || flags.Contains("--help"))
{
    Usage();
    return positional.Length == 0 ? 1 : 0;
}

var version = positional[0].TrimStart('v', 'V');
var buildDir = Path.GetFullPath(positional.Length > 1 ? positional[1] : "publish");
var outDir = Path.GetFullPath(positional.Length > 2 ? positional[2] : "release_assets");
var legacy = flags.Contains("--legacy");
var fullInstall = flags.Contains("--full");
var repo = flags.FirstOrDefault(f => f.StartsWith("--repo=", StringComparison.OrdinalIgnoreCase))
                ?["--repo=".Length..];

if (repo is null && !flags.Contains("--no-marker"))
{
    Fail("не указан --repo=владелец/репозиторий");
    Console.Error.WriteLine("  Сборщик кладёт рядом с exe release.json — он включает");
    Console.Error.WriteLine("  автообновление и деинсталляцию и говорит лаунчеру, откуда");
    Console.Error.WriteLine("  брать релизы. Без этого файла собранная копия считает себя");
    Console.Error.WriteLine("  рабочей папкой разработчика: обновления не проверяются,");
    Console.Error.WriteLine("  а удаление заблокировано, чтобы не снести исходники.");
    Console.Error.WriteLine("  Пример: --repo=Sevelinish/cs2Prak");
    Console.Error.WriteLine("  Собрать заведомо без маркера — --no-marker");
    return 1;
}

if (!CheckVersion(version, flags.Contains("--any-version"))) return 1;

var exePath = Path.Combine(buildDir, "cs2prak.exe");
var depsPath = Path.Combine(buildDir, "cs2prak.deps.json");

if (!File.Exists(exePath))
{
    Fail($"cs2prak.exe не найден в {buildDir}");
    Console.Error.WriteLine("  Сначала опубликуйте сборку:");
    Console.Error.WriteLine("  dotnet publish src/Cs2Prak.App/Cs2Prak.App.csproj -c Release "
                          + "-r win-x64 --self-contained true -o publish");
    return 1;
}
if (!File.Exists(depsPath))
{
    Fail($"cs2prak.deps.json не найден в {buildDir}");
    return 1;
}

if (!CheckSourceVersion(version)) return 1;

if (repo is not null)
{
    var marker = new JsonObject { ["repo"] = repo, ["version"] = version };
    File.WriteAllText(Path.Combine(buildDir, "release.json"),
                      marker.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Маркер установки: release.json -> {repo}");
}

var own = OwnAssemblies(depsPath);
Console.WriteLine($"Сборок вне рантайма .NET: {own.Count}");

if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
Directory.CreateDirectory(outDir);

var files = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
var bundlePath = Path.Combine(outDir, BundleName);
long raw = 0;

using (var bundle = ZipFile.Open(bundlePath, ZipArchiveMode.Create))
{
    foreach (var full in Directory.EnumerateFiles(buildDir, "*", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(buildDir, full).Replace('\\', '/');
        if (!IsUpdatable(relative, own)) continue;

        bundle.CreateEntryFromFile(full, relative, CompressionLevel.Optimal);

        var entry = new JsonObject { ["sha256"] = Sha256(full) };
        if (legacy)
        {
            var asset = relative.Replace("/", "__", StringComparison.Ordinal);
            File.Copy(full, Path.Combine(outDir, asset), overwrite: true);
            entry["asset"] = asset;
        }

        files[relative] = entry;
        raw += new FileInfo(full).Length;
    }
}

if (files.Count == 0)
{
    Fail("в сборке не нашлось ни одного обновляемого файла");
    return 1;
}

var manifest = new JsonObject
{
    ["version"] = version,
    ["files"] = new JsonObject(files.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))),
    ["bundle"] = new JsonObject
    {
        ["asset"] = BundleName,
        ["sha256"] = Sha256(bundlePath),
        ["size"] = new FileInfo(bundlePath).Length,
    },
};

File.WriteAllText(Path.Combine(outDir, "manifest.json"),
                  manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

var bundleSize = new FileInfo(bundlePath).Length;
Console.WriteLine($"Релиз {version}: {files.Count} файлов, "
                + $"{bundleSize / 1048576.0:F1} МБ в {BundleName} ({raw / 1048576.0:F1} МБ сырьём)"
                + (legacy ? $" + {files.Count} отдельных ассетов" : ", только бандл"));

if (!CheckComplete(files.Keys)) return 1;

if (fullInstall)
{
    var installer = Path.Combine(outDir, $"cs2prak-{version}-win-x64.zip");
    ZipFile.CreateFromDirectory(buildDir, installer, CompressionLevel.Optimal,
                                includeBaseDirectory: false);
    Console.WriteLine($"  полная установка: {Path.GetFileName(installer)} "
                    + $"({new FileInfo(installer).Length / 1048576.0:F0} МБ)");
}

Console.WriteLine();
Console.WriteLine($"Дальше: создайте на GitHub релиз с тегом {version} "
                + $"и приложите к нему все файлы из {outDir}");
return 0;

static void Usage()
{
    Console.WriteLine("""
        make-release — упаковка инкрементального обновления cs2prak.

        Использование:
            dotnet run --project tools/Cs2Prak.Release -- <версия> [сборка] [куда] [флаги]

          <версия>       например 1.0.01 — тег релиза на GitHub должен совпадать
          сборка         каталог dotnet publish. По умолчанию: publish
          куда           куда сложить ассеты. По умолчанию: release_assets

        Флаги:
          --repo=X/Y     репозиторий обновлений; кладёт release.json рядом с exe
          --full         дополнительно собрать архив полной установки
          --legacy       дополнительно разложить каждый файл отдельным ассетом
          --any-version  не проверять формат версии
          --no-marker    собрать без release.json (обновления работать не будут)
          --help         эта справка

        Кладёт в каталог назначения manifest.json и update.zip. Апдейтер скачивает
        манифест, сверяет хэши локальных файлов и забирает только то, что разошлось.

        Перед упаковкой не забудьте поднять Version в src/Cs2Prak.Core/AppInfo.cs —
        инструмент это проверяет.
        """);
}

static void Fail(string message) => Console.Error.WriteLine($"! {message}");

static bool CheckVersion(string version, bool skip)
{
    if (skip) return true;

    var match = Regex.Match(version, @"^(\d+)\.(\d+)\.(\d{2})$");
    if (match.Success && int.Parse(match.Groups[3].Value) >= 1) return true;

    Fail($"версия «{version}» не соответствует схеме проекта");
    Console.Error.WriteLine("  Ожидается X.Y.NN, где NN — две цифры от 01 до 99:");
    Console.Error.WriteLine("  1.0.01 … 1.0.09 … 1.0.99, дальше 1.1.01");
    Console.Error.WriteLine("  Ноль в 01 обязателен: без него порядок версий на GitHub");
    Console.Error.WriteLine("  читается человеком неверно, хотя апдейтер сравнивает числа.");
    Console.Error.WriteLine("  Если версия всё же нужна нестандартная — --any-version");
    return false;
}

static bool CheckSourceVersion(string version)
{
    var source = FindAppInfo();
    if (source is null)
    {
        Console.WriteLine("  AppInfo.cs не найден рядом — версию в коде не сверяю");
        return true;
    }

    var declared = Regex.Match(File.ReadAllText(source), @"Version\s*=\s*""([^""]+)""");
    if (!declared.Success)
    {
        Console.WriteLine("  версию в AppInfo.cs не разобрать — пропускаю сверку");
        return true;
    }

    if (declared.Groups[1].Value == version) return true;

    Fail($"версия в коде — {declared.Groups[1].Value}, а упаковать просят {version}");
    Console.Error.WriteLine($"  Поднимите Version в {source} и пересоберите,");
    Console.Error.WriteLine("  иначе установленный лаунчер после обновления доложит старую версию");
    Console.Error.WriteLine("  и предложит то же обновление снова.");
    return false;
}

static string? FindAppInfo()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, "src", "Cs2Prak.Core", "AppInfo.cs");
        if (File.Exists(candidate)) return candidate;
    }
    return null;
}

static HashSet<string> OwnAssemblies(string depsPath)
{
    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (JsonNode.Parse(File.ReadAllText(depsPath)) is not JsonObject deps) return names;

    foreach (var (_, target) in deps["targets"] as JsonObject ?? [])
    {
        foreach (var (package, entry) in target as JsonObject ?? [])
        {
            if (package.StartsWith("runtimepack.", StringComparison.Ordinal)) continue;

            foreach (var section in (string[])["runtime", "native"])
            {
                foreach (var (file, _) in entry?[section] as JsonObject ?? [])
                    names.Add(Path.GetFileName(file.Replace('\\', '/')));
            }
        }
    }
    return names;
}

static bool IsUpdatable(string relative, HashSet<string> own)
{
    if (Ends(relative, ".pdb", ".xml")) return false;

    if (relative is "cs2prak.exe" or "cs2prak.deps.json" or "cs2prak.runtimeconfig.json"
                 or "release.json" or "icon.ico") return true;

    if (relative.StartsWith("static/radars/", StringComparison.Ordinal))
        return relative.EndsWith("/calibration.json", StringComparison.OrdinalIgnoreCase);

    if (relative.StartsWith("static/", StringComparison.Ordinal))
        return Ends(relative, ".js", ".css", ".html", ".svg", ".png", ".jpg", ".jpeg",
                    ".ico", ".json");

    if (relative.StartsWith("templates/", StringComparison.Ordinal))
        return Ends(relative, ".html");

    if (relative.StartsWith("maps/", StringComparison.Ordinal))
        return Ends(relative, ".jpg", ".jpeg", ".png");

    return own.Contains(Path.GetFileName(relative));

    static bool Ends(string text, params string[] suffixes) =>
        suffixes.Any(s => text.EndsWith(s, StringComparison.OrdinalIgnoreCase));
}

static bool CheckComplete(IEnumerable<string> packed)
{
    (string Name, string Why)[] required =
    [
        ("cs2prak.exe", "запускать нечего"),
        ("cs2prak.dll", "нет кода приложения"),
        ("cs2prak.deps.json", "рантайм не найдёт ни одной сборки"),
        ("cs2prak.runtimeconfig.json", "рантайм не поймёт, какую версию .NET брать"),
        ("Cs2Prak.Core.dll", "нет ядра"),
        ("Cs2Prak.Server.dll", "нет веб-сервера"),
        ("Microsoft.Web.WebView2.Core.dll", "окно не откроется"),
        ("Microsoft.Web.WebView2.WinForms.dll", "окно не откроется"),
        ("WebView2Loader.dll", "окно не откроется"),
        ("templates/index.html", "показывать нечего"),
        ("static/script.js", "интерфейс не заработает"),
        ("static/style.css", "интерфейс останется без оформления"),
    ];

    var set = packed.ToHashSet(StringComparer.OrdinalIgnoreCase);
    var missing = required
        .Where(r => !set.Contains(r.Name)
                 && !set.Any(p => p.EndsWith("/" + r.Name, StringComparison.OrdinalIgnoreCase)))
        .ToList();

    if (missing.Count == 0)
    {
        Console.WriteLine($"  проверка полноты: {required.Length} обязательных файлов на месте");
        return true;
    }

    Fail("набор неполон — такое обновление сломает установленный лаунчер:");
    foreach (var (name, why) in missing)
        Console.Error.WriteLine($"    нет {name}  ({why})");
    Console.Error.WriteLine("  Либо сборка неполная, либо правило отбора файлов их больше не ловит.");
    return false;
}

static string Sha256(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexStringLower(SHA256.HashData(stream));
}
