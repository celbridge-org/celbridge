using System.Text.Json.Nodes;
using Celbridge.LanguageServers;
using Celbridge.LanguageServers.Helpers;
using Celbridge.LanguageServers.Services;
using Celbridge.Resources;
using Celbridge.Settings;
using Celbridge.Tests.Helpers;
using Microsoft.Extensions.Time.Testing;

namespace Celbridge.Tests.LanguageServers;

/// <summary>
/// Covers one language server's session, against a fake server on a pair of streams.
/// </summary>
[TestFixture]
public class LanguageServerSessionTests
{
    private static readonly string ProjectFolderPath = OperatingSystem.IsWindows()
        ? @"D:\Projects\Acme"
        : "/Projects/Acme";

    private static readonly string ProjectFolderUri = OperatingSystem.IsWindows()
        ? "file:///d%3A/Projects/Acme"
        : "file:///Projects/Acme";

    private static readonly string MainUri = ProjectFolderUri + "/src/main.ts";
    private static readonly string OtherUri = ProjectFolderUri + "/src/other.ts";

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    private FakeLanguageServer _server = null!;
    private FakeLanguageServerWorkspace _workspace = null!;
    private ILanguageServerDefinition _definition = null!;
    private FakeTimeProvider _timeProvider = null!;
    private TaskCompletionSource<bool> _ended = null!;

    [SetUp]
    public void Setup()
    {
        _server = new FakeLanguageServer();
        _workspace = new FakeLanguageServerWorkspace();
        _timeProvider = new FakeTimeProvider();
        _ended = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _definition = Substitute.For<ILanguageServerDefinition>();
        _definition.Id.Returns("fake");
        _definition.Feature.Returns(FeatureFlagCatalog.TypeScript);
    }

    [Test]
    public async Task Start_SendsInitializeWithTheProjectFolderTheOptionsAndTheCapabilities()
    {
        var options = new JsonObject
        {
            ["mode"] = "strict"
        };
        _definition.CreateInitializationOptions().Returns(options);
        var session = CreateSession();

        session.Start();
        var initializeRequest = await _server.ReadMethodAsync("initialize");

        var parameters = initializeRequest["params"]!;
        ((int)parameters["processId"]!).Should().Be(Environment.ProcessId);
        ((string?)parameters["rootUri"]).Should().Be(ProjectFolderUri);
        ((string?)parameters["workspaceFolders"]![0]!["uri"]).Should().Be(ProjectFolderUri);
        ((string?)parameters["workspaceFolders"]![0]!["name"]).Should().Be("Acme");
        ((string?)parameters["initializationOptions"]!["mode"]).Should().Be("strict");

        var capabilities = parameters["capabilities"]!;
        ((bool)capabilities["workspace"]!["diagnostics"]!["refreshSupport"]!).Should().BeTrue();
        capabilities["textDocument"]!["diagnostic"].Should().NotBeNull();
        capabilities["textDocument"]!["completion"].Should().NotBeNull();
        capabilities["workspace"]!["didChangeWatchedFiles"].Should().BeNull("the server watches the files itself");
        ((bool)capabilities["workspace"]!["applyEdit"]!).Should().BeTrue();
        ((bool)capabilities["textDocument"]!["rename"]!["prepareSupport"]!).Should().BeTrue();
        capabilities["textDocument"]!["codeAction"]!["codeActionLiteralSupport"].Should().NotBeNull();
    }

    [Test]
    public async Task AddPage_BeforeTheServerIsReady_GetsTheCapabilitiesOnceTheServerAnswers()
    {
        var session = CreateSession();
        session.Start();
        var page = session.AddPage(MainUri, "typescript")!;

        var capabilities = new JsonObject
        {
            ["hoverProvider"] = true
        };
        await _server.CompleteInitializeAsync(capabilities);
        var ready = await ReadPageAsync(page);

        ((string?)ready["method"]).Should().Be("celbridge/serverReady");
        ((string?)ready["params"]!["serverId"]).Should().Be("fake");
        ((bool)ready["params"]!["capabilities"]!["hoverProvider"]!).Should().BeTrue();
    }

    [Test]
    public async Task ReportFileChanges_AfterTheServerIsReady_SendsAWatchedFilesNotification()
    {
        var session = await StartReadySessionAsync();

        session.ReportFileChanges(new[] { new FileEvent(MainUri, 1), new FileEvent(OtherUri, 3) });
        var notification = await _server.ReadMethodAsync("workspace/didChangeWatchedFiles");

        var changes = notification["params"]!["changes"]!.AsArray();
        changes.Should().HaveCount(2);
        ((string?)changes[0]!["uri"]).Should().Be(MainUri);
        ((int)changes[0]!["type"]!).Should().Be(1);
        ((string?)changes[1]!["uri"]).Should().Be(OtherUri);
        ((int)changes[1]!["type"]!).Should().Be(3);
    }

