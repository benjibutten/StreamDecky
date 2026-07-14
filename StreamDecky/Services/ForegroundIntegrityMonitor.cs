using StreamDecky.Helpers;

namespace StreamDecky.Services;

/// <summary>
/// Detects when a window owned by a higher-integrity (e.g. administrator) process keeps the
/// foreground while StreamDecky itself is not elevated. In that situation Windows suppresses a
/// non-elevated process's global hotkey while the elevated window is in front, so the overlay can
/// appear unresponsive inside the game even though the hotkey registered successfully.
/// <para>
/// To avoid false alarms for transient elevated tools (Task Manager, an elevated editor, an
/// installer prompt), the elevated window must stay in the foreground for several consecutive
/// checks before <see cref="CheckForeground"/> raises <see cref="HigherIntegrityForegroundDetected"/>.
/// The event fires at most once per offending process so the caller can surface a one-time notice.
/// The integrity providers are injectable for testing.
/// </para>
/// </summary>
public sealed class ForegroundIntegrityMonitor
{
    private const int DefaultConsecutiveChecksRequired = 3;

    private readonly Func<uint> _getForegroundProcessId;
    private readonly Func<uint, int?> _getProcessIntegrityLevel;
    private readonly int? _ownIntegrityLevel;
    private readonly int _consecutiveChecksRequired;
    private readonly HashSet<uint> _warnedProcessIds = new();

    private uint _candidateProcessId;
    private int _candidateStreak;

    public event Action? HigherIntegrityForegroundDetected;

    public ForegroundIntegrityMonitor(
        Func<uint>? getForegroundProcessId = null,
        Func<uint, int?>? getProcessIntegrityLevel = null,
        Func<int?>? getOwnIntegrityLevel = null,
        int consecutiveChecksRequired = DefaultConsecutiveChecksRequired)
    {
        _getForegroundProcessId = getForegroundProcessId ?? ProcessIntegrity.GetForegroundProcessId;
        _getProcessIntegrityLevel = getProcessIntegrityLevel ?? ProcessIntegrity.GetProcessIntegrityLevel;
        _ownIntegrityLevel = (getOwnIntegrityLevel ?? ProcessIntegrity.GetCurrentProcessIntegrityLevel)();
        _consecutiveChecksRequired = Math.Max(1, consecutiveChecksRequired);
    }

    /// <summary>
    /// True when our own integrity level is known. When false there is nothing actionable to
    /// detect, so the caller can skip polling entirely.
    /// </summary>
    public bool IsActive => _ownIntegrityLevel.HasValue;

    /// <summary>
    /// Inspects the current foreground window once. Returns true when it just warned about a
    /// sustained higher-integrity foreground process (which also raises the event).
    /// </summary>
    public bool CheckForeground()
    {
        if (_ownIntegrityLevel is not int ownLevel)
            return false;

        uint pid = _getForegroundProcessId();
        if (pid == 0 || pid == (uint)Environment.ProcessId || _warnedProcessIds.Contains(pid))
        {
            ResetCandidate();
            return false;
        }

        if (_getProcessIntegrityLevel(pid) is not int foregroundLevel || foregroundLevel <= ownLevel)
        {
            ResetCandidate();
            return false;
        }

        // Only warn once the same elevated window has held the foreground for several checks in a
        // row; brief elevated popups never accumulate enough to trigger.
        if (pid == _candidateProcessId)
            _candidateStreak++;
        else
        {
            _candidateProcessId = pid;
            _candidateStreak = 1;
        }

        if (_candidateStreak < _consecutiveChecksRequired)
            return false;

        _warnedProcessIds.Add(pid);
        ResetCandidate();
        AppDiagnostics.Warning(
            $"Foreground process {pid} has stayed in front at integrity level 0x{foregroundLevel:X}, " +
            $"higher than StreamDecky (0x{ownLevel:X}). The global hotkey may not reach it unless " +
            "StreamDecky is run as administrator.");
        HigherIntegrityForegroundDetected?.Invoke();
        return true;
    }

    private void ResetCandidate()
    {
        _candidateProcessId = 0;
        _candidateStreak = 0;
    }
}
