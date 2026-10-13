"""Explicitly execute a test module with new coverage data and source snapshots."""

import argparse
from datetime import datetime, timezone
import json
import math
import os
from pathlib import Path
import signal
import subprocess
import sys
import uuid

from coverage_provenance import POLICY, snapshot_digest, source_snapshot
import coverage_review
import source_facts


def now():
    return datetime.now(timezone.utc).isoformat()


def run_command(command, root, output, timeout):
    environment = {key: value for key, value in os.environ.items()
                   if not key.upper().startswith(("COVERAGE_", "PYTHON")) and key.upper() != "PYTEST_ADDOPTS"}
    # Third-party pytest auto-plugins can start their own competing coverage session.
    environment["PYTEST_DISABLE_PLUGIN_AUTOLOAD"] = "1"
    options = {"creationflags": subprocess.CREATE_NEW_PROCESS_GROUP} if os.name == "nt" else {"start_new_session": True}
    with (output / "tests.log").open("w", encoding="utf-8") as log:
        with subprocess.Popen(command, cwd=root, env=environment, stdout=log, stderr=subprocess.STDOUT, **options) as child:
            try:
                return child.wait(timeout=timeout)
            except subprocess.TimeoutExpired:
                if os.name == "nt":
                    subprocess.run(["taskkill", "/PID", str(child.pid), "/T", "/F"], capture_output=True, check=False)
                else:
                    os.killpg(child.pid, signal.SIGKILL)
                child.kill()
                child.wait()
                raise ValueError("Coverage test command timed out; collection is incomplete.")


def collect(target, output, *, python=sys.executable, module="pytest", arguments=(), timeout=1800, **selection):
    if not math.isfinite(timeout) or timeout <= 0 or not module or module.startswith("-"):
        raise ValueError("Provide a module name and a positive timeout.")
    root = Path(target).resolve()
    output = Path(output).resolve()
    executable = Path(python).resolve()
    if not root.is_dir() or not executable.is_file():
        raise ValueError("Collection requires a project directory and a Python executable.")
    if output.is_relative_to(root):
        raise ValueError("Collection output must be outside the target root.")
    output.mkdir(parents=False, exist_ok=False)
    manifest = {"schemaVersion": 1, "policy": POLICY, "collectionId": str(uuid.uuid4()),
                "startedAtUtc": now(), "finishedAtUtc": None, "root": str(root), "status": "incomplete",
                "configuration": {"branch": True, "configFile": False, "processScope": "single-process", "freshBytecodeCache": True},
                "sourceSelection": None, "beforeSourceSha256": None, "afterSourceSha256": None,
                "sourceFactsPolicy": None, "sourceFactsProducer": None, "reportSha256": None,
                "command": [], "runtime": None, "testExitCode": None, "diagnostics": []}
    try:
        before = source_facts.analyze(root, **selection)
        if before["status"] != "complete":
            raise ValueError(f"Source snapshot failed: {before['diagnostics']}")
        manifest.update(sourceSelection=source_snapshot(before["selection"]), beforeSourceSha256=snapshot_digest(before["selection"]),
                        sourceFactsPolicy=before["measurementPolicy"], sourceFactsProducer=before["producer"])
        command = [str(executable), "-I", "-X", f"pycache_prefix={output / 'bytecode'}",
                   str(Path(__file__).with_name("coverage_worker.py").resolve()), str(root), str(output), module, *arguments]
        manifest["command"] = command
        manifest["testExitCode"] = run_command(command, root, output, timeout)
        after = source_facts.analyze(root, **selection)
        manifest["afterSourceSha256"] = snapshot_digest(after["selection"])
        if after["status"] != "complete" or manifest["beforeSourceSha256"] != manifest["afterSourceSha256"]:
            raise ValueError("Source inventory changed during collection.")
        if manifest["testExitCode"] != 0:
            raise ValueError(f"Test command exited {manifest['testExitCode']}; see tests.log.")
        runtime = json.loads((output / "runtime.json").read_text(encoding="utf-8"))
        manifest["runtime"] = runtime
        if type(runtime.get("testExitCode")) is not int or runtime["testExitCode"] != 0:
            raise ValueError("Worker did not record successful test completion.")
        _, provenance = coverage_review.load_coverage(output / "coverage.json")
        manifest["reportSha256"] = provenance["sha256"]
        manifest["status"] = "complete"
    except (OSError, ValueError, RecursionError) as error:
        manifest["diagnostics"].append({"code": "coverage-collection", "message": str(error)})
    manifest["finishedAtUtc"] = now()
    with (output / "provenance.json").open("x", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2)
        handle.write("\n")
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target", type=Path)
    parser.add_argument("--output-directory", type=Path, required=True)
    parser.add_argument("--python", default=sys.executable, help="Python 3.12 with coverage and test dependencies installed")
    parser.add_argument("--module", default="pytest", help="Explicit test module to execute")
    parser.add_argument("--timeout", type=float, default=1800)
    for option in ("include", "exclude", "test-path", "production-path"):
        parser.add_argument(f"--{option}", action="append", default=[])
    for option in ("include-tests", "include-generated", "include-support"):
        parser.add_argument(f"--{option}", action="store_true")
    # Split explicitly: argparse REMAINDER would consume collector options after target.
    argv = sys.argv[1:]
    separator = argv.index("--") if "--" in argv else len(argv)
    args = parser.parse_args(argv[:separator])
    try:
        result = collect(args.target, args.output_directory, python=args.python, module=args.module,
                         arguments=argv[separator + 1:], timeout=args.timeout,
                         include=args.include, exclude=args.exclude, test_paths=args.test_path, production_paths=args.production_path,
                         include_tests=args.include_tests, include_generated=args.include_generated, include_support=args.include_support)
        print(json.dumps({"status": result["status"], "collectionId": result["collectionId"],
                          "directory": str(args.output_directory), "diagnostics": result["diagnostics"]}))
        return 0 if result["status"] == "complete" else 2
    except (OSError, ValueError) as error:
        print(f"Cannot collect coverage: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
