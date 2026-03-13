namespace Nadosh.Core.Models;

/// <summary>
/// Tracks webhook delivery attempts for polling and auditing
/// </summary>
public sealed class WebhookDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Type of webhook event (e.g., "ChangeNotification", "CveAlert")
    /// </summary>
    public required string EventType { get; set; }
    
    /// <summary>
    /// Target webhook URL
    /// </summary>
    public required string Url { get; set; }
    
    /// <summary>
    /// JSON payload sent to webhook
    /// </summary>
    public required string Payload { get; set; }
    
    /// <summary>
    /// HTTP status code from delivery attempt (200 = success)
    /// </summary>
    public int? HttpStatusCode { get; set; }
    
    /// <summary>
    /// Whether delivery succeeded
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Error message if delivery failed
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// When the webhook was sent
    /// </summary>
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
