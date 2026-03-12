using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Nadosh.Core.Services;

/// <summary>
/// CVE enrichment service that queries vulnerability databases to identify known vulnerabilities
/// in detected services and versions. Supports NVD (National Vulnerability Database) API.
/// </summary>
public class CveEnrichmentService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CveEnrichmentService> _logger;
    private const string NvdApiBase = "https://services.nvd.nist.gov/rest/json/cves/2.0";

    public CveEnrichmentService(HttpClient httpClient, ILogger<CveEnrichmentService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        // NVD API requires a User-Agent header
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Nadosh/1.0");
    }

    /// <summary>
    /// Search for CVEs related to a specific product/vendor/version combination
    /// </summary>
    public async Task<List<CveResult>> SearchCvesAsync(string product, string? vendor = null, string? version = null, CancellationToken ct = default)
    {
        try
        {
            // Build CPE (Common Platform Enumeration) match string
            var keywords = new List<string>();
            if (!string.IsNullOrEmpty(vendor)) keywords.Add(vendor);
            if (!string.IsNullOrEmpty(product)) keywords.Add(product);
            if (!string.IsNullOrEmpty(version)) keywords.Add(version);

            var keywordSearch = string.Join(" ", keywords);
            
            // Query NVD API with keyword search
            var url = $"{NvdApiBase}?keywordSearch={Uri.EscapeDataString(keywordSearch)}&resultsPerPage=10";
            
            _logger.LogDebug("Searching NVD for: {Keywords}", keywordSearch);
            
            var response = await _httpClient.GetAsync(url, ct);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("NVD API returned {StatusCode} for search: {Keywords}", response.StatusCode, keywordSearch);
                return [];
            }

            var nvdResponse = await response.Content.ReadFromJsonAsync<NvdCveResponse>(cancellationToken: ct);
            
            if (nvdResponse?.Vulnerabilities == null || nvdResponse.Vulnerabilities.Count == 0)
            {
                _logger.LogDebug("No CVEs found for: {Keywords}", keywordSearch);
                return [];
            }

            var results = new List<CveResult>();
            
            foreach (var vuln in nvdResponse.Vulnerabilities)
            {
                var cve = vuln.Cve;
                if (cve == null) continue;

                // Extract CVSS metrics
                var cvssScore = 0.0;
                var cvssVector = string.Empty;
                var severity = "unknown";

                if (cve.Metrics?.CvssMetricV31?.Count > 0)
                {
                    var metric = cve.Metrics.CvssMetricV31[0];
                    cvssScore = metric.CvssData?.BaseScore ?? 0.0;
                    cvssVector = metric.CvssData?.VectorString ?? string.Empty;
                    severity = metric.CvssData?.BaseSeverity?.ToLowerInvariant() ?? "unknown";
                }
                else if (cve.Metrics?.CvssMetricV2?.Count > 0)
                {
                    var metric = cve.Metrics.CvssMetricV2[0];
                    cvssScore = metric.CvssData?.BaseScore ?? 0.0;
                    cvssVector = metric.CvssData?.VectorString ?? string.Empty;
                    severity = DeriveV2Severity(cvssScore);
                }

                // Extract description
                var description = cve.Descriptions?.FirstOrDefault(d => d.Lang == "en")?.Value ?? "No description available";

                results.Add(new CveResult
                {
                    CveId = cve.Id,
                    Description = description,
                    CvssScore = cvssScore,
                    CvssVector = cvssVector,
                    Severity = severity,
                    PublishedDate = cve.Published,
                    LastModified = cve.LastModified,
                    References = cve.References?.Select(r => r.Url).ToList() ?? []
                });
            }

            _logger.LogInformation("Found {Count} CVEs for {Keywords}", results.Count, keywordSearch);
            
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search CVEs for product: {Product}, vendor: {Vendor}, version: {Version}", 
                product, vendor, version);
            return [];
        }
    }

    /// <summary>
    /// Get detailed information for a specific CVE by ID
    /// </summary>
    public async Task<CveResult?> GetCveByIdAsync(string cveId, CancellationToken ct = default)
    {
        try
        {
            var url = $"{NvdApiBase}?cveId={Uri.EscapeDataString(cveId)}";
            
            var response = await _httpClient.GetAsync(url, ct);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("NVD API returned {StatusCode} for CVE: {CveId}", response.StatusCode, cveId);
                return null;
            }

            var nvdResponse = await response.Content.ReadFromJsonAsync<NvdCveResponse>(cancellationToken: ct);
            
            if (nvdResponse?.Vulnerabilities == null || nvdResponse.Vulnerabilities.Count == 0)
                return null;

            var cve = nvdResponse.Vulnerabilities[0].Cve;
            if (cve == null) return null;

            var cvssScore = 0.0;
            var cvssVector = string.Empty;
            var severity = "unknown";

            if (cve.Metrics?.CvssMetricV31?.Count > 0)
            {
                var metric = cve.Metrics.CvssMetricV31[0];
                cvssScore = metric.CvssData?.BaseScore ?? 0.0;
                cvssVector = metric.CvssData?.VectorString ?? string.Empty;
                severity = metric.CvssData?.BaseSeverity?.ToLowerInvariant() ?? "unknown";
            }
            else if (cve.Metrics?.CvssMetricV2?.Count > 0)
            {
                var metric = cve.Metrics.CvssMetricV2[0];
                cvssScore = metric.CvssData?.BaseScore ?? 0.0;
                cvssVector = metric.CvssData?.VectorString ?? string.Empty;
                severity = DeriveV2Severity(cvssScore);
            }

            var description = cve.Descriptions?.FirstOrDefault(d => d.Lang == "en")?.Value ?? "No description available";

            return new CveResult
            {
                CveId = cve.Id,
                Description = description,
                CvssScore = cvssScore,
                CvssVector = cvssVector,
                Severity = severity,
                PublishedDate = cve.Published,
                LastModified = cve.LastModified,
                References = cve.References?.Select(r => r.Url).ToList() ?? []
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch CVE: {CveId}", cveId);
            return null;
        }
    }

    private static string DeriveV2Severity(double score)
    {
        return score switch
        {
            >= 7.0 => "high",
            >= 4.0 => "medium",
            > 0.0 => "low",
            _ => "unknown"
        };
    }
}

