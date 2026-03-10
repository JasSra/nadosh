using System;

namespace Nadosh.Core.Models;

public class RuleConfig
{
    public string RuleId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ServiceType { get; set; } = string.Empty;
    public string TriggerConditionsJson { get; set; } = "{}";
    public string RequestDefinitionJson { get; set; } = "{}";
    public string MatcherDefinitionJson { get; set; } = "{}";
    public string SeverityMappingJson { get; set; } = "{}";
    public bool Enabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
