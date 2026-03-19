import json
import typer
from celbridge import __version__


def version_command():
    """Get version information for the celbridge python package."""
    output = {
        "version": __version__,
        "api": "1.0",
    }
    typer.echo(json.dumps(output))
