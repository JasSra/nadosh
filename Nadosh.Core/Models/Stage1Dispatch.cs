using System.Text.Json;

namespace Nadosh.Core.Models;

public class Stage1Dispatch
{
    public string BatchId { get; set; } = string.Empty;
    public string TargetIp { get; set; } = string.Empty;
    public Stage1DispatchState State { get; set; } = Stage1DispatchState.Scheduled;
    public DateTime ScheduledAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? WorkerId { get; set; }
    public int DeliveryCount { get; set; }
    public string PortsJson { get; set; } = "[]";
    public DateTime? ObservationLinkedAt { get; set; }
    public int ObservationCount { get; set; }
    public int OpenObservationCount { get; set; }
    public string? LastError { get; set; }

    public IReadOnlyList<int> GetPorts()
    {
        try
        {
            return JsonSerializer.Deserialize<List<int>>(PortsJson) ?? [];
        }
        catch
        {
            return [];
        }
    }
}

public enum Stage1DispatchState
{
    Scheduled = 0,
    Stage1Scanning = 1,
    Stage1Observed = 2,
    Error = 3
}

public enum Stage1DispatchTransitionStatus
{
    Applied = 0,
    NoOp = 1,
    Rejected = 2,
    NotFound = 3
}

public sealed class Stage1DispatchTransitionResult
{
    public Stage1DispatchTransitionStatus Status { get; init; }
    public Stage1DispatchState? PreviousState { get; init; }
    public Stage1DispatchState? CurrentState { get; init; }
    public string? Reason { get; init; }

    public bool AppliedOrAlreadySatisfied => Status is Stage1DispatchTransitionStatus.Applied or Stage1DispatchTransitionStatus.NoOp;
}
