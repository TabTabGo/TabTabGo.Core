#!/usr/bin/env python3
"""Release verification used by .github/workflows/release.yml (tested by scripts/test_release_verify.py).

    release_verify.py tag <tag> --branches TEXT [--root .]
    release_verify.py version [--root .]
    release_verify.py audit --kind vulnerable|deprecated --file FILE [--ignore-project REGEX]
    release_verify.py not-published --dir out --version V          (or --expected "Id1 Id2")
    release_verify.py baseline --packages-dir DIR [--root .] [--config scripts/baseline.json]

The same file is used on every release line (v8, v10, v11). Why a script and not shell: `unzip -l f | grep -q x` under
`set -o pipefail` fails at random (SIGPIPE), and the release rules are easier to test in Python.

Rules enforced by `tag`:
  * only stable vMAJOR.MINOR.PATCH tags (ASCII digits, no leading zeros, no -suffix, no +metadata);
  * every packable project resolves to exactly the tag version (Directory.Build.props <Version> and/or the project's own
    <Version>; a project with no version source is refused);
  * the tag major equals the target framework major of every packable project (net8.0 / net10.0 / net11.0);
  * the tagged commit is on a releases/vMAJOR.MINOR.x/* branch.

Exit code 0 = ok; 1 = a check failed (printed as a GitHub ::error::). `tag` prints `version=<x.y.z>` for $GITHUB_OUTPUT.
"""
import argparse
import dataclasses
import hashlib
import json
import pathlib
import re
import sys

# ASCII digits only and no leading zeros: \d also matches other scripts' digits, and v010.0.0 is not canonical.
NUM = r"(0|[1-9][0-9]*)"
TAG_RE = re.compile(rf"^v{NUM}\.{NUM}\.{NUM}$", re.ASCII)
NOT_STABLE_RE = re.compile(r"^v[0-9]+\.[0-9]+\.[0-9]+[-+]", re.ASCII)
STABLE_ONLY = "only stable vMAJOR.MINOR.PATCH tags are released (no pre-release suffix, no build metadata)"
STABLE_VERSION_RE = re.compile(rf"^{NUM}\.{NUM}\.{NUM}$", re.ASCII)


class ReleaseError(Exception):
    """A release check failed."""


@dataclasses.dataclass(frozen=True)
class Tag:
    version: str
    major: int
    minor: int


def parse_tag(tag: str) -> Tag:
    # fullmatch on a single line: `$` would accept a trailing newline
    match = TAG_RE.fullmatch(tag) if "\n" not in tag else None
    if match is None:
        if "\n" not in tag and NOT_STABLE_RE.match(tag):
            raise ReleaseError(STABLE_ONLY)
        raise ReleaseError("tag does not match vMAJOR.MINOR.PATCH")
    major, minor, _patch = match.groups()
    return Tag(version=tag[1:], major=int(major), minor=int(minor))


def _property(text: str, name: str):
    found = re.search(rf"<{name}>\s*([^<]*?)\s*<", text)
    return found.group(1) if found else None


def packable_projects(root: pathlib.Path):
    """Every src/**/*.csproj that is not marked <IsPackable>false</IsPackable>."""
    projects = []
    for path in sorted((root / "src").rglob("*.csproj")):
        text = path.read_text(encoding="utf-8-sig")
        if (_property(text, "IsPackable") or "").lower() == "false":
            continue
        projects.append((path, text))
    if not projects:
        raise ReleaseError("no packable project found under src/")
    return projects


DEPENDENCY_RE = re.compile(r"<(?:PackageVersion|PackageReference|PackageDownload)\b[^>]*?\bVersion=\"([^\"]+)\"", re.I)


def check_dependencies(root: pathlib.Path) -> None:
    """No pre-release, floating or -local dependency version anywhere in the build inputs (src, test and the shared props)."""
    files = [p for pattern in ("*.csproj", "Directory.Packages.props", "Directory.Build.props", "Directory.Build.targets")
             for p in root.rglob(pattern) if not {"bin", "obj", "node_modules"} & set(p.parts)]
    for path in sorted(files):
        for version in DEPENDENCY_RE.findall(path.read_text(encoding="utf-8-sig")):
            if re.search(r"[-*]|local", version, re.I) and "$(" not in version:
                raise ReleaseError(f"{path.relative_to(root)} depends on a pre-release, floating or -local version '{version}'")


