using System.Text.Json.Nodes;

namespace Cs2Prak.Core.Demos;

public sealed record RadarCalibration(double PosX, double PosY, double Scale, double LowerLevelMax)
{
    public bool HasLower => LowerLevelMax > -1e5;

    public (double X, double Y) ToPixels(double x, double y) =>
        (Math.Round((x - PosX) / Scale, 1), Math.Round((PosY - y) / Scale, 1));

    private static Dictionary<string, RadarCalibration>? _maps;

    public static IReadOnlyDictionary<string, RadarCalibration> All => _maps ??= Load();

    public static RadarCalibration? For(string map) =>
        All.TryGetValue(map, out var cal) ? cal : null;

    public static string SupportedMaps => string.Join(", ", All.Keys.Order(StringComparer.Ordinal));

    private static Dictionary<string, RadarCalibration> Load()
    {
        var maps = new Dictionary<string, RadarCalibration>(StringComparer.Ordinal);
        try
        {
            var path = Path.Combine(AppPaths.StaticDir, "radars", "calibration.json");
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root) return maps;

            foreach (var (name, node) in root)
            {
                if (node is not JsonObject cal) continue;
                maps[name] = new RadarCalibration(
                    PosX: cal["pos_x"]?.GetValue<double>() ?? 0,
                    PosY: cal["pos_y"]?.GetValue<double>() ?? 0,
                    Scale: cal["scale"]?.GetValue<double>() ?? 1,
                    LowerLevelMax: cal["lower_level_max_units"]?.GetValue<double>() ?? -1e9);
            }
        }
        catch (Exception)
        {
        }
        return maps;
    }
}