/// <summary>
/// CVE enrichment result containing vulnerability details
/// </summary>
public class CveResult
{
    public string CveId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double CvssScore { get; set; }
    public string CvssVector { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public DateTime PublishedDate { get; set; }
    public DateTime LastModified { get; set; }
    public List<string> References { get; set; } = [];
}

#region NVD API Response Models

public class NvdCveResponse
{
    [JsonPropertyName("resultsPerPage")]
    public int ResultsPerPage { get; set; }
    
    [JsonPropertyName("startIndex")]
    public int StartIndex { get; set; }
    
    [JsonPropertyName("totalResults")]
    public int TotalResults { get; set; }
    
    [JsonPropertyName("vulnerabilities")]
    public List<VulnerabilityItem> Vulnerabilities { get; set; } = [];
}

public class VulnerabilityItem
{
    [JsonPropertyName("cve")]
    public CveItem? Cve { get; set; }
}

public class CveItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonPropertyName("published")]
    public DateTime Published { get; set; }
    
    [JsonPropertyName("lastModified")]
    public DateTime LastModified { get; set; }
    
    [JsonPropertyName("descriptions")]
    public List<CveDescription>? Descriptions { get; set; }
    
    [JsonPropertyName("metrics")]
    public CveMetrics? Metrics { get; set; }
    
    [JsonPropertyName("references")]
    public List<CveReference>? References { get; set; }
}

public class CveDescription
{
    [JsonPropertyName("lang")]
    public string Lang { get; set; } = string.Empty;
    
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

public class CveMetrics
{
    [JsonPropertyName("cvssMetricV31")]
    public List<CvssMetricV31>? CvssMetricV31 { get; set; }
    
    [JsonPropertyName("cvssMetricV2")]
    public List<CvssMetricV2>? CvssMetricV2 { get; set; }
}

public class CvssMetricV31
{
    [JsonPropertyName("cvssData")]
    public CvssDataV31? CvssData { get; set; }
}

public class CvssMetricV2
{
    [JsonPropertyName("cvssData")]
    public CvssDataV2? CvssData { get; set; }
}

public class CvssDataV31
{
    [JsonPropertyName("baseScore")]
    public double BaseScore { get; set; }
    
    [JsonPropertyName("baseSeverity")]
    public string? BaseSeverity { get; set; }
    
    [JsonPropertyName("vectorString")]
    public string VectorString { get; set; } = string.Empty;
}

public class CvssDataV2
{
    [JsonPropertyName("baseScore")]
    public double BaseScore { get; set; }
    
    [JsonPropertyName("vectorString")]
    public string VectorString { get; set; } = string.Empty;
}

public class CveReference
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

#endregion
