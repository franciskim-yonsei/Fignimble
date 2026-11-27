"""Relay a tunnel command over the Windows app's authenticated SSH connection."""

from __future__ import annotations

import os
import struct
import sys
import threading
import time

BLOCK = 65536


class WindowsPipe:
    """Use overlapped I/O so a pending read cannot block writes on the same pipe."""

    def __init__(self, path):
        import _winapi

        self.api = _winapi
        deadline = time.monotonic() + 10
        while True:
            try:
                self.handle = _winapi.CreateFile(path, _winapi.GENERIC_READ | _winapi.GENERIC_WRITE,
                                                 0, 0, _winapi.OPEN_EXISTING,
                                                 _winapi.FILE_FLAG_OVERLAPPED, 0)
                break
            except OSError as exc:
                if exc.winerror != _winapi.ERROR_PIPE_BUSY or time.monotonic() >= deadline:
                    raise
                time.sleep(0.025)

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.api.CloseHandle(self.handle)

    def read(self, length):
        pending, _ = self.api.ReadFile(self.handle, length, overlapped=True)
        self._result(pending)
        return pending.getbuffer()

    def write(self, data):
        pending, _ = self.api.WriteFile(self.handle, data, overlapped=True)
        return self._result(pending)

    @staticmethod
    def _result(pending):
        count, error = pending.GetOverlappedResult(True)
        if error:
            raise OSError(None, "Shared SSH pipe I/O ended", None, error)
        return count


def _read_exact(pipe, length):
    data = bytearray()
    while len(data) < length:
        chunk = pipe.read(length - len(data))
        if not chunk:
            raise EOFError("The shared SSH connection closed")
        data.extend(chunk)
    return data


def _send(pipe, data):
    frame = memoryview(struct.pack("<I", len(data)) + data)
    while frame:
        count = pipe.write(frame)
        if not count:
            raise OSError("The shared SSH connection closed")
        frame = frame[count:]


def relay(pipe, command):
    _send(pipe, command.encode("utf-8"))

    def forward_input():
        try:
            # Use the raw descriptor so the daemon reader never holds a Python buffer lock at shutdown.
            while data := os.read(sys.stdin.fileno(), BLOCK):
                _send(pipe, data)
            _send(pipe, b"")
        except OSError:
            pass

    threading.Thread(target=forward_input, daemon=True).start()
    while True:
        kind, length = struct.unpack("<BI", _read_exact(pipe, 5))
        if length > BLOCK or kind not in (1, 2, 3) or (kind == 3 and length != 4):
            raise ValueError("Invalid shared SSH response")
        data = _read_exact(pipe, length)
        if kind == 3:
            return struct.unpack("<i", data)[0]
        output = sys.stdout.buffer if kind == 1 else sys.stderr.buffer
        output.write(data)
        output.flush()


def main():
    import msvcrt

    # ssh_command supplies OpenSSH-style options before the host and a single remote shell command.
    args = iter(sys.argv[1:])
    try:
        host = next(args)
        while host.startswith("-") and host != "--":
            if host == "-o":
                next(args)
            elif host != "-T":
                raise ValueError("Unsupported option for the shared SSH connection")
            host = next(args)
        if host != os.environ.get("FIGNIMBLE_SSH_SESSION_HOST") or next(args) != "--":
            raise ValueError("The SSH command is for a different connection")
        command, = args
        for stream in (sys.stdin, sys.stdout, sys.stderr):
            msvcrt.setmode(stream.fileno(), os.O_BINARY)
        pipe_path = r"\\.\pipe" + "\\" + os.environ["FIGNIMBLE_SSH_PIPE"]
        with WindowsPipe(pipe_path) as pipe:
            return relay(pipe, command)
    except (EOFError, OSError, ValueError, KeyError, StopIteration) as exc:
        print(f"Shared SSH connection: {exc}", file=sys.stderr)
        return 255


if __name__ == "__main__":
    sys.exit(main())