    [Test]
    public async Task ReportFileChanges_BeforeTheServerIsReady_WaitsForTheServersAnswer()
    {
        var session = CreateSession();
        session.Start();

        session.ReportFileChanges(new[] { new FileEvent(MainUri, 1) });
        session.ReportFileChanges(new[] { new FileEvent(OtherUri, 1) });
        await _server.CompleteInitializeAsync(new JsonObject());
        var notification = await _server.ReadMethodAsync("workspace/didChangeWatchedFiles");

        var changes = notification["params"]!["changes"]!.AsArray();
        changes.Should().HaveCount(2);
        ((string?)changes[0]!["uri"]).Should().Be(MainUri);
        ((string?)changes[1]!["uri"]).Should().Be(OtherUri);
    }

    [Test]
    public async Task AddPage_AfterTheServerIsReady_GetsTheCapabilitiesAtOnce()
    {
        var session = await StartReadySessionAsync();

        var page = session.AddPage(MainUri, "typescript")!;
        var ready = await ReadPageAsync(page);

        ((string?)ready["method"]).Should().Be("celbridge/serverReady");
    }

    [Test]
    public async Task DidOpen_ReachesTheServerWithTheDocumentsUriAndLanguage()
    {
        var session = await StartReadySessionAsync();
        var page = session.AddPage(MainUri, "typescriptreact")!;

        page.Receive("""{"method":"textDocument/didOpen","params":{"text":"let a = 1;","version":4}}""");
        var didOpen = await _server.ReadMethodAsync("textDocument/didOpen");

        var textDocument = didOpen["params"]!["textDocument"]!;
        ((string?)textDocument["uri"]).Should().Be(MainUri);
        ((string?)textDocument["languageId"]).Should().Be("typescriptreact");
        ((int)textDocument["version"]!).Should().Be(4);
        ((string?)textDocument["text"]).Should().Be("let a = 1;");
    }

    [Test]
    public async Task DidChange_ReachesTheServerWithTheDocumentsUriAndTheChanges()
    {
        var session = await StartReadySessionAsync();
        var page = await OpenPageAsync(session, MainUri);

        page.Receive("""{"method":"textDocument/didChange","params":{"version":5,"contentChanges":[{"text":"let b = 2;"}]}}""");
        var didChange = await _server.ReadMethodAsync("textDocument/didChange");

        ((string?)didChange["params"]!["textDocument"]!["uri"]).Should().Be(MainUri);
        ((int)didChange["params"]!["textDocument"]!["version"]!).Should().Be(5);
        ((string?)didChange["params"]!["contentChanges"]![0]!["text"]).Should().Be("let b = 2;");
    }

    [Test]
    public async Task Request_GetsAFreshIdAndTheDocumentsUri_AndTheAnswerReturnsWithThePagesId()
    {
        var session = await StartReadySessionAsync();
        var page = await OpenPageAsync(session, MainUri);

        // The page names another document, and the session replaces the name with the page's own document.
        page.Receive("""{"id":7,"method":"textDocument/hover","params":{"textDocument":{"uri":"file:///elsewhere.ts"},"position":{"line":1,"character":2}}}""");
        var hoverRequest = await _server.ReadMethodAsync("textDocument/hover");
        await _server.RespondAsync(hoverRequest, CreateHoverResult("a number"));
        var answer = await ReadPageAsync(page);

        ((string?)hoverRequest["params"]!["textDocument"]!["uri"]).Should().Be(MainUri);
        ((int)hoverRequest["params"]!["position"]!["line"]!).Should().Be(1);
        ((int)hoverRequest["id"]!).Should().NotBe(7);
        ((int)answer["id"]!).Should().Be(7);
        ((string?)answer["result"]!["contents"]).Should().Be("a number");
    }

    [Test]
    public async Task Request_AnErrorFromTheServer_ReturnsToThePageAsAnError()
    {
        var session = await StartReadySessionAsync();
        var page = await OpenPageAsync(session, MainUri);

        page.Receive("""{"id":"a","method":"textDocument/definition","params":{"position":{"line":0,"character":0}}}""");
        var request = await _server.ReadMethodAsync("textDocument/definition");
        var error = new JsonObject
        {
            ["code"] = -32801,
            ["message"] = "Content modified"
        };
        var response = new JsonObject
        {
            ["id"] = request["id"]!.DeepClone(),
            ["error"] = error
        };
        await _server.SendAsync(response);
        var answer = await ReadPageAsync(page);

        ((string?)answer["id"]).Should().Be("a");
        ((int)answer["error"]!["code"]!).Should().Be(-32801);
    }

    [Test]
    public async Task ResolveRequest_PassesTheItemToTheServerUnchanged()
    {
        var session = await StartReadySessionAsync();
        var page = await OpenPageAsync(session, MainUri);

        page.Receive("""{"id":3,"method":"completionItem/resolve","params":{"label":"alpha","data":{"key":9}}}""");
        var request = await _server.ReadMethodAsync("completionItem/resolve");

        ((string?)request["params"]!["label"]).Should().Be("alpha");
        ((int)request["params"]!["data"]!["key"]!).Should().Be(9);
        request["params"]!["textDocument"].Should().BeNull();
    }

