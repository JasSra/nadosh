using Nadosh.Core.Models;

namespace Nadosh.Core.Interfaces;

/// <summary>
/// Determines which ports to scan for a given target.
/// Implementations range from a static top-100 list to AI-assisted prediction.
/// </summary>
public interface IPortSelectionStrategy
{
    string Name { get; }
    int Priority { get; }
    Task<List<int>> SelectPortsAsync(Target target, CancellationToken ct = default);
}
