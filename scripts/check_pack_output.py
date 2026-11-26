#!/usr/bin/env python3
"""Assert the output of `dotnet pack` for the TabTabGo.Core v8 line.

Checks, for the directory given:
  * exactly the expected set of package ids (and one .snupkg per id when any exist or --require-snupkg),
  * a single version for every package, equal to --version,
  * no `-local` / prerelease version on any package or dependency range,
  * every package depends on the same-version TabTabGo.* siblings only (no stray 0.0.0 / prerelease).

Usage: python3 scripts/check_pack_output.py <pack-output-dir> [--version 8.0.1] [--ids Id1,Id2,...]
Exit code 0 when green, 1 otherwise.
"""
import argparse
import re
import sys
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

DEFAULT_IDS = [
    "TabTabGo.Core",
    "TabTabGo.Core.Services",
    "TabTabGo.Core.Api",
    "TabTabGo.Data.EF",
    "TabTabGo.Core.Country",
    "TabTabGo.Core.Infrastructure",
]
PRERELEASE = re.compile(r"-[0-9A-Za-z]")


def read_nuspec(package: Path) -> ET.Element:
    with zipfile.ZipFile(package) as zf:
        name = next(n for n in zf.namelist() if n.endswith(".nuspec") and "/" not in n)
        return ET.fromstring(zf.read(name))


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("pack_dir", type=Path)
    parser.add_argument("--version", default="8.0.1")
    parser.add_argument("--require-snupkg", action="store_true",
                        help="fail when a .snupkg is missing (pack with -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg)")
    parser.add_argument("--ids", default=",".join(DEFAULT_IDS), help="comma separated expected package ids")
    args = parser.parse_args()

    expected = set(i for i in args.ids.split(",") if i)
    errors: list[str] = []
    found: dict[str, str] = {}

    nupkgs = sorted(p for p in args.pack_dir.rglob("*.nupkg"))
    snupkgs = sorted(p for p in args.pack_dir.rglob("*.snupkg"))
    if not nupkgs:
        errors.append(f"no .nupkg files under {args.pack_dir}")

    for package in nupkgs:
        meta = next(e for e in read_nuspec(package) if local(e.tag) == "metadata")
        pid = next(e.text for e in meta if local(e.tag) == "id")
        version = next(e.text for e in meta if local(e.tag) == "version")
        if pid in found:
            errors.append(f"{pid}: more than one package/version ({found[pid]}, {version})")
        found[pid] = version
        if version != args.version:
            errors.append(f"{pid}: version {version} != {args.version}")
        if "local" in version.lower() or PRERELEASE.search(version):
            errors.append(f"{pid}: local/prerelease version {version}")
        for dep in meta.iter():
            if local(dep.tag) != "dependency":
                continue
            did, drange = dep.get("id"), dep.get("version", "")
            if PRERELEASE.search(drange) or "local" in drange.lower():
                errors.append(f"{pid}: prerelease/local dependency {did} {drange}")
            if did and did.startswith("TabTabGo.") and drange != f"[{args.version}, )" and drange != args.version:
                errors.append(f"{pid}: sibling dependency {did} range '{drange}' != '[{args.version}, )'")
        # an empty range is also a defect
            if not drange:
                errors.append(f"{pid}: dependency {did} has no version range")

    ids = set(found)
    if ids != expected:
        errors.append(
            f"package id set mismatch: missing={sorted(expected - ids)} unexpected={sorted(ids - expected)}")

    snupkg_ids = {re.sub(rf"\.{re.escape(args.version)}\.snupkg$", "", p.name) for p in snupkgs}
    if (snupkgs or args.require_snupkg) and snupkg_ids != expected:
        errors.append(
            f"snupkg set mismatch: missing={sorted(expected - snupkg_ids)} unexpected={sorted(snupkg_ids - expected)}")

    for pid in sorted(found):
        print(f"  {pid} {found[pid]}")
    if errors:
        print("FAIL")
        for e in errors:
            print(f" - {e}")
        return 1
    print(f"OK: {len(found)} packages, all {args.version}, {len(snupkgs)} snupkg")
    return 0


if __name__ == "__main__":
    sys.exit(main())
