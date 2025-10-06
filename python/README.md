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

## Usage

```bash
# CLI
celbridge version --format json

# Python host (programmatic access)
python -c "from celbridge_host import cel; print(cel.version(format='json'))"
```

## License

MIT
