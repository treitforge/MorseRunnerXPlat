namespace MorseRunner.Client;

public sealed class MorseRunnerTransportException(
    string transportStatus,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string TransportStatus { get; } = transportStatus;
}
