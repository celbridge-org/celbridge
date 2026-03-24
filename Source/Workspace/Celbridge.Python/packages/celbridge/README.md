# Celbridge Python Connector

Python connector for the [Celbridge](https://github.com/nicholasgasior/celbridge) application. Provides an interactive IPython REPL with a `cel` proxy object for calling application tools via JSON-RPC.

## Usage

The connector is installed automatically by the Celbridge application. When Celbridge opens a project, it launches a terminal with the `cel` proxy available:

```python
# Get help
help(cel)

# Use namespaced commands
cel.app.version()
cel.document.open("Project/readme.md")
cel.resource.move("Project/old.txt", "Project/new.txt")

# Namespace shortcuts are available directly in the REPL
resource.move("Project/old.txt", "Project/new.txt")
```

In scripts, import what you need:

```python
from celbridge import cel
cel.resource.move("Project/old.txt", "Project/new.txt")

# Or import namespaces directly
from celbridge import resource, document
resource.move("Project/old.txt", "Project/new.txt")
document.open("Project/readme.md")
```
