using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nadosh.Infrastructure.Data;
using Nadosh.Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Nadosh.Workers.Workers;

/// <summary>
/// Enriches Target records with ASN, country, ISP metadata.
/// In production, use MaxMind GeoIP2 or similar commercial database.
/// This demo uses simple IP range mapping for common providers.
/// </summary>
public class EnrichmentWorker : BackgroundService
{
    private readonly ILogger<EnrichmentWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public EnrichmentWorker(
        ILogger<EnrichmentWorker> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EnrichmentWorker starting. Will run every {Interval}", _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnrichTargetsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during enrichment cycle");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task EnrichTargetsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NadoshDbContext>();

        // Find targets that need enrichment (Country is null)
        var targets = await db.Targets
            .Where(t => t.Country == null && t.Monitored)
            .Take(500)
            .ToListAsync(ct);

        if (!targets.Any())
        {
            _logger.LogInformation("No targets need enrichment");
            return;
        }

        _logger.LogInformation("Enriching {Count} targets with ASN/geo data", targets.Count);

        int enriched = 0;
        foreach (var target in targets)
        {
            try
            {
                var ipAddress = IPAddress.Parse(target.Ip);
                var geoInfo = GetGeoInfo(ipAddress);

                target.Country = geoInfo.Country;
                target.City = geoInfo.City;
                target.Region = geoInfo.Region;
                target.Latitude = geoInfo.Latitude;
                target.Longitude = geoInfo.Longitude;
                target.AsnNumber = geoInfo.AsnNumber;
                target.AsnOrganization = geoInfo.AsnOrganization;
                target.IspName = geoInfo.IspName;
                target.DataCenter = geoInfo.DataCenter;
                target.EnrichmentCompletedAt = DateTime.UtcNow;

                enriched++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enrich target {Ip}", target.Ip);
            }
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Enriched {Enriched}/{Total} targets successfully", enriched, targets.Count);
    }

    /// <summary>
    /// Demo geo lookup - in production use MaxMind GeoIP2 or ip-api.com
    /// </summary>
    private GeoInfo GetGeoInfo(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();

        // RFC 1918 private ranges
        if (IsPrivateNetwork(ip))
        {
            return new GeoInfo
            {
                Country = "ZZ", // Reserved
                City = "Private Network",
                Region = "Private",
                Latitude = 0,
                Longitude = 0,
                AsnNumber = 0,
                AsnOrganization = "Private Network",
                IspName = "Private",
                DataCenter = null
            };
        }

        // Demo: Common cloud provider ranges
        // AWS us-east-1: 3.0.0.0/8 (demo - not accurate)
        if (bytes[0] == 3)
        {
            return new GeoInfo
            {
                Country = "US",
                City = "Ashburn",
                Region = "Virginia",
                Latitude = 39.0438,
                Longitude = -77.4874,
                AsnNumber = 16509,
                AsnOrganization = "Amazon.com, Inc.",
                IspName = "Amazon Web Services",
                DataCenter = "AWS"
            };
        }

        // Google Cloud: 34.0.0.0/8 (demo)
        if (bytes[0] == 34)
        {
            return new GeoInfo
            {
                Country = "US",
                City = "Council Bluffs",
                Region = "Iowa",
                Latitude = 41.2619,
                Longitude = -95.8608,
                AsnNumber = 15169,
                AsnOrganization = "Google LLC",
                IspName = "Google Cloud Platform",
                DataCenter = "GCP"
            };
        }

        // Azure: 13.0.0.0/8 (demo)
        if (bytes[0] == 13)
        {
            return new GeoInfo
            {
                Country = "US",
                City = "Des Moines",
                Region = "Iowa",
                Latitude = 41.6005,
                Longitude = -93.6091,
                AsnNumber = 8075,
                AsnOrganization = "Microsoft Corporation",
                IspName = "Microsoft Azure",
                DataCenter = "Azure"
            };
        }

        // Default: unknown
        return new GeoInfo
        {
            Country = "XX", // Unknown
            City = "Unknown",
            Region = null,
            Latitude = 0,
            Longitude = 0,
            AsnNumber = 0,
            AsnOrganization = "Unknown",
            IspName = "Unknown ISP",
            DataCenter = null
        };
    }

    private bool IsPrivateNetwork(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();

        // 10.0.0.0/8
        if (bytes[0] == 10)
            return true;

        // 172.16.0.0/12
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            return true;

        // 192.168.0.0/16
        if (bytes[0] == 192 && bytes[1] == 168)
            return true;

        // 127.0.0.0/8 localhost
        if (bytes[0] == 127)
            return true;

        return false;
    }
}

public class GeoInfo
{
    public string Country { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? Region { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int AsnNumber { get; set; }
    public string? AsnOrganization { get; set; }
    public string? IspName { get; set; }
    public string? DataCenter { get; set; }
}
