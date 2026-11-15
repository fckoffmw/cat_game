"""Launch independent macOS players and save PR4 screenshots or PR5 traffic samples."""
import argparse
import asyncio
from pathlib import Path
from time import monotonic

from udp_lab import Relay


async def wait_for(log, marker, timeout=50):
    deadline = monotonic() + timeout
    while monotonic() < deadline:
        content = log.read_text(errors="replace") if log.exists() else ""
        if "Exception:" in content:
            raise RuntimeError(f"Unity exception in {log}")
        if marker in content:
            return
        await asyncio.sleep(.1)
    raise TimeoutError(f"{log}: missing {marker}")


async def scenario(options, name, port, delay=0, loss=0, full=False):
    fish = name in ("full", "events")
    processes, relays, logs = [], [], []

    async def launch(role, endpoint, args, graphics=False):
        log = options.output / f"{name}-{role}.log"
        log.write_text("")
        logs.append(log)
        display = ["-screen-fullscreen", "0", "-screen-width", "960", "-screen-height", "600"]
        process = await asyncio.create_subprocess_exec(
            str(options.player), *(display if graphics else ["-batchmode", "-nographics"]),
            "-roofProbe", "-roofPort", str(endpoint), *args, "-logFile", str(log),
            stdout=asyncio.subprocess.DEVNULL, stderr=asyncio.subprocess.DEVNULL)
        processes.append(process)
        return log

    try:
        host_args = ["-roofHost"]
        if fish:
            host_args += ["-sixty", "-fishTrial"]
        if full:
            host_args.append("-fullFish")
        host = await launch("host", port, host_args)
        await wait_for(host, "HOST_READY")
        for index in range(2 if fish else 1):
            endpoint = port + index + 1
            relay = Relay(port, 0 if loss else delay, 0 if loss else loss, 42 + index)
            transport, _ = await asyncio.get_running_loop().create_datagram_endpoint(
                lambda: relay, local_addr=("127.0.0.1", endpoint))
            relays.append((transport, relay))
            args = ["-fishTrial"] if fish else ["-roofCapture", str(options.output / name)]
            if name == "baseline":
                args.append("-baseline")
            client = await launch(f"client{index}", endpoint, args, graphics=not fish)
            await wait_for(client, "CLIENT_READY")
            if loss:
                # Measure gameplay after joining. Keep scene synchronization out of this experiment.
                relay.delay = delay / 1000
                relay.loss = loss / 100
            if fish and index == 0:
                await wait_for(host, "FISH_HELD")

        if fish:
            await wait_for(host, "FISH_FINISHED")
            initial = sum(relay.out_bytes for _, relay in relays)
            start = monotonic()
            await asyncio.sleep(60)
            seconds = monotonic() - start
            size = sum(relay.out_bytes for _, relay in relays) - initial
            await wait_for(host, "FISH_SAMPLE")
            for log in logs[1:]:
                await wait_for(log, "FISH_CLIENT restored=60")
            result = f"{name} seconds={seconds:.3f} payload_bytes={size} kB/s={size/seconds/1000:.3f}"
            (options.output / f"{name}-traffic.txt").write_text(result + "\n")
            print(result, flush=True)
        else:
            await wait_for(client, "PROBE_END")
            await wait_for(host, " REJECT #")
            await wait_for(client, "VENT_OK")
            for moment in ("jump", "correction"):
                capture = options.output / f"{name}-{moment}.png"
                if not capture.exists():
                    raise RuntimeError(f"Missing {capture}")
        print(name, "PASS", flush=True)
    finally:
        for process in processes:
            if process.returncode is None:
                process.terminate()
        await asyncio.gather(*(p.wait() for p in processes))
        for transport, _ in relays:
            transport.close()


async def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("suite", choices=["prediction", "fish", "loss"])
    parser.add_argument("player", type=Path, help="executable inside Rooftop.app/Contents/MacOS")
    parser.add_argument("output", type=Path)
    parser.add_argument("--base-port", type=int, default=23770)
    args = parser.parse_args()
    args.output = args.output.resolve()
    args.output.mkdir(parents=True, exist_ok=True)
    if args.suite == "loss":
        await scenario(args, "loss", args.base_port + 30, 175, 4)
    elif args.suite == "fish":
        for i, name in enumerate(("full", "events")):
            await scenario(args, name, args.base_port + 40 + 10*i, full=i == 0)
    else:
        for i, (name, delay, loss) in enumerate((
                ("direct", 0, 0), ("baseline", 100, 0), ("prediction", 100, 0), ("loss", 175, 4))):
            await scenario(args, name, args.base_port + 10*i, delay, loss)


if __name__ == "__main__":
    asyncio.run(main())
