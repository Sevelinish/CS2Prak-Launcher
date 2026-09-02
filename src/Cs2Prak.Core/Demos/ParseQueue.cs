using System.Text.Json.Nodes;

namespace Cs2Prak.Core.Demos;

public sealed record QueuedDemo(int id, string name, string status, string error, string key);

public static class ParseQueue
{
    private sealed class Item
    {
        public int Id;
        public required string Name;
        public required string Path;
        public string Status = "queued";
        public string Error = "";
        public string Key = "";
    }

    private static readonly object Gate = new();
    private static readonly List<Item> Items = [];
    private static readonly AutoResetEvent Pending = new(false);
    private static int _sequence;
    private static bool _started;

    public static string StageDir => Path.Combine(Path.GetTempPath(), "cs2prak_stage");

    public static void Start()
    {
        lock (Gate)
        {
            if (_started) return;
            _started = true;
        }
        new Thread(Worker) { IsBackground = true, Name = "demo-parse-queue" }.Start();
    }

    public static int Enqueue(string name, string path)
    {
        int id;
        lock (Gate)
        {
            id = ++_sequence;
            Items.Add(new Item { Id = id, Name = name, Path = path });
        }
        Pending.Set();
        return id;
    }

    public static List<QueuedDemo> Snapshot()
    {
        lock (Gate)
            return Items.Select(i => new QueuedDemo(i.Id, i.Name, i.Status, i.Error, i.Key)).ToList();
    }

    public static void ClearFinished()
    {
        lock (Gate)
            Items.RemoveAll(i => i.Status is not ("queued" or "parsing"));
    }

    public static string NextStageName()
    {
        lock (Gate)
            return $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{_sequence}";
    }

    private static void Worker()
    {
        while (true)
        {
            Item? item;
            lock (Gate)
            {
                item = Items.FirstOrDefault(i => i.Status == "queued");
                if (item is not null) item.Status = "parsing";
            }

            if (item is null)
            {
                Pending.WaitOne();
                continue;
            }

            string? raw = null;
            try
            {
                raw = DemoArchive.ToRawDemo(item.Path);
                var parsed = DemoParsing.Run(raw);

                var meta = parsed.Meta;
                DemoLibrary.Add(
                    parsed.Key,
                    item.Name,
                    Text(meta["map"]),
                    Number(meta["sa"]),
                    Number(meta["sb"]),
                    Text(meta["winner"]));

                lock (Gate)
                {
                    item.Status = "done";
                    item.Key = parsed.Key;
                }
            }
            catch (Exception e)
            {
                lock (Gate)
                {
                    item.Status = "error";
                    item.Error = e.Message.Length > 200 ? e.Message[..200] : e.Message;
                }
            }
            finally
            {
                Delete(item.Path);
                if (raw is not null && raw != item.Path) Delete(raw);
            }
        }
    }

    private static int Number(JsonNode? node)
    {
        if (node is not JsonValue value) return 0;
        if (value.TryGetValue(out int i)) return i;
        if (value.TryGetValue(out long l)) return (int)l;
        if (value.TryGetValue(out double d)) return (int)d;
        return 0;
    }

    private static string Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? s) ? s ?? "" : "";

    private static void Delete(string path)
    {
        try { File.Delete(path); } catch (Exception) { }
    }
}
