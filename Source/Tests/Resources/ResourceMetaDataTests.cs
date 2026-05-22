using Celbridge.Explorer.Services;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.UserInterface.Services;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for the reference-graph subsystem of IResourceMetaData. The
/// frontmatter-index methods are not yet implemented and throw
/// NotImplementedException; those scenarios are covered separately.
/// </summary>
[TestFixture]
public class ResourceMetaDataTests
{
    private string _projectFolderPath = null!;
    private ResourceRegistry _resourceRegistry = null!;
    private ResourceMetaData _metaData = null!;
    private IMessengerService _messengerService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ResourceMetaDataTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);

        _messengerService = new MessengerService();
        var fileIconService = new FileIconService();
        _resourceRegistry = new ResourceRegistry(
            Substitute.For<ILogger<ResourceRegistry>>(),
            _messengerService,
            fileIconService);
        _resourceRegistry.ProjectFolderPath = _projectFolderPath;

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.IsWorkspacePageLoaded.Returns(true);
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        // The mutation tools route writes through IResourceFileSystem, so the
        // fixture wires a real implementation against the temp project folder.
        var fileSystem = new ResourceFileSystem(
            Substitute.For<ILogger<ResourceFileSystem>>(),
            _messengerService,
            _workspaceWrapper);
        workspaceService.ResourceFileSystem.Returns(fileSystem);

        _metaData = new ResourceMetaData(
            Substitute.For<ILogger<ResourceMetaData>>(),
            _messengerService,
            _workspaceWrapper,
            new TextBinarySniffer());
    }

    [TearDown]
    public void TearDown()
    {
        _metaData.Dispose();
        if (Directory.Exists(_projectFolderPath))
        {
            try
            {
                Directory.Delete(_projectFolderPath, true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    [Test]
    public void ScanTextForReferences_FindsQuotedReferences()
    {
        // Both double-quoted and single-quoted references are detected; the
        // unquoted "project:other/file.txt" between them is not a tracked
        // reference because references must always be quoted.
        var text = "Some text with \"project:foo/bar.md\" and project:other/file.txt and 'project:third/file.md' embedded.";

        var references = ResourceMetaData.ScanTextForReferences(text);

        references.Should().Contain(new ResourceKey("project:foo/bar.md"));
        references.Should().Contain(new ResourceKey("project:third/file.md"));
        references.Should().NotContain(new ResourceKey("project:other/file.txt"));
    }

    [Test]
    public void ScanTextForReferences_SkipsBareReferences()
    {
        // A "project:" marker not preceded by an ASCII quote is not a tracked
        // reference, even if it parses as a valid key. This is the contract
        // that lets the scanner avoid false positives in arbitrary prose.
        var text = "see project:foo/bar.md and project:another/file.txt for details";

        var references = ResourceMetaData.ScanTextForReferences(text);

        references.Should().BeEmpty();
    }

    [Test]
    public void ScanTextForReferences_SkipsInvalidQuotedCandidates()
    {
        // Quoted but structurally invalid (double slashes are not legal in a
        // resource key path). The candidate is dropped silently.
        var text = "garbage \"project://invalid\" more garbage";

        var references = ResourceMetaData.ScanTextForReferences(text);

        references.Should().BeEmpty();
    }

    [Test]
    public void ScanTextForReferences_StopsAtKeyTerminators()
    {
        // The closing quote should terminate the candidate; bar.md is the full key.
        var text = "see \"project:foo/bar.md\" for details";

        var references = ResourceMetaData.ScanTextForReferences(text);

        references.Should().HaveCount(1);
        references.Should().Contain(new ResourceKey("project:foo/bar.md"));
    }

    [TestCase('"')]
    [TestCase('\'')]
    public void ScanTextForReferences_FindsKeyWithSpacesInsideAsciiQuotes(char quote)
    {
        var text = $"target = {quote}project:docs/My Document.md{quote}";

        var references = ResourceMetaData.ScanTextForReferences(text);

        references.Should().HaveCount(1);
        references.Should().Contain(new ResourceKey("project:docs/My Document.md"));
    }

    [TestCase('"')]
    [TestCase('\'')]
    public void ScanTextForReferences_FindsJsonEscapedQuotedReference(char quote)
    {
        // JSON / TOML basic / C-family escape sequence \" or \'. The two-char
        // opener takes precedence over the single-char opener so the delimited
        // region closes on the matching \" or \' two-char sequence.
        var text = $"text = \"See \\{quote}project:docs/My Doc.md\\{quote} thanks\"";

        var references = ResourceMetaData.ScanTextForReferences(text);

        references.Should().Contain(new ResourceKey("project:docs/My Doc.md"));
    }

    [Test]
    public void ScanTextForReferences_RejectsBracketWrappedReferences()
    {
        // Only ASCII " and ' open a delimited region. A reference wrapped in
        // brackets (or any other char) is not a tracked reference. There is
        // no bare-scan fallback, so nothing is detected.
        var text = "see [project:docs/My Document.md] and (project:foo) for details";

        var references = ResourceMetaData.ScanTextForReferences(text);

        references.Should().BeEmpty();
    }

    [Test]
    public void ScanTextForReferences_RejectsReferencesWithUnmatchedQuote()
    {
        // The opening " starts a delimited scan looking for the closing ".
        // The line ends before the close is found, so the candidate is dropped.
        // No bare fallback, no phantom reference.
        var text = "see \"project:docs/foo and more text";

        var references = ResourceMetaData.ScanTextForReferences(text);

        references.Should().BeEmpty();
    }

    [Test]
    public void ScanTextForReferences_RejectsReferencesThatSpanNewlines()
    {
        // A newline inside the supposed delimited region aborts the scan and
        // the candidate is dropped. References do not span lines.
        var text = "\"project:docs/foo\nbar.md\"";

        var references = ResourceMetaData.ScanTextForReferences(text);

        references.Should().BeEmpty();
    }

    [Test]
    public void ScannerAndRewrite_AgreeOnDetectedReferencePositions()
    {
        // Symmetry property: for every reference the scanner detects, the
        // rewrite cascade's leading and trailing boundary checks must accept
        // the same position. If this test fails, the two code paths have
        // drifted and the cascade will silently skip references the index
        // thinks exist.
        var samples = new[]
        {
            "double-quoted: target = \"project:foo.md\"",
            "single-quoted: target = 'project:foo.md'",
            "double-quoted with space: target = \"project:docs/My Doc.md\"",
            "single-quoted with space: target = 'project:docs/My Doc.md'",
            "json-escaped: text = \"See \\\"project:foo.md\\\" thanks\"",
            "json-escaped with space: text = \"See \\\"project:docs/My Doc.md\\\" thanks\"",
            "two refs: \"project:first.md\" then \"project:second.md\"",
            "double then single: \"project:a.md\" plus 'project:b.md'",
            "back-to-back: \"project:x.md\"\"project:y.md\"",
            "reference at start of file: \"project:start.md\" and the rest",
        };

        foreach (var sample in samples)
        {
            var detected = ResourceMetaData.ScanTextForReferences(sample);

            // Every sample is constructed to contain at least one detectable
            // reference; an empty result means the scanner has regressed.
            detected.Should().NotBeEmpty($"sample '{sample}' has at least one tracked reference");

            foreach (var key in detected)
            {
                var sourceLiteral = ReferenceLiteralRules.ReferenceMarker + key.Path;
                int matchIndex = sample.IndexOf(sourceLiteral, StringComparison.Ordinal);
                matchIndex.Should().BeGreaterThanOrEqualTo(0,
                    $"scanner detected '{key}' in '{sample}' but the literal isn't there");

                bool leadingOk = matchIndex == 0
                    || ReferenceLiteralRules.IsNonKeyBoundary(sample[matchIndex - 1]);
                leadingOk.Should().BeTrue(
                    $"leading boundary check would reject the rewrite for '{key}' in '{sample}'");

                int afterMatch = matchIndex + sourceLiteral.Length;
                bool trailingOk = afterMatch == sample.Length
                    || ReferenceLiteralRules.IsNonKeyBoundary(sample[afterMatch]);
                trailingOk.Should().BeTrue(
                    $"trailing boundary check would reject the rewrite for '{key}' in '{sample}'");
            }
        }
    }

    [Test]
    public async Task RebuildAsync_ProducesReferenceGraph()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "source.md"),
            "This file references \"project:target.md\".");
        File.WriteAllText(Path.Combine(_projectFolderPath, "target.md"), "Target file.");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        var rebuildResult = await _metaData.RebuildAsync();

        rebuildResult.IsSuccess.Should().BeTrue();
        rebuildResult.Value.ReferencesFound.Should().Be(1);

        var referencers = _metaData.GetReferencers(new ResourceKey("target.md"));
        referencers.Should().Contain(new ResourceKey("source.md"));

        var references = _metaData.GetReferences(new ResourceKey("source.md"));
        references.Should().Contain(new ResourceKey("target.md"));
    }

    [Test]
    public async Task RebuildAsync_SkipsBinaryFiles()
    {
        // A PNG containing a quoted reference literal must still be skipped —
        // binary files don't participate in the reference graph regardless of
        // their bytes. Using a quoted form (rather than bare bytes) ensures the
        // skip behaviour is exercised against a literal the scanner would
        // otherwise track if the file were text.
        var pngSignature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var marker = System.Text.Encoding.UTF8.GetBytes("\"project:foo\"");
        var pngBytes = new byte[pngSignature.Length + marker.Length];
        Buffer.BlockCopy(pngSignature, 0, pngBytes, 0, pngSignature.Length);
        Buffer.BlockCopy(marker, 0, pngBytes, pngSignature.Length, marker.Length);
        File.WriteAllBytes(Path.Combine(_projectFolderPath, "image.png"), pngBytes);

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        await _metaData.RebuildAsync();

        _metaData.GetReferencers(new ResourceKey("foo")).Should().BeEmpty();
    }

    [Test]
    public async Task RebuildAsync_SkipsOversizeFiles()
    {
        var oversize = new string('x', 11 * 1024 * 1024); // > 10MB
        File.WriteAllText(Path.Combine(_projectFolderPath, "big.txt"), oversize);

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        var rebuildResult = await _metaData.RebuildAsync();

        rebuildResult.IsSuccess.Should().BeTrue();
        rebuildResult.Value.FilesSkipped.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task RebuildAsync_MarksServiceReady()
    {
        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        await _metaData.RebuildAsync();

        _metaData.IsReady.Should().BeTrue();
        await _metaData.WaitUntilReadyAsync();
    }

    [Test]
    public async Task GetAllReferencedTargets_ReturnsUnionOfTargets()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "a.md"),
            "Refers to \"project:x.md\" and \"project:y.md\".");
        File.WriteAllText(Path.Combine(_projectFolderPath, "b.md"),
            "Refers to \"project:y.md\".");
        File.WriteAllText(Path.Combine(_projectFolderPath, "x.md"), string.Empty);
        File.WriteAllText(Path.Combine(_projectFolderPath, "y.md"), string.Empty);

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();
        await _metaData.RebuildAsync();

        var targets = _metaData.GetAllReferencedTargets();
        targets.Should().Contain(new ResourceKey("x.md"));
        targets.Should().Contain(new ResourceKey("y.md"));
    }

    [Test]
    public async Task RebuildAsync_IndexesSidecarFrontmatter()
    {
        // A .cel sidecar contributes its top-level frontmatter to the
        // per-resource snapshot keyed against the parent file.
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md"), "Notes body.");
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md.cel"),
            "+++\ntags = [\"meeting\", \"follow-up\"]\npriority = \"high\"\n+++\n");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();
        var rebuildResult = await _metaData.RebuildAsync();

        rebuildResult.IsSuccess.Should().BeTrue();
        rebuildResult.Value.FrontmatterEntries.Should().Be(1);

        var parent = new ResourceKey("notes.md");
        var frontmatterResult = _metaData.GetFrontmatter(parent);
        frontmatterResult.IsSuccess.Should().BeTrue();
        frontmatterResult.Value["priority"].Should().Be("high");

        _metaData.GetTags(parent).Should().Contain("meeting").And.Contain("follow-up");

        _metaData.FindByMetaData("priority", "high").Should().Contain(parent);
        _metaData.FindByTag("meeting").Should().Contain(parent);
    }

    [Test]
    public async Task RebuildAsync_BrokenSidecar_ProducesNoFrontmatterEntries()
    {
        // Frontmatter that fails to parse is dropped silently — the registry's
        // pairing pass classifies the sidecar as Broken via SidecarReport and
        // the metadata service simply records no index entries for it.
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md"), "Notes body.");
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md.cel"),
            "+++\nthis is not valid toml === yikes\n+++\n");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();
        var rebuildResult = await _metaData.RebuildAsync();

        rebuildResult.IsSuccess.Should().BeTrue();
        rebuildResult.Value.FrontmatterEntries.Should().Be(0);

        var parent = new ResourceKey("notes.md");
        _metaData.GetFrontmatter(parent).IsFailure.Should().BeTrue();
        _metaData.GetTags(parent).Should().BeEmpty();
    }

    [Test]
    public async Task RebuildAsync_SidecarWithReferencesAndFrontmatter_ContributesToBothIndexes()
    {
        // A .cel file can carry both project: references inside its frontmatter
        // (e.g. a "related" field) and indexable scalar fields. Both indexes
        // must reflect the file.
        File.WriteAllText(Path.Combine(_projectFolderPath, "doc.md"), "Doc body.");
        File.WriteAllText(Path.Combine(_projectFolderPath, "target.md"), "Target.");
        File.WriteAllText(Path.Combine(_projectFolderPath, "doc.md.cel"),
            "+++\npriority = \"high\"\nrelated = \"project:target.md\"\n+++\nBody text.");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();
        await _metaData.RebuildAsync();

        var parent = new ResourceKey("doc.md");
        var sidecarKey = new ResourceKey("doc.md.cel");

        // The frontmatter index keys against the parent.
        _metaData.FindByMetaData("priority", "high").Should().Contain(parent);

        // The reference graph indexes the sidecar as a source (it's the file
        // that contains the literal), pointing at target.md.
        _metaData.GetReferencers(new ResourceKey("target.md")).Should().Contain(sidecarKey);
    }

    [Test]
    public async Task FindByMetaData_ListField_MatchesByContains()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "a.md"), "Body A.");
        File.WriteAllText(Path.Combine(_projectFolderPath, "b.md"), "Body B.");
        File.WriteAllText(Path.Combine(_projectFolderPath, "a.md.cel"),
            "+++\ntags = [\"alpha\", \"beta\"]\n+++\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "b.md.cel"),
            "+++\ntags = [\"alpha\", \"gamma\"]\n+++\n");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();
        await _metaData.RebuildAsync();

        _metaData.FindByMetaData("tags", "alpha")
            .Should().Contain(new ResourceKey("a.md"))
            .And.Contain(new ResourceKey("b.md"));

        _metaData.FindByMetaData("tags", "beta")
            .Should().Contain(new ResourceKey("a.md"))
            .And.NotContain(new ResourceKey("b.md"));
    }

    [Test]
    public async Task FindByMetaData_NumericQuery_FindsLongTypedScalar()
    {
        // TOML integers parse as long; the query must canonicalise an int
        // argument into the same key form so the lookup succeeds.
        File.WriteAllText(Path.Combine(_projectFolderPath, "task.md"), "Task body.");
        File.WriteAllText(Path.Combine(_projectFolderPath, "task.md.cel"),
            "+++\nestimate = 42\n+++\n");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();
        await _metaData.RebuildAsync();

        _metaData.FindByMetaData("estimate", 42).Should().Contain(new ResourceKey("task.md"));
        _metaData.FindByMetaData("estimate", (long)42).Should().Contain(new ResourceKey("task.md"));
    }

    [Test]
    public async Task RebuildAsync_InvalidSidecarFile_DoesNotIndexFrontmatter()
    {
        // A file ending in .cel.cel is the invalid-sidecar marker; the registry
        // refuses to pair it and the metadata service must follow suit.
        File.WriteAllText(Path.Combine(_projectFolderPath, "weird.png.cel.cel"),
            "+++\ntags = [\"should-not-index\"]\n+++\n");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();
        await _metaData.RebuildAsync();

        _metaData.FindByMetaData("tags", "should-not-index").Should().BeEmpty();
    }

    [Test]
    public async Task TransientReadFailure_PreservesExistingIndexEntries()
    {
        // Index a file with a known reference, then lock it exclusively to
        // simulate an external editor holding the file open mid-write. A
        // change event during the lock must not drop the existing reference
        // entries — the transient failure should be retried, not converted to
        // "this file has no references".
        var sourcePath = Path.Combine(_projectFolderPath, "source.md");
        var targetPath = Path.Combine(_projectFolderPath, "target.md");
        File.WriteAllText(sourcePath, "Refers to \"project:target.md\".");
        File.WriteAllText(targetPath, "Target file.");

        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();
        (await _metaData.RebuildAsync()).IsSuccess.Should().BeTrue();

        var sourceKey = new ResourceKey("source.md");
        var targetKey = new ResourceKey("target.md");
        _metaData.GetReferencers(targetKey).Should().Contain(sourceKey);

        using (var lockStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // While the file is locked, send a change event. The worker will
            // attempt to read source.md, fail with an IOException, and classify
            // the failure as transient.
            _messengerService.Send(new ResourceChangedMessage(sourceKey));
            await _metaData.WaitForPendingUpdatesAsync();
            await Task.Delay(150);

            // The existing index entry for source.md → target.md must survive.
            _metaData.GetReferencers(targetKey).Should().Contain(sourceKey);
        }
    }

    [Test]
    public async Task SetFrontmatterField_UpdatesIndex_BeforeWatcherEventDelivers()
    {
        // The file watcher delivers ResourceChangedMessage asynchronously via
        // the UI dispatcher, so the post-write index update must run inline
        // inside the mutation path. Otherwise an agent that calls set followed
        // by get on the same call sees a stale "no frontmatter indexed" error.
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md"), "Body.");
        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();
        (await _metaData.RebuildAsync()).IsSuccess.Should().BeTrue();

        var resource = new ResourceKey("notes.md");
        var setResult = await _metaData.SetFrontmatterFieldAsync(resource, "priority", "high");
        setResult.IsSuccess.Should().BeTrue();

        // No WaitForPendingUpdatesAsync / Task.Delay here — the index must
        // reflect the write the moment SetFrontmatterFieldAsync returns.
        var frontmatterResult = _metaData.GetFrontmatter(resource);
        frontmatterResult.IsSuccess.Should().BeTrue();
        frontmatterResult.Value.Should().ContainKey("priority");
        frontmatterResult.Value["priority"].Should().Be("high");

        _metaData.FindByMetaData("priority", "high").Should().Contain(resource);
    }

    [Test]
    public async Task AddTag_UpdatesIndex_BeforeWatcherEventDelivers()
    {
        // Same race as Set, exercised through the tag-specific surface. The
        // FindByTag lookup is a thin alias for FindByMetaData against "tags",
        // so this also covers the list-of-scalar indexing path.
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md"), "Body.");
        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();
        (await _metaData.RebuildAsync()).IsSuccess.Should().BeTrue();

        var resource = new ResourceKey("notes.md");
        var addResult = await _metaData.AddTagAsync(resource, "flagged");
        addResult.IsSuccess.Should().BeTrue();

        _metaData.GetTags(resource).Should().Contain("flagged");
        _metaData.FindByTag("flagged").Should().Contain(resource);
    }

    [Test]
    public async Task SpuriousDeleteEvent_DoesNotClearIndex_WhenFileStillOnDisk()
    {
        // Third race: ResourceFileSystem.WriteAtomicAsync uses
        // File.Move(overwrite: true) to replace existing files, which on
        // Windows fires a FileSystemWatcher delete event for the
        // destination followed by a create event. If OnResourceDeleted
        // clears the index unconditionally, the synchronous entry from
        // MutateSidecarFrontmatterAsync gets clobbered in the window
        // before the rescan reads the file back. Sending a delete event
        // for a file that is still on disk simulates the spurious case.
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md"), "Body.");
        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();
        (await _metaData.RebuildAsync()).IsSuccess.Should().BeTrue();

        var resource = new ResourceKey("notes.md");
        (await _metaData.AddTagAsync(resource, "alpha")).IsSuccess.Should().BeTrue();
        (await _metaData.AddTagAsync(resource, "beta")).IsSuccess.Should().BeTrue();

        // The sidecar file is on disk and the index has both tags. Fire a
        // delete event for the sidecar key while the file still exists —
        // this is the shape WriteAtomicAsync's overwrite produces.
        var sidecarKey = new ResourceKey("notes.md.cel");
        _messengerService.Send(new ResourceDeletedMessage(sidecarKey));

        // The index must still reflect both tags. The companion create
        // event would normally arrive next; we don't send it here, since
        // the assertion target is that the spurious delete alone does
        // not clear the entry.
        var frontmatterResult = _metaData.GetFrontmatter(resource);
        frontmatterResult.IsSuccess.Should().BeTrue();
        var tags = (System.Collections.IEnumerable)frontmatterResult.Value["tags"];
        tags.Cast<object>().Select(t => t?.ToString()).Should().Contain(new[] { "alpha", "beta" });
    }

    [Test]
    public async Task WatcherRescan_DoesNotClobberFreshIndex_WhenRegistryLags()
    {
        // Second race: after MutateSidecarFrontmatterAsync writes a new
        // sidecar and synchronously seeds the index, the file watcher's
        // ResourceCreatedMessage arrives at the worker. If the worker
        // looked up the sidecar's parent via the registry, the lookup would
        // fail (the registry's pairing table is only refreshed by
        // UpdateResourceRegistry, which lags the watcher) and the fresh
        // index entry would be dropped. The fix derives the parent key by
        // stripping ".cel" directly so the worker can refresh the entry
        // without depending on the registry catching up.
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md"), "Body.");
        _resourceRegistry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();
        (await _metaData.RebuildAsync()).IsSuccess.Should().BeTrue();

        var resource = new ResourceKey("notes.md");
        (await _metaData.SetFrontmatterFieldAsync(resource, "priority", "high")).IsSuccess.Should().BeTrue();

        // Deliberately skip UpdateResourceRegistry so the registry still
        // does not know about notes.md.cel. Send the watcher event the
        // way ResourceMonitor would have.
        var sidecarKey = new ResourceKey("notes.md.cel");
        _messengerService.Send(new ResourceCreatedMessage(sidecarKey));
        await _metaData.WaitForPendingUpdatesAsync();
        await Task.Delay(50);

        // The frontmatter entry seeded by SetFrontmatterFieldAsync must
        // survive the rescan, even though the registry has not paired the
        // sidecar yet.
        var frontmatterResult = _metaData.GetFrontmatter(resource);
        frontmatterResult.IsSuccess.Should().BeTrue();
        frontmatterResult.Value["priority"].Should().Be("high");
    }
}
