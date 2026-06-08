"""Shared test helpers.

These take the celbridge module objects as parameters because the new layout
has no module-level globals.
"""
import base64
import os


def delete_if_exists(explorer, resource):
    """Delete a resource, ignoring errors if it does not exist."""
    try:
        explorer.delete(resource)
    except Exception:
        pass


def close_if_open(document, resource):
    """Close a document if it is currently open.

    Tool responses emit resource keys in canonical "root:path" form, so accept
    both the bare path the caller typed and the prefixed form the document
    state will report.
    """
    canonical = resource if ":" in resource else f"project:{resource}"
    try:
        ctx = document.get_state()
        if any(d["resource"] == canonical for d in ctx.get("openDocuments", [])):
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


def write_cel_file_directly(app, relative_path, content):
    """Write a .cel file to disk via raw filesystem access, bypassing the
    file.* tools' denial. The byte-write tools refuse .cel targets to protect
    sidecar structure; tests that need to fabricate adversarial sidecar
    states (orphans, broken TOML) write through the filesystem instead.
    Triggers app.refresh_files() so the resource registry picks the new file
    up before the caller queries it.
    """
    project_folder = os.environ.get("CELBRIDGE_PROJECT_FOLDER")
    if not project_folder:
        raise RuntimeError(
            "CELBRIDGE_PROJECT_FOLDER is not set; cannot write .cel directly."
        )
    full_path = os.path.join(project_folder, relative_path)
    os.makedirs(os.path.dirname(full_path), exist_ok=True)
    with open(full_path, "w", encoding="utf-8", newline="") as handle:
        handle.write(content)
    app.refresh_files()
