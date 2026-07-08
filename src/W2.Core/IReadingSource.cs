namespace W2.Core;

/// <summary>
/// A source of <see cref="W2Reading"/>s on a background thread. Implemented by the real
/// <see cref="SerialReader"/> and by <see cref="W2SimReader"/>, so the app layer can drive
/// the exact same pipeline from a live meter or from synthetic data. Events fire on a
/// background thread — subscribers must marshal to their UI thread.
/// </summary>
public interface IReadingSource : IDisposable
{
    event Action<W2Reading>? ReadingReceived;
    event Action<string, bool>? StatusChanged;   // (message, isError)

    bool IsRunning { get; }

    /// <summary>
    /// Start reading. <paramref name="resolvePort"/>, if given, is re-queried on every (re)connect
    /// to follow a USB replug/renumber to the cable's current /dev/tty*; null keeps the fixed port.
    /// </summary>
    void Start(string portName, Func<string?>? resolvePort = null);
    void Stop();

    /// <summary>Queue a single W2 command char (e.g. 'N','Y','O','0','1','L') to send to the meter.</summary>
    void Send(char command);
}
