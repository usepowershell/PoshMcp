"""Provision pinned inputs on dedicated native CI runners, not the shared workspace."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tarfile
import urllib.request
import zipfile

from validate import ROOT, native_rid

VERSION = "7.6.0"
HASHES = {
    "linux-x64": "04517472cf57d7f9cbd93897da9bed467c73ca6063c29d7655ebc20aa1d6023f",
    "linux-arm64": "dddf7564fb3b52dc26be5580fc5b4e08eb3fa65b094488aae6d4b3cad5fea460",
    "win-x64": "9e725837af682b87bb212cd1efe3657c06c540404203810857ec2516ae2ca322",
    "win-arm64": "c2ac26385210ebff5b0696bfb26bc74a9867fd9d561a75ea52635e553974f0cb",
}


def provision(rid, kind):
    if rid != native_rid():
        raise RuntimeError(f"Runner {native_rid()} cannot qualify {rid}; emulation is not accepted.")
    if kind not in ("PowerShell7", "WindowsPowerShell51") or (kind == "WindowsPowerShell51" and rid != "win-x64"):
        raise RuntimeError("Unknown/unqualified runtime combination.")
    root = ROOT / ".runtime"
    root.mkdir(exist_ok=True)
    windows = rid.startswith("win-")
    name = f"PowerShell-{VERSION}-{rid}.zip" if windows else f"powershell-{VERSION}-{rid}.tar.gz"
    archive = root / name
    with urllib.request.urlopen(f"https://github.com/PowerShell/PowerShell/releases/download/v{VERSION}/{name}", timeout=120) as source, archive.open("wb") as output:
        while chunk := source.read(1024 * 1024):
            output.write(chunk)
    with archive.open("rb") as source:
        digest = hashlib.file_digest(source, "sha256").hexdigest()
    if digest != HASHES[rid]:
        raise RuntimeError("PowerShell release checksum mismatch; refusing extraction/execution.")
    runtime_dir = root / rid
    runtime_dir.mkdir(exist_ok=True)
    if windows:
        with zipfile.ZipFile(archive) as package:
            for entry in package.infolist():
                if not (runtime_dir / entry.filename).resolve().is_relative_to(runtime_dir.resolve()):
                    raise RuntimeError("Unsafe runtime archive path.")
            package.extractall(runtime_dir)
    else:
        with tarfile.open(archive) as package:
            package.extractall(runtime_dir, filter="data")
    pwsh = runtime_dir / ("pwsh.exe" if windows else "pwsh")
    if not windows:
        pwsh.chmod(0o755)
    modules = ROOT / ".modules"
    modules.mkdir(exist_ok=True)
    environment = os.environ.copy()
    environment["POSHMCP_MODULE_DESTINATION"] = str(modules)
    subprocess.run([str(pwsh), "-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
                    "$ErrorActionPreference='Stop'; Save-Module PSScriptAnalyzer -RequiredVersion 1.24.0 -Path $env:POSHMCP_MODULE_DESTINATION -Repository PSGallery"], env=environment, check=True)
    executable = pwsh
    if kind == "WindowsPowerShell51":
        executable = Path(os.environ["SystemRoot"]) / "System32/WindowsPowerShell/v1.0/powershell.exe"
        if not executable.is_file():
            raise RuntimeError("Explicit Windows PowerShell 5.1 is unavailable; no fallback.")
    (root / f"{rid}-{kind}-inputs.json").write_text(json.dumps({
        "rid": rid, "runtime": kind, "powerShellAsset": name, "sha256": digest,
        "analyzer": "PSScriptAnalyzer@1.24.0", "nativeHost": native_rid()
    }, indent=2))
    with open(os.environ["GITHUB_ENV"], "a", encoding="utf-8") as output:
        for key, value in {
            "POSHMCP_TEST_RUNTIME": executable, "POSHMCP_TEST_KIND": kind,
            "POSHMCP_LINT_RUNTIME": pwsh, "POSHMCP_TEST_MODULE_PATH": modules
        }.items():
            output.write(f"{key}={value}\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("rid", choices=HASHES)
    parser.add_argument("kind", choices=["PowerShell7", "WindowsPowerShell51"])
    args = parser.parse_args()
    provision(args.rid, args.kind)
