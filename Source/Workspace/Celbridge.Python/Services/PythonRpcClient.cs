namespace Celbridge.Python.Services;

public class PythonRpcClient : IPythonRpcClient
{
    private readonly IRpcService _rpcService;

    public PythonRpcClient(
        IRpcService rpcService)
    {
        _rpcService = rpcService;
    }

    public async Task<Result<string>> GetVersionAsync()
    {
        return await _rpcService.InvokeAsync<string>("get_version");
    }

    public async Task<Result<SystemInfo>> GetSystemInfoAsync()
    {
        return await _rpcService.InvokeAsync<SystemInfo>("get_system_info");
    }
}
