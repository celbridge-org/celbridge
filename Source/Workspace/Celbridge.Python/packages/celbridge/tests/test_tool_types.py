"""Tests for tool_types module: type mapping, signatures, and namespace partitioning."""

from celbridge.tool_types import (
    to_python_type,
    build_signature,
    build_inspect_signature,
    partition_tools_by_namespace,
    format_namespace_doc,
)


def test_to_python_type_maps_json_schema_types():
    """Test that JSON Schema types are mapped to Python type names."""
    assert to_python_type("string") == "str"
    assert to_python_type("boolean") == "bool"
    assert to_python_type("integer") == "int"
    assert to_python_type("number") == "float"
    assert to_python_type("array") == "list"
    assert to_python_type("object") == "dict"


def test_to_python_type_passes_through_unknown_types():
    """Test that unknown type names are returned unchanged."""
    assert to_python_type("custom_type") == "custom_type"
    assert to_python_type("") == ""


def test_build_signature_with_return_type():
    """Test that return type is included in the signature."""
    tool = {"parameters": [], "returnType": "string"}
    assert build_signature(tool) == "() -> str"


def test_build_signature_maps_json_schema_parameter_types():
    """Test that JSON Schema parameter types are mapped to Python names."""
    tool = {
        "parameters": [
            {"name": "fileResource", "type": "string", "hasDefaultValue": False},
            {"name": "forceReload", "type": "boolean", "hasDefaultValue": True, "defaultValue": False},
        ]
    }
    assert build_signature(tool) == "(file_resource: str, force_reload: bool = False)"


def test_build_inspect_signature_has_correct_annotations():
    """Test that inspect.Signature has Python type annotations."""
    tool = {
        "parameters": [
            {"name": "message", "type": "string", "hasDefaultValue": False},
        ],
        "returnType": "string",
    }
    sig = build_inspect_signature(tool)

    params = list(sig.parameters.values())
    assert len(params) == 1
    assert params[0].name == "message"
    assert params[0].annotation is str
    assert sig.return_annotation is str


def test_build_inspect_signature_with_defaults():
    """Test that inspect.Signature includes default values."""
    tool = {
        "parameters": [
            {"name": "confirm", "type": "boolean", "hasDefaultValue": True, "defaultValue": True},
        ],
    }
    sig = build_inspect_signature(tool)

    params = list(sig.parameters.values())
    assert params[0].default is True


def test_partition_tools_separates_flat_and_namespaced():
    """Test that tools are correctly partitioned by namespace."""
    tools = [
        {"alias": "version", "name": "app_version"},
        {"alias": "app.log", "name": "log_info"},
        {"alias": "app.status", "name": "get_project_status"},
        {"alias": "resource.delete", "name": "resource_delete"},
    ]
    top_level, namespaced = partition_tools_by_namespace(tools)

    assert len(top_level) == 1
    assert top_level[0]["alias"] == "version"
    assert "app" in namespaced
    assert len(namespaced["app"]) == 2
    assert "resource" in namespaced
    assert len(namespaced["resource"]) == 1


def test_partition_tools_skips_empty_aliases():
    """Test that tools with empty aliases are excluded from both groups."""
    tools = [
        {"alias": "", "name": "hidden_tool"},
        {"alias": "version", "name": "app_version"},
    ]
    top_level, namespaced = partition_tools_by_namespace(tools)

    assert len(top_level) == 1
    assert len(namespaced) == 0


def test_format_namespace_doc_includes_methods_and_descriptions():
    """Test that namespace doc includes method signatures and descriptions."""
    tools = [
        {
            "alias": "app.version",
            "name": "app_version",
            "description": "Returns the version",
            "parameters": [],
            "returnType": "string",
        },
        {
            "alias": "app.log",
            "name": "log_info",
            "description": "Logs a message",
            "parameters": [
                {"name": "message", "type": "string", "hasDefaultValue": False}
            ],
        },
    ]
    doc = format_namespace_doc("app", tools)

    assert "cel.app" in doc
    assert ".log(message: str)" in doc
    assert "Logs a message" in doc
    assert ".version() -> str" in doc
    assert "Returns the version" in doc