    [Test]
    public async Task CancelRequest_ReachesTheServerWithTheServersId()
    {
        var session = await StartReadySessionAsync();
        var page = await OpenPageAsync(session, MainUri);

        page.Receive("""{"id":7,"method":"textDocument/references","params":{"position":{"line":0,"character":0}}}""");
        var request = await _server.ReadMethodAsync("textDocument/references");
        page.Receive("""{"method":"$/cancelRequest","params":{"id":7}}""");
        var cancellation = await _server.ReadMethodAsync("$/cancelRequest");

        ((long)cancellation["params"]!["id"]!).Should().Be((long)request["id"]!);
    }

    [Test]
    public async Task TwoPages_SendTheSameRequestId_AndEachGetsItsOwnAnswer()
    {
        var session = await StartReadySessionAsync();
        var mainPage = await OpenPageAsync(session, MainUri);
        var otherPage = await OpenPageAsync(session, OtherUri);

        mainPage.Receive("""{"id":1,"method":"textDocument/hover","params":{"position":{"line":0,"character":0}}}""");
        otherPage.Receive("""{"id":1,"method":"textDocument/hover","params":{"position":{"line":0,"character":0}}}""");
        var firstRequest = await _server.ReadMethodAsync("textDocument/hover");
        var secondRequest = await _server.ReadMethodAsync("textDocument/hover");

        // The server answers the second request first.
        var secondUri = (string?)secondRequest["params"]!["textDocument"]!["uri"];
        var firstUri = (string?)firstRequest["params"]!["textDocument"]!["uri"];
        await _server.RespondAsync(secondRequest, CreateHoverResult(secondUri));
        await _server.RespondAsync(firstRequest, CreateHoverResult(firstUri));
        var mainAnswer = await ReadPageAsync(mainPage);
        var otherAnswer = await ReadPageAsync(otherPage);

        ((string?)mainAnswer["result"]!["contents"]).Should().Be(MainUri);
        ((string?)otherAnswer["result"]!["contents"]).Should().Be(OtherUri);
    }

    [TestCase("workspace/executeCommand")]
    [TestCase("textDocument/diagnostic")]
    [TestCase("textDocument/didClose")]
    [TestCase("shutdown")]
    public async Task Request_AMethodOutsideTheDocumentsFeatures_IsRefusedAndNeverReachesTheServer(string method)
    {
        var session = await StartReadySessionAsync();
        var page = await OpenPageAsync(session, MainUri);

        var request = new JsonObject
        {
            ["id"] = 5,
            ["method"] = method,
            ["params"] = new JsonObject()
        };
        page.Receive(request.ToJsonString());
        var answer = await ReadPageAsync(page);

        // The hover request is the next message that the server gets.
        page.Receive("""{"id":6,"method":"textDocument/hover","params":{"position":{"line":0,"character":0}}}""");
        var nextMessage = await _server.ReadAsync();

        ((int)answer["id"]!).Should().Be(5);
        ((int)answer["error"]!["code"]!).Should().Be(-32601);
        ((string?)nextMessage["method"]).Should().Be("textDocument/hover");
    }

    [Test]
    public async Task DisposeThePage_ClosesTheDocumentAndCancelsThePagesRequests()
    {
        var session = await StartReadySessionAsync();
        var page = await OpenPageAsync(session, MainUri);
        page.Receive("""{"id":7,"method":"textDocument/references","params":{"position":{"line":0,"character":0}}}""");
        var request = await _server.ReadMethodAsync("textDocument/references");

        page.Dispose();
        var cancellation = await _server.ReadMethodAsync("$/cancelRequest");
        var didClose = await _server.ReadMethodAsync("textDocument/didClose");
        var lastMessage = await page.ReadNextAsync(CancellationToken.None);

        ((long)cancellation["params"]!["id"]!).Should().Be((long)request["id"]!);
        ((string?)didClose["params"]!["textDocument"]!["uri"]).Should().Be(MainUri);
        lastMessage.Should().BeNull();
    }

    [Test]
    public async Task ServerRequest_Configuration_ReturnsTheDefinitionsSettingsForEachSection()
    {
        var alphaSettings = new JsonObject
        {
            ["level"] = 1
        };
        _definition.CreateConfiguration("alpha").Returns(alphaSettings);
        _definition.CreateConfiguration("beta").Returns((JsonNode?)null);
        await StartReadySessionAsync();

        var alphaItem = new JsonObject
        {
            ["section"] = "alpha"
        };
        var betaItem = new JsonObject
        {
            ["section"] = "beta"
        };
        var parameters = new JsonObject
        {
            ["items"] = new JsonArray(alphaItem, betaItem)
        };
        var request = new JsonObject
        {
            ["id"] = "config-1",
            ["method"] = "workspace/configuration",
            ["params"] = parameters
        };
        await _server.SendAsync(request);
        var response = await _server.ReadAsync();

        ((string?)response["id"]).Should().Be("config-1");
        var result = response["result"]!.AsArray();
        ((int)result[0]!["level"]!).Should().Be(1);
        result[1].Should().BeNull();
    }

