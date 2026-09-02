namespace Cs2Prak.Core.Demos;

public static class RoundEndReasons
{
    public static string Name(int reason) => reason switch
    {
        0 => "still_in_progress",
        1 => "bomb_exploded",
        2 => "vip_escaped",
        3 => "vip_killed",
        4 => "t_escape",
        5 => "ct_stop_escape",
        6 => "t_stopped",
        7 => "bomb_defused",
        8 => "ct_killed",
        9 => "t_killed",
        10 => "draw",
        11 => "hostages_rescued",
        12 => "time_ran_out",
        13 => "hostages_not_rescued",
        14 => "t_not_escape",
        15 => "vip_not_escaped",
        16 => "game_start",
        17 => "t_surrender",
        18 => "ct_surrender",
        19 => "t_planted",
        20 => "cts_reached_hostage",
        _ => $"reason_{reason}",
    };

    public const int TeamTerrorist = 2;
    public const int TeamCounterTerrorist = 3;

    public static string Side(int teamNumber) =>
        teamNumber == TeamCounterTerrorist ? "CT" : "T";
}
