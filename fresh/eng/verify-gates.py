"""Prove negative build, unit, classification and formatting gates in a disposable clone."""
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CASES = [
    ("failing-unit", '[Trait("Category", "Unit")] public class Negative { [Fact] public void DeliberateFailure() => Assert.Fail("negative-unit-control"); }', "unit", "negative-unit-control"),
    ("unclassified", 'public class Negative { [Fact] public void MissingCategory() { } }', "unit", "Every test needs exactly one"),
    ("unit-resource", '[Trait("Category", "Unit")] public class Negative { [Fact] public void ResourceAccess() => System.Diagnostics.Process.GetCurrentProcess().Dispose(); }', "unit", "external resources"),
    ("analyzer", 'public class Negative { private int _unused; }', "build", "CS0169"),
    ("formatting", 'public class Negative{public int Value{get;set;}}', "format", "WHITESPACE"),
]


def main():
    log = []
    with tempfile.TemporaryDirectory(prefix="poshmcp-negative-gates-") as directory:
        clone = Path(directory) / "fresh"
        shutil.copytree(ROOT, clone, ignore=shutil.ignore_patterns("bin", "obj", ".runtime", ".modules", ".tools", "packages", "TestResults", "__pycache__", "poshmcp-launcher", "*.exe"))
        source = clone / "PoshMcp.Foundation.Tests" / "Negative.cs"
        for name, body, lane, marker in CASES:
            source.write_text("using Xunit;\nnamespace PoshMcp.Foundation.Tests;\n" + body + "\n")
            command = (["dotnet", "format", "PoshMcp.Foundation.slnx", "--verify-no-changes", "--no-restore"] if lane == "format"
                       else [sys.executable, "eng/validate.py", lane])
            result = subprocess.run(command, cwd=clone, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
            log.append(f"CASE {name}: exit={result.returncode}\n{result.stdout}")
            if result.returncode == 0 or marker not in result.stdout:
                raise RuntimeError(f"Negative {name} gate did not fail for the intended reason:\n{result.stdout}")
            source.unlink()
    results = ROOT / "TestResults"
    results.mkdir(exist_ok=True)
    (results / "negative-gates.log").write_text("\n".join(log))
    print("All five negative gate controls were rejected for the intended reason.")


if __name__ == "__main__":
    main()