def declared_version(root: pathlib.Path) -> str:
    """The single version every packable project declares (Directory.Build.props and/or the project's own <Version>)."""
    props_path = root / "Directory.Build.props"
    props_version = _property(props_path.read_text(encoding="utf-8-sig"), "Version") if props_path.is_file() else None
    versions = {}
    for path, text in packable_projects(root):
        version = _property(text, "Version") or props_version
        if version is None:
            raise ReleaseError(f"{path.name} has no <Version> and Directory.Build.props declares none")
        versions[path.name] = version
    if len(set(versions.values())) != 1:
        raise ReleaseError(f"the packable projects declare different versions: {versions}")
    return next(iter(versions.values()))


def check_tag(tag: str, root: pathlib.Path, branches_text: str) -> Tag:
    parsed = parse_tag(tag)
    check_dependencies(root)
    props_path = root / "Directory.Build.props"
    props = props_path.read_text(encoding="utf-8-sig") if props_path.is_file() else ""
    props_version = _property(props, "Version")
    props_tfm = _property(props, "TargetFramework")
    for path, text in packable_projects(root):
        name = path.name
        version = _property(text, "Version") or props_version
        if version is None:
            raise ReleaseError(f"{name} has no <Version> and Directory.Build.props declares none")
        if version != parsed.version:
            raise ReleaseError(f"tag version {parsed.version} differs from the version {version} of {name}")
        tfm = _property(text, "TargetFramework") or props_tfm
        if tfm != f"net{parsed.major}.0":
            raise ReleaseError(f"tag major {parsed.major} does not match TargetFramework {tfm} of {name}")
    wanted = f"origin/releases/v{parsed.major}.{parsed.minor}.x/"
    if not any(line.strip().lstrip("* ").startswith(wanted) for line in branches_text.splitlines()):
        raise ReleaseError(f"tagged commit is not on a releases/v{parsed.major}.{parsed.minor}.x/* branch")
    return parsed


# What `dotnet list package --vulnerable|--deprecated` prints when it could not read its data source. The command still
# exits 0, so the output is the verdict and anything unclear is a failure.
AUDIT_FAILURE_MARKERS = (
    "NU1900", "Unable to load the service index", "Failed to retrieve information", "Error occurred while getting",
    "error:", "Response status code does not indicate success", "No such host is known", "Name or service not known",
)


def drop_projects(text: str, ignore_pattern: str) -> str:
    """Remove the findings blocks of projects whose name matches the pattern (test projects: they are never shipped)."""
    if not ignore_pattern:
        return text
    pattern = re.compile(ignore_pattern)
    kept, skipping = [], False
    for line in text.splitlines():
        header = re.match(r"^Project `([^`]+)` has the following", line)
        if header:
            skipping = bool(pattern.search(header.group(1)))
        elif line.startswith(("Project `", "The given project")):
            skipping = False
        if not skipping:
            kept.append(line)
    return "\n".join(kept) + "\n"


def check_audit(text: str, kind: str, ignore_project: str = "") -> None:
    """Fail closed: findings, audit-source errors and output that does not say 'no <kind> packages' all fail."""
    text = drop_projects(text, ignore_project)
    if f"has the following {kind} packages" in text or re.search(r"^\s+>\s", text, re.M):
        raise ReleaseError(f"{kind} packages found")
    for marker in AUDIT_FAILURE_MARKERS:
        if marker in text:
            raise ReleaseError(f"the {kind} audit could not complete ({marker!r} in the output); it is treated as a failure")
    if f"has no {kind} packages" not in text:
        raise ReleaseError(f"the {kind} audit output does not report any project as clean; it is treated as a failure")


def _status(url: str) -> int:
    import urllib.error
    import urllib.request

    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, *args, **kwargs):
            return None

    opener = urllib.request.build_opener(NoRedirect)
    try:
        with opener.open(urllib.request.Request(url, method="HEAD"), timeout=30) as response:
            return response.status
    except urllib.error.HTTPError as error:
        return error.code


def check_not_published(package_ids, version: str, status=_status) -> None:
    """No --skip-duplicate anywhere: a re-run must fail instead of looking green. Unknown answers are failures."""
    for package_id in package_ids:
        lower = package_id.lower()
        url = f"https://api.nuget.org/v3-flatcontainer/{lower}/{version.lower()}/{lower}.nuspec"
        try:
            code = status(url)
        except Exception as error:  # network failure: unknown, so not safe to publish
            raise ReleaseError(f"could not check whether {package_id} {version} exists on nuget.org: {error}") from error
        if code == 200:
            raise ReleaseError(f"{package_id} {version} is already published on nuget.org")
        if code != 404:
            raise ReleaseError(f"unexpected answer {code} for {package_id} {version} on nuget.org")


