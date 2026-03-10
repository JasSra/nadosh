namespace Nadosh.Core.Interfaces;

/// <summary>
/// Distributed rate limiter shared across all worker instances.
/// Enforces per-CIDR and global packets-per-second limits to ensure
/// Nadosh is a responsible internet citizen.
/// </summary>
public interface IScanRateLimiter
{
    /// <summary>
    /// Attempts to acquire a permit to send a probe to the given IP.
    /// Returns true if allowed, false if rate limit exceeded (caller should back off).
    /// </summary>
    Task<bool> TryAcquireAsync(string targetIp, CancellationToken ct = default);

    /// <summary>
    /// Returns the current utilisation of the rate limiter bucket for the given /24 CIDR.
    /// </summary>
    Task<double> GetUtilisationAsync(string cidr24, CancellationToken ct = default);
}
