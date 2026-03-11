using Microsoft.EntityFrameworkCore;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;

namespace Nadosh.Infrastructure.Data;

public class RuleConfigRepository : IRuleConfigRepository
{
    private readonly NadoshDbContext _db;

    public RuleConfigRepository(NadoshDbContext db) => _db = db;

    public Task<RuleConfig?> GetActiveRuleAsync(string ruleId, CancellationToken cancellationToken = default)
        => _db.RuleConfigs
            .Where(r => r.RuleId == ruleId && r.Enabled)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<RuleConfig>> GetActiveRulesAsync(
        IEnumerable<string> ruleIds,
        CancellationToken cancellationToken = default)
    {
        var ids = ruleIds.ToList();
        var all = await _db.RuleConfigs
            .Where(r => ids.Contains(r.RuleId) && r.Enabled)
            .ToListAsync(cancellationToken);

        // Return only the latest version per ruleId
        return all
            .GroupBy(r => r.RuleId)
            .Select(g => g.OrderByDescending(r => r.Version).First())
            .ToList();
    }

    public async Task<bool> UpsertRuleAsync(RuleConfig ruleConfig, CancellationToken cancellationToken = default)
    {
        var existing = await _db.RuleConfigs
            .FirstOrDefaultAsync(
                r => r.RuleId == ruleConfig.RuleId && r.Version == ruleConfig.Version,
                cancellationToken);

        if (existing is null)
        {
            _db.RuleConfigs.Add(ruleConfig);
        }
        else
        {
            _db.Entry(existing).CurrentValues.SetValues(ruleConfig);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
