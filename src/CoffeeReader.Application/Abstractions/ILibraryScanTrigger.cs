namespace CoffeeReader.Application.Abstractions;

/// <summary>
/// Queues a background library scan.
/// </summary>
public interface ILibraryScanTrigger
{
    void RequestScan(string reason);
}
