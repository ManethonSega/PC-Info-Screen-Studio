namespace PCInfoScreenStudio.Services;

/// <summary>One display runtime per account; opening the app again shows the existing editor.</summary>
public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    public bool IsPrimary { get; }
    public SingleInstanceService(bool waitForRestart = false, bool activateExisting = true, string? instanceName = null)
    {
        var name = instanceName ?? "Local\\PCInfoScreenStudio-" + WindowsStartupService.CurrentUserSid;
        _mutex = new Mutex(false, name);
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-show");
        try { IsPrimary = _mutex.WaitOne(waitForRestart ? TimeSpan.FromSeconds(30) : TimeSpan.Zero); }
        catch (AbandonedMutexException) { IsPrimary = true; }
        if (!IsPrimary && activateExisting) _activate.Set();
    }
    public bool ConsumeActivation() => _activate.WaitOne(0);
    public void Dispose()
    {
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
        _activate.Dispose();
    }
}
