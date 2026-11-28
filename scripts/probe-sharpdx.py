#!/usr/bin/env python3
"""Verify normalized SharpDX copies and hash/identity-checked Mono sidecar loading."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('wine', 'prefix', 'cecil', 'source', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    wine, prefix, cecil, source, output = (getattr(args, name).resolve() for name in ('wine', 'prefix', 'cecil', 'source', 'output'))
    output.mkdir(parents=True, exist_ok=True)
    repo = Path(__file__).resolve().parents[1]
    spec = importlib.util.spec_from_file_location('builder', repo / 'scripts/build-winrt-release.py')
    builder = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(builder)
    toolchain = builder.Toolchain(wine)
    original = output / 'SharpDX.dll'
    shutil.copyfile(source, original)
    before = hashlib.sha256(original.read_bytes()).hexdigest()
    env = dict(os.environ, WINEPREFIX=str(prefix), WINEDEBUG='-all')

    def run(name, command, expected=None):
        with (output / (name + '.log')).open('w') as log:
            result = subprocess.run(command, cwd=output, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=90, check=False)
        text = (output / (name + '.log')).read_text(errors='replace')
        if (expected is None and result.returncode) or (expected is not None and (result.returncode == 0 or expected not in text)):
            raise SystemExit(name + ' failed:\n' + text[-5000:])
        return text

    normalize = [sys.executable, str(repo / 'scripts/normalize-sharpdx.py'), str(output), '--wine', str(wine), '--prefix', str(prefix), '--cecil', str(cecil)]
    run('normalize', normalize)
    manifest = output / 'appx-projections/runtime-overrides.xml'
    before_manifest = manifest.read_bytes()
    run('idempotent', normalize)
    if manifest.read_bytes() != before_manifest or hashlib.sha256(original.read_bytes()).hexdigest() != before:
        raise SystemExit('normalization changed the original or was not idempotent')
    command = toolchain.compile(output / 'sharpdx-test.exe', [repo / 'tests/winrt_projection/sharpdx_write.cs', repo / 'host/projections.cs', repo / 'host/components.cs'])
    run('compile', command)
    command = toolchain.tool(output / 'sharpdx-test.exe') + [toolchain.path(original), toolchain.path(output)]
    text = run('copies', command)
    if 'PASS:' not in text:
        raise SystemExit(text)
    for field, value, message in (('Sha256', '0' * 64, 'Runtime override hash mismatch'), ('Identity', 'Wrong, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null', 'Runtime override identity mismatch')):
        document = ET.fromstring(before_manifest)
        document.find('Assembly').set(field, value)
        try:
            manifest.write_bytes(ET.tostring(document))
            run('reject-' + field, command, message)
        finally:
            manifest.write_bytes(before_manifest)
    print(json.dumps(dict(copies=True, original_unchanged=True, idempotent=True, invalid_hash_rejected=True, invalid_identity_rejected=True)), flush=True)


if __name__ == '__main__':
    main()
