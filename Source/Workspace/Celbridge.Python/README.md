# Celbridge.Python

Celbridge workspace project that manages the Python connector. Builds the `celbridge` Python wheel and bundles it as an asset for installation at runtime.

## Architecture

The Celbridge .NET application starts a TCP JSON-RPC server and launches a terminal process with the `CELBRIDGE_RPC_PORT` environment variable set. The Python connector reads this variable, connects to the server, and launches an IPython REPL with the `cel` proxy injected.

The `celbridge-py` command is installed as a uv tool, so users can type `celbridge-py` in the terminal to start a new REPL session after exiting. It is installed once per application, alongside uv and the wheel, because the environment it lives in only runs the bootstrap shim: the interpreter and packages the REPL imports come from an inner `uv run` against the open project's own cache.

## Package

The Python source lives in `packages/celbridge/`. It has a single runtime dependency (`ipython`) and uses only the Python standard library for JSON-RPC communication. The `cel` proxy discovers available tools from the MCP server via the McpToolBridge and generates proxy methods dynamically.

## Running Tests

Create a virtual environment and install the package with test dependencies. Create it at the repo root, never inside `Source/`: the Uno SDK's item globs and the architecture tests both walk every folder under a project, and neither can be told to skip a venv sitting there.

```bash
python -m venv .venv          # from the repo root
.venv\Scripts\activate        # Windows
# source .venv/bin/activate   # Linux/macOS
cd Source/Workspace/Celbridge.Python
pip install -e "packages/celbridge[dev]"
```

The venv is for running these tests only. `build.py` takes its interpreter from uv and never looks for a venv, so where the venv lives has no bearing on the wheel build.

Run all tests:

```bash
python run_tests.py
```

## Building the Wheel

The celbridge Python wheel is built automatically by MSBuild when the Celbridge.Python project is built. The MSBuild target detects changes to Python source files and rebuilds the wheel as needed. The wheel is not committed: it is built through the uv the project downloads, so building it needs no Python on the machine.

To build the wheel manually, with a uv on PATH:

```bash
uv run --no-project --managed-python build.py
```

Rebuilding from unchanged sources produces the same bytes. Two things make that hold: `build.py` pins `SOURCE_DATE_EPOCH` so the archive timestamps settle, and `pyproject.toml` pins the setuptools version because setuptools stamps its own version into the wheel's metadata. That matters because the application's version marker carries the wheel's hash: without it, every build would look like changed code and reinstall the shared Python folder. If a rebuild ever does change the hash without a source change, one of those two has moved.
