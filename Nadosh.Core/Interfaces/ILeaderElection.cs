namespace Nadosh.Core.Interfaces;

/// <summary>
/// Provides distributed leader election so only one scheduler instance
/// runs at a time across a multi-machine worker cluster.
/// </summary>
public interface ILeaderElection
{
    /// <summary>
    /// Attempts to acquire the leader lock for the given resource.
    /// Returns true if this instance is now the leader.
    /// </summary>
    Task<bool> TryAcquireLeadershipAsync(string resource, string instanceId, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// Renews the leader lock. Returns false if leadership was lost.
    /// </summary>
    Task<bool> RenewLeadershipAsync(string resource, string instanceId, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// Releases the leader lock.
    /// </summary>
    Task ReleaseLeadershipAsync(string resource, string instanceId, CancellationToken ct = default);
}
