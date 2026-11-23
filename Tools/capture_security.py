"""Capture the actual loopback interface while running the security scenarios on macOS."""
import argparse
import asyncio
import signal
from pathlib import Path

from check_security import run


async def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('player', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--base-port', type=int, default=27300)
    parser.add_argument('--dumpcap', type=Path, default=Path('/Applications/Wireshark.app/Contents/MacOS/dumpcap'))
    options = parser.parse_args()
    options.player = options.player.resolve()
    options.output = options.output.resolve()
    options.output.mkdir(parents=True, exist_ok=True)
    for plain, port in ((True, options.base_port), (False, options.base_port + 10)):
        mode = 'plaintext' if plain else 'encrypted'
        capture = options.output / (mode + '-loopback.pcapng')
        log = options.output / (mode + '-capture.txt')
        with log.open('w') as stream:
            recorder = await asyncio.create_subprocess_exec(
                str(options.dumpcap), '-i', 'lo0', '-f', f'udp port {port} or udp port {port + 1}',
                '-w', str(capture), stdout=stream, stderr=stream)
            try:
                await asyncio.sleep(1)
                if recorder.returncode is not None:
                    raise RuntimeError(f'Capture failed. See {log}')
                await run(options, plain, port)
            finally:
                if recorder.returncode is None:
                    recorder.send_signal(signal.SIGINT)
                await recorder.wait()
            if recorder.returncode != 0:
                raise RuntimeError(f'Capture failed. See {log}')
        print(f'INTERFACE CAPTURE PASS {capture.name}', flush=True)


if __name__ == '__main__':
    asyncio.run(main())