    [Test]
    public async Task ServerRequest_WorkspaceFolders_ReturnsTheProjectFolder()
    {
        await StartReadySessionAsync();

        await SendServerRequestAsync(50, "workspace/workspaceFolders");
        var response = await _server.ReadAsync();

        ((string?)response["result"]![0]!["uri"]).Should().Be(ProjectFolderUri);
    }

    [TestCase("client/registerCapability")]
    [TestCase("client/unregisterCapability")]
    [TestCase("window/workDoneProgress/create")]
    public async Task ServerRequest_ARequestTheClientAccepts_ReturnsSuccess(string method)
    {
        await StartReadySessionAsync();

        var request = new JsonObject
        {
            ["id"] = 51,
            ["method"] = method,
            ["params"] = new JsonObject()
        };
        await _server.SendAsync(request);
        var response = await _server.ReadAsync();

        ((int)response["id"]!).Should().Be(51);
        response.AsObject().ContainsKey("result").Should().BeTrue();
        response["error"].Should().BeNull();
    }

    [Test]
    public async Task ServerRequest_AnUnknownMethod_ReturnsMethodNotFound()
    {
        await StartReadySessionAsync();

        await SendServerRequestAsync(52, "workspace/somethingElse");
        var response = await _server.ReadAsync();

        ((int)response["error"]!["code"]!).Should().Be(-32601);
    }

    [Test]
    public async Task Diagnostics_AfterDidOpen_TheSessionAsksAndThePageGetsTheItems()
    {
        var session = await StartReadySessionAsync(pullsDiagnostics: true);
        var page = session.AddPage(MainUri, "typescript")!;
        await ReadPageAsync(page);

        page.Receive("""{"method":"textDocument/didOpen","params":{"text":"let a: string = 1;","version":1}}""");
        var diagnosticRequest = await _server.ReadMethodAsync("textDocument/diagnostic");
        await RespondWithDiagnosticsAsync(diagnosticRequest, "result-1", "Type 'number' is not assignable to type 'string'.");
        var published = await ReadPageAsync(page);

        ((string?)diagnosticRequest["params"]!["textDocument"]!["uri"]).Should().Be(MainUri);
        ((string?)published["method"]).Should().Be("textDocument/publishDiagnostics");
        ((string?)published["params"]!["diagnostics"]![0]!["message"]).Should().StartWith("Type 'number'");
    }

    [Test]
    public async Task Diagnostics_AChangeInOnePage_AsksForEveryOpenDocument()
    {
        var session = await StartReadySessionAsync(pullsDiagnostics: true);
        var mainPage = await OpenPageWithDiagnosticsAsync(session, MainUri, openDocumentCount: 1);
        var otherPage = await OpenPageWithDiagnosticsAsync(session, OtherUri, openDocumentCount: 2);

        mainPage.Receive("""{"method":"textDocument/didChange","params":{"version":2,"contentChanges":[{"text":"export const a = 1;"}]}}""");
        var firstRequest = await _server.ReadMethodAsync("textDocument/diagnostic");
        var secondRequest = await _server.ReadMethodAsync("textDocument/diagnostic");

        var requestedUris = new List<string?>
        {
            (string?)firstRequest["params"]!["textDocument"]!["uri"],
            (string?)secondRequest["params"]!["textDocument"]!["uri"],
        };
        requestedUris.Should().BeEquivalentTo(MainUri, OtherUri);
        otherPage.Should().NotBeNull();
    }

    [Test]
    public async Task Diagnostics_ARefreshRequest_StartsARound()
    {
        var session = await StartReadySessionAsync(pullsDiagnostics: true);
        await OpenPageWithDiagnosticsAsync(session, MainUri, openDocumentCount: 1);

        await SendServerRequestAsync(60, "workspace/diagnostic/refresh");
        var response = await _server.ReadAsync();
        var diagnosticRequest = await _server.ReadAsync();

        ((int)response["id"]!).Should().Be(60);
        ((string?)diagnosticRequest["method"]).Should().Be("textDocument/diagnostic");
        ((string?)diagnosticRequest["params"]!["previousResultId"]).Should().Be("result-1");
    }

