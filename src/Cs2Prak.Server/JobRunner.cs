using Cs2Prak.Core;

namespace Cs2Prak.Server;

public sealed class JobRunner
{
    private readonly object _gate = new();
    private readonly JobLog _log = new();
    private bool _running;
    private int? _exitCode;

    public bool Running { get { lock (_gate) return _running; } }

    public int? ExitCode { get { lock (_gate) return _exitCode; } }

    public string[] Log => _log.Snapshot();

    public bool TryStart(Func<JobLog, int> work, string name)
    {
        lock (_gate)
        {
            if (_running) return false;
            _log.Clear();
            _exitCode = null;
            _running = true;
        }

        new Thread(() =>
        {
            var code = -1;
            try
            {
                code = work(_log);
            }
            catch (Exception e)
            {
                _log.Add($"ERROR: {Describe(e)}");
            }
            finally
            {
                lock (_gate)
                {
                    _exitCode = code;
                    _running = false;
                }
            }
        })
        {
            IsBackground = true,
            Name = name,
        }.Start();

        return true;
    }

    private static string Describe(Exception e) =>
        e is InvalidOperationException ? e.Message : $"{e.GetType().Name}: {e.Message}";
}
