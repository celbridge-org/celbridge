"""Tests for CelProxy alias-based tool discovery, namespacing, and dispatch."""

from unittest.mock import MagicMock
from celbridge.cel_proxy import CelProxy, CelError, ToolNamespace
from celbridge.tool_types import (
    snake_to_camel,
    camel_to_snake,
    build_signature,
    build_docstring,
)


# -- Name conversion helpers --


def test_snake_to_camel_simple():
    """Test converting snake_case to camelCase."""
    assert snake_to_camel("file_resource") == "fileResource"


def test_snake_to_camel_single_word():
    """Test that a single word stays unchanged."""
    assert snake_to_camel("name") == "name"


def test_snake_to_camel_multiple_underscores():
    """Test multi-word snake_case."""
    assert snake_to_camel("force_reload_now") == "forceReloadNow"


def test_camel_to_snake_simple():
    """Test converting camelCase to snake_case."""
    assert camel_to_snake("fileResource") == "file_resource"


def test_camel_to_snake_single_word():
    """Test that a single word stays unchanged."""
    assert camel_to_snake("name") == "name"


def test_camel_to_snake_multiple_capitals():
    """Test multi-capital camelCase."""
    assert camel_to_snake("forceReloadNow") == "force_reload_now"


# -- Signature and docstring builders --


def test_build_signature_no_params():
    """Test building a signature with no parameters."""
    tool = {"name": "app/version", "alias": "version", "parameters": []}
    assert build_signature(tool) == "()"


def test_build_signature_required_param():
    """Test building a signature with a typed required parameter."""
    tool = {
        "name": "app/log",
        "alias": "log",
        "parameters": [
            {"name": "message", "type": "string", "hasDefaultValue": False}
        ],
    }
    assert build_signature(tool) == "(message: str)"


def test_build_signature_optional_param():
    """Test building a signature with typed parameters and defaults."""
    tool = {
        "name": "document/open",
        "alias": "open",
        "parameters": [
            {"name": "fileResource", "type": "string", "hasDefaultValue": False},
            {
                "name": "forceReload",
                "type": "boolean",
                "hasDefaultValue": True,
                "defaultValue": False,
            },
        ],
    }
    assert build_signature(tool) == "(file_resource: str, force_reload: bool = False)"


def testbuild_docstring():
    """Test building a docstring from a tool descriptor."""
    tool = {
        "name": "app/log",
        "alias": "log",
        "description": "Writes a message to the application log",
        "parameters": [
            {
                "name": "message",
                "type": "string",
                "description": "The message to log",
            }
        ],
    }
    docstring = build_docstring(tool)
    assert "Writes a message to the application log" in docstring
    assert "message (str): The message to log" in docstring


# -- CelProxy tool discovery and dispatch --


def _make_mock_client(tools=None):
    """Create a mock RpcClient that returns the given tools from tools/list."""
    mock_client = MagicMock()

    def call_side_effect(method, **kwargs):
        if method == "tools/list":
            return tools or []
        if method == "tools/call":
            return {"isSuccess": True, "errorMessage": "", "value": "mock_result"}
        return None

    mock_client.call.side_effect = call_side_effect
    return mock_client


def test_discover_tools_creates_proxy_methods_from_alias():
    """Test that discovered tools become methods using their alias."""
    tools = [
        {
            "name": "app/version",
            "alias": "version",
            "description": "Returns the app version",
            "parameters": [],
        },
        {
            "name": "document/open",
            "alias": "open",
            "description": "Opens a document",
            "parameters": [
                {
                    "name": "fileResource",
                    "type": "string",
                    "description": "Resource key",
                    "hasDefaultValue": False,
                }
            ],
        },
    ]
    mock_client = _make_mock_client(tools)
    cel = CelProxy(mock_client)

    assert hasattr(cel, "version")
    assert hasattr(cel, "open")
    assert callable(cel.version)
    assert callable(cel.open)


def test_proxy_method_calls_tools_call_with_tool_name():
    """Test that calling a proxy method sends tools/call with the canonical tool name."""
    tools = [
        {
            "name": "app/version",
            "alias": "version",
            "description": "Returns the app version",
            "parameters": [],
        }
    ]
    mock_client = _make_mock_client(tools)
    cel = CelProxy(mock_client)

    result = cel.version()

    mock_client.call.assert_any_call(
        "tools/call", name="app/version", arguments={}
    )
    assert result == "mock_result"


