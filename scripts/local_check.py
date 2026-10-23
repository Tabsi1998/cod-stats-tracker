#!/usr/bin/env python3
"""Run every check of this repository on this computer, before anything is pushed.

GitHub Actions is the second, independent confirmation. This is the first.

Groups:
    repository  Gitleaks over the history and over uncommitted and new files
    dotnet      locked restore, format, build, all .NET tests (API tests need Docker), vulnerable NuGet packages
    web         npm ci, lint, type check, tests, build, npm audit
    desktop     self-contained publish of the Windows client (Windows only)
    release     installer (Inno Setup 6), portable ZIP and SHA256SUMS from the published client; only with --release

Usage:
    python scripts/local_check.py                every group
    python scripts/local_check.py --only dotnet  some groups
    python scripts/local_check.py --release      also build installer, ZIP and checksums (nothing is published)
    python scripts/local_check.py --list         the steps, without running them

Results go to .local-testing/, which Git ignores.
"""

from __future__ import annotations

import argparse
import contextlib
import hashlib
import io
import json
import os
import platform
import re
import shutil
import subprocess
import sys
import tarfile
import time
import urllib.request
import xml.etree.ElementTree as ET
import zipfile
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable, Iterable

ROOT = Path(__file__).resolve().parents[1]
SOLUTION = "cod-stats-tracker.slnx"
WEB = ROOT / "apps" / "web"
DESKTOP = ROOT / "apps" / "desktop"

STATE = ROOT / ".local-testing"
TOOLS_DIR = STATE / "tools"

GITLEAKS_VERSION = "8.30.1"
GITLEAKS_BUILDS = {
    ("Windows", "amd64"): (
        "gitleaks_8.30.1_windows_x64.zip",
        "d29144deff3a68aa93ced33dddf84b7fdc26070add4aa0f4513094c8332afc4e",
    ),
    ("Linux", "x86_64"): (
        "gitleaks_8.30.1_linux_x64.tar.gz",
        "551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb",
    ),
}

# Fewer tests than this means tests silently stopped being discovered. The Windows client's tests run on
# Windows only, so Linux (the CI runner with Docker) runs the API and contract tests alone.
MIN_DOTNET_TESTS = 70
MIN_DOTNET_TESTS_LINUX = 62
API_TESTS = "tests/api/CodStats.Api.Tests/CodStats.Api.Tests.csproj"

GROUPS = ("repository", "dotnet", "web", "desktop", "release")
DEFAULT_GROUPS = ("repository", "dotnet", "web", "desktop")
PUBLISH_DIR = STATE / "publish" / "win-x64"
RELEASE_DIR = STATE / "release"
PASS, FAIL, SKIP = "passed", "failed", "skipped"

# Variables whose names look like credentials never reach code under test.
SECRET_NAME = re.compile(
    r"TOKEN|SECRET|PASSW|CREDENTIAL|PRIVATE|API_?KEY|ENCRYPTION|_KEY$|^(POSTGRES|PG)",
    re.IGNORECASE,
)


class StepFailed(Exception):
    """A step found a problem. The message says what, and how to fix it."""


class StepSkipped(Exception):
    """A step cannot run here. The message says why."""


@dataclass
class Step:
    group: str
    name: str
    describe: str
    action: Callable[["Context"], str | None]
    needs: tuple[str, ...] = ()


@dataclass
class Result:
    group: str
    name: str
    describe: str
    status: str
    seconds: float
    detail: str = ""


def clean_environment(source: dict[str, str]) -> tuple[dict[str, str], list[str]]:
    kept: dict[str, str] = {}
    withheld: list[str] = []
    for name, value in source.items():
        if SECRET_NAME.search(name):
            withheld.append(name)
        else:
            kept[name] = value
    return kept, sorted(withheld)


@dataclass
class Context:
    env: dict[str, str]
    log: io.TextIOBase
    version: str = "0.0.0-local"
    passed: set[str] = field(default_factory=set)

    def tool(self, name: str) -> str | None:
        return shutil.which(name, path=self.env.get("PATH"))

    def run(self, command: Iterable[object], cwd: Path = ROOT, echo: bool = True) -> tuple[int, str]:
        argv = [str(part) for part in command]
        shown = "> " + subprocess.list2cmdline(argv)
        print(shown, flush=True)
        self.log.write(shown + "\n")
        try:
            child = subprocess.Popen(
                argv, cwd=cwd, env=self.env,
                stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                text=True, encoding="utf-8", errors="replace",
            )
        except OSError as error:
            raise StepFailed(f"{argv[0]} could not be started: {error}") from error

        captured: list[str] = []
        with child:
            assert child.stdout is not None
            for line in child.stdout:
                self.log.write(line)
                captured.append(line)
                if echo:
                    print(line, end="", flush=True)
        return child.returncode, "".join(captured)

    def require(self, command: Iterable[object], what: str, cwd: Path = ROOT) -> str:
        code, output = self.run(command, cwd)
        if code != 0:
            raise StepFailed(what)
        return output


