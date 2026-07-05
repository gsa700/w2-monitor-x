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
    void Start(string portName);
    void Stop();

    /// <summary>Queue a single W2 command char (e.g. 'N','Y','O','0','1','L') to send to the meter.</summary>
    void Send(char command);
}
