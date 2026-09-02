namespace Cs2Prak.Core;

public sealed class JobLog
{
    private readonly object _gate = new();
    private readonly List<string> _lines = [];

    public void Add(string line)
    {
        lock (_gate) _lines.Add(line);
    }

    public string[] Snapshot()
    {
        lock (_gate) return [.. _lines];
    }

    public void Clear()
    {
        lock (_gate) _lines.Clear();
    }
}
