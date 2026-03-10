using System;

namespace Nadosh.Core.Models;

public class SuppressionRule
{
    public long Id { get; set; }
    public string? TargetIp { get; set; }
    public string? Cidr { get; set; }
    public int? Port { get; set; }
    public string? ServiceType { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Creator { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiryDate { get; set; }
}
