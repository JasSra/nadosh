using Nadosh.Core.Models;
using Nadosh.Core.Services;

namespace Nadosh.Core.Tests.Edge;

public sealed class AuthorizedTaskScopeEvaluatorTests
{
    [Fact]
    public void ValidateTarget_AllowsTargetInsideAllowedCidr()
    {
        var scope = new AuthorizedTaskScope
        {
            AllowedCidrs = ["192.168.10.0/24"]
        };

        var result = AuthorizedTaskScopeEvaluator.ValidateTarget(AuthorizedTaskKinds.Stage1Scan, scope, "192.168.10.42", [80, 443]);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void ValidateTarget_DeniesTargetOutsideAllowedCidr()
    {
        var scope = new AuthorizedTaskScope
        {
            AllowedCidrs = ["192.168.10.0/24"]
        };

        var result = AuthorizedTaskScopeEvaluator.ValidateTarget(AuthorizedTaskKinds.Stage1Scan, scope, "10.10.10.10", [80]);

        Assert.False(result.IsAllowed);
        Assert.Contains("outside the authorized scope", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateTarget_DeniesPortsOutsideAllowedPorts()
    {
        var scope = new AuthorizedTaskScope
        {
            AllowedTargets = ["192.168.1.15"],
            AllowedPorts = [22, 443]
        };

        var result = AuthorizedTaskScopeEvaluator.ValidateTarget(AuthorizedTaskKinds.Stage1Scan, scope, "192.168.1.15", [22, 8443]);

        Assert.False(result.IsAllowed);
        Assert.Contains("outside the authorized scope", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateTarget_AllowsWhenScopeMatchDisabled()
    {
        var scope = new AuthorizedTaskScope
        {
            RequireScopeMatch = false
        };

        var result = AuthorizedTaskScopeEvaluator.ValidateTarget(AuthorizedTaskKinds.MacEnrichment, scope, "10.0.0.15");

        Assert.True(result.IsAllowed);
    }
}
