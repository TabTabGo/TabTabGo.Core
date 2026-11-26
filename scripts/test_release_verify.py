"""Tests for scripts/release_verify.py and for the guarantees of .github/workflows/release.yml.

Run: python3 -m unittest discover -s scripts -p "test_*.py"
"""
import hashlib
import json
import pathlib
import re
import sys
import tempfile
import unittest

SCRIPTS = pathlib.Path(__file__).resolve().parent
REPO = SCRIPTS.parent
sys.path.insert(0, str(SCRIPTS))
import release_verify as rv  # noqa: E402


def branches(*names: str) -> str:
    return "\n".join(f"  origin/{n}" for n in names)


def make_repo(root: pathlib.Path, tfm="net10.0", version="10.0.0", props_version=True, csproj_version=None, extra=""):
    """A minimal tree: Directory.Build.props, one packable and one non-packable project."""
    pv = f"<Version>{version}</Version>" if props_version else ""
    (root / "Directory.Build.props").write_text(
        f"<Project><PropertyGroup><TargetFramework>{tfm}</TargetFramework>{pv}"
        f"<PackageValidationBaselineVersion>8.0.1</PackageValidationBaselineVersion></PropertyGroup></Project>")
    lib = root / "src" / "Lib"
    lib.mkdir(parents=True)
    cv = f"<Version>{csproj_version}</Version>" if csproj_version else ""
    (lib / "Lib.csproj").write_text(f"<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup>{cv}{extra}</PropertyGroup></Project>")
    empty = root / "src" / "Empty"
    empty.mkdir(parents=True)
    (empty / "Empty.csproj").write_text("<Project><PropertyGroup><IsPackable>false</IsPackable></PropertyGroup></Project>")


class TagTests(unittest.TestCase):
    CASES = [
        ("v8.0.1", "net8.0", "8.0.1", "releases/v8.0.x/v8.0.1"),
        ("v10.0.0", "net10.0", "10.0.0", "releases/v10.0.x/v10.0.0"),
        ("v11.0.0", "net11.0", "11.0.0", "releases/v11.0.x/v11.0.0"),
    ]

    def check(self, tag, tfm, version, branch, **kw):
        with tempfile.TemporaryDirectory() as d:
            root = pathlib.Path(d)
            make_repo(root, tfm, version, **kw)
            return rv.check_tag(tag, root, branches("master", branch))

    def test_valid_tags_pass(self):
        for tag, tfm, version, branch in self.CASES:
            with self.subTest(tag=tag):
                self.assertEqual(tag[1:], self.check(tag, tfm, version, branch).version)

    def test_v8_style_version_in_each_csproj_passes(self):
        self.assertEqual("8.0.1", self.check("v8.0.1", "net8.0", "8.0.1", "releases/v8.0.x/v8.0.1",
                                              props_version=False, csproj_version="8.0.1").version)

    def test_project_without_any_version_is_refused(self):
        with self.assertRaises(rv.ReleaseError):
            self.check("v8.0.1", "net8.0", "8.0.1", "releases/v8.0.x/v8.0.1", props_version=False)

    def test_version_mismatch_is_refused(self):
        with self.assertRaises(rv.ReleaseError):
            self.check("v10.0.1", "net10.0", "10.0.0", "releases/v10.0.x/v10.0.1")
        with self.assertRaises(rv.ReleaseError):  # a csproj overriding the shared version
            self.check("v10.0.0", "net10.0", "10.0.0", "releases/v10.0.x/v10.0.0", csproj_version="10.0.1")

    def test_wrong_major_is_refused(self):
        with self.assertRaises(rv.ReleaseError):
            self.check("v10.0.0", "net8.0", "10.0.0", "releases/v10.0.x/v10.0.0")
        with self.assertRaises(rv.ReleaseError):
            self.check("v11.0.0", "net10.0", "11.0.0", "releases/v11.0.x/v11.0.0")

    def test_commit_must_be_on_the_matching_release_branch(self):
        for bad in ("releases/v10.0.x/v10.0.0", "releases/v11.1.x/v11.1.0", "features/x", "master"):
            with self.subTest(branch=bad), tempfile.TemporaryDirectory() as d:
                root = pathlib.Path(d)
                make_repo(root, "net11.0", "11.0.0")
                with self.assertRaises(rv.ReleaseError):
                    rv.check_tag("v11.0.0", root, branches(bad))

    def test_unstable_and_malformed_tags_are_refused(self):
        for tag in ("v11.0.0-rc.1", "v11.0.0+meta", "v11.0.0-local", "v011.0.0", "v11.00.0", "v11.0", "11.0.0", "v11.0.0\n", "v１１.0.0", "vx"):
            with self.subTest(tag=tag), self.assertRaises(rv.ReleaseError):
                rv.parse_tag(tag)

    def test_stable_only_message_for_prerelease(self):
        with self.assertRaises(rv.ReleaseError) as ctx:
            rv.parse_tag("v11.0.0-rc.1")
        self.assertIn("stable", str(ctx.exception))

    def test_cli_prints_version_output_and_fails_with_exit_code_1(self):
        with tempfile.TemporaryDirectory() as d:
            root = pathlib.Path(d)
            make_repo(root)
            self.assertEqual(0, rv.main(["tag", "v10.0.0", "--root", d, "--branches", branches("releases/v10.0.x/v10.0.0")]))
            self.assertEqual(1, rv.main(["tag", "v10.0.0-rc.1", "--root", d, "--branches", branches("releases/v10.0.x/v10.0.0")]))