def execute(steps: list[Step], context: Context) -> list[Result]:
    """Run the steps in order. A step whose prerequisite did not pass is skipped."""
    results: list[Result] = []
    status_of: dict[str, str] = {}
    for step in steps:
        started = time.monotonic()
        blocked = [need for need in step.needs if status_of.get(need) != PASS]
        if blocked:
            status, detail = SKIP, "needs " + ", ".join(blocked) + " to pass first"
        else:
            print(f"\n== {step.group}: {step.describe}", flush=True)
            try:
                detail = step.action(context) or ""
                status = PASS
            except StepSkipped as reason:
                status, detail = SKIP, str(reason)
            except StepFailed as problem:
                status, detail = FAIL, str(problem)
        status_of[step.name] = status
        if status == PASS:
            context.passed.add(step.name)
        results.append(Result(step.group, step.name, step.describe, status,
                              round(time.monotonic() - started, 1), detail))
    return results


# Repository.

def git(context: Context) -> str:
    found = context.tool("git")
    if not found:
        raise StepFailed("Git is not on PATH")
    return found


def verify_sha256(data: bytes, expected: str, name: str) -> None:
    actual = hashlib.sha256(data).hexdigest()
    if actual != expected:
        raise StepFailed(f"{name} does not match its published SHA-256 (got {actual}), so it was not used")


def extract_member(data: bytes, archive: str, member: str) -> bytes:
    try:
        if archive.endswith(".zip"):
            with zipfile.ZipFile(io.BytesIO(data)) as bundle:
                return bundle.read(member)
        with tarfile.open(fileobj=io.BytesIO(data), mode="r:gz") as bundle:
            extracted = bundle.extractfile(member)
            if extracted is None:
                raise KeyError(member)
            return extracted.read()
    except (KeyError, zipfile.BadZipFile, tarfile.TarError) as error:
        raise StepFailed(f"{archive} does not contain {member}") from error


def ensure_gitleaks(context: Context) -> str:
    """The pinned Gitleaks, from PATH or downloaded and checksum-verified once."""
    on_path = context.tool("gitleaks")
    if on_path:
        code, output = context.run([on_path, "version"], echo=False)
        if code == 0 and output.strip() == GITLEAKS_VERSION:
            return on_path

    build = GITLEAKS_BUILDS.get((platform.system(), platform.machine().lower()))
    if build is None:
        raise StepFailed(f"no pinned Gitleaks {GITLEAKS_VERSION} build for {platform.system()} "
                         f"{platform.machine()}; put gitleaks {GITLEAKS_VERSION} on PATH")
    archive, digest = build
    member = "gitleaks.exe" if platform.system() == "Windows" else "gitleaks"
    target = TOOLS_DIR / f"gitleaks-{GITLEAKS_VERSION}" / member
    if target.exists():
        return str(target)

    url = f"https://github.com/gitleaks/gitleaks/releases/download/v{GITLEAKS_VERSION}/{archive}"
    print(f"Downloading Gitleaks {GITLEAKS_VERSION} and checking its SHA-256", flush=True)
    try:
        with urllib.request.urlopen(url, timeout=120) as response:
            data = response.read()
    except OSError as error:
        raise StepFailed(f"Gitleaks could not be downloaded: {error}") from error
    verify_sha256(data, digest, archive)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(extract_member(data, archive, member))
    if platform.system() != "Windows":
        target.chmod(0o755)
    return str(target)


def has_commits(context: Context) -> bool:
    code, _ = context.run([git(context), "rev-parse", "--verify", "HEAD"], echo=False)
    return code == 0


def gitleaks_history(context: Context) -> None:
    binary = ensure_gitleaks(context)
    if not has_commits(context):
        raise StepSkipped("no commits yet")
    context.require(
        [binary, "git", ".", "--redact", "--config", ".gitleaks.toml", "--log-opts=HEAD", "--no-banner"],
        what="Gitleaks found a possible secret in the history. Rotate it first, then follow SECURITY.md")


