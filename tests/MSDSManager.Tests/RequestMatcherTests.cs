using MSDSManager.Services;
using Xunit;

namespace MSDSManager.Tests;

public sealed class RequestMatcherTests
{
    [Fact]
    public void Extracts_product_code_without_sds_cue()
    {
        var terms = RequestMatcher.ExtractRequestedTerms(
            "Need sheets please",
            """
            Hi Kim,

            Need the following:
            LUX5
            Fairy Professional

            Thanks
            """);

        Assert.Contains(terms, t => t.Equals("LUX5", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(terms, t => t.Contains("Fairy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Extracts_list_without_please_send_sds_for()
    {
        var terms = RequestMatcher.ExtractRequestedTerms(
            "SDS request",
            """
            Hi,
            LUX-5
            Cif cream
            """);

        Assert.Contains(terms, t => RequestMatcher.Compact(t) == "lux5");
        Assert.Contains(terms, t => t.Contains("Cif", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Matches_product_code_against_filename()
    {
        var (repo, _, lifetime) = TestHelpers.CreateRepository();
        using (lifetime)
        {
            TestHelpers.SeedDocument(repo, "Luxury Wash 5L", "LUX5 SDS.pdf");
            var matcher = new RequestMatcher(repo);

            var results = matcher.MatchFromEmailText(
                "Sheets",
                "Please send SDS for LUX5 and also the washroom spray.");

            var lux = Assert.Single(results, r =>
                r.RequestedTerm.Contains("LUX5", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(lux.SelectedDocument);
            Assert.True(lux.Confidence >= 80);
            Assert.True(lux.Include);
        }
    }

    [Fact]
    public void Matches_hyphenated_code_to_compact_filename()
    {
        var (repo, _, lifetime) = TestHelpers.CreateRepository();
        using (lifetime)
        {
            TestHelpers.SeedDocument(repo, "Luxury Wash", "LUX-5.pdf");
            var matcher = new RequestMatcher(repo);

            var results = matcher.MatchFromEmailText("request", "Need LUX5 please");
            var match = results.FirstOrDefault(r => RequestMatcher.Compact(r.RequestedTerm) == "lux5");
            Assert.NotNull(match);
            Assert.NotNull(match!.SelectedDocument);
        }
    }

    [Fact]
    public void Learns_compact_alias_variant()
    {
        var variants = RequestMatcher.AliasVariants("LUX-5").ToList();
        Assert.Contains(variants, v => v.Equals("LUX-5", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(variants, v => v.Equals("lux5", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Ignores_section_and_page_false_codes()
    {
        var terms = RequestMatcher.ExtractRequestedTerms(
            "SDS",
            "See section16 on page2 of the attached.");
        Assert.DoesNotContain(terms, t => t.Equals("section16", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(terms, t => t.Equals("page2", StringComparison.OrdinalIgnoreCase));
    }
}