class VersionTests(unittest.TestCase):
    def test_prints_the_common_version_and_refuses_differences(self):
        with tempfile.TemporaryDirectory() as d:
            root = pathlib.Path(d)
            make_repo(root, version="10.0.0")
            self.assertEqual("10.0.0", rv.declared_version(root))
        with tempfile.TemporaryDirectory() as d:
            root = pathlib.Path(d)
            make_repo(root, props_version=False, csproj_version="8.0.1")
            other = root / "src" / "Other"
            other.mkdir()
            (other / "Other.csproj").write_text("<Project><PropertyGroup><Version>8.0.2</Version></PropertyGroup></Project>")
            with self.assertRaises(rv.ReleaseError):
                rv.declared_version(root)


class DependencyTests(unittest.TestCase):
    def test_prerelease_floating_and_local_versions_are_refused(self):
        for version in ("1.0.0-beta.1", "1.*", "[1.0.0, 2.0.0)-rc", "1.0.0-local"):
            with self.subTest(version=version), tempfile.TemporaryDirectory() as d:
                root = pathlib.Path(d)
                make_repo(root)
                (root / "Directory.Packages.props").write_text(f'<Project><ItemGroup><PackageVersion Include="X" Version="{version}" /></ItemGroup></Project>')
                with self.assertRaises(rv.ReleaseError):
                    rv.check_tag("v10.0.0", root, branches("releases/v10.0.x/v10.0.0"))

    def test_stable_ranges_and_property_references_pass(self):
        with tempfile.TemporaryDirectory() as d:
            root = pathlib.Path(d)
            make_repo(root)
            (root / "Directory.Packages.props").write_text(
                '<Project><ItemGroup><PackageVersion Include="X" Version="13.0.4" /><PackageReference Include="Y" Version="$(V)" />'
                '</ItemGroup></Project>')
            rv.check_tag("v10.0.0", root, branches("releases/v10.0.x/v10.0.0"))


