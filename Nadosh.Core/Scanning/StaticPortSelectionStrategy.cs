using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;

namespace Nadosh.Core.Scanning;

/// <summary>
/// Default port selection strategy. Returns the top 100 ports for cold/standard targets,
/// and the extended list for warm/hot targets that already have known open ports.
/// </summary>
public class StaticPortSelectionStrategy : IPortSelectionStrategy
{
    public string Name => "Static";
    public int Priority => 0;

    public Task<List<int>> SelectPortsAsync(Target target, CancellationToken ct = default)
    {
        var ports = target.Cadence switch
        {
            ScanCadence.Cold => PortProfiles.QuickSweep.ToList(),
            ScanCadence.Standard => PortProfiles.Top100.Distinct().ToList(),
            ScanCadence.Warm or ScanCadence.Hot or ScanCadence.Critical
                => PortProfiles.Extended.Distinct().ToList(),
            _ => PortProfiles.Top100.Distinct().ToList()
        };

        return Task.FromResult(ports);
    }
}