    [Test]
    public async Task Diagnostics_ChangesDuringARound_StartOneMoreRoundWhenTheRoundEnds()
    {
        var session = await StartReadySessionAsync(pullsDiagnostics: true);
        var page = await OpenPageWithDiagnosticsAsync(session, MainUri, openDocumentCount: 1);

        page.Receive("""{"method":"textDocument/didChange","params":{"version":2,"contentChanges":[{"text":"a"}]}}""");
        var firstRequest = await _server.ReadMethodAsync("textDocument/diagnostic");
        page.Receive("""{"method":"textDocument/didChange","params":{"version":3,"contentChanges":[{"text":"b"}]}}""");
        page.Receive("""{"method":"textDocument/didChange","params":{"version":4,"contentChanges":[{"text":"c"}]}}""");
        await _server.ReadMethodAsync("textDocument/didChange");
        await _server.ReadMethodAsync("textDocument/didChange");

        // Only the answer to the running round starts the next round.
        await RespondWithDiagnosticsAsync(firstRequest, "result-2", "first");
        var secondRequest = await _server.ReadAsync();
        await RespondWithDiagnosticsAsync(secondRequest, "result-3", "second");
        page.Receive("""{"id":1,"method":"textDocument/hover","params":{"position":{"line":0,"character":0}}}""");
        var nextMessage = await _server.ReadAsync();

        ((string?)secondRequest["method"]).Should().Be("textDocument/diagnostic");
        ((string?)nextMessage["method"]).Should().Be("textDocument/hover", "two changes during a round start one round, not two");
    }

    [Test]
    public async Task Diagnostics_AnUnchangedResult_SendsThePageNothing()
    {
        var session = await StartReadySessionAsync(pullsDiagnostics: true);
        var page = await OpenPageWithDiagnosticsAsync(session, MainUri, openDocumentCount: 1);

        await SendServerRequestAsync(61, "workspace/diagnostic/refresh");
        var diagnosticRequest = await _server.ReadMethodAsync("textDocument/diagnostic");
        var unchangedResult = new JsonObject
        {
            ["kind"] = "unchanged",
            ["resultId"] = "result-1"
        };
        await _server.RespondAsync(diagnosticRequest, unchangedResult);

        // The hover answer is the next message that the page gets.
        page.Receive("""{"id":1,"method":"textDocument/hover","params":{"position":{"line":0,"character":0}}}""");
        var hoverRequest = await _server.ReadMethodAsync("textDocument/hover");
        await _server.RespondAsync(hoverRequest, null);
        var nextPageMessage = await ReadPageAsync(page);

        ((int)nextPageMessage["id"]!).Should().Be(1);
    }

    [Test]
    public async Task Diagnostics_ACancelledRequest_IsSentAgain()
    {
        var session = await StartReadySessionAsync(pullsDiagnostics: true);
        await OpenPageWithDiagnosticsAsync(session, MainUri, openDocumentCount: 1);

        await SendServerRequestAsync(62, "workspace/diagnostic/refresh");
        var diagnosticRequest = await _server.ReadMethodAsync("textDocument/diagnostic");
        await RespondWithCancellationAsync(diagnosticRequest, retriggerRequest: true);
        var nextMessage = await _server.ReadAsync();

        ((string?)nextMessage["method"]).Should().Be("textDocument/diagnostic");
    }

    [Test]
    public async Task Diagnostics_ACancelledRequestThatForbidsARetry_IsNotSentAgain()
    {
        var session = await StartReadySessionAsync(pullsDiagnostics: true);
        await OpenPageWithDiagnosticsAsync(session, MainUri, openDocumentCount: 1);

        await SendServerRequestAsync(63, "workspace/diagnostic/refresh");
        var diagnosticRequest = await _server.ReadMethodAsync("textDocument/diagnostic");
        await RespondWithCancellationAsync(diagnosticRequest, retriggerRequest: false);

        // The session reads the server's messages in order. A retry would reach the server before this answer.
        await SendServerRequestAsync(64, "workspace/workspaceFolders");
        var nextMessage = await _server.ReadAsync();

        ((int)nextMessage["id"]!).Should().Be(64);
        nextMessage["method"].Should().BeNull();
    }

    [Test]
    public async Task PushedDiagnostics_ReachThePageOfTheNamedDocument()
    {
        var session = await StartReadySessionAsync();
        var page = await OpenPageAsync(session, MainUri);

        // A server may spell a Windows drive differently from the page, for example D: for d%3A. A Unix path has no
        // drive.
        var serverUri = OperatingSystem.IsWindows()
            ? "file:///D:/Projects/Acme/src/main.ts"
            : MainUri;
        var diagnostic = new JsonObject
        {
            ["message"] = "pushed"
        };
        var parameters = new JsonObject
        {
            ["uri"] = serverUri,
            ["diagnostics"] = new JsonArray(diagnostic)
        };
        var notification = new JsonObject
        {
            ["method"] = "textDocument/publishDiagnostics",
            ["params"] = parameters
        };
        await _server.SendAsync(notification);
        var published = await ReadPageAsync(page);

        ((string?)published["method"]).Should().Be("textDocument/publishDiagnostics");
        ((string?)published["params"]!["diagnostics"]![0]!["message"]).Should().Be("pushed");
    }

