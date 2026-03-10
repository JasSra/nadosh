using System;
using System.Collections.Generic;
using System.Linq;
using Bogus;
using Nadosh.Core.Models;

namespace Nadosh.Core.Seed;

public static class DataSeeder
{
    public static List<Target> GenerateTargets(int count = 1000)
    {
        var faker = new Faker<Target>()
            .RuleFor(t => t.Ip, f => f.Internet.Ip())
            .RuleFor(t => t.CidrSource, f => $"{f.Internet.Ip()}/24")
            .RuleFor(t => t.OwnershipTags, f => f.Make(f.Random.Int(1, 3), () => f.Commerce.Department()))
            .RuleFor(t => t.Monitored, f => f.Random.Bool())
            .RuleFor(t => t.LastScheduled, f => f.Date.Past(1))
            .RuleFor(t => t.NextScheduled, f => f.Date.FutureOffset(15).UtcDateTime);

        return faker.Generate(count);
    }

    public static List<Observation> GenerateObservations(List<Target> targets)
    {
        var services = new[] { (80, "http"), (443, "https"), (22, "ssh"), (3389, "rdp") };
        var states = new[] { "open", "closed", "filtered" };
        var observations = new List<Observation>();
        
        var faker = new Faker();

        foreach (var target in targets)
        {
            var portCount = faker.Random.Int(1, 4);
            var selectedServices = faker.PickRandom(services, portCount).ToList();

            foreach (var s in selectedServices)
            {
                observations.Add(new Observation
                {
                    TargetId = target.Ip,
                    ObservedAt = faker.Date.Recent(30).ToUniversalTime(),
                    Port = s.Item1,
                    Protocol = "tcp",
                    State = faker.PickRandom(states),
                    LatencyMs = faker.Random.Int(10, 500),
                    Fingerprint = faker.Random.Hash(),
                    EvidenceJson = $"{{\"banner\":\"{faker.Lorem.Sentence()}\"}}",
                    ScanRunId = Guid.NewGuid().ToString()
                });
            }
        }
        return observations;
    }

    public static List<CurrentExposure> GenerateCurrentExposures(List<Observation> recentObservations)
    {
        return recentObservations.Select(o => new CurrentExposure
        {
            TargetId = o.TargetId,
            Port = o.Port,
            Protocol = o.Protocol,
            CurrentState = o.State,
            FirstSeen = o.ObservedAt.AddDays(-30),
            LastSeen = o.ObservedAt,
            LastChanged = o.ObservedAt.AddDays(-5),
            Classification = o.Port == 443 ? "https" : "unknown",
            Severity = o.Port == 3389 ? "high" : "low"
        }).ToList();
    }
}
