using System.Text.Json.Nodes;

namespace Cs2Prak.Core.Highlights;

public sealed record HighlightClip(string name, string path, double seconds);

public sealed record HighlightJobView(
    string? jobId,
    string state,
    int stage,
    int total,
    string title,
    string detail,
    int queuePosition,
    int clipCount,
    double totalSeconds,
    IReadOnlyList<HighlightClip> clips,
    string? outputDirectory,
    string? error);

public static class HighlightJobs
{
    private const int Stages = 8;

    private static readonly Lock Gate = new();

    private static string? _jobId;
    private static string _state = "idle";
    private static int _stage;
    private static string _title = "";
    private static string _detail = "";
    private static int _queuePosition;
    private static int _clipCount;
    private static double _totalSeconds;
    private static List<HighlightClip> _clips = [];
    private static string? _outputDirectory;
    private static string? _error;
    private static Thread? _follower;

    public static bool Busy
    {
        get { lock (Gate) return _state is "queued" or "running"; }
    }

    public static HighlightJobView View()
    {
        lock (Gate)
            return new HighlightJobView(_jobId, _state, _stage, Stages, _title, _detail,
                                        _queuePosition, _clipCount, _totalSeconds,
                                        _clips, _outputDirectory, _error);
    }

    public static JsonNode Preview(JsonObject request) =>
        HighlighterClient.Call("plan.preview", request, TimeSpan.FromMinutes(4));

    public static HighlightJobView Submit(JsonObject request)
    {
        lock (Gate)
        {
            if (_state is "queued" or "running")
                throw new InvalidOperationException("A recording is already running.");
        }

        var job = HighlighterClient.Call("jobs.submit", request, TimeSpan.FromMinutes(4));
        var jobId = job["jobId"]?.GetValue<string>()
                    ?? throw new InvalidOperationException("HighlighterCS2 returned no job id.");

        lock (Gate)
        {
            _jobId = jobId;
            _state = job["state"]?.GetValue<string>() ?? "queued";
            _stage = 0;
            _title = "";
            _detail = "";
            _queuePosition = job["queuePosition"]?.GetValue<int>() ?? 0;
            _clipCount = 0;
            _totalSeconds = 0;
            _clips = [];
            _outputDirectory = null;
            _error = null;

            _follower = new Thread(() => Follow(jobId))
            {
                IsBackground = true,
                Name = "highlighter-job",
            };
            _follower.Start();
        }

        return View();
    }

    public static HighlightJobView Cancel()
    {
        string? jobId;
        lock (Gate) jobId = _jobId;

        if (jobId is not null)
        {
            try
            {
                HighlighterClient.Call("jobs.cancel", new JsonObject { ["jobId"] = jobId },
                                       TimeSpan.FromSeconds(30));
            }
            catch (Exception) { }
        }

        return View();
    }

    private static void Follow(string jobId)
    {
        long since = 0;

        while (true)
        {
            JsonNode page;
            try
            {
                page = HighlighterClient.Call("jobs.events", new JsonObject
                {
                    ["jobId"] = jobId,
                    ["since"] = since,
                    ["waitSeconds"] = 30,
                }, TimeSpan.FromSeconds(90));
            }
            catch (Exception e)
            {
                if (Settle(jobId, e.Message)) return;
                Thread.Sleep(2000);
                continue;
            }

            foreach (var node in page["events"] as JsonArray ?? [])
            {
                if (node is JsonObject item && Apply(item)) return;
            }

            since = page["nextSequence"]?.GetValue<long>() ?? since;
        }
    }

    private static bool Apply(JsonObject item)
    {
        var type = item["type"]?.GetValue<string>() ?? "";
        var data = item["data"] as JsonObject ?? [];

        lock (Gate)
        {
            switch (type)
            {
                case "job.queued":
                    _state = "queued";
                    _queuePosition = data["queuePosition"]?.GetValue<int>() ?? 0;
                    break;

                case "job.started":
                    _state = "running";
                    _queuePosition = 0;
                    break;

                case "stage.begin":
                    _state = "running";
                    _stage = data["stage"]?.GetValue<int>() ?? _stage;
                    _title = data["title"]?.GetValue<string>() ?? _title;
                    _detail = "";
                    break;

                case "stage.detail":
                    _detail = data["message"]?.GetValue<string>() ?? "";
                    break;

                case "stage.end":
                    _stage = data["stage"]?.GetValue<int>() ?? _stage;
                    _detail = data["note"]?.GetValue<string>() ?? "";
                    break;

                case "stage.failed":
                    _detail = data["note"]?.GetValue<string>() ?? "";
                    break;

                case "plan.ready":
                    if (data["plan"]?["summary"] is JsonObject summary)
                    {
                        _clipCount = summary["clipCount"]?.GetValue<int>() ?? 0;
                        _totalSeconds = summary["totalSeconds"]?.GetValue<double>() ?? 0;
                    }
                    break;

                case "clip.written":
                    _clips = [.. _clips, new HighlightClip(
                        data["name"]?.GetValue<string>() ?? "clip",
                        data["path"]?.GetValue<string>() ?? "",
                        data["durationSeconds"]?.GetValue<double>() ?? 0)];
                    break;

                case "job.succeeded":
                    _state = "succeeded";
                    _stage = Stages;
                    _outputDirectory = data["result"]?["outputDirectory"]?.GetValue<string>();
                    return true;

                case "job.failed":
                    _state = "failed";
                    _error = data["error"]?["message"]?.GetValue<string>() ?? "Recording failed.";
                    return true;

                case "job.cancelled":
                    _state = "cancelled";
                    return true;
            }
        }

        return false;
    }

    private static bool Settle(string jobId, string reason)
    {
        JsonNode? job;
        try
        {
            job = HighlighterClient.Call("jobs.get", new JsonObject { ["jobId"] = jobId },
                                         TimeSpan.FromSeconds(30));
        }
        catch (Exception)
        {
            lock (Gate)
            {
                if (_state is not ("queued" or "running")) return true;
                _error = reason;
                _state = "failed";
            }
            return true;
        }

        var state = job["state"]?.GetValue<string>() ?? "";
        if (state is not ("succeeded" or "failed" or "cancelled")) return false;

        lock (Gate)
        {
            _state = state;
            _error = job["error"]?["message"]?.GetValue<string>();
            _outputDirectory = job["result"]?["outputDirectory"]?.GetValue<string>();
        }
        return true;
    }
}
