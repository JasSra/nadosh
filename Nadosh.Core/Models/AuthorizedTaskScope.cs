namespace Nadosh.Core.Models;

public sealed class AuthorizedTaskScope
{
    public bool RequireScopeMatch { get; init; } = true;
    public List<string> AllowedCidrs { get; init; } = new();
    public List<string> AllowedTargets { get; init; } = new();
    public List<int> AllowedPorts { get; init; } = new();
}

public sealed class AuthorizedTaskScopeValidationResult
{
    public bool IsAllowed { get; init; }
    public string Reason { get; init; } = string.Empty;

    public static AuthorizedTaskScopeValidationResult Allow(string reason = "allowed")
        => new() { IsAllowed = true, Reason = reason };

    public static AuthorizedTaskScopeValidationResult Deny(string reason)
        => new() { IsAllowed = false, Reason = reason };
}
