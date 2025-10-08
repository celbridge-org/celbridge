"""CLI commands for Celbridge.
"""

import json
import typer
from typing_extensions import Annotated
from celbridge import __version__


def version_command(
    format: Annotated[
        str,
        typer.Option(
            "--format",
            "-f",
            help="Output format: text or json",
        ),
    ] = "text",
):
    """Display version information."""
    if format == "json":
        output = {
            "version": __version__,
            "api": "1.0",
        }
        typer.echo(json.dumps(output))
    elif format == "text":
        typer.echo(f"celbridge {__version__}")
    else:
        typer.echo(f"Error: Unknown format '{format}'. Use 'text' or 'json'.", err=True)
        raise typer.Exit(code=1)
