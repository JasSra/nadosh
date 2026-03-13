namespace Nadosh.Agents.Tests.Agents;

public sealed class GetCapabilitiesToolsAgentTests
{
    private readonly GetCapabilitiesToolsAgent _agent = new(NullLogger<GetCapabilitiesToolsAgent>.Instance);

    [Fact]
    public void GetSupportedToolCatalog_ReturnsSortedCatalogWithSuiteMetadata()
    {
        var catalog = _agent.GetSupportedToolCatalog();

        Assert.NotEmpty(catalog);
        Assert.Equal(
            catalog
                .OrderBy(tool => tool.ToolSuite, StringComparer.OrdinalIgnoreCase)
                .ThenBy(tool => tool.ToolName, StringComparer.OrdinalIgnoreCase)
                .Select(tool => $"{tool.ToolSuite}:{tool.ToolName}"),
            catalog.Select(tool => $"{tool.ToolSuite}:{tool.ToolName}"));

        Assert.All(catalog, tool =>
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.ToolSuite));
            Assert.False(string.IsNullOrWhiteSpace(tool.PackageName));
            Assert.NotEmpty(tool.Capabilities);
            Assert.NotEmpty(tool.CommonFlags);
        });
    }

    [Fact]
    public void GetSupportedToolCatalog_ContainsExpectedBaseImageTools()
    {
        var catalog = _agent.GetSupportedToolCatalog();

        Assert.Contains(catalog, tool => tool.ToolName == "nmap" && tool.ToolSuite == "network-reconnaissance" && tool.PackageName == "nmap");
        Assert.Contains(catalog, tool => tool.ToolName == "masscan" && tool.ToolSuite == "network-reconnaissance" && tool.PackageName == "masscan");
        Assert.Contains(catalog, tool => tool.ToolName == "nikto" && tool.ToolSuite == "web-assessment" && tool.PackageName == "nikto");
        Assert.Contains(catalog, tool => tool.ToolName == "nuclei" && tool.ToolSuite == "vulnerability-validation" && tool.PackageName == "nuclei");
        Assert.Contains(catalog, tool => tool.ToolName == "hydra" && tool.ToolSuite == "password-auditing" && tool.PackageName == "hydra");
        Assert.Contains(catalog, tool => tool.ToolName == "python3" && tool.ToolSuite == "python-tooling" && tool.PackageName == "python3");

        var netcat = Assert.Single(catalog, tool => tool.ToolName == "netcat");
        Assert.Contains("nc", netcat.Aliases);
        Assert.Contains("nc.traditional", netcat.Aliases);

        var testSsl = Assert.Single(catalog, tool => tool.ToolName == "testssl.sh");
        Assert.Contains("testssl", testSsl.Aliases);
    }

    [Fact]
    public void GetSupportedToolsGroupedBySuite_GroupsToolsIntoExpectedSuites()
    {
        var grouped = _agent.GetSupportedToolsGroupedBySuite();

        Assert.True(grouped.ContainsKey("network-reconnaissance"));
        Assert.True(grouped.ContainsKey("web-assessment"));
        Assert.True(grouped.ContainsKey("vulnerability-validation"));
        Assert.True(grouped.ContainsKey("tls-analysis"));
        Assert.True(grouped.ContainsKey("service-enumeration"));
        Assert.True(grouped.ContainsKey("password-auditing"));
        Assert.True(grouped.ContainsKey("utilities"));
        Assert.True(grouped.ContainsKey("python-tooling"));

        Assert.Contains(grouped["network-reconnaissance"], tool => tool.ToolName == "nmap");
        Assert.Contains(grouped["network-reconnaissance"], tool => tool.ToolName == "masscan");
        Assert.Contains(grouped["network-reconnaissance"], tool => tool.ToolName == "netcat");
        Assert.Contains(grouped["web-assessment"], tool => tool.ToolName == "ffuf");
        Assert.Contains(grouped["web-assessment"], tool => tool.ToolName == "curl");
        Assert.Contains(grouped["tls-analysis"], tool => tool.ToolName == "sslscan");
        Assert.Contains(grouped["tls-analysis"], tool => tool.ToolName == "testssl.sh");
    }
}