def gitleaks_changes(context: Context) -> str:
    binary = ensure_gitleaks(context)
    what = "Gitleaks found a possible secret in uncommitted changes. Remove it before committing"
    if has_commits(context):
        scan = [binary, "git", ".", "--redact", "--config", ".gitleaks.toml", "--no-banner"]
        context.require(scan + ["--pre-commit"], what=what)
        context.require(scan + ["--pre-commit", "--staged"], what=what)

    # git diff does not show files Git does not know yet.
    code, output = context.run([git(context), "ls-files", "--others", "--exclude-standard"], echo=False)
    if code != 0:
        raise StepFailed("new files could not be listed")
    new = [line.strip() for line in output.splitlines() if line.strip()]
    for path in new:
        context.require([binary, "dir", path, "--redact", "--config", ".gitleaks.toml", "--no-banner"],
                        what=f"Gitleaks found a possible secret in {path}. Remove it before committing")
    return f"{len(new)} new file(s) scanned" if new else ""


def repository_steps() -> list[Step]:
    return [
        Step("repository", "gitleaks-history", "Gitleaks over the history", gitleaks_history),
        Step("repository", "gitleaks-changes", "Gitleaks over uncommitted and new files", gitleaks_changes),
    ]


# .NET: contracts, API, Windows client and their tests, all in one solution.

def dotnet(context: Context) -> str:
    found = context.tool("dotnet")
    if not found:
        raise StepFailed("dotnet is not on PATH. Install the .NET 10 SDK")
    return found


def docker_running(context: Context) -> bool:
    found = context.tool("docker")
    if not found:
        return False
    code, _ = context.run([found, "info", "--format", "{{.ServerVersion}}"], echo=False)
    return code == 0


def trx_counts(text: str) -> tuple[int, int]:
    """Executed and failed test counts from a TRX file."""
    counters = ET.fromstring(text).find(".//{*}Counters")
    if counters is None:
        return 0, 0
    return int(counters.get("executed", "0")), int(counters.get("failed", "0"))


def dotnet_tests(context: Context) -> str:
    binary = dotnet(context)
    if not docker_running(context):
        raise StepFailed("Docker is not running. The API tests start PostgreSQL with Testcontainers; start Docker Desktop")
    results = STATE / "dotnet-tests"
    shutil.rmtree(results, ignore_errors=True)
    windows = platform.system() == "Windows"
    minimum = MIN_DOTNET_TESTS if windows else MIN_DOTNET_TESTS_LINUX
    context.require([binary, "test", SOLUTION if windows else API_TESTS, "--no-build", "--configuration", "Release",
                     "--logger", "trx", "--results-directory", results],
                    what="tests failed; the output above names them")
    executed = failed = 0
    for trx in results.rglob("*.trx"):
        run, broken = trx_counts(trx.read_text(encoding="utf-8"))
        executed += run
        failed += broken
    if failed:
        raise StepFailed(f"{failed} test(s) failed")
    if executed < minimum:
        raise StepFailed(f"only {executed} tests ran, expected at least {minimum}; were tests lost?")
    return f"{executed} tests"


def vulnerable_packages(report: str) -> list[str]:
    return sorted({line.strip() for line in report.splitlines() if line.strip().startswith(">")})


def nuget_vulnerabilities(context: Context) -> str:
    output = context.require([dotnet(context), "list", SOLUTION, "package", "--vulnerable", "--include-transitive"],
                             what="the package audit could not run")
    found = vulnerable_packages(output)
    if found:
        raise StepFailed("vulnerable NuGet packages: " + "; ".join(found))
    return ""


def dotnet_steps() -> list[Step]:
    return [
        Step("dotnet", "restore", "Locked restore",
             lambda c: c.require([dotnet(c), "restore", SOLUTION, "--locked-mode"],
                                 what="restore failed; if a package changed on purpose run dotnet restore --force-evaluate")),
        Step("dotnet", "format", "Formatting (dotnet format --verify-no-changes)",
             lambda c: c.require([dotnet(c), "format", SOLUTION, "--verify-no-changes", "--no-restore"],
                                 what="code is not formatted; run dotnet format " + SOLUTION), needs=("restore",)),
        Step("dotnet", "build", "Build (warnings are errors)",
             lambda c: c.require([dotnet(c), "build", SOLUTION, "--no-restore", "--configuration", "Release",
                                  "-warnaserror"], what="the build failed"), needs=("restore",)),
        Step("dotnet", "tests", "All .NET tests (API against PostgreSQL)", dotnet_tests, needs=("build",)),
        Step("dotnet", "nuget-audit", "No vulnerable NuGet packages", nuget_vulnerabilities, needs=("restore",)),
    ]