def check_baseline(root: pathlib.Path, config_path: pathlib.Path, packages_dir: pathlib.Path) -> None:
    """The ApiCompat baseline the build compared against must be a published stable package with a pinned SHA-256.

    scripts/baseline.json holds {"version": "<published version>", "sha256": {"<PackageId>": "<hex>"}}. The pins are EMPTY until
    the baseline line is published and the hashes are recorded: until then a release fails closed.
    """
    config = json.loads(config_path.read_text(encoding="utf-8"))
    version = config.get("version", "")
    pins = config.get("sha256", {})
    if not STABLE_VERSION_RE.fullmatch(version):
        raise ReleaseError(f"{config_path} must name a stable published baseline version, found {version!r}")
    props_path = root / "Directory.Build.props"
    props = props_path.read_text(encoding="utf-8-sig")
    declared = _property(props, "PackageValidationBaselineVersion")
    if declared != version:
        raise ReleaseError(
            f"Directory.Build.props PackageValidationBaselineVersion is {declared!r} but {config_path.name} pins {version}; "
            "the release is blocked until the build compares against the published baseline")
    validated = []
    for path, text in packable_projects(root):
        if (_property(text, "EnablePackageValidation") or _property(props, "EnablePackageValidation") or "").lower() != "false":
            validated.append(_property(text, "PackageId") or path.stem)
    if not validated:
        raise ReleaseError("no project has package validation enabled")
    for package_id in validated:
        expected = pins.get(package_id)
        if not expected:
            raise ReleaseError(f"no pinned SHA-256 for {package_id} {version} in {config_path.name}: record the published hashes first")
        lower = package_id.lower()
        nupkg = packages_dir / lower / version / f"{lower}.{version}.nupkg"
        if not nupkg.is_file():
            raise ReleaseError(f"baseline package {nupkg} was not restored; the ApiCompat gate did not run against it")
        actual = hashlib.sha256(nupkg.read_bytes()).hexdigest()
        if actual != expected.lower():
            raise ReleaseError(f"{package_id} {version}: restored baseline hash {actual} differs from the pinned {expected}")


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="command", required=True)
    t = sub.add_parser("tag")
    t.add_argument("tag")
    t.add_argument("--root", default=".")
    t.add_argument("--branches", required=True, help="output of `git branch -r --contains <sha>`")
    a = sub.add_parser("audit")
    a.add_argument("--kind", choices=["vulnerable", "deprecated"], required=True)
    a.add_argument("--file", required=True)
    a.add_argument("--ignore-project", default="", help="regex of project names whose findings are ignored (test projects)")
    n = sub.add_parser("not-published")
    n.add_argument("--expected", help="space-separated package ids")
    n.add_argument("--dir", help="folder with the packed <id>.<version>.nupkg files (ids are taken from the file names)")
    n.add_argument("--version", required=True)
    v = sub.add_parser("version", help="print the one version every packable project declares")
    v.add_argument("--root", default=".")
    b = sub.add_parser("baseline")
    b.add_argument("--root", default=".")
    b.add_argument("--config", default="scripts/baseline.json")
    b.add_argument("--packages-dir", required=True)
    args = ap.parse_args(argv)
    try:
        if args.command == "audit":
            check_audit(pathlib.Path(args.file).read_text(errors="replace"), args.kind, args.ignore_project)
        elif args.command == "not-published":
            ids = args.expected.split() if args.expected else [
                f.name[: -len(f".{args.version}.nupkg")] for f in sorted(pathlib.Path(args.dir).glob(f"*.{args.version}.nupkg"))]
            if not ids:
                raise ReleaseError("no packages to check: pass --expected or a --dir that holds the packed packages")
            check_not_published(ids, args.version)
        elif args.command == "version":
            print(declared_version(pathlib.Path(args.root)))
        elif args.command == "baseline":
            root = pathlib.Path(args.root)
            check_baseline(root, root / args.config, pathlib.Path(args.packages_dir))
        else:
            result = check_tag(args.tag, pathlib.Path(args.root), args.branches)
            print(f"version={result.version}")
    except ReleaseError as error:
        print(f"::error::{error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
