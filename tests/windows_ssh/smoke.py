"""Exercise the Windows SSH connection and pipe against a loopback SSH server."""

import base64
import hashlib
import os
import socket
import subprocess
import sys
import threading
from pathlib import Path

import paramiko


class Server(paramiko.ServerInterface):
    def get_allowed_auths(self, _):
        return "password,keyboard-interactive"

    def check_auth_password(self, username, password):
        return paramiko.AUTH_SUCCESSFUL if (username, password) == ("researcher", "test-password") else paramiko.AUTH_FAILED

    def check_auth_interactive(self, username, _):
        if username == "researcher":
            return paramiko.InteractiveQuery("Test login", "", ("Verification code: ", False))
        return paramiko.AUTH_FAILED

    def check_auth_interactive_response(self, responses):
        return paramiko.AUTH_SUCCESSFUL if responses == ["31337"] else paramiko.AUTH_FAILED

    def check_channel_request(self, kind, _):
        return paramiko.OPEN_SUCCEEDED if kind == "session" else paramiko.OPEN_FAILED_ADMINISTRATIVELY_PROHIBITED

    def check_channel_exec_request(self, channel, command):
        threading.Thread(target=self.execute, args=(channel, command), daemon=True).start()
        return True

    def check_channel_pty_request(self, channel, term, width, height, *_):
        return term == b"xterm-256color" and (width, height) == (80, 24)

    def check_channel_shell_request(self, channel):
        threading.Thread(target=self.shell, args=(channel,), daemon=True).start()
        return True

    def check_channel_window_change_request(self, channel, width, height, *_):
        return (width, height) == (100, 35)

    @staticmethod
    def execute(channel, command):
        try:
            if command == b"echo-input":
                while data := channel.recv(32768):
                    channel.sendall(data)
            else:
                assert command == b"fail-fast"
                channel.sendall(b"out\0\n")
            channel.sendall_stderr(b"server stderr\r\n")
            channel.send_exit_status(0 if command == b"echo-input" else 23)
        finally:
            channel.close()

    @staticmethod
    def shell(channel):
        try:
            reader = channel.makefile("rb")
            assert reader.readline().strip() == b"exec project-shell"
            channel.sendall(b"project shell\r\n")
            request = bytearray()
            while not request.endswith(b"\r"):
                request.extend(channel.recv(1024))
            assert request == b"pwd\r"
            channel.sendall(b"/work/project\r\n")
        finally:
            channel.close()


def main():
    repo = Path(__file__).resolve().parents[2]
    key = paramiko.RSAKey.generate(2048)
    fingerprint = base64.b64encode(hashlib.sha256(key.asbytes()).digest()).decode().rstrip("=")
    env = dict(os.environ, PYTHONPATH=str(repo / "engine"), PYTHONDONTWRITEBYTECODE="1")
    with socket.create_server(("127.0.0.1", 0)) as listener:
        def serve():
            for _ in range(2):
                client, _ = listener.accept()
                transport = paramiko.Transport(client)
                transport.add_server_key(key)
                transport.start_server(server=Server())
                transports.append(transport)

        transports = []
        threading.Thread(target=serve, daemon=True).start()
        try:
            subprocess.run(["dotnet", "run", "--project", str(Path(__file__).with_name("Smoke.csproj")),
                            "--", str(listener.getsockname()[1]), fingerprint, sys.executable],
                           env=env, check=True, timeout=120)
        finally:
            for transport in transports:
                transport.close()


if __name__ == "__main__":
    main()
