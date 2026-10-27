namespace LuxEngine.Output.Drivers;

public enum OutputState
{
    Stopped,
    Running,
    Error,
}

/// <summary>Something that can send one DMX universe: a network protocol or a USB interface.</summary>
public interface IDmxOutput : IDisposable
{
    string Description { get; }

    /// <summary>LuxEngine universe this output sends (1-based).</summary>
    int Universe { get; }

    OutputState State { get; }
    string? LastError { get; }

    void Open();

    /// <summary>Sends 512 channel values. Called from the output thread only.</summary>
    void Send(ReadOnlySpan<byte> data);
}

/// <summary>Shared error handling: an output that fails reports the error and retries every few seconds.</summary>
public abstract class DmxOutputBase : IDmxOutput
{
    private DateTime _retryAt;

    protected DmxOutputBase(int universe) => Universe = universe;

    public abstract string Description { get; }
    public int Universe { get; }
    public OutputState State { get; private set; } = OutputState.Stopped;
    public string? LastError { get; private set; }

    public void Open()
    {
        try
        {
            OpenCore();
            State = OutputState.Running;
            LastError = null;
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    public void Send(ReadOnlySpan<byte> data)
    {
        if (State == OutputState.Error)
        {
            if (DateTime.UtcNow < _retryAt) return;
            CloseCore();
            Open();
            if (State != OutputState.Running) return;
        }
        else if (State == OutputState.Stopped) return;

        try
        {
            SendCore(data);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private void Fail(Exception ex)
    {
        State = OutputState.Error;
        LastError = ex.Message;
        _retryAt = DateTime.UtcNow.AddSeconds(3);
    }

    protected abstract void OpenCore();
    protected abstract void SendCore(ReadOnlySpan<byte> data);
    protected abstract void CloseCore();

    public void Dispose()
    {
        CloseCore();
        State = OutputState.Stopped;
        GC.SuppressFinalize(this);
    }
}
