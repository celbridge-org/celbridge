# Celbridge Python Connector

Minimal Python connector for the Celbridge application. Connects to the Celbridge .NET application via JSON-RPC over TCP and provides an interactive IPython REPL with a `cel` proxy object for calling application methods.

## Architecture

The Celbridge .NET application starts a TCP JSON-RPC server and launches a terminal process with the `CELBRIDGE_RPC_PORT` environment variable set. The Python connector reads this variable, connects to the server, and launches an IPython REPL with the `cel` proxy injected.

The `celbridge-py` command is also installed as a uv tool, so users can type `celbridge-py` in the terminal to start a new REPL session after exiting.

## Package

- **`celbridge`** - Python connector with a single dependency (`ipython`). Uses only the Python standard library for JSON-RPC communication. The `cel` proxy converts Python method calls to JSON-RPC requests sent to the Celbridge application.

## Running Tests

Create a virtual environment and install the package with test dependencies:

```bash
cd Source/Python
python -m venv .venv
.venv\Scripts\activate        # Windows
# source .venv/bin/activate   # Linux/macOS
pip install -e "packages/celbridge[dev]"
```

Run all tests:

```bash
python run_tests.py
```

## Building the Wheel

The celbridge Python wheel is built automatically by MSBuild when the Celbridge.Python project is built. The MSBuild target detects changes to Python source files and rebuilds the wheel as needed.

To build the wheel manually, use the `build.py` script which builds the package and copies it to the Celbridge.Python Assets folder:

```bash
python build.py
```
