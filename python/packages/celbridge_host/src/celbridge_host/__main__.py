import sys
import atexit
from . import logging_config
from . import repl_environment
from . import rpc_service


def _main(argv=None) -> int:
    """
    Main entry point for the Celbridge Host module.
    """
    # Configure logging first
    logging_config.configure_logging()
    
    # Initialize RPC server if CELBRIDGE_RPC_PIPE env variable is set
    rpc_service.initialize_rpc_service()
    
    # Register shutdown handler
    atexit.register(rpc_service.shutdown_rpc_service)
    
    return repl_environment.initialize_repl_environment()


if __name__ == "__main__":
    code = _main()
    if code != 0:
        raise SystemExit(code)
