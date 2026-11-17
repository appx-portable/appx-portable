#!/usr/bin/env python3
"""Exercise an installed game's original CommonDX music wrapper and audio asset."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('wine', 'prefix', 'game', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--asset', default='Content/MonsterIslandMenu.mp3', help='game-relative audio file')
    args = parser.parse_args()
    wine, prefix, game, output = (getattr(args, name).resolve() for name in ('wine', 'prefix', 'game', 'output'))
    asset = Path(args.asset)
    if asset.is_absolute() or '..' in asset.parts or not (game / asset).is_file():
        parser.error('--asset must name an existing game-relative file')
    if output.exists():
        parser.error('output folder already exists; choose a fresh one')
    output.mkdir(parents=True)
    repo = Path(__file__).resolve().parents[1]
    spec = importlib.util.spec_from_file_location('builder', repo / 'scripts/build-winrt-release.py')
    builder = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(builder)
    toolchain = builder.Toolchain(wine)
    names = ('CommonDX.dll', 'SharpDX.MediaFoundation.dll', 'SharpDX.dll', 'Windows.dll',
             'System.Runtime.WindowsRuntime.dll', 'System.Runtime.WindowsRuntime.UI.Xaml.dll',
             'AppxPortable.WinRT.Interop.dll', 'appxportable.dll')
    before = {name: digest(game / name) for name in names}
    for name in names:
        shutil.copyfile(game / name, output / name)
    manifest = game / 'appx-projections/runtime-overrides.xml'
    if manifest.is_file():
        entry = next((e for e in ET.parse(manifest).getroot() if e.get('Name') == 'SharpDX.dll'), None)
        if entry is not None:
            compatible = game / 'appx-projections/runtime-overrides/SharpDX.dll'
            if entry.get('SourceSha256') != before['SharpDX.dll'] or entry.get('Sha256') != digest(compatible):
                raise SystemExit('SharpDX override hash mismatch')
            shutil.copyfile(compatible, output / 'SharpDX.dll')
    (output / asset).parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(game / asset, output / asset)
    (output / 'AppxManifest.xml').write_text('<Package><Identity Name="Fixture.CommonMusic" Version="1.0.0.0" ProcessorArchitecture="x86" Publisher="CN=Fixture"/><Applications><Application Id="App" Executable="music.exe" EntryPoint="Fixture.Entry"/></Applications></Package>')
    env = dict(os.environ, WINEPREFIX=str(prefix), WINEDEBUG='-all', MVK_CONFIG_LOG_LEVEL='0', APPX_PORTABLE_LOG='1')
    for name in ('MONO_PATH', 'MONO_ENV_OPTIONS', 'WINE_MONO_AOT', 'WINEDLLOVERRIDES'):
        env.pop(name, None)
    references = [output / name for name in ('Windows.dll', 'CommonDX.dll', 'SharpDX.dll', 'SharpDX.MediaFoundation.dll')]
    references.append(toolchain.references / 'Facades/System.Runtime.dll')
    command = toolchain.compile(output / 'music.exe', [repo / 'tests/winrt_projection/common_music.cs'], ['/r:' + toolchain.path(path) for path in references])
    with (output / 'compile.log').open('w') as log:
        result = subprocess.run(command, env=env, cwd=output, stdout=log, stderr=subprocess.STDOUT, timeout=60)
    if result.returncode:
        raise SystemExit((output / 'compile.log').read_text(errors='replace'))
    with (output / 'music.log').open('w') as log:
        result = subprocess.run(toolchain.tool(output / 'music.exe') + [str(asset).replace('/', '\\')], env=env, cwd=output,
                                stdout=log, stderr=subprocess.STDOUT, timeout=30)
    text = (output / 'music.log').read_text(errors='replace')
    unchanged = all(digest(game / name) == value for name, value in before.items())
    passed = result.returncode == 0 and 'PASS:' in text and unchanged
    report = dict(passed=passed, exit=result.returncode, original_assemblies_unchanged=unchanged,
                  common_dx_sha256=before['CommonDX.dll'], asset=args.asset, asset_sha256=digest(game / asset),
                  speaker_audibility_verified=False)
    (output / 'results.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))
    if not passed:
        print(text[-5000:])
    return 0 if passed else 1


if __name__ == '__main__':
    raise SystemExit(main())
