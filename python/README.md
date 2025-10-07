# Celbridge Python Packages

Python components for the Celbridge project management system.

## Packages

- **`celbridge`** - CLI for managing Celbridge projects
- **`celbridge_host`** - Python host for .NET integration (REPL, JSON-RPC, MCP)

## Development Setup

```bash
# Install uv (recommended) or use pip
pip install uv

# Create virtual environment and install packages
uv venv
source .venv/bin/activate  # On Windows: .\.venv\Scripts\Activate.ps1
uv pip install -e packages/celbridge[dev] -e packages/celbridge_host[dev]

# Verify
celbridge version
pytest packages/ -v
```

## Building for .NET Integration

The `build.py` script builds the python packages as wheel files and copies them to the Celbridge.Python Assets folder:

```bash
python build.py
```
