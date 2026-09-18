using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace TrafficLightsEnhancement.Tests.Compatibility;

public class ReleaseVersionTests
{
    private const string ExpectedSemanticVersion = "1.0.9";
    private const string ExpectedDisplayName = "Traffic Lights Enhancement";
    private const string ExpectedDeployFolder = "TrafficLightsEnhancementExtended";

    [Fact]
    public void Tle_release_metadata_uses_the_current_semantic_version()
    {
        string repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

        XDocument project = XDocument.Load(
            Path.Combine(
                repositoryRoot,
                "TrafficLightsEnhancement",
                "TrafficLightsEnhancement.csproj"));
        string projectVersion = project.Descendants("Version").Single().Value;
        string informationalVersion =
            project.Descendants("InformationalVersion").Single().Value;

        using JsonDocument uiManifest = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(
                    repositoryRoot,
                    "TrafficLightsEnhancement",
                    "UI",
                    "mod.json")));
        string uiVersion = uiManifest.RootElement
            .GetProperty("version")
            .GetString()!;

        Assert.Equal($"{ExpectedSemanticVersion}.0", projectVersion);
        Assert.Equal($"{ExpectedSemanticVersion}.0", informationalVersion);
        Assert.Equal(ExpectedSemanticVersion, uiVersion);
    }

    [Fact]
    public void Tle_uses_the_player_facing_display_name_without_changing_compatibility_identifiers()
    {
        string repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

        XDocument project = XDocument.Load(
            Path.Combine(
                repositoryRoot,
                "TrafficLightsEnhancement",
                "TrafficLightsEnhancement.csproj"));
        XDocument publishConfiguration = XDocument.Load(
            Path.Combine(
                repositoryRoot,
                "TrafficLightsEnhancement",
                "Properties",
                "PublishConfiguration.xml"));

        Assert.Equal(
            "C2VM.TrafficLightsEnhancement",
            project.Descendants("AssemblyName").Single().Value);
        Assert.Equal(
            "C2VM.TrafficLightsEnhancement",
            project.Descendants("RootNamespace").Single().Value);
        Assert.Equal(
            ExpectedDeployFolder,
            project.Descendants("TleDeployFolder").Single().Value);
        Assert.Equal(
            ExpectedDisplayName,
            publishConfiguration.Root!
                .Element("DisplayName")!
                .Attribute("Value")!
                .Value);
    }
}
