using FluentAssertions;
using NetSamLite.Server.Normalization;

namespace NetSamLite.Server.Tests.Normalization;

public sealed class NormalizationRuleLoaderTests
{
    [Fact]
    public void LoadFromFile_ActualRulesFile_ParsesAllFourRuleGroupsWithExpectedRuleCounts()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Normalization", "normalization-rules.json");

        var document = NormalizationRuleLoader.LoadFromFile(path);

        document.Version.Should().Be(1);
        document.RuleGroups.Should().HaveCount(4);
        document.RuleGroups.Single(g => g.Type == "exclude").Rules.Should().ContainSingle();
        document.RuleGroups.Single(g => g.Type == "alias").Rules.Should().ContainSingle();
        document.RuleGroups.Single(g => g.Type == "productPattern").Rules.Should().HaveCount(2);
        document.RuleGroups.Single(g => g.Type == "commonCleanup").Rules.Should().HaveCount(2);
        document.UnclassifiedPolicy.Action.Should().Be("passThroughAsIs");
        document.UnclassifiedPolicy.Flag.Should().Be("unclassified");
    }

    [Fact]
    public void LoadFromFile_PathDoesNotExist_ThrowsFileNotFoundException()
    {
        var missingPath = Path.Combine(AppContext.BaseDirectory, "Normalization", "does-not-exist.json");

        var act = () => NormalizationRuleLoader.LoadFromFile(missingPath);

        act.Should().Throw<FileNotFoundException>();
    }
}
