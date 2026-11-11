"""Pinned Wine Mono DEP correction and its packaged provenance."""
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import struct
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("package_wine_clr", ROOT / "scripts/package-wine-clr.py")
package = importlib.util.module_from_spec(spec)
spec.loader.exec_module(package)
ASSEMBLY = "Contents/Resources/wine/share/wine/mono/wine-mono-11.3.0/lib/mono/gac/Accessibility/4.0.0.0__b03f5f7f11d50a3a/Accessibility.dll"


def digest(data):
    return hashlib.sha256(data).hexdigest()


def fixture():
    data = bytearray((i % 251 for i in range(512)))
    data[:2] = b"MZ"
    struct.pack_into("<I", data, 0x3c, 0x80)
    data[0x80:0x84] = b"PE\0\0"
    struct.pack_into("<H", data, 0x80 + 24, 0x10b)
    struct.pack_into("<H", data, 0x80 + 24 + 70, 0x8040)
    corrected = bytearray(data)
    struct.pack_into("<H", corrected, 0x80 + 24 + 70, 0x8140)
    return bytes(data), bytes(corrected)


class MonoNxCompatTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.app = Path(self.temp.name) / "Wine Devel.app"
        self.target = self.app / ASSEMBLY
        self.target.parent.mkdir(parents=True)
        self.original, self.corrected = fixture()
        self.target.write_bytes(self.original)
        pin = patch.object(package, "MONO_ACCESSIBILITY", (ASSEMBLY, digest(self.original), digest(self.corrected)), create=True)
        pin.start()
        self.addCleanup(pin.stop)

    def test_sets_only_nx_compat_and_records_original_and_corrected_hashes(self):
        result = package.patch_mono_nx_compat(self.app)
        self.assertEqual(self.target.read_bytes(), self.corrected)
        self.assertEqual(result, dict(path=ASSEMBLY, original_sha256=digest(self.original), sha256=digest(self.corrected)))

    def test_already_corrected_assembly_is_accepted_idempotently(self):
        self.target.write_bytes(self.corrected)
        result = package.patch_mono_nx_compat(self.app)
        self.assertEqual(self.target.read_bytes(), self.corrected)
        self.assertEqual(result["original_sha256"], digest(self.original))
        self.assertEqual(result["sha256"], digest(self.corrected))

    def test_unknown_or_malformed_input_is_rejected_without_modification(self):
        for data in (b"", b"not a PE", self.original[:-1], self.original + b"unexpected"):
            with self.subTest(data=data[:8]):
                self.target.write_bytes(data)
                with self.assertRaisesRegex(SystemExit, "Accessibility.*SHA-256"):
                    package.patch_mono_nx_compat(self.app)
                self.assertEqual(self.target.read_bytes(), data)

    def test_missing_assembly_fails_clearly(self):
        self.target.unlink()
        with self.assertRaisesRegex(SystemExit, "Accessibility"):
            package.patch_mono_nx_compat(self.app)

    def test_unexpected_result_hash_is_rejected_before_writing(self):
        with patch.object(package, "MONO_ACCESSIBILITY", (ASSEMBLY, digest(self.original), "0" * 64)):
            with self.assertRaisesRegex(SystemExit, "Accessibility.*SHA-256"):
                package.patch_mono_nx_compat(self.app)
        self.assertEqual(self.target.read_bytes(), self.original)

    def test_packaged_runtime_is_corrected_before_signing_with_matching_provenance(self):
        build = Path(self.temp.name) / "build"
        build.mkdir()
        cpu = bytearray(256)
        cpu[:2] = b"MZ"
        cpu[0x40:0x50] = b"Wine builtin DLL"
        struct.pack_into("<I", cpu, 0x3c, 0x80)
        cpu[0x80:0x84] = b"PE\0\0"
        struct.pack_into("<H", cpu, 0x84, 0x8664)
        for name, data in {"wow64cpu.dll": cpu, "appxmenu.so": b"menu", "wow64cpu.patch": b"patch",
                           "wine-11.18.tar.xz": b"source", "build-wine-clr.sh": b"build",
                           "game_menu.m": b"menu source", "build-macos-menu.sh": b"menu build",
                           "game_menu_test.m": b"menu test"}.items():
            (build / name).write_bytes(data)
        base = io.BytesIO()
        with tarfile.open(fileobj=base, mode="w:xz") as archive:
            for name, data in {"Wine Devel.app/" + ASSEMBLY: self.original,
                               "Wine Devel.app/Contents/Resources/wine/lib/wine/x86_64-windows/wow64cpu.dll": b"stock",
                               "Wine Devel.app/Contents/Resources/wine/lib/wine/x86_64-unix/appxmenu.so": b"stock menu"}.items():
                info = tarfile.TarInfo(name)
                info.size = len(data)
                archive.addfile(info, io.BytesIO(data))
        base_file = build / "base.tar.xz"
        base_file.write_bytes(base.getvalue())
        signed = []

        def codesign(command, check):
            self.assertTrue(check)
            self.assertEqual(command[0], "codesign")
            self.assertEqual((build / "runtime/Wine Devel.app" / ASSEMBLY).read_bytes(), self.corrected)
            signed.append(command)

        with patch.object(package, "WINE_DEVEL", ("https://example.invalid/pinned.tar.xz", digest(base.getvalue()))), \
             patch.object(sys, "argv", ["package-wine-clr.py", "--build", str(build), "--base-archive", str(base_file)]), \
             patch.object(package.subprocess, "run", side_effect=codesign):
            package.main()
        self.assertEqual(len(signed), 2)
        self.assertIn("--sign", signed[0])
        self.assertIn("--verify", signed[1])
        manifest = json.loads((build / "runtime/wine-clr.json").read_text())
        self.assertEqual(manifest["schema"], 1)
        self.assertEqual(manifest["mono_nx_compat"], dict(path=ASSEMBLY, original_sha256=digest(self.original), sha256=digest(self.corrected)))
        with tarfile.open(build / "appx-portable-wine-clr-macos.tar.xz") as archive:
            self.assertEqual(archive.extractfile("Wine Devel.app/" + ASSEMBLY).read(), self.corrected)
            self.assertEqual(json.load(archive.extractfile("wine-clr.json")), manifest)
        with tarfile.open(build / "appx-portable-wine-clr-sources.tar.xz") as archive:
            self.assertEqual(archive.extractfile("scripts/package-wine-clr.py").read(), (ROOT / "scripts/package-wine-clr.py").read_bytes())
            self.assertEqual(json.load(archive.extractfile("wine-clr.json")), manifest)


if __name__ == "__main__":
    unittest.main()
