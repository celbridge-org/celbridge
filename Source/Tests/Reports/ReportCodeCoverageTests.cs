using System.Reflection;
using System.Text.RegularExpressions;
using Celbridge.Reports;
using Celbridge.Tests.Architecture;
using Celbridge.Tests.Localization;

namespace Celbridge.Tests.Reports;

/// <summary>
/// A finding code is the stable identity of a finding kind, and reports carrying it persist on disk
/// long after the build that wrote them. These tests hold the properties that makes that safe: codes
/// are unique, they follow one shape, and nothing is declared that no producer emits.
/// </summary>
[TestFixture]
public class ReportCodeCoverageTests
{
    private static readonly Regex CodeFormatRegex =
        new(@"^CEL_[A-Z]+_\d{3}$", RegexOptions.Compiled);

    [Test]
    public void EveryCodeIsUnique()
    {
        var duplicateCodes = GetDescriptors()
            .GroupBy(descriptor => descriptor.Code.ToString(), StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        duplicateCodes.Should().BeEmpty(
            "a code is the stable identity of one finding kind, and a report on disk cannot be reinterpreted");
    }

    [Test]
    public void EveryCodeFollowsTheHostFormat()
    {
        // ReportCode itself only guarantees a code can survive a round trip. This is the host's own
        // format on top of that, which a contribution's namespaced code is not held to.
        foreach (var descriptor in GetDescriptors())
        {
            var code = descriptor.Code.ToString();

            code.Should().StartWith(ReportFindingCatalog.CodePrefix);
            CodeFormatRegex.IsMatch(code).Should().BeTrue(
                $"'{code}' should read CEL_<AREA>_<NNN>, matching CEL_FS_001 on DirectFileSystemAccessAnalyzer");
        }
    }

    [Test]
    public void EveryDescriptorHasAMessageAndIsAFinding()
    {
        foreach (var descriptor in GetDescriptors())
        {
            descriptor.MessageTemplate.Should().NotBeNullOrWhiteSpace();

            // Info is what a fact carries. A finding is something that needs attention, so a descriptor
            // declared at Info would render as a finding nothing is wrong with.
            descriptor.DefaultSeverity.Should().NotBe(ReportSeverity.Info,
                $"'{descriptor.Code}' is declared as a finding");
        }
    }

    [Test]
    public void EveryDescriptorIsEmittedByAProducer()
    {
        // The descriptor set is the registry, so a descriptor nothing emits is a code that can never
        // appear in a report and can never be looked up.
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        sourceFolder.Should().NotBeEmpty();

        var producerSource = string.Join(
            "\n",
            ArchitectureHelpers.EnumerateProductionSourceFiles(sourceFolder)
                .Where(filePath => !filePath.EndsWith("ReportFindingCatalog.cs", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));

        foreach (var entry in GetCatalogEntries())
        {
            var reference = $"{entry.Group}.{entry.Name}";
            producerSource.Should().Contain(reference,
                $"{entry.Descriptor.Code} is declared but no producer emits it");
        }
    }

    [Test]
    public void EveryDescriptorMessageResolvesToAHostString()
    {
        // A template is looked up as a key and used verbatim when nothing matches, which is what lets a
        // contribution supply its own wording. The host's own descriptors are keys, so one with no entry
        // would render its key name in the report rather than failing anywhere.
        var strings = TestLocalizerService.LoadStrings();
        strings.Should().NotBeEmpty("the application's en-US Resources.resw should be readable");

        foreach (var entry in GetCatalogEntries())
        {
            var template = entry.Descriptor.MessageTemplate;

            strings.Should().ContainKey(template,
                $"{entry.Descriptor.Code} names a key with no entry in Resources.resw");
        }
    }

    private static IEnumerable<ReportFindingDescriptor> GetDescriptors()
    {
        return GetCatalogEntries().Select(entry => entry.Descriptor);
    }

    private static List<CatalogEntry> GetCatalogEntries()
    {
        var entries = new List<CatalogEntry>();

        foreach (var groupType in typeof(ReportFindingCatalog).GetNestedTypes(BindingFlags.Public))
        {
            var fields = groupType.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.FieldType == typeof(ReportFindingDescriptor));

            foreach (var field in fields)
            {
                var descriptor = field.GetValue(null) as ReportFindingDescriptor;
                if (descriptor is null)
                {
                    continue;
                }

                entries.Add(new CatalogEntry(groupType.Name, field.Name, descriptor));
            }
        }

        return entries;
    }

    private record CatalogEntry(string Group, string Name, ReportFindingDescriptor Descriptor);
}
