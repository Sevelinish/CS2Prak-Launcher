namespace Cs2Prak.Core.Update;

public sealed class UpdateState
{
    public string current { get; set; } = AppInfo.Version;
    public string? latest { get; set; }
    public bool available { get; set; }
    public bool staged { get; set; }
    public string status { get; set; } = "dev";
    public string message { get; set; } = "";
    public List<string> files { get; set; } = [];
    public long size { get; set; }
    public bool seen { get; set; }
    public string notes { get; set; } = "";

    public static readonly UpdateState Current = new();
}
