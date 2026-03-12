using Nadosh.Core.Interfaces;
using Nadosh.Core.Services;

namespace Nadosh.Core.Tests.Assessment;

public sealed class DefaultAssessmentToolCatalogTests
{
    private readonly IAssessmentToolCatalog _catalog = new DefaultAssessmentToolCatalog();

    [Fact]
    public void GetAll_ReturnsDefinitionsSortedByToolId()
    {
        var definitions = _catalog.GetAll();

        Assert.NotEmpty(definitions);
        Assert.Equal(definitions.OrderBy(definition => definition.ToolId, StringComparer.OrdinalIgnoreCase).Select(definition => definition.ToolId),
            definitions.Select(definition => definition.ToolId));
    }

    [Fact]
    public void GetAll_EnforcesNonOffensiveGuardrails()
    {
        var definitions = _catalog.GetAll();

        Assert.All(definitions, definition =>
        {
            Assert.False(definition.AllowsStateChangingActions);
            Assert.False(definition.AllowsBinaryPayloads);
            Assert.False(definition.AllowsRemoteCodeExecution);
            Assert.NotEmpty(definition.SafetyChecks);

            if (definition.ExternalUseAllowed)
            {
                Assert.True(definition.RequiresApprovalForExternalTargets);
            }
        });
    }

    [Fact]
    public void GetById_IsCaseInsensitive()
    {
        var definition = _catalog.GetById("SERVICE.TLS.CERTIFICATE.COLLECT");

        Assert.NotNull(definition);
        Assert.Equal("service.tls.certificate.collect", definition!.ToolId);
    }

    [Fact]
    public void IsRegistered_ReturnsFalseForUnknownTool()
    {
        Assert.False(_catalog.IsRegistered("not-a-real-tool"));
    }
}