    [Test]
    public async Task TheServerEndsUnexpectedly_ThePagesConnectionsCloseAndTheSessionReportsACrash()
    {
        var session = await StartReadySessionAsync();
        var page = await OpenPageAsync(session, MainUri);

        _server.Exit();
        var lastMessage = await page.ReadNextAsync(CancellationToken.None);
        var crashed = await _ended.Task.WaitAsync(ReadTimeout);
        var laterPage = session.AddPage(OtherUri, "typescript");

        lastMessage.Should().BeNull();
        crashed.Should().BeTrue();
        _server.WasDisposed.Should().BeTrue();
        laterPage.Should().BeNull();
    }

    [Test]
    public async Task Stop_SendsShutdownAndExit_AndTheSessionEndsWithoutACrash()
    {
        var session = await StartReadySessionAsync();
        var page = await OpenPageAsync(session, MainUri);

        var stopTask = session.StopAsync();
        var shutdownRequest = await _server.ReadMethodAsync("shutdown");
        await _server.RespondAsync(shutdownRequest, null);
        await _server.ReadMethodAsync("exit");
        _server.Exit();
        await stopTask.WaitAsync(ReadTimeout);
        var crashed = await _ended.Task.WaitAsync(ReadTimeout);
        var lastMessage = await page.ReadNextAsync(CancellationToken.None);

        crashed.Should().BeFalse();
        _server.WasKilled.Should().BeFalse();
        lastMessage.Should().BeNull();
    }

    [Test]
    public async Task Stop_AServerThatStaysRunning_IsKilledAfterTwoSeconds()
    {
        var session = await StartReadySessionAsync();

        var stopTask = session.StopAsync();
        await _server.ReadMethodAsync("shutdown");
        _server.WasKilled.Should().BeFalse();
        _timeProvider.Advance(TimeSpan.FromSeconds(2));
        await stopTask.WaitAsync(ReadTimeout);

        _server.WasKilled.Should().BeTrue();
        (await _ended.Task.WaitAsync(ReadTimeout)).Should().BeFalse();
    }

    [Test]
    public async Task PageEdit_ReachesTheWorkspaceAsRangeEditsForEachFile_AndThePageGetsTheResult()
    {
        var session = await StartReadySessionAsync();
        var page = await OpenPageAsync(session, MainUri);
        await NameFileToPageAsync(page, OtherUri);

        page.Receive(CreatePageEditRequest(9, CreateChanges(MainUri, OtherUri)));
        var edits = await _workspace.WaitForEditAsync();
        var answer = await ReadPageAsync(page);

        edits.Select(edit => edit.FilePath).Should().Equal(LspUri.ToPath(MainUri), LspUri.ToPath(OtherUri));
        edits[1].Edits.Should().Equal(new RangeEdit(4, 5, 4, 11, "size"));
        ((int)answer["id"]!).Should().Be(9);
        ((bool)answer["result"]!["applied"]!).Should().BeTrue();
    }

    [Test]
    public async Task PageEdit_AFileThatNoAnswerNamed_IsRefused()
    {
        var session = await StartReadySessionAsync();
        var page = await OpenPageAsync(session, MainUri);

        page.Receive(CreatePageEditRequest(9, CreateChanges(OtherUri)));
        var answer = await ReadPageAsync(page);

        ((bool)answer["result"]!["applied"]!).Should().BeFalse();
        ((string?)answer["result"]!["failureReason"]).Should().Contain("has not named");
        _workspace.WaitForEditAsync().IsCompleted.Should().BeFalse();
    }

    [Test]
    public async Task PageEdit_AVersionThatDiffersFromThePagesLastVersion_IsRefused()
    {
        var session = await StartReadySessionAsync();
        var page = await OpenPageAsync(session, MainUri);
        page.Receive("""{"method":"textDocument/didChange","params":{"version":5,"contentChanges":[{"text":"let b = 2;"}]}}""");
        await _server.ReadMethodAsync("textDocument/didChange");

        page.Receive(CreatePageEditRequest(9, CreateDocumentChanges(MainUri, 4)));
        var refused = await ReadPageAsync(page);
        page.Receive(CreatePageEditRequest(10, CreateDocumentChanges(MainUri, 5)));
        var applied = await ReadPageAsync(page);

        ((bool)refused["result"]!["applied"]!).Should().BeFalse();
        ((string?)refused["result"]!["failureReason"]).Should().Contain("changed");
        ((bool)applied["result"]!["applied"]!).Should().BeTrue();
    }

    [Test]
    public async Task PageEdit_TheWorkspaceFails_ThePageGetsTheReason()
    {
        _workspace.ApplyResult = Result.Fail("The editor refused the edits");
        var session = await StartReadySessionAsync();
        var page = await OpenPageAsync(session, MainUri);

        page.Receive(CreatePageEditRequest(9, CreateChanges(MainUri)));
        var answer = await ReadPageAsync(page);

        ((bool)answer["result"]!["applied"]!).Should().BeFalse();
        ((string?)answer["result"]!["failureReason"]).Should().Be("The editor refused the edits");
    }