def test_proxy_converts_snake_kwargs_to_camel():
    """Test that snake_case kwargs are converted to camelCase arguments."""
    tools = [
        {
            "name": "document/open",
            "alias": "open",
            "description": "Opens a document",
            "parameters": [
                {"name": "fileResource", "type": "string", "hasDefaultValue": False},
                {
                    "name": "forceReload",
                    "type": "boolean",
                    "hasDefaultValue": True,
                    "defaultValue": False,
                },
            ],
        }
    ]
    mock_client = _make_mock_client(tools)
    cel = CelProxy(mock_client)

    cel.open(file_resource="Project/readme.md", force_reload=True)

    mock_client.call.assert_any_call(
        "tools/call",
        name="document/open",
        arguments={"fileResource": "Project/readme.md", "forceReload": True},
    )


def test_proxy_accepts_positional_arguments():
    """Test that positional args are mapped to parameter names in order."""
    tools = [
        {
            "name": "document/open",
            "alias": "open",
            "description": "Opens a document",
            "parameters": [
                {"name": "fileResource", "type": "string", "hasDefaultValue": False},
                {
                    "name": "forceReload",
                    "type": "boolean",
                    "hasDefaultValue": True,
                    "defaultValue": False,
                },
            ],
        }
    ]
    mock_client = _make_mock_client(tools)
    cel = CelProxy(mock_client)

    cel.open("Project/readme.md")

    mock_client.call.assert_any_call(
        "tools/call",
        name="document/open",
        arguments={"fileResource": "Project/readme.md"},
    )


def test_proxy_accepts_positional_and_keyword_mixed():
    """Test mixing positional and keyword arguments."""
    tools = [
        {
            "name": "document/open",
            "alias": "open",
            "description": "Opens a document",
            "parameters": [
                {"name": "fileResource", "type": "string", "hasDefaultValue": False},
                {
                    "name": "forceReload",
                    "type": "boolean",
                    "hasDefaultValue": True,
                    "defaultValue": False,
                },
            ],
        }
    ]
    mock_client = _make_mock_client(tools)
    cel = CelProxy(mock_client)

    cel.open("Project/readme.md", force_reload=True)

    mock_client.call.assert_any_call(
        "tools/call",
        name="document/open",
        arguments={"fileResource": "Project/readme.md", "forceReload": True},
    )


def test_proxy_raises_on_tool_failure():
    """Test that a failed tool call raises RuntimeError."""
    tools = [
        {"name": "app/version", "alias": "version", "description": "Returns version", "parameters": []}
    ]
    mock_client = MagicMock()

    def call_side_effect(method, **kwargs):
        if method == "tools/list":
            return tools
        if method == "tools/call":
            return {
                "isSuccess": False,
                "errorMessage": "Something went wrong",
                "value": None,
            }
        return None

    mock_client.call.side_effect = call_side_effect
    cel = CelProxy(mock_client)

    try:
        cel.version()
        assert False, "Expected CelError"
    except CelError as exception:
        assert "Something went wrong" in str(exception)


def test_proxy_has_docstring():
    """Test that proxy methods have auto-generated docstrings."""
    tools = [
        {
            "name": "app/log",
            "alias": "log",
            "description": "Writes a message to the application log",
            "parameters": [
                {
                    "name": "message",
                    "type": "string",
                    "description": "The message to log",
                    "hasDefaultValue": False,
                }
            ],
        }
    ]
    mock_client = _make_mock_client(tools)
    cel = CelProxy(mock_client)

    assert cel.log.__doc__ is not None
    assert "Writes a message to the application log" in cel.log.__doc__


# -- Dot-namespaced tools --


def test_dotted_alias_creates_namespace():
    """Test that a dotted alias like 'sheet.delete' creates a namespace object."""
    tools = [
        {
            "name": "excel/delete_sheet",
            "alias": "sheet.delete",
            "description": "Deletes a sheet",
            "parameters": [
                {"name": "sheetName", "type": "string", "hasDefaultValue": False}
            ],
        }
    ]
    mock_client = _make_mock_client(tools)
    cel = CelProxy(mock_client)

    assert hasattr(cel, "sheet")
    assert isinstance(cel.sheet, ToolNamespace)
    assert hasattr(cel.sheet, "delete")
    assert callable(cel.sheet.delete)


