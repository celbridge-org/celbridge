"""Shared test helpers.

These take the celbridge module objects as parameters because the new layout
has no module-level globals.
"""
import base64


def delete_if_exists(explorer, resource):
    """Delete a resource, ignoring errors if it does not exist."""
    try:
        explorer.delete(resource)
    except Exception:
        pass


def close_if_open(document, resource):
    """Close a document if it is currently open."""
    try:
        ctx = document.get_state()
        if any(d["resource"] == resource for d in ctx.get("openDocuments", [])):
            document.close(resource, force_close=True)
    except Exception:
        pass


def write_with_line_endings(file, resource, text_with_lf, line_ending):
    """Write a file with explicit line endings, bypassing file.write's
    platform-default conversion. Used by line-ending preservation tests
    to set up a file with known endings regardless of host OS.
    """
    text = text_with_lf.replace("\n", line_ending)
    encoded = base64.b64encode(text.encode("utf-8")).decode("ascii")
    file.write_binary(resource, encoded)
