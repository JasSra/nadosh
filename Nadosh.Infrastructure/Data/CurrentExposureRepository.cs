using Microsoft.EntityFrameworkCore;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;

namespace Nadosh.Infrastructure.Data;

public class CurrentExposureRepository : ICurrentExposureRepository
{
    private readonly NadoshDbContext _db;

    public CurrentExposureRepository(NadoshDbContext db) => _db = db;

    public async Task UpsertExposureAsync(CurrentExposure exposure, CancellationToken cancellationToken = default)
    {
        var existing = await _db.CurrentExposures
            .FirstOrDefaultAsync(
                e => e.TargetId == exposure.TargetId && e.Port == exposure.Port && e.Protocol == exposure.Protocol,
                cancellationToken);

        if (existing is null)
        {
            _db.CurrentExposures.Add(exposure);
        }
        else
        {
            existing.CurrentState = exposure.CurrentState;
            existing.LastSeen = exposure.LastSeen;
            existing.LastChanged = exposure.LastChanged;
            existing.Classification = exposure.Classification;
            existing.Severity = exposure.Severity;
            existing.CachedSummary = exposure.CachedSummary;
            existing.MacAddress = exposure.MacAddress;
            existing.MacVendor = exposure.MacVendor;
            existing.DeviceType = exposure.DeviceType;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
