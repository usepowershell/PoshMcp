"""Trusted workflow-only immutable publication and canonical tag reruns."""
import argparse
import base64
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
import zipfile

from packages import TAG, inspect_package, manifest, sha256, version_for_event


def command(*args):
    result = subprocess.run([str(a) for a in args], text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    if result.returncode:
        # Do not include API keys, command lines, or arbitrary registry responses in an exception.
        raise RuntimeError(f"Publication command {args[0]} failed with exit {result.returncode}.")
    return result.stdout


def get(url, headers=None):
    try:
        with urllib.request.urlopen(urllib.request.Request(url, headers=headers or {}), timeout=120) as response:
            return response.read()
    except urllib.error.HTTPError as error:
        if error.code == 404:
            return None
        raise


def registry(name):
    if name == "github":
        owner = os.environ["GITHUB_REPOSITORY_OWNER"]
        token = os.environ["GH_TOKEN"]
        basic = base64.b64encode(f"{os.environ['GITHUB_ACTOR']}:{token}".encode()).decode()
        return f"https://nuget.pkg.github.com/{owner}/index.json", token, {"Authorization": "Basic " + basic}
    if name == "nuget":
        return "https://api.nuget.org/v3/index.json", os.environ.get("NUGET_API_KEY", ""), {}
    raise ValueError("Unknown registry.")


def existing_package(record, index, headers):
    document = get(index, headers)
    if document is None:
        raise RuntimeError("Registry service index is unavailable; not treating it as an absent package.")
    addresses = [r["@id"] for r in json.loads(document)["resources"]
                 if r["@type"].startswith("PackageBaseAddress/")]
    if not addresses:
        raise RuntimeError("Registry does not advertise a package download endpoint.")
    identity, version = record["id"].lower(), record["version"].lower()
    return get(addresses[0].rstrip("/") + f"/{identity}/{version}/{identity}.{version}.nupkg", headers)


def verify_download(record, data, name):
    if data is None:
        raise RuntimeError("Package is not yet downloadable after publication.")
    with tempfile.TemporaryDirectory(prefix="poshmcp-registry-verify-") as directory:
        path = Path(directory) / record["file"]
        path.write_bytes(data)
        if sha256(path) == record["sha256"]:
            return
        # nuget.org repository-signs uploads, changing container bytes but not payload.
        if name != "nuget":
            raise RuntimeError("Existing package bytes conflict with the immutable validated artifact.")
        with zipfile.ZipFile(path) as package:
            if ".signature.p7s" not in package.namelist():
                raise RuntimeError("NuGet package differs without a repository signature.")
        command("dotnet", "nuget", "verify", "--all", path)
        if inspect_package(path, record["rid"], record["version"]) != record["payload"]:
            raise RuntimeError("Repository-signed NuGet package payload conflicts with the validated artifact.")


def publish_set(directory, record_set, name):
    index, token, headers = registry(name)
    if not token:
        raise RuntimeError("Publishing credential is not configured; no registry write attempted.")
    for record in record_set["packages"]:
        data = existing_package(record, index, headers)
        if data is not None:
            verify_download(record, data, name)
            print(f"Verified existing immutable {name} package {record['id']} {record['version']}.")
            continue
        path = Path(directory) / record["file"]
        try:
            command("dotnet", "nuget", "push", path, "--source", index, "--api-key", token)
        except RuntimeError:
            # A competing upload is only acceptable if the resulting package verifies.
            data = existing_package(record, index, headers)
            if data is None:
                raise
            verify_download(record, data, name)
        for _ in range(120):
            data = existing_package(record, index, headers)
            if data is not None:
                verify_download(record, data, name)
                break
            time.sleep(5)
        else:
            raise RuntimeError("Published package did not become downloadable; rerun will verify/resume.")
        print(f"Verified published {name} package {record['id']} {record['version']}.")


def release_state(tag):
    data = get(f"https://api.github.com/repos/{os.environ['GITHUB_REPOSITORY']}/releases/tags/{tag}",
               {"Authorization": "Bearer " + os.environ["GH_TOKEN"], "Accept": "application/vnd.github+json"})
    return None if data is None else json.loads(data)


def release_notes(version, commit):
    return (
        f"# PoshMcp foundation {version}\n\nValidated commit: `{commit}`.\n\n"
        "Four native .NET-tool foundation packages. Install the matching RID package at this exact version.\n"
        "Prerequisites: .NET 11, separately provisioned native PowerShell 7.6.0; "
        "Windows PowerShell 5.1 is explicit on qualified Windows x64 only.\n\n"
        "This P0 foundation is not the production MCP server; protocol/pool/migration qualification is deferred. "
        "See docs/p0-foundations.md for evidence and limitations. "
        "SHA256SUMS and manifest.json describe the exact canonical upload bytes. "
        "nuget.org may add its repository signature; downloaded signed bytes are verified separately.\n"
    )


def canonical_release(directory, version, commit):
    tag = os.environ["GITHUB_REF_NAME"]
    if not TAG.fullmatch(tag) or tag != "v" + version:
        raise ValueError("Tag/version mismatch.")
    directory = Path(directory)
    manifest(directory, version, commit)
    state = release_state(tag)
    if state is None:
        command("gh", "release", "create", tag, "--draft", "--verify-tag", "--target", commit,
                "--title", f"PoshMcp foundation {version}", "--notes", release_notes(version, commit))
        state = release_state(tag)
    canonical_asset = next((a for a in state["assets"] if a["name"] == "canonical.zip"), None)
    if canonical_asset is not None:
        with tempfile.TemporaryDirectory(prefix="poshmcp-canonical-") as temporary:
            command("gh", "release", "download", tag, "--pattern", "canonical.zip", "--dir", temporary)
            with zipfile.ZipFile(Path(temporary) / "canonical.zip") as package:
                for name in package.namelist():
                    if Path(name).is_absolute() or ".." in Path(name).parts or "/" in name or "\\" in name:
                        raise ValueError("Unsafe canonical release archive.")
                package.extractall(directory)
        existing = json.loads((directory / "manifest.json").read_text())
        if existing["commit"] != commit or existing["version"] != version:
            raise ValueError("Existing canonical release belongs to another commit/version.")
        # Canonical bytes were validated by the first complete gate for this exact tag.
        for record in existing["packages"]:
            if sha256(directory / record["file"]) != record["sha256"]:
                raise ValueError("Canonical release checksum conflict.")
    record_set = manifest(directory, version, commit)
    canonical = directory / "canonical.zip"
    with zipfile.ZipFile(canonical, "w", zipfile.ZIP_DEFLATED) as package:
        for path in sorted(directory.iterdir()):
            if path.name.endswith((".nupkg", ".json")) or path.name == "SHA256SUMS":
                package.write(path, path.name)
    # Never clobber immutable release assets, including on partially failed uploads.
    for path in [canonical, directory / "manifest.json", directory / "SHA256SUMS"] + sorted(directory.glob("*.nupkg")):
        asset = next((a for a in state["assets"] if a["name"] == path.name), None)
        if asset:
            if path.name == "canonical.zip":
                continue
            with tempfile.TemporaryDirectory(prefix="poshmcp-asset-") as temporary:
                command("gh", "release", "download", tag, "--pattern", path.name, "--dir", temporary)
                if sha256(Path(temporary) / path.name) != sha256(path):
                    raise ValueError("Existing release asset conflicts; refusing overwrite.")
        else:
            command("gh", "release", "upload", tag, path)
    return record_set


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("lane", choices=["main", "tag"])
    parser.add_argument("directory")
    parser.add_argument("version")
    parser.add_argument("commit")
    args = parser.parse_args()
    if os.environ.get("GITHUB_ACTIONS") != "true":
        raise RuntimeError("Registry publication is restricted to the trusted workflow, not manual commands.")
    if args.commit != os.environ["GITHUB_SHA"] or args.version != version_for_event(
            args.lane, os.environ["GITHUB_REF_NAME"], args.commit,
            os.environ["GITHUB_RUN_ID"], os.environ["GITHUB_RUN_ATTEMPT"]):
        raise ValueError("Publication version/commit differs from the exact workflow event.")
    event = os.environ["GITHUB_REF"]
    if args.lane == "main":
        if event != "refs/heads/main" or os.environ["GITHUB_EVENT_NAME"] != "push":
            raise ValueError("Only trusted main pushes publish prereleases.")
        records = manifest(args.directory, args.version, args.commit)
        publish_set(args.directory, records, "github")
    else:
        if not event.startswith("refs/tags/") or os.environ["GITHUB_EVENT_NAME"] != "push":
            raise ValueError("Release publication requires a version tag.")
        if "-" not in args.version and os.environ.get("P0_STABLE_QUALIFIED_COMMIT") != args.commit:
            raise RuntimeError("Stable promotion requires administrator-recorded P5 qualification of this exact commit.")
        if not os.environ.get("NUGET_API_KEY"):
            raise RuntimeError("The protected nuget.org credential is missing; no release/registry write attempted.")
        records = canonical_release(args.directory, args.version, args.commit)
        publish_set(args.directory, records, "github")
        publish_set(args.directory, records, "nuget")
        # Finalizing the GitHub Release happens only after native registry installations pass.


if __name__ == "__main__":
    main()
