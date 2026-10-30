using Seruni22Bakery.Domain.Entities;

namespace Seruni22Bakery.Domain.Interfaces;

public interface IPrintJobRepository
{
    Task<int> CreateAsync(PrintJob job);
    Task MarkPrintedAsync(int id, DateTime finishedAt);
    Task MarkFailedAsync(int id, string error, DateTime finishedAt);
    Task<IReadOnlyList<PrintJob>> GetRecentAsync(int limit = 50);
    /// <summary>Menandai pekerjaan Pending yang lebih tua dari batas sebagai Failed.</summary>
    Task<int> FailStalePendingAsync(string error, DateTime createdBefore);
}