def test_dotted_alias_dispatches_correctly():
    """Test that calling a namespaced method sends the correct tool name."""
    tools = [
        {
            "name": "excel/delete_sheet",
            "alias": "sheet.delete",
            "description": "Deletes a sheet",
            "parameters": [
                {"name": "sheetName", "type": "string", "hasDefaultValue": False}
            ],
        }
    ]
    mock_client = _make_mock_client(tools)
    cel = CelProxy(mock_client)

    cel.sheet.delete("Sheet1")

    mock_client.call.assert_any_call(
        "tools/call",
        name="excel/delete_sheet",
        arguments={"sheetName": "Sheet1"},
    )


def test_multiple_tools_in_same_namespace():
    """Test that multiple dotted aliases share the same namespace."""
    tools = [
        {
            "name": "excel/delete_sheet",
            "alias": "sheet.delete",
            "description": "Deletes a sheet",
            "parameters": [],
        },
        {
            "name": "excel/rename_sheet",
            "alias": "sheet.rename",
            "description": "Renames a sheet",
            "parameters": [],
        },
    ]
    mock_client = _make_mock_client(tools)
    cel = CelProxy(mock_client)

    assert hasattr(cel.sheet, "delete")
    assert hasattr(cel.sheet, "rename")


# -- Help output --


def test_help_cel_shows_typed_signatures():
    """Test that help(cel) shows compact namespace listing."""
    tools = [
        {"name": "app/version", "alias": "app.version", "description": "Returns the app version", "parameters": []},
        {"name": "app/log", "alias": "app.log", "description": "Writes a log message", "parameters": []},
    ]
    mock_client = _make_mock_client(tools)
    cel = CelProxy(mock_client)

    doc = cel.__doc__
    assert "cel.app" in doc
    assert "2 methods" in doc
    assert "Namespaces" in doc
    assert "cel.tools()" in doc


def test_help_cel_open_shows_method_doc():
    """Test that help(cel.open) shows the method's docstring."""
    tools = [
        {
            "name": "document/open",
            "alias": "open",
                       "description": "Opens a document in the editor",
            "parameters": [
                {"name": "fileResource", "type": "string", "description": "Resource key of the file", "hasDefaultValue": False}
            ],
        },
    ]
    mock_client = _make_mock_client(tools)
    cel = CelProxy(mock_client)

    doc = cel.open.__doc__
    assert "Opens a document in the editor" in doc
    assert "file_resource (str): Resource key of the file" in doc


def test_help_namespace_shows_all_commands():
    """Test that help(cel.sheet) shows all commands in the namespace."""
    tools = [
        {"name": "excel/delete_sheet", "alias": "sheet.delete", "description": "Deletes a sheet", "parameters": []},
        {"name": "excel/rename_sheet", "alias": "sheet.rename", "description": "Renames a sheet", "parameters": []},
    ]
    mock_client = _make_mock_client(tools)
    cel = CelProxy(mock_client)

    doc = cel.sheet.__doc__
    assert "cel.sheet" in doc
    assert ".delete()" in doc
    assert ".rename()" in doc
    assert "Deletes a sheet" in doc
    assert "Renames a sheet" in doc


def test_help_cel_shows_namespaced_tools():
    """Test that __doc__ lists namespaces with method counts."""
    tools = [
        {"name": "excel/delete_sheet", "alias": "sheet.delete", "description": "Deletes a sheet", "parameters": []},
        {"name": "excel/rename_sheet", "alias": "sheet.rename", "description": "Renames a sheet", "parameters": []},
    ]
    mock_client = _make_mock_client(tools)
    cel = CelProxy(mock_client)

    doc = cel.__doc__
    assert "cel.sheet" in doc
    assert "2 methods" in doc
    assert "cel.test()" in doc


# -- Edge cases --


def test_discover_tools_handles_connection_failure():
    """Test that CelProxy gracefully handles broker connection failure."""
    mock_client = MagicMock()
    mock_client.call.side_effect = ConnectionError("Connection refused")

    cel = CelProxy(mock_client)
    assert cel._tools == []


def test_unknown_attribute_suggests_similar_names():
    """Test that accessing an unknown attribute suggests close matches."""
    tools = [
        {"name": "resource/rename", "alias": "rename", "description": "Renames", "parameters": []},
        {"name": "resource/delete", "alias": "delete", "description": "Deletes", "parameters": []},
    ]
    mock_client = _make_mock_client(tools)
    cel = CelProxy(mock_client)

    try:
        cel.renam()
        assert False, "Expected AttributeError"
    except AttributeError as exception:
        message = str(exception)
        assert "cel.renam is not a known command" in message
        assert "cel.rename" in message


