import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import packages
import publish


class PackageTests(unittest.TestCase):
    def test_main_versions_include_run_attempt_and_commit_not_build_metadata(self):
        sha = "a" * 40
        one = packages.version_for_event("main", "main", sha, "123", "1")
        two = packages.version_for_event("main", "main", sha, "123", "2")
        self.assertNotEqual(one, two)
        self.assertNotIn("+", one)
        self.assertIn(sha[:12], one)

    def test_exact_alpha_beta_stable_tags_and_invalid_versions(self):
        for tag in ("v1.0.0-alpha.1", "v1.0.0-beta.0", "v1.0.0"):
            self.assertEqual(tag[1:], packages.version_for_event("tag", tag, "a" * 40, "1", "1"))
        for tag in ("v1.0.1", "v1.0.0-alpha.01", "v1.0.0-alpha", "v1.0.0+commit", "v1.0.0-rc.1"):
            with self.assertRaises(ValueError):
                packages.version_for_event("tag", tag, "a" * 40, "1", "1")

    @staticmethod
    def fixture(directory, rid):
        version = "1.0.0-alpha.1"
        path = directory / f"PoshMcp.Foundation.{rid}.{version}.nupkg"
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("package.nuspec", f"<package><metadata><id>PoshMcp.Foundation.{rid}</id><version>{version}</version></metadata></package>")
            launcher = "poshmcp-launcher.exe" if rid.startswith("win-") else "poshmcp-launcher"
            for name in ("PoshMcp.Foundation.dll", "PoshMcp.Foundation.runtimeconfig.json", "DotnetToolSettings.xml", launcher):
                archive.writestr("tools/net11.0/any/" + name, rid)
        record = {
            "rid": rid, "id": f"PoshMcp.Foundation.{rid}", "version": version,
            "file": path.name, "commit": "a" * 40, "sha256": packages.sha256(path),
            "payload": packages.inspect_package(path, rid, version)
        }
        (directory / f"{rid}.json").write_text(json.dumps(record))
        return record

    def test_missing_target_and_tampered_package_block_complete_set(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            records = [self.fixture(directory, rid) for rid in packages.RIDS]
            manifest = packages.manifest(directory, "1.0.0-alpha.1", "a" * 40)
            self.assertEqual(4, len(manifest["packages"]))
            (directory / records[0]["file"]).write_bytes(b"tampered")
            with self.assertRaisesRegex(ValueError, "bytes/payload"):
                packages.manifest(directory, "1.0.0-alpha.1", "a" * 40)
            self.fixture(directory, packages.RIDS[0])
            (directory / "win-arm64.json").unlink()
            with self.assertRaisesRegex(ValueError, "Missing required native package record win-arm64"):
                packages.manifest(directory, "1.0.0-alpha.1", "a" * 40)

    def test_existing_matching_bytes_verify_and_conflicts_are_not_suppressed(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            record = self.fixture(directory, "linux-x64")
            data = (directory / record["file"]).read_bytes()
            publish.verify_download(record, data, "github")
            with self.assertRaisesRegex(RuntimeError, "conflict"):
                publish.verify_download(record, data + b"changed", "github")
            with self.assertRaisesRegex(RuntimeError, "without a repository signature"):
                publish.verify_download(record, data + b"changed", "nuget")

    def test_resume_only_pushes_missing_packages_and_verifies_existing(self):
        records = {"packages": [{"id": "one", "version": "v", "file": "one.nupkg"}, {"id": "two", "version": "v", "file": "two.nupkg"}]}
        with patch.object(publish, "registry", return_value=("index", "token", {})), \
             patch.object(publish, "existing_package", side_effect=[b"matching", None, b"published"]), \
             patch.object(publish, "verify_download") as verify, \
             patch.object(publish, "command") as command:
            publish.publish_set("packages", records, "github")
            self.assertEqual(2, verify.call_count)
            self.assertEqual(1, command.call_count)
            self.assertNotIn("--skip-duplicate", command.call_args.args)

    def test_publication_rejects_missing_credentials_before_writes(self):
        with patch.object(publish, "registry", return_value=("index", "", {})), \
             patch.object(publish, "command") as command, self.assertRaises(RuntimeError):
            publish.publish_set("packages", {"packages": []}, "nuget")
        command.assert_not_called()

    def test_release_rejects_missing_credentials_and_wrong_event_before_writes(self):
        environment = {
            "GITHUB_ACTIONS": "true", "GITHUB_SHA": "a" * 40,
            "GITHUB_REF_NAME": "v1.0.0-alpha.1", "GITHUB_REF": "refs/tags/v1.0.0-alpha.1",
            "GITHUB_RUN_ID": "123", "GITHUB_RUN_ATTEMPT": "1", "GITHUB_EVENT_NAME": "push"
        }
        for commit, error in (("a" * 40, RuntimeError), ("b" * 40, ValueError)):
            with self.subTest(commit=commit), patch.dict(os.environ, environment, clear=True), \
                 patch.object(sys, "argv", ["publish.py", "tag", "packages", "1.0.0-alpha.1", commit]), \
                 patch.object(publish, "canonical_release") as canonical, \
                 patch.object(publish, "publish_set") as write, self.assertRaises(error):
                publish.main()
            canonical.assert_not_called()
            write.assert_not_called()

    def test_canonical_release_rejects_another_commits_artifacts(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            target = root / "current"
            target.mkdir()
            for rid in packages.RIDS:
                self.fixture(target, rid)
            canonical = root / "canonical.zip"
            with zipfile.ZipFile(canonical, "w") as archive:
                archive.writestr("manifest.json", json.dumps({
                    "commit": "b" * 40, "version": "1.0.0-alpha.1", "packages": []
                }))

            def download(*args):
                if args[:3] == ("gh", "release", "download"):
                    destination = Path(args[-1]) / "canonical.zip"
                    destination.write_bytes(canonical.read_bytes())
                return ""

            with patch.dict(os.environ, {"GITHUB_REF_NAME": "v1.0.0-alpha.1"}), \
                 patch.object(publish, "release_state", return_value={"assets": [{"name": "canonical.zip"}]}), \
                 patch.object(publish, "command", side_effect=download), \
                 self.assertRaisesRegex(ValueError, "another commit"):
                publish.canonical_release(target, "1.0.0-alpha.1", "a" * 40)


if __name__ == "__main__":
    unittest.main()
