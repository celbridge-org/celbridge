"""Entry point for the Celbridge Python connector.

Usage: python -m celbridge
       celbridge-py       (when installed as a tool via uv)

A Celbridge python console seeds its configured launch into the environment (CELBRIDGE_PYTHON_*),
which this command re-execs through uv to honour, so retyping celbridge-py in that console
reproduces it. With none of that set the REPL starts directly from the installed tool venv. Any
arguments are forwarded to IPython, and the RPC port is read from CELBRIDGE_RPC_PORT.
"""

import logging
import os
import sys
from typing import NamedTuple

from celbridge.cel_proxy import CelProxy
from celbridge.repl_setup import POST_STARTUP_LINE, setup_repl
from celbridge.rpc_client import RpcClient

# Set on the re-exec'd process so the inner python -m celbridge never re-bootstraps, then cleared
# once consumed so terminals spawned from the REPL can bootstrap again.
BOOTSTRAP_MARKER = 'CELBRIDGE_BOOTSTRAPPED'

# Private OSC identifier carrying a diagnostic for the host application log, alongside the console's
# ready marker on 7000. A terminal that never sees it renders nothing, and the host lifts it out of the
# output stream before the user's terminal does. ConPTY forwards an OSC as it parses it rather than when
# it paints the surrounding text, so the sequence carries no position and must not be used to mark one.
DIAGNOSTIC_OSC_CODE = '7001'


class ResolvedLaunch(NamedTuple):
    """The launch a python console configured, plus the arguments to forward to IPython."""
    python_version: str | None
    with_packages: list[str]
    offline: bool
    ipython_arguments: list[str]

    @property
    def requires_bootstrap(self) -> bool:
        return bool(self.python_version or self.with_packages or self.offline)


def _build_exec_lines(startup_script):
    """Build IPython's exec_lines: the REPL customizations, then the console's startup script.

    The script is one entry rather than one per line, so IPython runs it as a single cell and multi-line
    constructs work. It runs here rather than being typed at the prompt because the REPL discards pending
    terminal input as it starts.
    """
    exec_lines = [POST_STARTUP_LINE]
    if startup_script and startup_script.strip():
        exec_lines.append(startup_script)

    return exec_lines


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


def _split_lines(value):
    """Split a newline-separated environment value into a list, dropping blank lines."""
    if not value:
        return []

    return [line.strip() for line in value.splitlines() if line.strip()]


def _resolve_launch(environ, arguments) -> ResolvedLaunch:
    """Read the launch a python console configured into its environment.

    A console configures the interpreter version and packages, never IPython flags: those are an
    implementation detail of the REPL rather than part of a console's configuration. Arguments therefore
    come only from the command line, where they are typed deliberately by whoever runs the tool.
    """
    return ResolvedLaunch(
        environ.get('CELBRIDGE_PYTHON_VERSION'),
        _split_lines(environ.get('CELBRIDGE_PYTHON_WITH')),
        environ.get('CELBRIDGE_PYTHON_OFFLINE') == '1',
        list(arguments),
    )


def _build_uv_run_command(resolved: ResolvedLaunch, environ, payload):
    """Build the uv run command that runs payload in the launch's environment."""
    uv_path = environ.get('CELBRIDGE_UV')
    wheel_path = environ.get('CELBRIDGE_WHEEL')
    if not uv_path or not wheel_path:
        raise SystemExit(
            "Error: launching the configured Python environment requires the Celbridge console "
            "environment (CELBRIDGE_UV and CELBRIDGE_WHEEL are not set)."
        )

    command = [uv_path, 'run']

    cache_dir = environ.get('CELBRIDGE_UV_CACHE_DIR')
    if cache_dir:
        command.extend(['--cache-dir', cache_dir])

    if resolved.offline:
        command.append('--offline')

    command.append('--no-project')

    if resolved.python_version:
        command.extend(['--python', resolved.python_version])

    command.extend(['--managed-python', '--with', wheel_path])

    for package in resolved.with_packages:
        command.extend(['--with', package])

    command.extend(payload)

    return command


def _build_bootstrap_command(resolved: ResolvedLaunch, environ):
    """Build the uv run command that relaunches the REPL with the resolved launch options."""
    payload = ['python', '-m', 'celbridge'] + list(resolved.ipython_arguments)

    return _build_uv_run_command(resolved, environ, payload)


def _build_probe_command(resolved: ResolvedLaunch, environ):
    """Build the cache probe: the launch command with --offline forced on and a no-op payload."""
    return _build_uv_run_command(resolved._replace(offline=True), environ, ['python', '-c', ''])