class AuditTests(unittest.TestCase):
    CLEAN = "The given project `A` has no vulnerable packages given the current sources.\n"

    def test_clean_passes(self):
        rv.check_audit(self.CLEAN, "vulnerable")

    def test_findings_fail(self):
        with self.assertRaises(rv.ReleaseError):
            rv.check_audit("Project `A` has the following vulnerable packages\n   > X 1.0 High\n", "vulnerable")

    def test_findings_of_ignored_test_projects_are_dropped_but_others_still_fail(self):
        text = (self.CLEAN + "Project `X.Test` has the following deprecated packages\n   [net8.0]:\n   > xunit 2.9.3 Legacy\n\n"
                "The given project `Lib` has no deprecated packages given the current sources.\n")
        rv.check_audit(text, "deprecated", r"\.Tests?$")
        with self.assertRaises(rv.ReleaseError):
            rv.check_audit(text, "deprecated")
        with self.assertRaises(rv.ReleaseError):
            rv.check_audit(text.replace("X.Test", "Lib2"), "deprecated", r"\.Tests?$")

    def test_unreadable_source_fails_closed(self):
        for text in (self.CLEAN + "warning NU1900: Error occurred while getting package vulnerability data", "", "garbage"):
            with self.subTest(text=text), self.assertRaises(rv.ReleaseError):
                rv.check_audit(text, "vulnerable")


class NotPublishedTests(unittest.TestCase):
    def test_not_found_passes(self):
        rv.check_not_published(["TabTabGo.Core"], "10.0.0", status=lambda url: 404)

    def test_exists_fails(self):
        with self.assertRaises(rv.ReleaseError):
            rv.check_not_published(["TabTabGo.Core"], "10.0.0", status=lambda url: 200)

    def test_unknown_answer_and_network_error_fail(self):
        with self.assertRaises(rv.ReleaseError):
            rv.check_not_published(["TabTabGo.Core"], "10.0.0", status=lambda url: 503)

        def boom(url):
            raise OSError("down")
        with self.assertRaises(rv.ReleaseError):
            rv.check_not_published(["TabTabGo.Core"], "10.0.0", status=boom)


class BaselineTests(unittest.TestCase):
    def setup(self, d, pins):
        root = pathlib.Path(d)
        make_repo(root, extra="<PackageId>TabTabGo.Lib</PackageId>")
        (root / "scripts").mkdir()
        (root / "scripts" / "baseline.json").write_text(json.dumps({"version": "8.0.1", "sha256": pins}))
        cache = root / "cache" / "tabtabgo.lib" / "8.0.1"
        cache.mkdir(parents=True)
        payload = b"nupkg"
        (cache / "tabtabgo.lib.8.0.1.nupkg").write_bytes(payload)
        return root, hashlib.sha256(payload).hexdigest()

    def run_check(self, root):
        rv.check_baseline(root, root / "scripts" / "baseline.json", root / "cache")

    def test_empty_pins_fail_closed(self):
        with tempfile.TemporaryDirectory() as d:
            root, _ = self.setup(d, {})
            with self.assertRaises(rv.ReleaseError):
                self.run_check(root)

    def test_matching_pin_passes_and_wrong_pin_fails(self):
        with tempfile.TemporaryDirectory() as d:
            root, digest = self.setup(d, {})
            (root / "scripts" / "baseline.json").write_text(json.dumps({"version": "8.0.1", "sha256": {"TabTabGo.Lib": digest}}))
            self.run_check(root)
            (root / "scripts" / "baseline.json").write_text(json.dumps({"version": "8.0.1", "sha256": {"TabTabGo.Lib": "0" * 64}}))
            with self.assertRaises(rv.ReleaseError):
                self.run_check(root)

    def test_props_baseline_must_equal_the_pinned_version(self):
        with tempfile.TemporaryDirectory() as d:
            root, digest = self.setup(d, {})
            (root / "scripts" / "baseline.json").write_text(json.dumps({"version": "8.0.1", "sha256": {"TabTabGo.Lib": digest}}))
            props = root / "Directory.Build.props"
            props.write_text(props.read_text().replace("8.0.1</Package", "8.0.1-alpha-01</Package"))
            with self.assertRaises(rv.ReleaseError):
                self.run_check(root)

    def test_prerelease_baseline_version_is_refused(self):
        with tempfile.TemporaryDirectory() as d:
            root, _ = self.setup(d, {})
            (root / "scripts" / "baseline.json").write_text(json.dumps({"version": "8.0.1-alpha-01", "sha256": {}}))
            with self.assertRaises(rv.ReleaseError):
                self.run_check(root)


