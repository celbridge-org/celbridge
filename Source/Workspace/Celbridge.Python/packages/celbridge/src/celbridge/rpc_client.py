"""Minimal JSON-RPC 2.0 client over TCP using only the Python standard library."""

import json
import socket
import logging

logger = logging.getLogger(__name__)


class RpcError(Exception):
    """Exception raised when a JSON-RPC call fails."""
    pass


# StreamJsonRpc's HeaderDelimitedMessageHandler uses HTTP-style framing:
# headers are separated from the message body by a blank line (\r\n\r\n).
HEADER_DELIMITER = b"\r\n\r\n"


class RpcClient:
    """JSON-RPC 2.0 client that communicates with the Celbridge application over TCP.

    Uses Content-Length header-delimited message framing, compatible with
    StreamJsonRpc's HeaderDelimitedMessageHandler on the C# side.
    """

    def __init__(self, host: str, port: int):
        self._socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._socket.connect((host, port))
        self._request_id = 0
        self._receive_buffer = b""
        logger.info(f"Connected to RPC server at {host}:{port}")

    def call(self, method: str, **params):
        """Send a JSON-RPC request and wait for the response.

        Args:
            method: The RPC method name to call.
            **params: Keyword arguments passed as the request params.

        Returns:
            The result value from the JSON-RPC response.

        Raises:
            RpcError: If the server returns an error or the connection fails.
        """
        self._request_id += 1
        request = self._build_request(method, params, request_id=self._request_id)
        self._send(json.dumps(request))
        response = json.loads(self._receive())

        if "error" in response:
            error_message = response["error"].get("message", "Unknown RPC error")
            raise RpcError(f"RPC error in '{method}': {error_message}")

        return response.get("result")

    def notify(self, method: str, **params) -> None:
        """Send a JSON-RPC notification (fire-and-forget, no response expected).

        Args:
            method: The RPC method name to call.
            **params: Keyword arguments passed as the notification params.
        """
        request = self._build_request(method, params)
        self._send(json.dumps(request))

    def _build_request(self, method: str, params: dict, request_id: int | None = None) -> dict:
        """Build a JSON-RPC 2.0 request dict."""
        request = {
            "jsonrpc": "2.0",
            "method": method,
        }
        if request_id is not None:
            request["id"] = request_id
        if params:
            request["params"] = params
        return request

    def close(self) -> None:
        """Close the TCP connection."""
        try:
            self._socket.close()
        except OSError:
            pass

    def _send(self, message: str) -> None:
        """Send a Content-Length framed message."""
        message_bytes = message.encode('utf-8')
        content_length = len(message_bytes)
        header = f"Content-Length: {content_length}".encode('utf-8') + HEADER_DELIMITER
        self._socket.sendall(header + message_bytes)

    def _receive(self) -> str:
        """Receive a Content-Length framed message.

        Returns:
            The message body as a string.

        Raises:
            RpcError: If the connection is closed or the message is malformed.
        """
        # Read headers until we find \r\n\r\n
        while HEADER_DELIMITER not in self._receive_buffer:
            chunk = self._socket.recv(4096)
            if not chunk:
                raise RpcError("Connection closed while reading response header")
            self._receive_buffer += chunk

        # Split headers from any remaining body data
        header_end = self._receive_buffer.index(HEADER_DELIMITER) + 4
        headers_bytes = self._receive_buffer[:header_end]
        self._receive_buffer = self._receive_buffer[header_end:]

        # Parse Content-Length header
        headers_str = headers_bytes.decode('utf-8')
        content_length = None
        for line in headers_str.split('\r\n'):
            if line.startswith('Content-Length:'):
                content_length = int(line.split(':', 1)[1].strip())
                break

        if content_length is None:
            raise RpcError("No Content-Length header in response")

        # Read more data if we don't have the full body yet
        while len(self._receive_buffer) < content_length:
            chunk = self._socket.recv(4096)
            if not chunk:
                raise RpcError("Connection closed while reading response body")
            self._receive_buffer += chunk

        # Extract the message body and leave any remainder in the buffer
        message_bytes = self._receive_buffer[:content_length]
        self._receive_buffer = self._receive_buffer[content_length:]

        return message_bytes.decode('utf-8')
