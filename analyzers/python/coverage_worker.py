"""Private child process for explicit coverage collection; executes target tests."""

import json
import os
from pathlib import Path
import platform
import runpy
import sys
import traceback


def main():
    if sys.version_info[:2] != (3, 12):
        raise ValueError("Coverage collection requires Python 3.12.")
    # Import the tooling before adding the target root to module lookup.
    import coverage

    root, output, module, *arguments = sys.argv[1:]
    output = Path(output)
    os.chdir(root)
    sys.path.insert(0, root)
    sys.argv = [module, *arguments]
    measurement = coverage.Coverage(data_file=str(output / "raw.coverage"), source=[root],
                                    branch=True, config_file=False)
    measurement.start()
    exit_code = 0
    try:
        runpy.run_module(module, run_name="__main__", alter_sys=True)
    except SystemExit as error:
        # pytest uses an IntEnum; SystemExit also permits bool as an integer code.
        exit_code = int(error.code) if isinstance(error.code, int) else (0 if error.code is None else 1)
    except BaseException:
        traceback.print_exc()
        exit_code = 1
    finally:
        measurement.stop()
        measurement.save()
    measurement.json_report(outfile=str(output / "coverage.json"))
    receipt = {"coverageVersion": coverage.__version__, "pythonVersion": platform.python_version(),
               "testExitCode": exit_code}
    (output / "runtime.json").write_text(json.dumps(receipt), encoding="utf-8")
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