class WorkflowTests(unittest.TestCase):
    """Static guarantees of the release workflow: they must not be weakened by an edit."""

    def setUp(self):
        self.text = (REPO / ".github" / "workflows" / "release.yml").read_text()

    def test_legacy_workflow_is_gone(self):
        self.assertFalse((REPO / ".github" / "workflows" / "publish-to-nuget.yml").exists())

    def test_no_skip_duplicate_and_no_api_key_secret(self):
        self.assertNotIn("--skip-duplicate", self.text)
        self.assertNotIn("NUGET_API_KEY }}", self.text.replace("steps.login.outputs.NUGET_API_KEY", ""))

    def test_trusted_publishing_and_environment(self):
        self.assertIn("environment: nuget-stable", self.text)
        self.assertIn("id-token: write", self.text)
        self.assertIn("NuGet/login@", self.text)

    def test_every_action_is_pinned_by_sha(self):
        for use in re.findall(r"^\s*(?:-\s*)?uses:\s*(\S+)", self.text, re.M):
            self.assertRegex(use, r"@[0-9a-f]{40}$", use)

    def test_checkouts_do_not_persist_credentials(self):
        self.assertEqual(self.text.count("actions/checkout@"), self.text.count("persist-credentials: false"))

    def test_no_pull_request_trigger(self):
        self.assertNotIn("pull_request", self.text)

    def job(self, name):
        match = re.search(rf"^  {re.escape(name)}:\n(.*?)(?=^  [a-z][\w-]*:\n|\Z)", self.text, re.M | re.S)
        self.assertIsNotNone(match, name)
        return match.group(1)

    def test_publish_checks_out_before_it_downloads_and_downloads_outside_the_workspace(self):
        publish = self.job("publish")
        # actions/checkout empties a workspace that has no .git: a download made before it would be wiped
        self.assertLess(publish.index("actions/checkout@"), publish.index("actions/download-artifact@"))
        self.assertRegex(publish, r"path: \$\{\{ runner\.temp \}\}/out")
        self.assertNotIn("check_pack_output.py out ", publish)
        self.assertNotIn('"out/', publish)
        self.assertIn('"$RUNNER_TEMP/out/', publish)

    def test_publish_trusts_the_resolved_commit_not_the_movable_tag(self):
        publish = self.job("publish")
        self.assertIn("ref: ${{ needs.guard.outputs.sha }}", publish)
        self.assertNotIn("refs/tags/", publish)

    def test_github_release_checks_that_the_tag_still_points_at_the_published_commit(self):
        release = self.job("github-release")
        self.assertIn("git/ref/tags/", release)
        self.assertIn("git/tags/", release)  # annotated tags are dereferenced
        self.assertIn("SHA: ${{ needs.guard.outputs.sha }}", release)
        self.assertLess(release.index('"$resolved" != "$SHA"'), release.index("gh release create"))

    def test_publish_runs_only_on_an_explicit_false_dry_run(self):
        self.assertIn("if: needs.guard.outputs.dry_run == 'false'", self.job("publish"))
        self.assertNotIn("dry_run != 'true'", self.text)

    def test_expected_packages_come_from_one_file(self):
        self.assertNotIn("EXPECTED_PACKAGES", self.text)
        ids = (REPO / "scripts" / "expected-packages.txt").read_text().split()
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(ids[0], "TabTabGo.Core")  # first: the others depend on it
        self.assertGreaterEqual(self.text.count("scripts/expected-packages.txt"), 3)

    def test_ci_does_not_hide_the_version_script_exit_code(self):
        ci = (REPO / ".github" / "workflows" / "ci.yml").read_text()
        self.assertNotIn('--version "$(python3', ci)
        self.assertIn('version="$(python3 scripts/release_verify.py version)"', ci)
        self.assertIn("scripts/expected-packages.txt", ci)


if __name__ == "__main__":
    unittest.main()
