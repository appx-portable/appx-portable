#!/usr/bin/env python3
"""Stage a validated Mono-compatible SharpDX copy without changing game files."""
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
    parser.add_argument('game', type=Path)
    for name in ('wine', 'prefix', 'cecil'):
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    game, wine, prefix, cecil = (getattr(args, name).resolve() for name in ('game', 'wine', 'prefix', 'cecil'))
    source = game / 'SharpDX.dll'
    before = digest(source)
    folder = game / 'appx-projections'
    folder.mkdir(exist_ok=True)
    manifest = folder / 'runtime-overrides.xml'
    document = ET.parse(manifest) if manifest.exists() else ET.ElementTree(ET.Element('RuntimeOverrides'))
    if document.getroot().tag != 'RuntimeOverrides':
        raise SystemExit('Invalid runtime override manifest')
    entry = next((e for e in document.getroot() if e.tag == 'Assembly' and e.get('Name') == source.name), None)
    target = folder / 'runtime-overrides' / source.name
    if entry is not None and entry.get('SourceSha256') == before and target.is_file() and entry.get('Sha256') == digest(target):
        print('SharpDX runtime override is current')
        return
    repo = Path(__file__).resolve().parents[1]
    spec = importlib.util.spec_from_file_location('builder', repo / 'scripts/build-winrt-release.py')
    builder = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(builder)
    toolchain = builder.Toolchain(wine)
    env = dict(os.environ, WINEPREFIX=str(prefix), WINEDEBUG='-all')
    with tempfile.TemporaryDirectory(prefix='sharpdx-normalize-') as temporary:
        work = Path(temporary)
        shutil.copyfile(cecil, work / 'Mono.Cecil.dll')
        command = toolchain.compile(work / 'generator.exe', [repo / 'host/winrt' / name for name in builder.GENERATOR], ['/r:' + toolchain.path(work / 'Mono.Cecil.dll')])
        with (work / 'compile.log').open('w') as log:
            result = subprocess.run(command, cwd=work, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=60, check=False)
        if result.returncode:
            raise SystemExit((work / 'compile.log').read_text(errors='replace'))
        command = toolchain.tool(work / 'generator.exe') + ['--normalize-sharpdx', toolchain.path(source), toolchain.path(work / 'SharpDX.dll')]
        with (work / 'normalize.log').open('w') as log:
            result = subprocess.run(command, cwd=work, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=60, check=False)
        text = (work / 'normalize.log').read_text(errors='replace')
        identity = next((line[len('NORMALIZED:'):].strip() for line in text.splitlines() if line.startswith('NORMALIZED:')), None)
        if result.returncode or not identity:
            raise SystemExit(text)
        if digest(source) != before:
            raise SystemExit('SharpDX source changed during normalization; no override published')
        target.parent.mkdir(exist_ok=True)
        staged = target.with_suffix('.dll.tmp')
        shutil.copyfile(work / 'SharpDX.dll', staged)
        os.replace(staged, target)
    if entry is None:
        entry = ET.SubElement(document.getroot(), 'Assembly')
    entry.attrib.update(Name=source.name, Identity=identity, SourceSha256=before, Sha256=digest(target))
    staged_manifest = manifest.with_suffix('.xml.tmp')
    document.write(staged_manifest, encoding='utf-8', xml_declaration=True)
    os.replace(staged_manifest, manifest)
    print('Staged Mono runtime override:', target)
    print('Original SharpDX SHA256:', before)


if __name__ == '__main__':
    main()
