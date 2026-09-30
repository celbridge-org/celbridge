using Celbridge.Python.Services;

namespace Celbridge.Tests.Console;

[TestFixture]
public class PythonEnvironmentFilterTests
{
    [TestCase("VIRTUAL_ENV")]
    [TestCase("CONDA_PREFIX")]
    [TestCase("UV")]
    [TestCase("UV_PYTHON")]
    [TestCase("UV_CACHE_DIR")]
    [TestCase("UV_PYTHON_PREFERENCE")]
    [TestCase("UV_A_SETTING_UV_ADDS_LATER")]
    [TestCase("PYTHONPATH")]
    [TestCase("PYTHONHOME")]
    public void Removes_VariablesThatSteerUvOrPython(string name)
    {
        PythonEnvironmentFilter.Removes(name).Should().BeTrue();
    }

    [TestCase("UV_INDEX_URL")]
    [TestCase("UV_INDEX_PRIVATE_PASSWORD")]
    [TestCase("UV_DEFAULT_INDEX")]
    [TestCase("UV_EXTRA_INDEX_URL")]
    [TestCase("UV_NATIVE_TLS")]
    [TestCase("UV_INSECURE_HOST")]
    [TestCase("UV_HTTP_TIMEOUT")]
    [TestCase("UV_KEYRING_PROVIDER")]
    [TestCase("UV_PYTHON_INSTALL_MIRROR")]
    public void Removes_KeepsUvIndexAndNetworkSettings(string name)
    {
        PythonEnvironmentFilter.Removes(name).Should().BeFalse();
    }

    [TestCase("PATH")]
    [TestCase("HOME")]
    [TestCase("HTTPS_PROXY")]
    [TestCase("UVICORN_PORT")]
    [TestCase("MY_PYTHONPATH")]
    public void Removes_KeepsEverythingElse(string name)
    {
        PythonEnvironmentFilter.Removes(name).Should().BeFalse();
    }

    [Test]
    public void Apply_RemovesTheMatchingVariablesAndNamesThem()
    {
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = "/usr/bin",
            ["VIRTUAL_ENV"] = "/work/.venv",
            ["UV_PYTHON"] = "/work/.venv/bin/python",
            ["UV_INDEX_URL"] = "https://mirror.example/simple",
        };

        var removedNames = PythonEnvironmentFilter.Apply(environment);

        removedNames.Should().Equal("UV_PYTHON", "VIRTUAL_ENV");
        environment.Keys.Should().BeEquivalentTo("PATH", "UV_INDEX_URL");
    }
}