# Web.

def npm(context: Context) -> str:
    if not (WEB / "package.json").exists():
        raise StepSkipped("apps/web does not exist yet")
    found = context.tool("npm")
    if not found:
        raise StepFailed("npm is not on PATH. Install Node 24")
    return found


def npm_script(name: str) -> Callable[[Context], None]:
    def action(context: Context) -> None:
        context.require([npm(context), "run", name], cwd=WEB, what=f"npm run {name} failed")
    return action


def web_steps() -> list[Step]:
    return [
        Step("web", "npm-ci", "npm ci",
             lambda c: c.require([npm(c), "ci", "--no-audit", "--no-fund"], cwd=WEB, what="npm ci failed")),
        Step("web", "lint", "Lint", npm_script("lint"), needs=("npm-ci",)),
        Step("web", "typecheck", "Type check", npm_script("typecheck"), needs=("npm-ci",)),
        Step("web", "web-tests", "Tests", npm_script("test"), needs=("npm-ci",)),
        Step("web", "web-build", "Build", npm_script("build"), needs=("npm-ci",)),
        Step("web", "npm-audit", "npm audit (high and critical)",
             lambda c: c.require([npm(c), "audit", "--audit-level=high", "--omit=dev"], cwd=WEB,
                                 what="npm audit found high or critical vulnerabilities"), needs=("npm-ci",)),
    ]


# Desktop.

# Not compressed on purpose: a compressed single file unpacks itself into memory at every start (260 MB instead of
# 95 MB working set). The installer compresses the download instead.
def desktop_publish(context: Context) -> str:
    if platform.system() != "Windows":
        raise StepSkipped("the Windows client builds on Windows only")
    target = PUBLISH_DIR
    shutil.rmtree(target, ignore_errors=True)
    # A self-contained publish adds the ILLink package to the lock files of the client and the contracts. Those
    # files must stay as the plain restore writes them (the Linux CI and the API image restore in locked mode),
    # so they are put back afterwards.
    locks = {path: path.read_bytes() for path in ROOT.glob("**/packages.lock.json") if ".local-testing" not in path.parts}
    try:
        context.require([dotnet(context), "publish", DESKTOP / "CodStats.Desktop" / "CodStats.Desktop.csproj",
                         "--configuration", "Release", "--runtime", "win-x64", "--self-contained", "true",
                         "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
                         f"-p:Version={context.version.split('-')[0]}", "--output", target],
                        what="the self-contained publish failed")
    finally:
        for path, content in locks.items():
            path.write_bytes(content)
    exe = target / "CodStats.exe"
    if not exe.exists():
        raise StepFailed("CodStats.exe is missing in the publish output")
    return f"{exe.stat().st_size // (1024 * 1024)} MB: {exe}"


def desktop_steps() -> list[Step]:
    return [Step("desktop", "desktop-publish", "Self-contained CodStats.exe (win-x64)", desktop_publish)]


# Release (dry run: builds the files a release would carry, publishes nothing).

def inno_setup(environ: dict[str, str]) -> str | None:
    """Inno Setup 6, installed for all users or, as winget does, for one."""
    on_path = shutil.which("ISCC", path=environ.get("PATH"))
    if on_path:
        return on_path
    bases = [environ.get("ProgramFiles(x86)"), environ.get("ProgramFiles")]
    if environ.get("LOCALAPPDATA"):
        bases.append(str(Path(environ["LOCALAPPDATA"]) / "Programs"))
    for base in bases:
        if base and (Path(base) / "Inno Setup 6" / "ISCC.exe").is_file():
            return str(Path(base) / "Inno Setup 6" / "ISCC.exe")
    return None


def installer(context: Context) -> str:
    iscc = inno_setup(context.env)
    if iscc is None:
        raise StepFailed("Inno Setup 6 is not installed: winget install JRSoftware.InnoSetup")
    RELEASE_DIR.mkdir(parents=True, exist_ok=True)
    context.require([iscc, "/Q", f"/DAppVersion={context.version}", f"/DPayloadDir={PUBLISH_DIR}",
                     f"/DOutputDir={RELEASE_DIR}", ROOT / "installer" / "codstats.iss"], what="the installer did not build")
    setup = RELEASE_DIR / "CodStats-Setup-win-x64.exe"
    return f"{setup.stat().st_size // (1024 * 1024)} MB: {setup}"


