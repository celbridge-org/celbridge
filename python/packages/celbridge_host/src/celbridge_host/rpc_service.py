"""Provides bidirectional RPC communication with Celbridge application via JSON-RPC."""

import os
import json
import logging
import threading
from typing import Optional, Any

from jsonrpcserver import dispatch
from jsonrpcclient import request_json, parse_json, Ok, Error as RpcClientError

# Import rpc_handler to register its @method decorated functions
from celbridge_host import rpc_handler  # noqa: F401
from celbridge_host.named_pipe import NamedPipeHandler, NamedPipeServer

# Configure logging
logger = logging.getLogger(__name__)

# Global server instance
_rpc_service: Optional['CelbridgeRpcService'] = None
_service_lock = threading.Lock()

# Global pipe handler for outgoing requests to C#
_pipe_handler: Optional[NamedPipeHandler] = None
_pipe_handler_lock = threading.Lock()


class CelbridgeRpcService:
    """JSON-RPC server that runs in a background thread and communicates over named pipes."""

    def __init__(self, pipe_name: str):
        """Initialize the RPC server.
        
        Args:
            pipe_name: Name of the Windows named pipe (without \\\\.\\pipe\\ prefix)
        """
        self.pipe_server = NamedPipeServer(pipe_name)
        self.running = False
        self.thread: Optional[threading.Thread] = None
        logger.info(f"RPC server initialized")

    def start(self):
        """Start the RPC server in a background thread."""
        if self.running:
            logger.warning("RPC server already running")
            return
        
        self.running = True
        self.thread = threading.Thread(target=self._run, daemon=True)
        self.thread.start()
        logger.info("RPC server started")

    def stop(self):
        """Stop the RPC server."""
        self.running = False
        if self.thread:
            # Note: We don't join the thread because it might be blocked on pipe operations
            # The daemon flag ensures it won't prevent process exit
            logger.info("RPC server stopped")

    def _run(self):
        """Main server loop that handles client connections."""
        while self.running:
            # Accept a client connection
            handler = self.pipe_server.accept_client(lambda: self.running)
            
            if handler:
                # Handle requests from this client
                self._handle_client(handler)
                
                # Close the pipe when done
                NamedPipeServer.close_pipe(handler)

    def _handle_client(self, handler: NamedPipeHandler):
        """Handle requests from a connected client.
        
        Args:
            handler: The pipe handler for the connected client
        """
        global _pipe_handler
        
        # Store the pipe handler globally for outgoing requests
        with _pipe_handler_lock:
            _pipe_handler = handler
        
        try:
            while self.running:
                # Read request
                request_str = handler.read_message()
                if not request_str:
                    logger.info("Client disconnected")
                    break
                
                logger.debug(f"Received request: {request_str}")
                
                try:
                    # Dispatch to appropriate method and get response
                    response_str = dispatch(request_str)
                    logger.debug(f"Sending response: {response_str}")
                    
                    # Send response
                    if not handler.write_message(response_str):
                        logger.error("Failed to write response")
                        break
                        
                except Exception as e:
                    logger.error(f"Error processing request: {e}")
                    # Send error response
                    error_response = json.dumps({
                        "jsonrpc": "2.0",
                        "error": {
                            "code": -32603,
                            "message": f"Internal error: {str(e)}"
                        },
                        "id": None
                    })
                    handler.write_message(error_response)
        finally:
            # Clear the global pipe handler when client disconnects
            with _pipe_handler_lock:
                _pipe_handler = None


def initialize_rpc_service() -> bool:
    """Initialize and start the RPC server if CELBRIDGE_RPC_PIPE is set.
    
    Returns:
        True if server was started, False otherwise
    """
    global _rpc_service
    
    pipe_name = os.environ.get('CELBRIDGE_RPC_PIPE')
    if not pipe_name:
        logger.info("CELBRIDGE_RPC_PIPE not set, RPC server not started")
        return False
    
    with _service_lock:
        if _rpc_service is not None:
            logger.warning("RPC server already initialized")
            return False
        
        try:
            _rpc_service = CelbridgeRpcService(pipe_name)
            _rpc_service.start()
            logger.info(f"RPC server initialized and started on pipe: {pipe_name}")
            return True
        except Exception as e:
            logger.error(f"Failed to initialize RPC server: {e}")
            return False


def shutdown_rpc_service():
    """Shutdown the RPC server."""
    global _rpc_service
    
    with _service_lock:
        if _rpc_service is not None:
            _rpc_service.stop()
            _rpc_service = None
            logger.info("RPC server shutdown complete")


def get_rpc_service() -> Optional[CelbridgeRpcService]:
    """Get the current RPC server instance.
    
    Returns:
        The RPC server instance, or None if not initialized
    """
    return _rpc_service


def get_pipe_handler() -> Optional[NamedPipeHandler]:
    """Get the current pipe handler for sending requests to C#.
    
    Returns:
        The pipe handler instance, or None if no client is connected
    """
    with _pipe_handler_lock:
        return _pipe_handler


class RpcError(Exception):
    """Exception raised when an RPC call fails."""
    pass


def call_csharp_method(method: str, **params) -> Any:
    """Call a C# method via JSON-RPC.
    
    Args:
        method: The C# method name to call
        **params: Method parameters as keyword arguments
        
    Returns:
        The result from the RPC call (can be None for void methods)
        
    Raises:
        RpcError: If the RPC call fails (connection, transport, or protocol error)
    """

    # Get the transport (pipe handler)
    pipe_handler = get_pipe_handler()
    if pipe_handler is None:
        raise RpcError(f"Cannot call '{method}': No active pipe connection to C#")
    
    # Build JSON-RPC request
    request_str = request_json(method, params=params)
    logger.debug(f"RPC call to C#: {method}({params})")
    
    # Send request
    if not pipe_handler.write_message(request_str):
        raise RpcError(f"Failed to send RPC request: {method}")
    
    # Receive response
    response_str = pipe_handler.read_message()
    if not response_str:
        raise RpcError(f"Failed to receive RPC response: {method}")
    
    # Parse JSON-RPC response
    response = parse_json(response_str)
    
    if isinstance(response, Ok):
        logger.debug(f"RPC call succeeded: {method}")
        return response.result
    elif isinstance(response, RpcClientError):
        raise RpcError(f"RPC error in '{method}': {response.message} (code: {response.code})")
    else:
        raise RpcError(f"Unexpected response type for '{method}': {type(response)}")
