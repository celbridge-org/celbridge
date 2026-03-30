"""Tests for RpcClient JSON-RPC communication."""

import json
import socket
import threading

import pytest
from celbridge.rpc_client import RpcClient, RpcError, HEADER_DELIMITER


@pytest.fixture
def mock_server():
    """Create a mock TCP server that accepts one connection.

    Yields (server_socket, port). The server socket is closed automatically
    after the test completes.
    """
    server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server_socket.bind(('127.0.0.1', 0))
    server_socket.listen(1)
    port = server_socket.getsockname()[1]

    yield server_socket, port

    server_socket.close()


def _read_framed_message(connection: socket.socket) -> str:
    """Read a Content-Length framed message from a socket."""
    buffer = b""
    while HEADER_DELIMITER not in buffer:
        chunk = connection.recv(4096)
        if not chunk:
            return ""
        buffer += chunk

    header_end = buffer.index(HEADER_DELIMITER) + len(HEADER_DELIMITER)
    headers = buffer[:header_end].decode('utf-8')
    remainder = buffer[header_end:]

    content_length = None
    for line in headers.split('\r\n'):
        if line.startswith('Content-Length:'):
            content_length = int(line.split(':', 1)[1].strip())
            break

    while len(remainder) < content_length:
        chunk = connection.recv(4096)
        remainder += chunk

    return remainder[:content_length].decode('utf-8')


def _send_framed_message(connection: socket.socket, message: str) -> None:
    """Send a Content-Length framed message to a socket."""
    message_bytes = message.encode('utf-8')
    header = f"Content-Length: {len(message_bytes)}".encode('utf-8') + HEADER_DELIMITER
    connection.sendall(header + message_bytes)


def test_call_returns_result(mock_server):
    """Test that call() sends a request and returns the result."""
    server_socket, port = mock_server
    received_requests = []

    def server_handler():
        connection, _ = server_socket.accept()
        request_str = _read_framed_message(connection)
        received_requests.append(json.loads(request_str))

        response = json.dumps({
            "jsonrpc": "2.0",
            "result": "1.2.3",
            "id": received_requests[0]["id"],
        })
        _send_framed_message(connection, response)
        connection.close()

    thread = threading.Thread(target=server_handler, daemon=True)
    thread.start()

    client = RpcClient('127.0.0.1', port)
    result = client.call("GetAppVersion")

    thread.join(timeout=5)

    assert result == "1.2.3"
    assert len(received_requests) == 1
    assert received_requests[0]["method"] == "GetAppVersion"
    assert received_requests[0]["jsonrpc"] == "2.0"
    assert "id" in received_requests[0]


def test_call_raises_on_error(mock_server):
    """Test that call() raises RpcError when the server returns an error."""
    server_socket, port = mock_server

    def server_handler():
        connection, _ = server_socket.accept()
        request_str = _read_framed_message(connection)
        request = json.loads(request_str)

        response = json.dumps({
            "jsonrpc": "2.0",
            "error": {"code": -32601, "message": "Method not found"},
            "id": request["id"],
        })
        _send_framed_message(connection, response)
        connection.close()

    thread = threading.Thread(target=server_handler, daemon=True)
    thread.start()

    client = RpcClient('127.0.0.1', port)

    with pytest.raises(RpcError, match="Method not found"):
        client.call("NonexistentMethod")

    thread.join(timeout=5)


def test_call_sends_params(mock_server):
    """Test that call() includes keyword arguments as params."""
    server_socket, port = mock_server
    received_requests = []

    def server_handler():
        connection, _ = server_socket.accept()
        request_str = _read_framed_message(connection)
        received_requests.append(json.loads(request_str))

        response = json.dumps({
            "jsonrpc": "2.0",
            "result": None,
            "id": received_requests[0]["id"],
        })
        _send_framed_message(connection, response)
        connection.close()

    thread = threading.Thread(target=server_handler, daemon=True)
    thread.start()

    client = RpcClient('127.0.0.1', port)
    client.call("Log", message="hello")

    thread.join(timeout=5)

    assert received_requests[0]["params"] == {"message": "hello"}


def test_notify_sends_no_id(mock_server):
    """Test that notify() sends a request without an id field."""
    server_socket, port = mock_server
    received_requests = []

    def server_handler():
        connection, _ = server_socket.accept()
        request_str = _read_framed_message(connection)
        received_requests.append(json.loads(request_str))
        connection.close()

    thread = threading.Thread(target=server_handler, daemon=True)
    thread.start()

    client = RpcClient('127.0.0.1', port)
    client.notify("Log", message="hello")
    client.close()

    thread.join(timeout=5)

    assert received_requests[0]["method"] == "Log"
    assert "id" not in received_requests[0]
    assert received_requests[0]["params"] == {"message": "hello"}


def test_close_is_idempotent(mock_server):
    """Test that calling close() multiple times does not raise."""
    server_socket, port = mock_server

    def server_handler():
        connection, _ = server_socket.accept()
        connection.close()

    thread = threading.Thread(target=server_handler, daemon=True)
    thread.start()

    client = RpcClient('127.0.0.1', port)
    client.close()
    client.close()

    thread.join(timeout=5)


def test_connection_refused_raises():
    """Test that connecting to a closed port raises an error."""
    with pytest.raises(ConnectionRefusedError):
        RpcClient('127.0.0.1', 1)


def test_call_without_params(mock_server):
    """Test that call() works without keyword arguments (no params field)."""
    server_socket, port = mock_server
    received_requests = []

    def server_handler():
        connection, _ = server_socket.accept()
        request_str = _read_framed_message(connection)
        received_requests.append(json.loads(request_str))

        response = json.dumps({
            "jsonrpc": "2.0",
            "result": "ok",
            "id": received_requests[0]["id"],
        })
        _send_framed_message(connection, response)
        connection.close()

    thread = threading.Thread(target=server_handler, daemon=True)
    thread.start()

    client = RpcClient('127.0.0.1', port)
    result = client.call("Ping")

    thread.join(timeout=5)

    assert result == "ok"
    assert "params" not in received_requests[0]
