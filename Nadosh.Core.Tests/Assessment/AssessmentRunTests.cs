using Nadosh.Core.Models;

namespace Nadosh.Core.Tests.Assessment;

public sealed class AssessmentRunTests
{
    [Fact]
    public void NewRun_HasExpectedSafeDefaults()
    {
        var run = new AssessmentRun();

        Assert.NotEmpty(run.RunId);
        Assert.Equal(AssessmentExecutionEnvironment.Lab, run.Environment);
        Assert.Equal(AssessmentRunStatus.PendingPolicy, run.Status);
        Assert.Equal("{}", run.ParametersJson);
        Assert.Equal("{}", run.PolicyDecisionJson);
        Assert.False(run.DryRun);
        Assert.False(run.RequiresApproval);
    }
}
