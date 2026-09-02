using DemoFile.Game.Cs;

namespace Cs2Prak.Core.Demos;

internal sealed class GrenadeTrack
{
    public required string Kind;
    public ulong ThrowerSteamId;
    public string ThrowerName = "";

    public double[]? ThrowPosition;
    public double[]? ThrowAngles;
    public int ThrowSide;

    public readonly List<Sample> Rows = [];

    public readonly record struct Sample(int Tick, float X, float Y, float Z, int Bounces);

    public static string KindOf(string serverClass) => serverClass switch
    {
        "CSmokeGrenadeProjectile" => "smoke",
        "CMolotovProjectile" => "molotov",
        "CIncendiaryGrenade" => "molotov",
        "CFlashbangProjectile" => "flash",
        "CDecoyProjectile" => "decoy",
        _ => "he",
    };

    public void CaptureThrow(CCSPlayerPawn? thrower)
    {
        if (thrower is null) return;

        var origin = thrower.Origin;
        var angles = thrower.EyeAngles;
        if (float.IsNaN(origin.X)) return;

        ThrowPosition =
        [
            Math.Round((double)origin.X, MidpointRounding.ToEven),
            Math.Round((double)origin.Y, MidpointRounding.ToEven),
            Math.Round((double)origin.Z, MidpointRounding.ToEven),
        ];
        ThrowAngles =
        [
            Math.Round(angles.Pitch, 1, MidpointRounding.ToEven),
            Math.Round(angles.Yaw, 1, MidpointRounding.ToEven),
        ];
        ThrowSide = (int)thrower.CSTeamNum == 3 ? 1 : 0;

        var controller = thrower.OriginalController;
        if (controller is not null)
        {
            ThrowerSteamId = controller.SteamID;
            ThrowerName = controller.PlayerName ?? "";
        }
    }
}

internal sealed record Flight(
    string Kind,
    List<double[]> Points,
    List<double[]> Bounces,
    string By,
    string SteamId,
    double[]? ThrowPosition,
    double[]? ThrowAngles,
    int? ThrowSide);
