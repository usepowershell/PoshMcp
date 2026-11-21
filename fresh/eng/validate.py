"""Shared fresh-core validation. All-tests never uses category filters."""
import argparse
import os
from pathlib import Path
import platform
import re
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SOLUTION = "PoshMcp.Foundation.slnx"
PROJECT = "PoshMcp.Foundation/PoshMcp.Foundation.csproj"


def run(*args, env=None, capture=False):
    return subprocess.run(
        [str(arg) for arg in args], cwd=ROOT, env=env, check=True,
        text=True, stdout=subprocess.PIPE if capture else None
    )


def native_rid():
    architecture = {"AMD64": "x64", "x86_64": "x64", "aarch64": "arm64", "ARM64": "arm64"}.get(platform.machine())
    system = {"Linux": "linux", "Windows": "win"}.get(platform.system())
    if not architecture or not system:
        raise RuntimeError("Unsupported native runner; qualification cannot be skipped.")
    return f"{system}-{architecture}"


def compile_native():
    if native_rid().startswith("linux-"):
        run("cc", "-std=c11", "-Wall", "-Wextra", "-Werror", "-O2",
            "native/launcher-linux.c", "-o", "PoshMcp.Foundation/poshmcp-launcher")
    else:
        run("cl", "/nologo", "/W4", "/WX", "/O2", "native/launcher-windows.c",
            "/Fe:PoshMcp.Foundation/poshmcp-launcher.exe", "/Fo:PoshMcp.Foundation/launcher.obj")


def build():
    run("dotnet", "restore", SOLUTION, "--locked-mode")
    run("dotnet", "build", SOLUTION, "-c", "Release", "--no-restore")


def unit_source_policy():
    for source in (ROOT / "PoshMcp.Foundation.Tests").rglob("*.cs"):
        if "obj" in source.parts or "bin" in source.parts:
            continue
        text = source.read_text()
        if '[Trait("Category", "Unit")]' in text and re.search(
                r"\b(Process|WorkerProbe|NamedPipeServerStream|Socket|File|Directory|DllImport)\b", text):
            raise RuntimeError(f"{source.name}: external resources must not be labeled Unit.")


def check_report(path, lane):
    report = ET.parse(path).getroot()
    counters = report.find(".//{*}Counters")
    if counters is None or int(counters.attrib["total"]) == 0:
        raise RuntimeError("No tests executed; a missing suite is not a successful gate.")
    if int(counters.attrib.get("notExecuted", "0")) or int(counters.attrib.get("failed", "0")):
        raise RuntimeError("Failed or skipped tests block publication.")
    if lane == "all":
        expected = []
        for source in (ROOT / "PoshMcp.Foundation.Tests").rglob("*.cs"):
            if not {"obj", "bin"}.intersection(source.parts):
                expected += re.findall(r"\[(?:Fact|Theory)(?:\([^]]*\))?\]\s+public\s+(?:async\s+)?(?:Task|void)\s+(\w+)", source.read_text())
        executed = [test.attrib["testName"] for test in report.findall(".//{*}UnitTestResult")]
        for name in expected:
            if not any("." + name in test for test in executed):
                raise RuntimeError(f"Implemented test {name} did not execute.")


def runtime_environment():
    environment = os.environ.copy()
    if os.environ.get("POSHMCP_TEST_MODULE_PATH"):
        environment["PSModulePath"] = os.environ["POSHMCP_TEST_MODULE_PATH"]
    return environment


def tests(lane):
    unit_source_policy()
    if lane == "all":
        runtime = os.environ.get("POSHMCP_TEST_RUNTIME")
        if not runtime or not Path(runtime).is_absolute() or not Path(runtime).is_file():
            raise RuntimeError("Provision the explicit external runtime; missing runtimes are failures, not skips.")
        compile_native()
    build()
    results = ROOT / "TestResults"
    results.mkdir(exist_ok=True)
    args = ["dotnet", "test", SOLUTION, "-c", "Release", "--no-build",
            "--logger", f"trx;LogFileName={lane}.trx", "--results-directory", str(results)]
    if lane == "unit":
        args += ["--filter", "Category=Unit"]
    run(*args)
    check_report(results / f"{lane}.trx", lane)


def lint():
    unit_source_policy()
    run("dotnet", "format", SOLUTION, "--verify-no-changes", "--no-restore")
    run(os.environ.get("POSHMCP_LINT_RUNTIME", "pwsh"), "-NoLogo", "-NoProfile", "-NonInteractive",
        "-File", "eng/lint.ps1", env=runtime_environment())


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("lane", choices=["build", "unit", "all", "lint"])
    lane = parser.parse_args().lane
    if lane == "build":
        build()
    elif lane == "lint":
        lint()
    else:
        tests(lane)
