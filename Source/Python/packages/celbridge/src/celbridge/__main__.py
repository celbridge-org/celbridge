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


def main():
    """Connect to the Celbridge application and launch an interactive REPL."""

    port = _resolve_rpc_port()

    # Configure logging first
    configure_logging()

    # Connect to the Celbridge application RPC server
    client = RpcClient('127.0.0.1', port)

    # Create the cel proxy that sends JSON-RPC calls to C#
    cel = CelProxy(client)

    # Set up the REPL environment (banner, python path)
    setup_repl()

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
        user_ns={'cel': cel},
        config=ipython_config,
    )


if __name__ == "__main__":
    main()