def portable_zip(context: Context) -> str:
    RELEASE_DIR.mkdir(parents=True, exist_ok=True)
    target = RELEASE_DIR / "CodStats-portable-win-x64.zip"
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as bundle:
        for file in sorted(PUBLISH_DIR.rglob("*")):
            if file.is_file() and file.suffix != ".pdb":
                bundle.write(file, file.relative_to(PUBLISH_DIR))
    return f"{target.stat().st_size // (1024 * 1024)} MB"


def checksums(context: Context) -> str:
    lines = [f"{hashlib.sha256(file.read_bytes()).hexdigest()}  {file.name}"
             for file in sorted(RELEASE_DIR.iterdir()) if file.is_file() and file.name != "SHA256SUMS"]
    (RELEASE_DIR / "SHA256SUMS").write_text("\n".join(lines) + "\n", encoding="utf-8")
    return f"{len(lines)} file(s)"


def release_steps() -> list[Step]:
    return [
        Step("release", "installer", "Installer (Inno Setup)", installer, needs=("desktop-publish",)),
        Step("release", "portable", "Portable ZIP", portable_zip, needs=("desktop-publish",)),
        Step("release", "checksums", "SHA256SUMS", checksums, needs=("installer", "portable")),
    ]


def default_version(changelog: str) -> str:
    """The newest released version in the changelog, or a local placeholder."""
    found = re.search(r"^## \[(\d+\.\d+\.\d+[^\]]*)\]", changelog, re.MULTILINE)
    return found.group(1) if found else "0.0.0-local"


def plan(groups: set[str]) -> list[Step]:
    builders = {"repository": repository_steps, "dotnet": dotnet_steps, "release": release_steps, "web": web_steps, "desktop": desktop_steps}
    return [step for group in GROUPS if group in groups for step in builders[group]()]


def summary(results: list[Result], seconds: float) -> str:
    counts = {status: sum(result.status == status for result in results) for status in (PASS, SKIP, FAIL)}
    lines = [f"\nLocal check: {counts[PASS]} passed, {counts[SKIP]} skipped, {counts[FAIL]} failed in {seconds:.0f} s"]
    for result in results:
        mark = {PASS: "PASS", SKIP: "SKIP", FAIL: "FAIL"}[result.status]
        line = f"  {mark}  {result.group:<10}  {result.describe:<48} {result.seconds:>6.1f} s"
        if result.detail:
            line += f"  {result.detail}"
        lines.append(line)
    return "\n".join(lines)


def main(argv: list[str] | None = None) -> int:
    for stream in (sys.stdout, sys.stderr):
        with contextlib.suppress(AttributeError, ValueError):
            stream.reconfigure(encoding="utf-8", errors="replace")

    parser = argparse.ArgumentParser(description="Run every check of this repository on this computer.")
    parser.add_argument("--only", help="comma-separated groups: " + ", ".join(GROUPS))
    parser.add_argument("--release", action="store_true", help="add installer, ZIP and checksums; nothing is published")
    parser.add_argument("--version", help="the version the release dry run builds, for example 0.1.0")
    parser.add_argument("--list", action="store_true", help="show the steps without running them")
    arguments = parser.parse_args(argv)

    groups = set(DEFAULT_GROUPS)
    if arguments.only:
        groups = {group.strip() for group in arguments.only.split(",") if group.strip()}
        unknown = sorted(groups - set(GROUPS))
        if unknown:
            parser.error("unknown group(s): " + ", ".join(unknown))
    if arguments.release:
        groups |= {"desktop", "release"}
    steps = plan(groups)

    if arguments.list:
        for step in steps:
            print(f"{step.group:<11} {step.describe}")
        return 0

    env, withheld = clean_environment(dict(os.environ))
    env.setdefault("DOTNET_CLI_TELEMETRY_OPTOUT", "1")
    env.setdefault("DOTNET_NOLOGO", "1")
    if withheld:
        print("Withheld from every step (names only): " + ", ".join(withheld), flush=True)

    STATE.mkdir(exist_ok=True)
    started = time.monotonic()
    with (STATE / "local-check.log").open("w", encoding="utf-8") as log:
        version = arguments.version or default_version((ROOT / "CHANGELOG.md").read_text(encoding="utf-8"))
        results = execute(steps, Context(env=env, log=log, version=version))
    seconds = time.monotonic() - started

    report = {"groups": sorted(groups), "seconds": round(seconds, 1),
              "results": [result.__dict__ for result in results]}
    (STATE / "local-check.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(summary(results, seconds))
    print(f"Report: {STATE / 'local-check.json'}")
    return 1 if any(result.status == FAIL for result in results) else 0


if __name__ == "__main__":
    sys.exit(main())
