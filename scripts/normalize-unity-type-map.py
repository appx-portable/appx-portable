#!/usr/bin/env python3
"""Stage a Mono-compatible copy of a legacy Unity WinRT type table."""
import argparse
import hashlib
import importlib.util
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("game", type=Path)
    for name in ("wine", "prefix", "cecil"):
        parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args()
    game, wine, prefix, cecil = (getattr(args, name).resolve() for name in ("game", "wine", "prefix", "cecil"))
    source = game / "UnityEngine.dll"
    source_digest = digest(source)
    folder = game / "appx-projections"
    manifest = folder / "runtime-overrides.xml"
    document = ET.parse(manifest) if manifest.exists() else ET.ElementTree(ET.Element("RuntimeOverrides"))
    root = document.getroot()
    if root.tag != "RuntimeOverrides":
        raise SystemExit("Invalid runtime override manifest")
    entry = next((node for node in root.findall("Assembly") if node.get("Name") == source.name), None)
    target = folder / "runtime-overrides" / source.name
    if entry is not None and entry.get("SourceSha256") == source_digest and target.is_file() and entry.get("Sha256") == digest(target):
        print("Unity type table runtime override is current")
        return

    repo = Path(__file__).resolve().parents[1]
    spec = importlib.util.spec_from_file_location("builder", repo / "scripts/build-winrt-release.py")
    builder = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(builder)
    toolchain = builder.Toolchain(wine)
    env = dict(os.environ, WINEPREFIX=str(prefix), WINEDEBUG="-all")
    with tempfile.TemporaryDirectory(prefix="unity-type-map-") as temporary:
        work = Path(temporary)
        generator = work / "generator.exe"
        dependency = work / "Mono.Cecil.dll"
        shutil.copyfile(cecil, dependency)

        def run(command):
            with (work / "command.log").open("w") as log:
                result = subprocess.run(command, cwd=work, env=env, stdout=log, stderr=subprocess.STDOUT,
                                        timeout=60, check=False)
            text = (work / "command.log").read_text(errors="replace")
            if result.returncode:
                raise SystemExit(text)
            return text

        run(toolchain.compile(generator, [repo / "host/winrt" / name for name in builder.GENERATOR],
                              ["/r:" + toolchain.path(dependency)]))
        output = work / source.name
        text = run(toolchain.tool(generator) + ["--normalize-unity-type-map", toolchain.path(source), toolchain.path(output)])
        if not output.exists():
            unchanged = next((line for line in text.splitlines() if line.startswith("UNCHANGED:")), None)
            if not unchanged:
                raise SystemExit("Unity normalization produced no override: " + text.strip())
            print("UnityEngine.dll needs no override:", unchanged[len("UNCHANGED:"):])
            return
        identity = next((line[len("NORMALIZED:"):].strip() for line in text.splitlines() if line.startswith("NORMALIZED:")), None)
        if not identity or digest(source) != source_digest:
            raise SystemExit("Unity normalization did not produce a valid override for the current source")
        target.parent.mkdir(parents=True, exist_ok=True)
        staged = target.with_suffix(".dll.tmp")
        shutil.copyfile(output, staged)
        os.replace(staged, target)

    if entry is None:
        entry = ET.SubElement(root, "Assembly")
    entry.attrib.update(Name=source.name, Identity=identity, SourceSha256=source_digest, Sha256=digest(target))
    staged_manifest = manifest.with_suffix(".xml.tmp")
    document.write(staged_manifest, encoding="utf-8", xml_declaration=True)
    os.replace(staged_manifest, manifest)
    print(text.strip())
    print("Staged Unity type table override:", target)


if __name__ == "__main__":
    main()
