"""P0 package/provenance validation; no registry writes in this module."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import tempfile
import zipfile
import xml.etree.ElementTree as ET

from validate import ROOT, PROJECT, native_rid, run, runtime_environment

RIDS = ("linux-x64", "linux-arm64", "win-x64", "win-arm64")
TAG = re.compile(r"v(1\.0\.0(?:-(?:alpha|beta)\.(?:0|[1-9][0-9]*))?)")


def version_for_event(event, ref, sha, run_id, attempt):
    if not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise ValueError("Exact commit SHA is required.")
    if event == "tag":
        match = TAG.fullmatch(ref)
        if not match:
            raise ValueError("Expected v1.0.0-alpha.N, v1.0.0-beta.N or v1.0.0; no build metadata or mismatched versions.")
        return match[1]
    if event != "main" or ref != "main" or not run_id.isdigit() or not attempt.isdigit():
        raise ValueError("Main package provenance is incomplete.")
    return f"1.0.0-alpha.ci.{run_id}.{attempt}.g{sha[:12]}"


def sha256(path):
    with Path(path).open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def inspect_package(path, rid, version):
    with zipfile.ZipFile(path) as package:
        names = package.namelist()
        if len(names) != len(set(names)) or any(Path(n).is_absolute() or ".." in Path(n).parts for n in names):
            raise ValueError("Unsafe or ambiguous package entries.")
        nuspecs = [n for n in names if n.endswith(".nuspec")]
        if len(nuspecs) != 1:
            raise ValueError("Package must have exactly one nuspec.")
        metadata = ET.fromstring(package.read(nuspecs[0]))
        if metadata.findtext(".//{*}id") != f"PoshMcp.Foundation.{rid}" or metadata.findtext(".//{*}version") != version:
            raise ValueError("Package identity/version mismatch.")
        launcher = "poshmcp-launcher.exe" if rid.startswith("win-") else "poshmcp-launcher"
        required = ["PoshMcp.Foundation.dll", "PoshMcp.Foundation.runtimeconfig.json", launcher, "DotnetToolSettings.xml"]
        for name in required:
            if "tools/net11.0/any/" + name not in names:
                raise ValueError(f"Missing tool payload {name}.")
        if any("System.Management.Automation" in n or n.endswith(".ps1") for n in names):
            raise ValueError("Embedded PowerShell or loose NuGet installation hooks are forbidden.")
        return {
            n: hashlib.sha256(package.read(n)).hexdigest()
            for n in sorted(names) if n != ".signature.p7s"
        }


def pack(rid, version, output):
    if native_rid() != rid:
        raise ValueError(f"Native host {native_rid()} cannot package/qualify {rid}.")
    output = Path(output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    run("dotnet", "pack", PROJECT, "-c", "Release", f"-p:TargetRid={rid}", f"-p:Version={version}",
        f"-p:RepositoryCommit={os.environ.get('GITHUB_SHA', '')}", "-o", output)
    path = output / f"PoshMcp.Foundation.{rid}.{version}.nupkg"
    payload = inspect_package(path, rid, version)
    with tempfile.TemporaryDirectory(prefix="poshmcp-package-install-") as directory:
        # Local-source-only config prevents a public feed from satisfying the smoke test.
        config = Path(directory) / "NuGet.Config"
        root = ET.Element("configuration")
        feeds = ET.SubElement(root, "packageSources")
        ET.SubElement(feeds, "clear")
        ET.SubElement(feeds, "add", key="local", value=str(output))
        ET.ElementTree(root).write(config, encoding="utf-8", xml_declaration=True)
        tool = Path(directory) / "tool"
        run("dotnet", "tool", "install", f"PoshMcp.Foundation.{rid}", "--version", version,
            "--configfile", config, "--tool-path", tool)
        executable = tool / ("poshmcp-foundation.exe" if rid.startswith("win-") else "poshmcp-foundation")
        run(executable, "--foundation-info")
        result = run(executable, "--probe", os.environ["POSHMCP_TEST_RUNTIME"],
                     os.environ["POSHMCP_TEST_KIND"], capture=True, env=runtime_environment())
        evidence = json.loads(result.stdout)
        if not evidence["result"]["success"] or evidence["measurement"]["rid"] != rid:
            raise ValueError("Installed package did not invoke the requested native runtime.")
    record = {
        "rid": rid, "id": f"PoshMcp.Foundation.{rid}", "version": version, "file": path.name,
        "sha256": sha256(path), "payload": payload, "commit": os.environ.get("GITHUB_SHA"),
        "runId": os.environ.get("GITHUB_RUN_ID"), "attempt": os.environ.get("GITHUB_RUN_ATTEMPT"),
        "nativeInstallation": evidence
    }
    (output / f"{rid}.json").write_text(json.dumps(record, indent=2) + "\n")


def manifest(directory, version, commit):
    directory = Path(directory)
    records = []
    for rid in RIDS:
        record_path = directory / f"{rid}.json"
        if not record_path.is_file():
            raise ValueError(f"Missing required native package record {rid}.")
        record = json.loads(record_path.read_text())
        if (record["rid"] != rid or record["id"] != f"PoshMcp.Foundation.{rid}"
                or record["file"] != f"PoshMcp.Foundation.{rid}.{version}.nupkg"
                or record["version"] != version or record["commit"] != commit):
            raise ValueError("Package matrix provenance mismatch.")
        path = directory / record["file"]
        if sha256(path) != record["sha256"] or inspect_package(path, rid, version) != record["payload"]:
            raise ValueError("Package bytes/payload do not match native validation record.")
        records.append(record)
    if len(list(directory.glob("*.nupkg"))) != 4:
        raise ValueError("Publication requires exactly four packages.")
    result = {"schema": 1, "version": version, "commit": commit, "packages": records}
    (directory / "manifest.json").write_text(json.dumps(result, indent=2) + "\n")
    (directory / "SHA256SUMS").write_text("".join(f"{r['sha256']}  {r['file']}\n" for r in records))
    return result


def main():
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="operation", required=True)
    meta = sub.add_parser("version")
    meta.add_argument("event", choices=["main", "tag"])
    build = sub.add_parser("pack")
    build.add_argument("rid", choices=RIDS)
    build.add_argument("version")
    build.add_argument("output")
    assemble = sub.add_parser("manifest")
    assemble.add_argument("directory")
    assemble.add_argument("version")
    assemble.add_argument("commit")
    args = parser.parse_args()
    if args.operation == "version":
        commit = run("git", "rev-parse", "HEAD", capture=True).stdout.strip()
        expected = os.environ["GITHUB_SHA"]
        tagged = run("git", "rev-parse", f"{os.environ['GITHUB_REF']}^{{commit}}", capture=True).stdout.strip()
        if commit != expected or tagged != expected:
            raise ValueError("Checkout/tag commit is not the exact event commit.")
        version = version_for_event(args.event, os.environ["GITHUB_REF_NAME"], commit,
                                    os.environ["GITHUB_RUN_ID"], os.environ["GITHUB_RUN_ATTEMPT"])
        with open(os.environ["GITHUB_OUTPUT"], "a") as output:
            output.write(f"version={version}\ncommit={commit}\n")
    elif args.operation == "pack":
        pack(args.rid, args.version, args.output)
    else:
        manifest(args.directory, args.version, args.commit)


if __name__ == "__main__":
    main()
