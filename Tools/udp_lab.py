"""Local UDP relay for a single Unity client. Use one listening port per client."""
import argparse
import asyncio
import random


class Relay(asyncio.DatagramProtocol):
    def __init__(self, server, delay, loss, seed):
        self.server = ("127.0.0.1", server)
        self.client = None
        self.delay = delay / 1000
        self.loss = loss / 100
        self.random = random.Random(seed)
        self.out_bytes = 0

    def connection_made(self, transport):
        self.transport = transport

    def datagram_received(self, data, peer):
        if peer == self.server:
            destination = self.client
            self.out_bytes += len(data)
        else:
            self.client = peer
            destination = self.server
        if destination and self.random.random() >= self.loss:
            asyncio.get_running_loop().call_later(
                self.delay, self.forward, data, destination)

    def forward(self, data, destination):
        if not self.transport.is_closing():
            self.transport.sendto(data, destination)


async def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=7778)
    parser.add_argument("--server", type=int, default=7777)
    parser.add_argument("--delay", type=int, default=100, help="one-way milliseconds")
    parser.add_argument("--loss", type=float, default=0, help="drop probability in percent")
    parser.add_argument("--seed", type=int, default=42)
    args = parser.parse_args()
    relay = Relay(args.server, args.delay, args.loss, args.seed)
    await asyncio.get_running_loop().create_datagram_endpoint(
        lambda: relay, local_addr=("127.0.0.1", args.port))
    print("Client port", args.port, "server port", args.server, flush=True)
    await asyncio.Future()


if __name__ == "__main__":
    asyncio.run(main())
