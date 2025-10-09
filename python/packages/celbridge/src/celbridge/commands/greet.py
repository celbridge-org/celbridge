"""Greet command for testing parameter validation."""

import json
import typer


def greet_command(name: str, greeting: str = "Hello"):
    """Greet someone with a custom message."""
    output = {
        "message": f"{greeting}, {name}!",
        "name": name,
        "greeting": greeting,
    }
    typer.echo(json.dumps(output))
