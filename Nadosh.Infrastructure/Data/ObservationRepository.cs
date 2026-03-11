using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;

namespace Nadosh.Infrastructure.Data;

public class ObservationRepository : IObservationRepository
{
    private readonly NadoshDbContext _db;

    public ObservationRepository(NadoshDbContext db) => _db = db;

    public async Task InsertBatchAsync(IEnumerable<Observation> observations, CancellationToken cancellationToken = default)
    {
        _db.Observations.AddRange(observations);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