    [Test]
    public async Task ServerApplyEdit_ReachesTheWorkspace_AndTheServerGetsTheResult()
    {
        await StartReadySessionAsync();

        var parameters = new JsonObject
        {
            ["edit"] = CreateChanges(OtherUri)
        };
        var request = new JsonObject
        {
            ["id"] = 30,
            ["method"] = "workspace/applyEdit",
            ["params"] = parameters
        };
        await _server.SendAsync(request);
        var edits = await _workspace.WaitForEditAsync();
        var response = await _server.ReadAsync();

        edits.Single().FilePath.Should().Be(LspUri.ToPath(OtherUri));
        ((int)response["id"]!).Should().Be(30);
        ((bool)response["result"]!["applied"]!).Should().BeTrue();
    }

    [Test]
    public async Task FileText_ANamedFile_ReturnsTheText_AndAnUnchangedStampReturnsNoText()
    {
        _workspace.Files[LspUri.ToPath(OtherUri)] = ("stamp-1", "export const other = 1;");
        var session = await StartReadySessionAsync();
        var page = await OpenPageAsync(session, MainUri);
        await NameFileToPageAsync(page, OtherUri);

        page.Receive($$$"""{"id":11,"method":"celbridge/fileText","params":{"uri":"{{{OtherUri}}}"}}""");
        var first = await ReadPageAsync(page);
        page.Receive($$$"""{"id":12,"method":"celbridge/fileText","params":{"uri":"{{{OtherUri}}}","stamp":"stamp-1"}}""");
        var second = await ReadPageAsync(page);

        ((string?)first["result"]!["text"]).Should().Be("export const other = 1;");
        ((string?)first["result"]!["stamp"]).Should().Be("stamp-1");
        second["result"]!["text"].Should().BeNull();
        _workspace.ReadCount.Should().Be(1);
    }

    [Test]
    public async Task FileText_AFileThatNoAnswerNamed_IsRefused()
    {
        _workspace.Files[LspUri.ToPath(OtherUri)] = ("stamp-1", "export const other = 1;");
        var session = await StartReadySessionAsync();
        var page = await OpenPageAsync(session, MainUri);

        page.Receive($$$"""{"id":11,"method":"celbridge/fileText","params":{"uri":"{{{OtherUri}}}"}}""");
        var answer = await ReadPageAsync(page);

        ((int)answer["error"]!["code"]!).Should().Be(-32803);
        _workspace.ReadCount.Should().Be(0);
    }

    [Test]
    public async Task ShowDocument_ANamedFile_OpensTheDocumentAtTheSelection()
    {
        var session = await StartReadySessionAsync();
        var page = await OpenPageAsync(session, MainUri);
        await NameFileToPageAsync(page, OtherUri);

        page.Receive(CreateShowDocumentRequest(13, OtherUri));
        var shown = await _workspace.WaitForShownDocumentAsync();
        var answer = await ReadPageAsync(page);

        shown.FilePath.Should().Be(LspUri.ToPath(OtherUri));
        shown.Selection.Should().Be(new DocumentRange(10, 17, 10, 21));
        ((bool)answer["result"]!["success"]!).Should().BeTrue();
    }

    private LanguageServerSession CreateSession()
    {
        return new LanguageServerSession(
            _definition,
            _server,
            _workspace,
            ProjectFolderPath,
            "1.8.0",
            new NullLogger<LanguageServerSession>(),
            _timeProvider,
            (_, crashed) => _ended.TrySetResult(crashed));
    }

    private async Task<LanguageServerSession> StartReadySessionAsync(bool pullsDiagnostics = false)
    {
        var capabilities = new JsonObject();
        if (pullsDiagnostics)
        {
            capabilities["diagnosticProvider"] = new JsonObject
            {
                ["interFileDependencies"] = true
            };
        }

        var session = CreateSession();
        session.Start();
        await _server.CompleteInitializeAsync(capabilities);

        return session;
    }

    // Connects a page, and opens its document on a server without pulled diagnostics.
    private async Task<ILanguagePageConnection> OpenPageAsync(LanguageServerSession session, string documentUri)
    {
        var page = session.AddPage(documentUri, "typescript")!;
        await ReadPageAsync(page);

        page.Receive("""{"method":"textDocument/didOpen","params":{"text":"","version":1}}""");
        await _server.ReadMethodAsync("textDocument/didOpen");

        return page;
    }

    // Connects a page, opens its document, and answers the round of diagnostics requests that the open starts.
    private async Task<ILanguagePageConnection> OpenPageWithDiagnosticsAsync(
        LanguageServerSession session,
        string documentUri,
        int openDocumentCount)
    {
        var page = await OpenPageAsync(session, documentUri);

        for (var index = 0; index < openDocumentCount; index++)
        {
            var diagnosticRequest = await _server.ReadMethodAsync("textDocument/diagnostic");
            await RespondWithDiagnosticsAsync(diagnosticRequest, "result-1", "opened");
        }

        var published = await ReadPageAsync(page);
        ((string?)published["method"]).Should().Be("textDocument/publishDiagnostics");

        return page;
    }

