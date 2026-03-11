using Microsoft.EntityFrameworkCore;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;

namespace Nadosh.Infrastructure.Data;

public class TargetRepository : ITargetRepository
{
    private readonly NadoshDbContext _db;

    public TargetRepository(NadoshDbContext db) => _db = db;

    public Task<Target?> GetByIdAsync(string ip, CancellationToken cancellationToken = default)
        => _db.Targets.FirstOrDefaultAsync(t => t.Ip == ip, cancellationToken);

    public async Task UpsertTargetAsync(Target target, CancellationToken cancellationToken = default)
    {
        var existing = await _db.Targets
            .FirstOrDefaultAsync(t => t.Ip == target.Ip, cancellationToken);

        if (existing is null)
        {
            _db.Targets.Add(target);
        }
        else
        {
            _db.Entry(existing).CurrentValues.SetValues(target);
            // SetValues does not copy collection properties, so OwnershipTags must be assigned explicitly.
            existing.OwnershipTags = target.OwnershipTags;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
