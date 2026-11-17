#!/usr/bin/env python3
"""Verify package async actions, results and collections against native Xbox services."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('wine', 'prefix', 'shim', 'metadata', 'cecil', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--runtime', type=Path, help='reuse these prebuilt SDK projections')
    parser.add_argument('--sdk-metadata', type=Path, help='SDK contracts when --runtime is omitted')
    parser.add_argument('--projection', type=Path, help='reuse an existing Microsoft.Xbox projection for a baseline')
    args = parser.parse_args()
    wine, prefix, shim, metadata, cecil, output = (getattr(args, name).resolve() for name in ('wine', 'prefix', 'shim', 'metadata', 'cecil', 'output'))
    output.mkdir(parents=True, exist_ok=True)
    repo = Path(__file__).resolve().parents[1]
    spec = importlib.util.spec_from_file_location('builder', repo / 'scripts/build-winrt-release.py')
    builder = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(builder)
    toolchain = builder.Toolchain(wine)
    if args.runtime:
        runtime = args.runtime.resolve()
    else:
        if not args.sdk_metadata:
            parser.error('provide --runtime or --sdk-metadata')
        runtime = output / 'runtime'
        previous = os.environ.get('WINEPREFIX')
        os.environ['WINEPREFIX'] = str(prefix)
        try:
            builder.build(toolchain, args.sdk_metadata.resolve(), cecil, runtime, output / 'build')
        finally:
            if previous is None:
                os.environ.pop('WINEPREFIX', None)
            else:
                os.environ['WINEPREFIX'] = previous
    env = dict(os.environ, WINEPREFIX=str(prefix), WINEDEBUG='-all', MVK_CONFIG_LOG_LEVEL='0', APPX_PORTABLE_LOG='1')
    for name in ('MONO_PATH', 'MONO_ENV_OPTIONS', 'WINE_MONO_AOT', 'WINEDLLOVERRIDES'):
        env.pop(name, None)
    for source in runtime.glob('*.dll'):
        shutil.copyfile(source, output / source.name)
    shutil.copyfile(shim, output / 'appxportable.dll')
    shutil.copyfile(cecil, output / 'Mono.Cecil.dll')
    (output / 'AppxManifest.xml').write_text('<Package><Identity Name="Fixture.Marketplace" Version="1.0.0.0" ProcessorArchitecture="x86" Publisher="CN=Fixture"/><Applications><Application Id="App" Executable="marketplace.exe" EntryPoint="Fixture.Entry"/></Applications></Package>')

    def run(name, command, environment=env):
        with (output / (name + '.log')).open('w') as log:
            result = subprocess.run(command, env=environment, cwd=output, stdout=log, stderr=subprocess.STDOUT, timeout=90, check=False)
        text = (output / (name + '.log')).read_text(errors='replace')
        if result.returncode:
            raise SystemExit(name + ' failed:\n' + text[-5000:])
        return text

    if args.projection:
        shutil.copyfile(args.projection.resolve(), output / 'Microsoft.Xbox.dll')
    else:
        run('compile-generator', toolchain.compile(output / 'generator.exe', [repo / 'host/winrt' / name for name in builder.GENERATOR], ['/r:' + toolchain.path(output / 'Mono.Cecil.dll')]))
        run('generate', toolchain.tool(output / 'generator.exe') + ['--component', toolchain.path(metadata), toolchain.path(output / 'Microsoft.Xbox.generated.cs'), '--reference', toolchain.path(output / 'Windows.dll')])
        # Independent of WinMD: the shim's xbox_abi.h declares this IID,
        # slot, and UINT32/INT32/INT64 input layout.
        generated = (output / 'Microsoft.Xbox.generated.cs').read_text()
        for expected in ('70be938f-0fb5-4cb8-baba-c1f04f555d2a',
                         '// Microsoft.Xbox.Leaderboards.ILeaderboardService::PostResultAsync, slot 9',
                         'private delegate int Native_PostResultAsync(global::System.IntPtr self, uint argument0, int argument1, long argument2, out global::System.IntPtr result);'):
            if expected not in generated:
                raise SystemExit('Leaderboard projection differs from native ABI: ' + expected)
        sources = [repo / 'host/winrt' / name for name in ('abi.cs', 'ccw.cs', 'dispatcher.cs', 'property_abi.cs', 'collections.cs')] + [output / 'Microsoft.Xbox.generated.cs']
        run('compile-projection', toolchain.compile(output / 'Microsoft.Xbox.dll', sources, ['/target:library', '/define:APPX_PORTABLE_INTEROP', '/r:' + toolchain.path(output / 'Windows.dll'), '/r:' + toolchain.path(output / 'AppxPortable.WinRT.Interop.dll')]))
    run('compile-consumer', toolchain.compile(output / 'marketplace.exe', [repo / 'tests/winrt_projection/xbox_marketplace.cs'], ['/r:' + toolchain.path(output / name) for name in ('Windows.dll', 'Microsoft.Xbox.dll', 'AppxPortable.WinRT.Interop.dll', 'System.Runtime.WindowsRuntime.dll')]))
    for case in ('offline', 'local'):
        environment = dict(env, APPX_PORTABLE_STUB_IAP='1' if case == 'local' else '0', APPX_PORTABLE_STUB_XBOX_PRODUCTS='11111111-2222-3333-4444-555555555555:0:1')
        text = run(case, toolchain.tool(output / 'marketplace.exe') + [case], environment)
        if 'PASS:' not in text:
            raise SystemExit(case + ' missing PASS marker:\n' + text)
        print(json.dumps(dict(case=case, passed=True)), flush=True)


if __name__ == '__main__':
    main()
