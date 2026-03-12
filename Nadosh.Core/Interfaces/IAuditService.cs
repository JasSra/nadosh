namespace Nadosh.Core.Interfaces;

public interface IAuditService
{
    Task WriteAsync(
        string actor,
        string action,
        string entityType,
        string entityId,
        object? oldValue = null,
        object? newValue = null,
        object? metadata = null,
        CancellationToken cancellationToken = default);
}