    private Task RespondWithDiagnosticsAsync(JsonObject diagnosticRequest, string resultId, string message)
    {
        var item = new JsonObject
        {
            ["message"] = message
        };
        var result = new JsonObject
        {
            ["kind"] = "full",
            ["resultId"] = resultId,
            ["items"] = new JsonArray(item)
        };

        return _server.RespondAsync(diagnosticRequest, result);
    }

    // Answers a diagnostics request with the error a server sends when it cancels the request.
    private Task RespondWithCancellationAsync(JsonObject diagnosticRequest, bool retriggerRequest)
    {
        var data = new JsonObject
        {
            ["retriggerRequest"] = retriggerRequest
        };
        var error = new JsonObject
        {
            ["code"] = -32802,
            ["message"] = "Server cancelled",
            ["data"] = data
        };
        var response = new JsonObject
        {
            ["id"] = diagnosticRequest["id"]!.DeepClone(),
            ["error"] = error
        };

        return _server.SendAsync(response);
    }

    // Sends a request without parameters from the server.
    private Task SendServerRequestAsync(int requestId, string method)
    {
        var request = new JsonObject
        {
            ["id"] = requestId,
            ["method"] = method
        };

        return _server.SendAsync(request);
    }

    // Makes the server's answer to a page's request name a file, so the page may then ask about the file.
    private async Task NameFileToPageAsync(ILanguagePageConnection page, string fileUri)
    {
        page.Receive("""{"id":"names","method":"textDocument/references","params":{"position":{"line":0,"character":0}}}""");
        var request = await _server.ReadMethodAsync("textDocument/references");
        var range = CreateRange(3, 4, 3, 10);
        var location = new JsonObject
        {
            ["uri"] = fileUri,
            ["range"] = range
        };
        await _server.RespondAsync(request, new JsonArray(location));
        await ReadPageAsync(page);
    }

    // A request to open a file with the name on line 10 selected.
    private static string CreateShowDocumentRequest(int requestId, string fileUri)
    {
        var parameters = new JsonObject
        {
            ["uri"] = fileUri,
            ["takeFocus"] = true,
            ["selection"] = CreateRange(9, 16, 9, 20)
        };
        var request = new JsonObject
        {
            ["id"] = requestId,
            ["method"] = "window/showDocument",
            ["params"] = parameters
        };

        return request.ToJsonString();
    }

    private static string CreatePageEditRequest(int requestId, JsonObject edit)
    {
        var parameters = new JsonObject
        {
            ["edit"] = edit
        };
        var request = new JsonObject
        {
            ["id"] = requestId,
            ["method"] = "workspace/applyEdit",
            ["params"] = parameters
        };

        return request.ToJsonString();
    }

    // A workspace edit that renames a name on line 4 of each file.
    private static JsonObject CreateChanges(params string[] fileUris)
    {
        var changes = new JsonObject();
        foreach (var fileUri in fileUris)
        {
            changes[fileUri] = new JsonArray(CreateTextEdit());
        }

        return new JsonObject
        {
            ["changes"] = changes
        };
    }

    // A workspace edit that names the document's version.
    private static JsonObject CreateDocumentChanges(string fileUri, int version)
    {
        var textDocument = new JsonObject
        {
            ["uri"] = fileUri,
            ["version"] = version
        };
        var documentEdit = new JsonObject
        {
            ["textDocument"] = textDocument,
            ["edits"] = new JsonArray(CreateTextEdit())
        };

        return new JsonObject
        {
            ["documentChanges"] = new JsonArray(documentEdit)
        };
    }

    private static JsonObject CreateTextEdit()
    {
        return new JsonObject
        {
            ["range"] = CreateRange(3, 4, 3, 10),
            ["newText"] = "size"
        };
    }

    private static JsonObject CreateRange(int line, int character, int endLine, int endCharacter)
    {
        var start = new JsonObject
        {
            ["line"] = line,
            ["character"] = character
        };
        var end = new JsonObject
        {
            ["line"] = endLine,
            ["character"] = endCharacter
        };

        return new JsonObject
        {
            ["start"] = start,
            ["end"] = end
        };
    }

    private static JsonObject CreateHoverResult(string? contents)
    {
        return new JsonObject
        {
            ["contents"] = contents
        };
    }

    private static async Task<JsonObject> ReadPageAsync(ILanguagePageConnection page)
    {
        using var timeoutSource = new CancellationTokenSource(ReadTimeout);
        var message = await page.ReadNextAsync(timeoutSource.Token);
        message.Should().NotBeNull("the page should get another message");

        return JsonNode.Parse(message!)!.AsObject();
    }
}
