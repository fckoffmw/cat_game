"""Run plaintext/encrypted NGO players through a recording UDP relay on localhost."""
import argparse
import asyncio
import socket
import struct
import time
from pathlib import Path

from check_practice import wait_for
from udp_lab import Relay


def checksum(data):
    if len(data) % 2:
        data += b"\0"
    value = sum(struct.unpack("!%dH" % (len(data) // 2), data))
    while value >> 16:
        value = (value & 65535) + (value >> 16)
    return (~value) & 65535


class CaptureRelay(Relay):
    def __init__(self, port, capture):
        super().__init__(port, 0, 0, 42)
        self.capture = capture
        self.corrupt_next = False
        self.corrupted = 0
        self.encrypted = 0
        self.capture.write(struct.pack("<IHHIIII", 0xa1b2c3d4, 2, 4, 0, 0, 65535, 101))

    def datagram_received(self, data, peer):
        marker = data.find(b"RF6\x01")
        if marker >= 0 and len(data) > marker + 41 and data[marker + 4] & 15 == 11:
            self.encrypted += 1
            if self.corrupt_next and peer != self.server:
                altered = bytearray(data)
                altered[marker + 25] ^= 1
                data = bytes(altered)
                self.corrupt_next = False
                self.corrupted += 1

        destination = self.client if peer == self.server else self.server
        if destination:
            ip = socket.inet_aton("127.0.0.1")
            udp = struct.pack("!HHHH", peer[1], destination[1], 8 + len(data), 0)
            header = struct.pack("!BBHHHBBH4s4s", 0x45, 0, 28 + len(data), 0, 0, 64, 17, 0, ip, ip)
            header = header[:10] + struct.pack("!H", checksum(header)) + header[12:]
            packet = header + udp + data
            now = time.time()
            self.capture.write(struct.pack("<IIII", int(now), int(now % 1 * 1e6), len(packet), len(packet)))
            self.capture.write(packet)
        super().datagram_received(data, peer)


async def run(options, plain, port):
    mode = "plaintext" if plain else "encrypted"
    processes = []
    with (options.output / (mode + ".pcap")).open("wb") as capture:
        relay = CaptureRelay(port, capture)
        transport, _ = await asyncio.get_running_loop().create_datagram_endpoint(
            lambda: relay, local_addr=("127.0.0.1", port + 1))
        async def launch(role, endpoint):
            log = options.output / (mode + "-" + role + ".log")
            args = ["-roofHost"] if role == "host" else []
            if plain:
                args.append("-roofPlaintext")
            process = await asyncio.create_subprocess_exec(
                str(options.player), "-batchmode", "-nographics", "-roofProbe",
                "-roofPort", str(endpoint), *args, "-logFile", str(log),
                stdout=asyncio.subprocess.DEVNULL, stderr=asyncio.subprocess.DEVNULL)
            processes.append(process)
            return log
        try:
            host = await launch("host", port)
            await wait_for(host, "HOST_READY")
            client = await launch("client", port + 1)
            await wait_for(client, "CLIENT_READY")
            relay.corrupt_next = not plain
            await wait_for(client, "PROBE_END")
            await wait_for(host, " REJECT #")
            if not plain:
                await wait_for(host, "PR6 AUTH_FAIL")
                await wait_for(client, "PR6 SECURE_CONNECTED")
                assert relay.encrypted > 0 and relay.corrupted == 1
            result = f"{mode} PASS encrypted_messages={relay.encrypted} tampered={relay.corrupted}"
            print(result, flush=True)
            (options.output / (mode + "-result.txt")).write_text(result + "\n")
        finally:
            transport.close()
            for process in processes:
                if process.returncode is None:
                    process.terminate()
            await asyncio.gather(*(process.wait() for process in processes))


async def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("player", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--base-port", type=int, default=25300)
    options = parser.parse_args()
    options.player = options.player.resolve()
    options.output = options.output.resolve()
    options.output.mkdir(parents=True, exist_ok=True)
    await run(options, True, options.base_port)
    await run(options, False, options.base_port + 10)


if __name__ == "__main__":
    asyncio.run(main())
