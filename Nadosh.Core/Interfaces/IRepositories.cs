using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nadosh.Core.Models;

namespace Nadosh.Core.Interfaces;

public interface ITargetRepository
{
    Task<Target?> GetByIdAsync(string ip, CancellationToken cancellationToken = default);
    Task UpsertTargetAsync(Target target, CancellationToken cancellationToken = default);
}

public interface IObservationRepository
{
    Task InsertBatchAsync(IEnumerable<Observation> observations, CancellationToken cancellationToken = default);
}

public interface ICurrentExposureRepository
{
    Task UpsertExposureAsync(CurrentExposure exposure, CancellationToken cancellationToken = default);
}
