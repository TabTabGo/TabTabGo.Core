#!/usr/bin/env python3
"""Inspect the output of `dotnet pack` for a TabTabGo.Core release line (tested by scripts/test_check_pack_output.py).

Checks, for the directory given:
  * EXACTLY the expected set of package ids, one package per id (no extra, no missing, no duplicate versions),
  * one .snupkg per id (symbols are always required for a release),
  * every package has the version --version, and no -local / prerelease version anywhere,
  * a lib/<tfm>/ folder in every package (when --tfm is given) and a README.md (when --require-readme),
  * every dependency has a version range, none is prerelease/-local, and every TabTabGo.* sibling dependency is exactly
    `[<version>, )` or `<version>`: no stray 0.0.0, no floating range, no foreign version.

Usage: python3 scripts/check_pack_output.py <dir> --version 8.0.1 --ids Id1,Id2,... [--tfm net8.0] [--require-readme]
Exit code 0 when green, 1 otherwise (each problem is printed as a GitHub ::error::).
"""
import argparse
import re
import sys
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

PRERELEASE = re.compile(r"-[0-9A-Za-z]")


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def read_package(package: Path):
    """Return (metadata element, member names) of a .nupkg."""
    with zipfile.ZipFile(package) as zf:
        names = zf.namelist()
        nuspecs = [n for n in names if n.endswith(".nuspec") and "/" not in n]
        if len(nuspecs) != 1:
            raise ValueError(f"{package.name}: expected exactly one root .nuspec, found {len(nuspecs)}")
        root = ET.fromstring(zf.read(nuspecs[0]))
    meta = next((e for e in root if local(e.tag) == "metadata"), None)
    if meta is None:
        raise ValueError(f"{package.name}: nuspec has no metadata")
    return meta, names


def inspect(pack_dir: Path, version: str, ids, tfm: str = "", require_readme: bool = False):
    """Return (errors, found) where found maps package id -> version."""
    expected = set(ids)
    errors = []
    found = {}
    nupkgs = sorted(p for p in pack_dir.rglob("*.nupkg"))
    snupkgs = sorted(p for p in pack_dir.rglob("*.snupkg"))
    if not nupkgs:
        errors.append(f"no .nupkg files under {pack_dir}")
    for package in nupkgs:
        try:
            meta, names = read_package(package)
        except (ValueError, zipfile.BadZipFile, ET.ParseError) as error:
            errors.append(str(error))
            continue
        pid = next((e.text for e in meta if local(e.tag) == "id"), None)
        pver = next((e.text for e in meta if local(e.tag) == "version"), None)
        if not pid or not pver:
            errors.append(f"{package.name}: nuspec without id or version")
            continue
        if pid in found:
            errors.append(f"{pid}: more than one package/version ({found[pid]}, {pver})")
        found[pid] = pver
        if package.name != f"{pid}.{pver}.nupkg":
            errors.append(f"{package.name}: file name differs from the nuspec id/version {pid} {pver}")
        if pver != version:
            errors.append(f"{pid}: version {pver} != {version}")
        if "local" in pver.lower() or PRERELEASE.search(pver):
            errors.append(f"{pid}: local/prerelease version {pver}")
        if tfm and not any(n.startswith(f"lib/{tfm}/") for n in names):
            errors.append(f"{pid}: no lib/{tfm}/ folder")
        if require_readme and "README.md" not in names:
            errors.append(f"{pid}: no README.md")
        for dep in meta.iter():
            if local(dep.tag) != "dependency":
                continue
            did, drange = dep.get("id"), dep.get("version", "")
            if not drange:
                errors.append(f"{pid}: dependency {did} has no version range")
                continue
            if PRERELEASE.search(drange) or "local" in drange.lower():
                errors.append(f"{pid}: prerelease/local dependency {did} {drange}")
            if did and did.startswith("TabTabGo.") and drange not in (f"[{version}, )", version):
                errors.append(f"{pid}: sibling dependency {did} range '{drange}' != '[{version}, )'")
    if set(found) != expected:
        errors.append(f"package id set mismatch: missing={sorted(expected - set(found))} unexpected={sorted(set(found) - expected)}")
    snupkg_ids = set()
    for p in snupkgs:
        if not p.name.endswith(f".{version}.snupkg"):
            errors.append(f"{p.name}: symbols package with a different version")
            continue
        snupkg_ids.add(p.name[: -len(f".{version}.snupkg")])
    if len(snupkgs) != len(snupkg_ids) or snupkg_ids != expected:
        errors.append(f"snupkg set mismatch: missing={sorted(expected - snupkg_ids)} unexpected={sorted(snupkg_ids - expected)}")
    return errors, found


def load_ids(ids: str = "", ids_file=None):
    """Expected ids from --ids (comma separated) and/or --ids-file (one per line, # comments allowed), without duplicates."""
    found = [i.strip() for i in ids.split(",") if i.strip()]
    if ids_file is not None:
        for line in Path(ids_file).read_text().splitlines():
            line = line.split("#", 1)[0].strip()
            if line:
                found.append(line)
    return list(dict.fromkeys(found))


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("pack_dir", type=Path)
    parser.add_argument("--version", required=True)
    parser.add_argument("--ids", default="", help="comma separated expected package ids")
    parser.add_argument("--ids-file", type=Path, help="file with one expected package id per line (scripts/expected-packages.txt)")
    parser.add_argument("--tfm", default="")
    parser.add_argument("--require-readme", action="store_true")
    args = parser.parse_args(argv)
    ids = load_ids(args.ids, args.ids_file)
    if not ids:
        print("::error::no expected package ids (--ids or --ids-file)", file=sys.stderr)
        return 1
    errors, found = inspect(args.pack_dir, args.version, ids, args.tfm, args.require_readme)
    for pid in sorted(found):
        print(f"  {pid} {found[pid]}")
    if errors:
        for e in errors:
            print(f"::error::{e}", file=sys.stderr)
        print("FAIL")
        return 1
    print(f"OK: {len(found)} packages, all {args.version}, {len(ids)} snupkg")
    return 0


if __name__ == "__main__":
    sys.exit(main())
