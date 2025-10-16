"""Windows named pipe handler for header-delimited message communication."""

import logging
from typing import Optional, Callable

import win32pipe  # type: ignore
import win32file  # type: ignore
import pywintypes  # type: ignore

# Configure logging
logger = logging.getLogger(__name__)


class NamedPipeHandler:
    """Handles reading and writing header-delimited messages over Windows named pipes."""

    def __init__(self, pipe_handle):
        """Initialize with a connected named pipe handle.
        
        Args:
            pipe_handle: Win32 pipe handle from CreateNamedPipe/ConnectNamedPipe
        """
        self.pipe_handle = pipe_handle

    def read_message(self) -> Optional[str]:
        """Read a header-delimited message from the pipe.
        
        Returns:
            The JSON-RPC message string, or None if connection closed
        """
        try:
            # Read headers until we find \r\n\r\n
            headers = b""
            while b"\r\n\r\n" not in headers:
                chunk = self._read_bytes(1)
                if not chunk:
                    return None
                headers += chunk
            
            # Parse Content-Length header
            headers_str = headers.decode('utf-8')
            content_length = None
            for line in headers_str.split('\r\n'):
                if line.startswith('Content-Length:'):
                    content_length = int(line.split(':', 1)[1].strip())
                    break
            
            if content_length is None:
                logger.error("No Content-Length header found")
                return None
            
            # Read the exact number of bytes specified
            message_bytes = self._read_bytes(content_length)
            if not message_bytes or len(message_bytes) != content_length:
                logger.error(f"Failed to read complete message (expected {content_length} bytes)")
                return None
            
            return message_bytes.decode('utf-8')
            
        except Exception as e:
            logger.error(f"Error reading message: {e}")
            return None

    def write_message(self, message: str) -> bool:
        """Write a header-delimited message to the pipe.
        
        Args:
            message: The JSON-RPC response string
            
        Returns:
            True if write succeeded, False otherwise
        """
        try:
            message_bytes = message.encode('utf-8')
            content_length = len(message_bytes)
            headers = f"Content-Length: {content_length}\r\n\r\n".encode('utf-8')
            
            # Write headers + message
            full_message = headers + message_bytes
            win32file.WriteFile(self.pipe_handle, full_message)
            return True
            
        except Exception as e:
            logger.error(f"Error writing message: {e}")
            return False

    def _read_bytes(self, count: int) -> Optional[bytes]:
        """Read exactly count bytes from the pipe.
        
        Args:
            count: Number of bytes to read
            
        Returns:
            The bytes read, or None if connection closed
        """
        try:
            result, data = win32file.ReadFile(self.pipe_handle, count)
            if result == 0:  # Success
                # Ensure we return bytes, not str
                if isinstance(data, str):
                    return data.encode('utf-8')
                return data
            return None
        except pywintypes.error as e:
            if e.winerror == 109:  # ERROR_BROKEN_PIPE
                return None
            raise


class NamedPipeServer:
    """Server for accepting connections on a Windows named pipe."""

    def __init__(self, pipe_name: str):
        """Initialize the pipe server.
        
        Args:
            pipe_name: Name of the Windows named pipe (without \\\\.\\pipe\\ prefix)
        """
        self.pipe_name = f"\\\\.\\pipe\\{pipe_name}"
        logger.info(f"Pipe server initialized for: {self.pipe_name}")

    def accept_client(self, running_flag: Callable[[], bool]) -> Optional[NamedPipeHandler]:
        """Wait for and accept a client connection.
        
        This is a blocking call that creates a named pipe, waits for a client
        to connect, and returns a handler for the connected pipe.
        
        Args:
            running_flag: Callable that returns False when server should stop
            
        Returns:
            NamedPipeHandler for the connected client, or None if stopped/error
        """
        pipe_handle = None
        try:
            # Create named pipe
            pipe_handle = win32pipe.CreateNamedPipe(
                self.pipe_name,
                win32pipe.PIPE_ACCESS_DUPLEX,
                win32pipe.PIPE_TYPE_BYTE | win32pipe.PIPE_READMODE_BYTE | win32pipe.PIPE_WAIT,
                1,  # Max instances
                65536,  # Out buffer size
                65536,  # In buffer size
                0,  # Default timeout
                None  # type: ignore  # Security attributes
            )
            
            logger.info(f"Waiting for client connection on {self.pipe_name}...")
            
            # Wait for client to connect
            win32pipe.ConnectNamedPipe(pipe_handle, None)
            logger.info("Client connected")
            
            # Return handler for this connection
            return NamedPipeHandler(pipe_handle)
            
        except pywintypes.error as e:
            if running_flag():
                logger.error(f"Pipe error: {e}")
            if pipe_handle:
                try:
                    win32file.CloseHandle(pipe_handle)
                except:
                    pass
            return None
        except Exception as e:
            if running_flag():
                logger.error(f"Server error: {e}")
            if pipe_handle:
                try:
                    win32file.CloseHandle(pipe_handle)
                except:
                    pass
            return None

    @staticmethod
    def close_pipe(handler: NamedPipeHandler):
        """Close a pipe connection.
        
        Args:
            handler: The pipe handler to close
        """
        try:
            win32file.CloseHandle(handler.pipe_handle)
        except Exception as e:
            logger.error(f"Error closing pipe: {e}")