def _probe_offline_cache(resolved: ResolvedLaunch, environ):
    """Measure whether the launch resolves entirely from the uv cache.

    Returns whether it did, and how long the measurement took in milliseconds. Exit zero means every
    package resolved with no network access, so the real launch can bootstrap offline. uv caches the
    environment it builds here, so the launch that follows finds it already built. The probe's output is
    discarded: on a cache miss uv explains itself at length, and the launch simply goes online instead.
    """
    import subprocess
    import time

    command = _build_probe_command(resolved, environ)

    started = time.monotonic()
    try:
        completed = subprocess.run(
            command,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
        exit_code = completed.returncode
    except OSError:
        exit_code = -1
    duration_ms = int((time.monotonic() - started) * 1000)

    return exit_code == 0, duration_ms


def _emit_diagnostic(text):
    """Write a diagnostic for the host to lift into its application log."""
    sys.stdout.write(f'\x1b]{DIAGNOSTIC_OSC_CODE};{text}\x07')
    sys.stdout.flush()


def _bootstrap(resolved: ResolvedLaunch):
    """Re-exec through uv. Does not return."""

    # A launch that was not told the cache is warm measures it rather than predicting it. Nothing is
    # remembered between launches, so no launch can inherit a wrong answer from an earlier one.
    if not resolved.offline:
        offline, duration_ms = _probe_offline_cache(resolved, os.environ)
        mode = 'offline' if offline else 'online'
        _emit_diagnostic(f'python-probe mode={mode} ms={duration_ms}')
        resolved = resolved._replace(offline=offline)

    command = _build_bootstrap_command(resolved, os.environ)

    environment = dict(os.environ)
    environment[BOOTSTRAP_MARKER] = '1'

    if os.name == 'posix':
        os.execvpe(command[0], command, environment)

    # Windows has no true exec, so run the child and mirror its exit code. Ctrl+C reaches both
    # processes through the shared console; the wrapper ignores it and lets the REPL handle it.
    import signal
    import subprocess

    process = subprocess.Popen(command, env=environment)
    signal.signal(signal.SIGINT, signal.SIG_IGN)
    raise SystemExit(process.wait())


def main():
    """Connect to the Celbridge application and launch an interactive REPL."""

    # A bootstrapped (inner) run received its effective arguments on the command line, so it skips
    # option resolution entirely and must not re-bootstrap.
    ipython_forward_arguments = sys.argv[1:]
    if os.environ.pop(BOOTSTRAP_MARKER, None) != '1':
        resolved = _resolve_launch(os.environ, sys.argv[1:])
        if resolved.requires_bootstrap:
            _bootstrap(resolved)
        ipython_forward_arguments = resolved.ipython_arguments

    port = _resolve_rpc_port()

    mcp_tools_enabled = os.environ.get('CELBRIDGE_MCP_TOOLS') == '1'

    # Always connect to the Celbridge application RPC server. The connection
    # signals to the host that the Python terminal is ready.
    client = RpcClient('127.0.0.1', port)

    # Bind this connection to the console that launched it, so the host can attribute peer consoles to
    # their sessions. Every in-app console seeds the token, and a spawned terminal inherits it. A REPL
    # started outside any console has no token and skips the handshake.
    session_token = os.environ.get('CELBRIDGE_SESSION_TOKEN')
    if session_token:
        try:
            bound = client.call("session/handshake", sessionToken=session_token)
            if not bound:
                logging.getLogger(__name__).debug(
                    "session/handshake did not bind: token does not match an open console")
        except Exception:
            logging.getLogger(__name__).debug("Host did not handle session/handshake", exc_info=True)

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

    # Forward the interpreter arguments (explicit, or the console-provided environment defaults on
    # an un-bootstrapped launch) to IPython.
    ipython_args.extend(ipython_forward_arguments)

    # Launch IPython with the cel proxy injected into the user namespace.
    # exec_lines runs after IPython is fully initialized, so customizations
    # that need get_ipython() (prompts, exit hooks, caching) work correctly.
    from traitlets.config import Config
    ipython_config = Config()
    ipython_config.InteractiveShellApp.exec_lines = _build_exec_lines(
        os.environ.get('CELBRIDGE_PYTHON_STARTUP', ''))

    import IPython
    IPython.start_ipython(
        argv=ipython_args,
        user_ns=user_namespace,
        config=ipython_config,
    )


if __name__ == "__main__":
    main()
