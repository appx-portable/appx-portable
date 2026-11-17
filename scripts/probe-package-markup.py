#!/usr/bin/env python3
"""Test a prebuilt managed runtime's PRI, XBF and startup controls under Wine."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('wine', 'prefix', 'runtime', 'shim', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--cases', nargs='+', choices=('resources', 'compiled', 'panel', 'appbar', 'search', 'light', 'webview', 'scroll', 'selection', 'navigation', 'files', 'grid', 'webview-loaded', 'mainloop', 'webview-layout', 'webview-resize', 'webview-unicode', 'webview-fonts', 'index-recycle', 'search-input'),
                        default=['resources', 'compiled', 'panel', 'appbar', 'search', 'light', 'webview', 'scroll', 'selection', 'navigation', 'files', 'grid', 'webview-loaded', 'mainloop', 'webview-layout', 'webview-resize', 'webview-unicode', 'webview-fonts', 'index-recycle', 'search-input'])
    args = parser.parse_args()
    wine, prefix, runtime, shim, output = (getattr(args, name).resolve() for name in ('wine', 'prefix', 'runtime', 'shim', 'output'))
    if output.exists():
        parser.error('--output must be a fresh directory')
    output.mkdir(parents=True)
    spec = importlib.util.spec_from_file_location('release_builder', ROOT / 'scripts/build-winrt-release.py')
    builder = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(builder)
    toolchain = builder.Toolchain(wine)
    fixtures = runpy.run_path(str(ROOT / 'tests/winrt_projection/compiled_fixture.py'))
    env = dict(os.environ, WINEPREFIX=str(prefix), WINEDEBUG='-all', MVK_CONFIG_LOG_LEVEL='0')
    for name in ('MONO_PATH', 'MONO_ENV_OPTIONS', 'WINE_MONO_AOT', 'WINEDLLOVERRIDES'):
        env.pop(name, None)
    results = []

    def run(name, command, cwd, marker=None):
        log = output / (name + '.log')
        with log.open('w') as stream:
            result = subprocess.run(command, cwd=cwd, env=env, stdout=stream, stderr=subprocess.STDOUT,
                                    stdin=subprocess.DEVNULL, start_new_session=True, timeout=90, check=False)
        passed = result.returncode == 0 and (marker is None or marker in log.read_text(errors='replace'))
        row = dict(case=name, exit=result.returncode, passed=passed, log=str(log))
        results.append(row)
        print(json.dumps(row), flush=True)
        if not passed:
            print(log.read_text(errors='replace')[-4000:], file=sys.stderr)
        return passed

    for group in ('resources', 'compiled', 'startup'):
        cases = [case for case in args.cases if (case if case in ('resources', 'compiled') else 'startup') == group]
        if not cases:
            continue
        folder = output / group
        folder.mkdir()
        for name in ('Windows.dll', 'AppxPortable.WinRT.Interop.dll', 'System.Runtime.WindowsRuntime.dll', 'System.Runtime.WindowsRuntime.UI.Xaml.dll'):
            shutil.copyfile(runtime / name, folder / name)
        shutil.copyfile(shim, folder / 'appxportable.dll')
        executable = folder / (group + '.exe')
        (folder / 'AppxManifest.xml').write_text(
            '<Package xmlns="http://schemas.microsoft.com/appx/2010/manifest"><Identity Name="PackageMarkupProbe" '
            'Publisher="CN=probe" Version="1.0.0.0" ProcessorArchitecture="x86"/><Applications><Application Id="App" '
            'Executable="' + executable.name + '" EntryPoint="Probe.App"/></Applications></Package>')
        fixtures['pri'](folder / 'resources.pri')
        fixtures['xbf'](folder / 'Compiled.xbf')
        fixtures['templates'](folder / 'Templates.xbf')
        fixtures['inheritance'](folder / 'Inheritance.xbf')
        fixtures['late_control'](folder / 'LateControl.xbf')
        command = toolchain.compile(executable, [ROOT / 'tests/winrt_projection' / (group + '.cs')],
                                    ['/r:' + toolchain.path(folder / 'Windows.dll'), '/r:' + toolchain.path(folder / 'AppxPortable.WinRT.Interop.dll')])
        if not run('compile-' + group, command, folder):
            continue
        for case in cases:
            command = toolchain.tool(executable) + ([case] if group == 'startup' else [])
            marker = {'resources': 'PASS: PRI resource strings', 'compiled': 'PASS: XBF 1 content'}.get(case, 'PASS: startup ' + case)
            run(case, command, folder, marker)
    (output / 'results.json').write_text(json.dumps(results, indent=2) + '\n')
    return 0 if results and all(row['passed'] for row in results) else 1


if __name__ == '__main__':
    raise SystemExit(main())
