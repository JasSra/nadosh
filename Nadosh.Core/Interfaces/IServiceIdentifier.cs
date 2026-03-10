namespace Nadosh.Core.Interfaces;

/// <summary>
/// Provides a service-name lookup for well-known ports.
/// Used by the classifier to identify services without a banner.
/// </summary>
public interface IServiceIdentifier
{
    string? IdentifyByPort(int port);
    string ClassifySeverity(int port, string state, string? serviceName);
}
