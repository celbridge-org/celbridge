"""Entry point for the Celbridge Python connector.

Usage: python -m celbridge
       celbridge          (when installed as a tool via uv)

The RPC port is read from the CELBRIDGE_RPC_PORT environment variable,
which is set by the Celbridge application when launching the terminal.
"""

import os

from celbridge.logging_config import configure_logging
from celbridge.rpc_client import RpcClient
from celbridge.cel_proxy import CelProxy
from celbridge.repl_setup import setup_repl, POST_STARTUP_LINE


def _resolve_rpc_port() -> int:
    """Resolve the RPC port from the CELBRIDGE_RPC_PORT environment variable."""
    port_string = os.environ.get('CELBRIDGE_RPC_PORT')
    if port_string is None:
        raise SystemExit(
            "Error: CELBRIDGE_RPC_PORT environment variable is not set.\n"
            "The Celbridge application sets this variable when launching a terminal."
        )
    try:
        return int(port_string)
    except ValueError:
        raise SystemExit(f"Error: CELBRIDGE_RPC_PORT has invalid value: '{port_string}'")


def _report_installed_packages(client: RpcClient) -> None:
    """Send the list of installed Python packages to the C# host."""
    try:
        from importlib.metadata import distributions
        packages = sorted(
            f"{dist.metadata['Name']}=={dist.metadata['Version']}"
            for dist in distributions()
        )
        client.notify("PythonReady", packages=packages)
    except Exception:
        pass


def main():
    """Connect to the Celbridge application and launch an interactive REPL."""

    port = _resolve_rpc_port()

    # Configure logging first
    configure_logging()

    mcp_tools_enabled = os.environ.get('CELBRIDGE_MCP_TOOLS') == '1'

    # Always connect to the Celbridge application RPC server. The connection
    # signals to the host that the Python terminal is ready, which enables
    # features like the Run context menu command for .py files.
    client = RpcClient('127.0.0.1', port)

    # Report installed packages to the C# host so MCP tools can include
    # environment info in the Python API reference.
    _report_installed_packages(client)

    cel = CelProxy(client)

    # Only expose cel in the REPL namespace when MCP tools are enabled.
    user_namespace = {}
    if mcp_tools_enabled:
        # Make cel and its namespaces importable for use in scripts.
        # e.g. "from celbridge import cel" or "from celbridge import resource"
        import celbridge
        celbridge.cel = cel
        for namespace_name in cel._get_namespace_names():
            setattr(celbridge, namespace_name, getattr(cel, namespace_name))

        user_namespace['cel'] = cel
        for namespace_name in cel._get_namespace_names():
            user_namespace[namespace_name] = getattr(cel, namespace_name)

    # Set up the REPL environment (banner, python path)
    setup_repl(mcp_tools_enabled)

    # Get IPython folder from environment variable (set by the host application)
    ipython_folder = os.environ.get('CELBRIDGE_IPYTHON_DIR', '')

    # Build IPython arguments
    ipython_args = ['--no-banner']
    if ipython_folder:
        ipython_args.extend(['--ipython-dir', ipython_folder])

    # Launch IPython with the cel proxy injected into the user namespace.
    # exec_lines runs after IPython is fully initialized, so customizations
    # that need get_ipython() (prompts, exit hooks, caching) work correctly.
    from traitlets.config import Config
    ipython_config = Config()
    ipython_config.InteractiveShellApp.exec_lines = [POST_STARTUP_LINE]

    import IPython
    IPython.start_ipython(
        argv=ipython_args,
        user_ns=user_namespace,
        config=ipython_config,
    )


if __name__ == "__main__":
    main()
