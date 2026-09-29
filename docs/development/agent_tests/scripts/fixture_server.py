"""Loopback fixture server for the agent test plans.

Serves a folder, prints what pages post to it, and offers the download responses the application's own
server cannot: attachments held back, slow, dropped or large. Every line it prints starts with the time in
seconds, so a run can line its output up with the application's log.

    python fixture_server.py [folder] [--port 8765]

    GET  /<path>                          a file from the folder; a missing one answers 404
    POST /<anything>                      prints the body and answers 204, for pages reporting what they saw
    GET  /attach?name=a.txt&hold=10       plain text marked as an attachment, its headers held back `hold` seconds
    GET  /slow?name=a.txt&size=2000000&rate=100000
                                          an attachment of `size` bytes sent at `rate` bytes a second
    GET  /drop?name=a.txt&after=100000&wait=1.5&length=1
                                          an attachment that sends `after` bytes, waits `wait` seconds, then drops
                                          the connection; `length=0` leaves out Content-Length
    GET  /big?name=a.bin&size=314572800   an attachment of `size` bytes sent as fast as the client takes them
"""

import argparse
import functools
import http.server
import os
import socket
import time
import urllib.parse


def log(line):
    """Prints a line stamped with the time in seconds."""
    print(f"{time.time():.3f} {line}", flush=True)


class FixtureHandler(http.server.SimpleHTTPRequestHandler):
    """Serves the folder's files and the download responses the plans need."""

    # A response with no length is sent in chunks, so a client can tell a dropped one from one that ended.
    protocol_version = "HTTP/1.1"

    def end_headers(self):
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def log_message(self, format, *args):
        # Each request is logged once, by the handler that served it.
        pass

    def do_OPTIONS(self):
        self.send_response(204)
        self.send_header("Access-Control-Allow-Headers", "*")
        self.send_header("Content-Length", "0")
        self.end_headers()

    def do_POST(self):
        length = int(self.headers.get("Content-Length", 0))
        body = self.rfile.read(length).decode("utf-8", "replace")
        log(f"POST {urllib.parse.urlparse(self.path).path} {body}")
        self.send_response(204)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def do_GET(self):
        try:
            if not self._serve_download():
                super().do_GET()
            log(f"GET {self.path} {self._status}")
        except (BrokenPipeError, ConnectionResetError):
            self.close_connection = True
            log(f"GET {self.path} aborted")

    def send_response(self, code, message=None):
        self._status = code
        super().send_response(code, message)

    def _serve_download(self):
        url = urllib.parse.urlparse(self.path)
        query = dict(urllib.parse.parse_qsl(url.query))
        name = query.get("name", "file.txt")

        def number(key, fallback):
            return float(query.get(key, fallback))

        if url.path == "/attach":
            time.sleep(number("hold", 0))
            body = f"attachment {name}\n".encode()
            self._start_attachment(name, len(body))
            self.wfile.write(body)
        elif url.path == "/slow":
            size = int(number("size", 2000000))
            self._start_attachment(name, size)
            self._send_bytes(size, b"s", rate=number("rate", 100000))
        elif url.path == "/drop":
            after = int(number("after", 100000))
            chunked = query.get("length") == "0"
            self._start_attachment(name, None if chunked else after * 10)
            self._send_bytes(after, b"d", chunked=chunked)
            time.sleep(number("wait", 1.5))
            log(f"dropping {name}")
            self.connection.shutdown(socket.SHUT_RDWR)
            self.close_connection = True
            self._status = "dropped"
        elif url.path == "/big":
            size = int(number("size", 300 * 1024 * 1024))
            self._start_attachment(name, size)
            self._send_bytes(size, b"b")
        else:
            return False
        return True

    def _start_attachment(self, name, length):
        self.send_response(200)
        self.send_header("Content-Type", "text/plain")
        self.send_header("Content-Disposition", f'attachment; filename="{name}"')
        if length is None:
            self.send_header("Transfer-Encoding", "chunked")
        else:
            self.send_header("Content-Length", str(length))
        self.end_headers()

    def _send_bytes(self, size, fill, rate=None, chunked=False):
        """Writes `size` bytes of `fill`, `rate` bytes a second when a positive rate is given."""
        if rate is not None and rate <= 0:
            rate = None
        # A rate below one byte a second still sends a byte at a time, so the loop always moves on.
        chunk = fill * (max(1, min(10000, int(rate))) if rate else 1024 * 1024)
        sent = 0
        while sent < size:
            n = min(len(chunk), size - sent)
            if rate:
                time.sleep(n / rate)
            if chunked:
                self.wfile.write(f"{n:x}\r\n".encode() + chunk[:n] + b"\r\n")
            else:
                self.wfile.write(chunk[:n])
            self.wfile.flush()
            sent += n


def main():
    parser = argparse.ArgumentParser(description="Loopback fixture server for the agent test plans.")
    parser.add_argument("folder", nargs="?", default=".", help="the folder to serve")
    parser.add_argument("--port", type=int, default=8765)
    args = parser.parse_args()

    root = os.path.abspath(args.folder)
    handler = functools.partial(FixtureHandler, directory=root)
    server = http.server.ThreadingHTTPServer(("127.0.0.1", args.port), handler)
    server.daemon_threads = True
    log(f"serving {root} on http://127.0.0.1:{args.port}/")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
