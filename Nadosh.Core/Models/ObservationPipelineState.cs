namespace Nadosh.Core.Models;

public enum ObservationPipelineState
{
    Scheduled = 0,
    Stage1Scanning = 1,
    Stage1Observed = 2,
    Classified = 3,
    FlaggedForEnrichment = 4,
    Stage2Queued = 5,
    Stage2Processing = 6,
    Enriched = 7,
    Suppressed = 8,
    Error = 9,
    Completed = 10
}

public enum ObservationPipelineTransitionStatus
{
    Applied = 0,
    NoOp = 1,
    Rejected = 2,
    NotFound = 3
}

public sealed class ObservationPipelineTransitionResult
{
    public ObservationPipelineTransitionStatus Status { get; init; }
    public ObservationPipelineState? PreviousState { get; init; }
    public ObservationPipelineState? CurrentState { get; init; }
    public string? Reason { get; init; }

    public bool AppliedOrAlreadyAtTarget => Status is ObservationPipelineTransitionStatus.Applied or ObservationPipelineTransitionStatus.NoOp;
}