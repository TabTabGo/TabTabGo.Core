"""Tests for scripts/check_pack_output.py. Run: python3 -m unittest discover -s scripts -p "test_*.py" """
import pathlib
import sys
import tempfile
import unittest
import zipfile

SCRIPTS = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPTS))
import check_pack_output as cpo  # noqa: E402

IDS = ["TabTabGo.Core", "TabTabGo.Core.Services"]


def nuspec(pid, version, deps):
    items = "".join(f'<dependency id="{i}" version="{v}" />' for i, v in deps)
    return (f'<?xml version="1.0"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata>'
            f'<id>{pid}</id><version>{version}</version><dependencies><group targetFramework="net8.0">{items}</group></dependencies>'
            f'</metadata></package>')


def write_pkg(directory, pid, version, deps=(), tfm="net8.0", readme=True, symbols=True, nupkg_version=None):
    nv = nupkg_version or version
    with zipfile.ZipFile(directory / f"{pid}.{version}.nupkg", "w") as z:
        z.writestr(f"{pid}.nuspec", nuspec(pid, nv, deps))
        z.writestr(f"lib/{tfm}/{pid}.dll", b"x")
        if readme:
            z.writestr("README.md", "r")
    if symbols:
        with zipfile.ZipFile(directory / f"{pid}.{version}.snupkg", "w") as z:
            z.writestr(f"{pid}.nuspec", nuspec(pid, nv, ()))


def good(d, version="8.0.1"):
    write_pkg(d, "TabTabGo.Core", version)
    write_pkg(d, "TabTabGo.Core.Services", version, [("TabTabGo.Core", f"[{version}, )"), ("Newtonsoft.Json", "[13.0.4, )")])


class InspectTests(unittest.TestCase):
    def run_inspect(self, d, **kw):
        return cpo.inspect(pathlib.Path(d), kw.pop("version", "8.0.1"), kw.pop("ids", IDS), kw.pop("tfm", "net8.0"), kw.pop("readme", True))[0]

    def test_clean_output_passes(self):
        with tempfile.TemporaryDirectory() as d:
            good(pathlib.Path(d))
            self.assertEqual([], self.run_inspect(d))

    def test_missing_unexpected_and_duplicate_packages_fail(self):
        with tempfile.TemporaryDirectory() as d:
            p = pathlib.Path(d)
            write_pkg(p, "TabTabGo.Core", "8.0.1")
            self.assertTrue(any("mismatch" in e for e in self.run_inspect(d)))
            write_pkg(p, "TabTabGo.Core.Services", "8.0.1")
            write_pkg(p, "TabTabGo.Core.Infrastructure", "8.0.1")
            self.assertTrue(any("unexpected" in e for e in self.run_inspect(d)))

    def test_wrong_version_prerelease_and_local_fail(self):
        for version in ("8.0.2", "8.0.1-rc.1", "8.0.1-local"):
            with self.subTest(version=version), tempfile.TemporaryDirectory() as d:
                good(pathlib.Path(d), version)
                self.assertNotEqual([], self.run_inspect(d))

    def test_missing_snupkg_fails(self):
        with tempfile.TemporaryDirectory() as d:
            p = pathlib.Path(d)
            write_pkg(p, "TabTabGo.Core", "8.0.1")
            write_pkg(p, "TabTabGo.Core.Services", "8.0.1", symbols=False)
            self.assertTrue(any("snupkg" in e for e in self.run_inspect(d)))

    def test_missing_tfm_lib_and_readme_fail(self):
        with tempfile.TemporaryDirectory() as d:
            p = pathlib.Path(d)
            write_pkg(p, "TabTabGo.Core", "8.0.1", tfm="net6.0", readme=False)
            write_pkg(p, "TabTabGo.Core.Services", "8.0.1")
            errors = self.run_inspect(d)
            self.assertTrue(any("lib/net8.0" in e for e in errors))
            self.assertTrue(any("README" in e for e in errors))

    def test_sibling_dependency_range_must_be_exact(self):
        for bad in ("[0.0.0, )", "[8.0.0, )", "[8.0.1-alpha, )", "8.0.*", ""):
            with self.subTest(range=bad), tempfile.TemporaryDirectory() as d:
                p = pathlib.Path(d)
                write_pkg(p, "TabTabGo.Core", "8.0.1")
                write_pkg(p, "TabTabGo.Core.Services", "8.0.1", [("TabTabGo.Core", bad)])
                self.assertNotEqual([], self.run_inspect(d))

    def test_prerelease_third_party_dependency_fails(self):
        with tempfile.TemporaryDirectory() as d:
            p = pathlib.Path(d)
            write_pkg(p, "TabTabGo.Core", "8.0.1", [("Foo", "[1.0.0-beta.1, )")])
            write_pkg(p, "TabTabGo.Core.Services", "8.0.1")
            self.assertTrue(any("prerelease" in e for e in self.run_inspect(d)))

    def test_empty_directory_fails(self):
        with tempfile.TemporaryDirectory() as d:
            self.assertNotEqual([], self.run_inspect(d))

    def test_cli_exit_codes(self):
        with tempfile.TemporaryDirectory() as d:
            good(pathlib.Path(d))
            args = [d, "--version", "8.0.1", "--ids", ",".join(IDS), "--tfm", "net8.0", "--require-readme"]
            self.assertEqual(0, cpo.main(args))
            self.assertEqual(1, cpo.main([d, "--version", "8.0.2", "--ids", ",".join(IDS)]))


class ListIdsTests(unittest.TestCase):
    def test_list_ids_prints_one_clean_id_per_line(self):
        import contextlib
        import io
        with tempfile.TemporaryDirectory() as tmp:
            f = pathlib.Path(tmp) / "ids.txt"
            f.write_text("# push order\nA\n\nB  # second\n")
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                self.assertEqual(cpo.main(["list-ids", str(f)]), 0)
            self.assertEqual(out.getvalue().split(), ["A", "B"])

    def test_list_ids_refuses_an_empty_list(self):
        with tempfile.TemporaryDirectory() as tmp:
            f = pathlib.Path(tmp) / "ids.txt"
            f.write_text("# nothing\n")
            self.assertEqual(cpo.main(["list-ids", str(f)]), 1)


class LoadIdsTests(unittest.TestCase):
    def test_ids_file_and_ids_are_merged_without_duplicates(self):
        with tempfile.TemporaryDirectory() as tmp:
            f = pathlib.Path(tmp) / "ids.txt"
            f.write_text("A\n# comment\nB  # trailing\n\nA\n")
            self.assertEqual(cpo.load_ids("C,A", f), ["C", "A", "B"])
            self.assertEqual(cpo.load_ids("", f), ["A", "B"])
            self.assertEqual(cpo.load_ids("X,Y", None), ["X", "Y"])

    def test_nothing_given_is_empty(self):
        self.assertEqual(cpo.load_ids("", None), [])


if __name__ == "__main__":
    unittest.main()
