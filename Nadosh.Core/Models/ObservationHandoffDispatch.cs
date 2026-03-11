namespace Nadosh.Core.Models;

public class ObservationHandoffDispatch
{
    public ObservationHandoffDispatchKind DispatchKind { get; set; }
    public long SourceObservationId { get; set; }
    public ObservationHandoffDispatchState State { get; set; } = ObservationHandoffDispatchState.Queued;
    public string BatchId { get; set; } = string.Empty;
    public string TargetIp { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Protocol { get; set; } = "tcp";
    public string? ServiceName { get; set; }
    public DateTime ScheduledAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? WorkerId { get; set; }
    public int DeliveryCount { get; set; }
    public long? ProducedObservationId { get; set; }
    public string? LastError { get; set; }
}

public enum ObservationHandoffDispatchKind
{
    BannerGrab = 0,
    Fingerprint = 1,
    Classification = 2,
    Stage2Enrichment = 3
}

public enum ObservationHandoffDispatchState
{
    Queued = 0,
    Processing = 1,
    Completed = 2,
    Error = 3
}

public enum ObservationHandoffDispatchTransitionStatus
{
    Applied = 0,
    NoOp = 1,
    Rejected = 2,
    NotFound = 3
}

public sealed class ObservationHandoffDispatchTransitionResult
{
    public ObservationHandoffDispatchTransitionStatus Status { get; init; }
    public ObservationHandoffDispatchState? PreviousState { get; init; }
    public ObservationHandoffDispatchState? CurrentState { get; init; }
    public long? ProducedObservationId { get; init; }
    public string? Reason { get; init; }

    public bool AppliedOrAlreadySatisfied => Status is ObservationHandoffDispatchTransitionStatus.Applied or ObservationHandoffDispatchTransitionStatus.NoOp;
}