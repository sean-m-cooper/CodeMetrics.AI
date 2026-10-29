"""Local collection receipts bind reports to source observations, not signatures."""

from datetime import datetime
import json
import os
from pathlib import Path
import uuid

from duplication import digest


POLICY = "python/coverage-collection-v1"
MAX_MANIFEST_BYTES = 16 * 1024 * 1024


def source_snapshot(selection):
    """Bind selected source, not incidental creation of ignored cache directories."""
    return {"policy": selection["policy"], "configurationFingerprint": selection["configurationFingerprint"],
            "options": selection["options"], "files": [row for row in selection["files"] if row["disposition"] == "analyzed"],
            "excludedFunctions": selection["excludedFunctions"]}


def snapshot_digest(selection):
    return digest(json.dumps(source_snapshot(selection), sort_keys=True, separators=(",", ":")))


def validate(path, root, facts, coverage_input, load_json):
    """Fail closed when the caller explicitly requests collection verification."""
    if coverage_input is None:
        raise ValueError("Collection provenance requires a coverage JSON report.")
    with Path(path).open("rb") as handle:
        raw = handle.read(MAX_MANIFEST_BYTES + 1)
    if len(raw) > MAX_MANIFEST_BYTES:
        raise ValueError("Collection provenance exceeds the manifest byte budget.")
    manifest = load_json(raw)
    if not isinstance(manifest, dict) or type(manifest.get("schemaVersion")) is not int or manifest["schemaVersion"] != 1:
        raise ValueError("Unsupported collection provenance schema.")
    if manifest.get("policy") != POLICY or manifest.get("status") != "complete":
        raise ValueError("Collection did not complete under the supported provenance policy.")
    try:
        collection_id = str(uuid.UUID(manifest["collectionId"]))
        started = datetime.fromisoformat(manifest["startedAtUtc"])
        finished = datetime.fromisoformat(manifest["finishedAtUtc"])
        if started.tzinfo is None or finished.tzinfo is None or finished < started:
            raise ValueError("Invalid collection timestamps.")
        subject = manifest["root"]
        if not isinstance(subject, str) or os.path.normcase(str(Path(subject).resolve())) != os.path.normcase(str(root.resolve())):
            raise ValueError("Collection root does not match the reviewed root.")
        if type(manifest["testExitCode"]) is not int or manifest["testExitCode"] != 0:
            raise ValueError("Collection test command did not succeed.")
        if manifest["reportSha256"] != coverage_input["sha256"]:
            raise ValueError("Coverage report differs from the collected report.")
        if coverage_input["branchDataPresent"] is not True:
            raise ValueError("Collected report is missing the configured branch data.")
        expected = snapshot_digest(facts["selection"])
        if manifest["beforeSourceSha256"] != expected or manifest["afterSourceSha256"] != expected:
            raise ValueError("Current source selection differs from collection snapshots.")
        if manifest["sourceSelection"] != source_snapshot(facts["selection"]):
            raise ValueError("Collection source inventory does not match current source.")
        if manifest["sourceFactsPolicy"] != facts["measurementPolicy"] or manifest["sourceFactsProducer"] != facts["producer"]:
            raise ValueError("Collection source measurement policy or parser differs.")
        runtime = manifest["runtime"]
        if (type(runtime["testExitCode"]) is not int or runtime["testExitCode"] != 0 or
                runtime["coverageVersion"] != coverage_input["version"] or not runtime["pythonVersion"].startswith("3.12.")):
            raise ValueError("Collection runtime does not match coverage metadata or supported Python.")
        if manifest["configuration"] != {"branch": True, "configFile": False, "processScope": "single-process", "freshBytecodeCache": True}:
            raise ValueError("Unsupported coverage collection configuration.")
        command = manifest["command"]
        if not isinstance(command, list) or not command or any(not isinstance(value, str) for value in command):
            raise ValueError("Collection command is missing or malformed.")
    except (KeyError, TypeError, AttributeError) as error:
        raise ValueError("Malformed collection provenance.") from error
    return {"path": str(Path(path).absolute()), "sha256": digest(raw.decode("utf-8")),
            "collectionId": collection_id, "policy": POLICY, "startedAtUtc": manifest["startedAtUtc"],
            "finishedAtUtc": manifest["finishedAtUtc"], "sourceBinding": "matched-pre-post-snapshots",
            "freshness": "source-matched", "command": command, "receipt": manifest,
            "limitations": "Local unsigned receipt; pre/post observations cannot detect edits reverted during execution, prove assertion quality, or cover subprocess execution."}