def test_unknown_attribute_shows_help_hint():
    """Test that accessing a completely unrelated name suggests help."""
    tools = [
        {"name": "app/version", "alias": "version", "description": "Version", "parameters": []},
    ]
    mock_client = _make_mock_client(tools)
    cel = CelProxy(mock_client)

    try:
        cel.xyz()
        assert False, "Expected AttributeError"
    except AttributeError as exception:
        assert "help(cel)" in str(exception)


def test_too_many_positional_args_shows_signature():
    """Test that passing too many args shows the expected signature."""
    tools = [
        {
            "name": "resource/rename",
            "alias": "rename",
            "description": "Renames",
            "parameters": [
                {"name": "resource", "type": "string", "hasDefaultValue": False}
            ],
        }
    ]
    mock_client = _make_mock_client(tools)
    cel = CelProxy(mock_client)

    try:
        cel.rename("a", "b")
        assert False, "Expected CelError"
    except CelError as exception:
        message = str(exception)
        assert "cel.rename(resource: str)" in message
        assert "2 positional arguments" in message


def test_tool_failure_shows_clean_message():
    """Test that a failed tool call shows alias and signature in the error."""
    tools = [
        {
            "name": "resource/rename",
            "alias": "rename",
            "description": "Renames a resource",
            "parameters": [
                {"name": "resource", "type": "string", "hasDefaultValue": False}
            ],
        }
    ]
    mock_client = MagicMock()

    def call_side_effect(method, **kwargs):
        if method == "tools/list":
            return tools
        if method == "tools/call":
            return {
                "isSuccess": False,
                "errorMessage": "Missing required parameter 'resource'",
                "value": None,
            }
        return None

    mock_client.call.side_effect = call_side_effect
    cel = CelProxy(mock_client)

    try:
        cel.rename()
        assert False, "Expected CelError"
    except CelError as exception:
        message = str(exception)
        assert "cel.rename(resource: str)" in message
        assert "Missing required parameter" in message


def test_tools_without_alias_are_skipped():
    """Test that tools with no alias are not registered as methods."""
    tools = [
        {"name": "app/version", "alias": "", "description": "Returns version", "parameters": []},
        {"name": "app/log", "alias": "log", "description": "Writes a log", "parameters": []},
    ]
    mock_client = _make_mock_client(tools)
    cel = CelProxy(mock_client)

    assert not hasattr(cel, "version")
    assert not hasattr(cel, "app_version")
    assert hasattr(cel, "log")


def test_list_argument_auto_serialized_for_string_parameter():
    """Test that list/dict arguments are auto-serialized to JSON when the
    tool parameter type is string."""
    tools = [
        {
            "name": "file/read_many",
            "alias": "file.read_many",
            "description": "Reads multiple files",
            "parameters": [
                {"name": "resources", "type": "string", "hasDefaultValue": False},
            ],
        }
    ]
    mock_client = MagicMock()
    captured_arguments = {}

    def call_side_effect(method, **kwargs):
        if method == "tools/list":
            return tools
        if method == "tools/call":
            captured_arguments.update(kwargs.get("arguments", {}))
            return {"isSuccess": True, "errorMessage": "", "value": "{}"}
        return None

    mock_client.call.side_effect = call_side_effect
    cel = CelProxy(mock_client)

    cel.file.read_many(["a.txt", "b.txt"])

    assert captured_arguments["resources"] == '["a.txt", "b.txt"]'


def test_list_argument_not_serialized_for_non_string_parameter():
    """Test that list arguments are left as-is when the parameter type is
    not string."""
    tools = [
        {
            "name": "misc/array_tool",
            "alias": "misc.array_tool",
            "description": "Tool with array parameter",
            "parameters": [
                {"name": "items", "type": "array", "hasDefaultValue": False},
            ],
        }
    ]
    mock_client = MagicMock()
    captured_arguments = {}

    def call_side_effect(method, **kwargs):
        if method == "tools/list":
            return tools
        if method == "tools/call":
            captured_arguments.update(kwargs.get("arguments", {}))
            return {"isSuccess": True, "errorMessage": "", "value": None}
        return None

    mock_client.call.side_effect = call_side_effect
    cel = CelProxy(mock_client)

    cel.misc.array_tool(["a", "b"])

    assert captured_arguments["items"] == ["a", "b"]
