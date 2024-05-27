using Celbridge.BaseLibrary.Project;
using Celbridge.Services.ProjectData;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Uno.Disposables;

namespace Celbridge.Tests;

[TestFixture]
public class ProjectTests
{
    [SetUp]
    public void Setup()
    {}

    [TearDown]
    public void TearDown()
    {}

    [Test]
    public void ICanCreateAndLoadAProject()
    {
        var folder = System.IO.Path.GetTempPath();
        var projectName = "TestProject.celbridge";
        var projectPath = System.IO.Path.Combine(folder, projectName);

        var createResult = ProjectData.CreateProjectData(projectPath, new ProjectConfig(Version: 1));
        createResult.IsSuccess.Should().BeTrue();

        var project = createResult.Value;
        project.Config.Version.Should().Be(1);

        project.TryDispose();

        File.Delete(projectPath);
        File.Exists(projectPath).Should().BeFalse();
    }
}
