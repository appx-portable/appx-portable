#!/usr/bin/env python3
"""Exercise managed CoreApplication callbacks through the real native host."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('wine', 'prefix', 'shim', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--runtime', type=Path, help='reuse these prebuilt WinRT assemblies')
    parser.add_argument('--metadata', type=Path, help='SDK contracts when building the projection')
    parser.add_argument('--cecil', type=Path, help='Mono.Cecil.dll when building the projection')
    parser.add_argument('--fixture', choices=('coreapplication', 'storage-buffer', 'task-bridge', 'message-dialog'), default='coreapplication')
    parser.add_argument('--case', action='append', help='only these fixture cases')
    args = parser.parse_args()
    wine, prefix, shim, output = (getattr(args, name).resolve() for name in ('wine', 'prefix', 'shim', 'output'))
    output.mkdir(parents=True, exist_ok=True)
    repo = Path(__file__).resolve().parents[1]
    spec = importlib.util.spec_from_file_location('builder', repo / 'scripts/build-winrt-release.py')
    builder = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(builder)
    toolchain = builder.Toolchain(wine)
    if args.runtime:
        runtime = args.runtime.resolve()
    else:
        if not args.metadata or not args.cecil:
            parser.error('provide --runtime or both --metadata and --cecil')
        runtime = output / 'runtime'
        previous = os.environ.get('WINEPREFIX')
        os.environ['WINEPREFIX'] = str(prefix)
        try:
            builder.build(toolchain, args.metadata.resolve(), args.cecil.resolve(), runtime, output / 'build')
        finally:
            if previous is None:
                os.environ.pop('WINEPREFIX', None)
            else:
                os.environ['WINEPREFIX'] = previous
    native = ['d3dcompiler_47.dll', 'XAudio2_8.dll', 'XAudio2_9.dll', 'DWrite.dll', 'd3d11.dll', 'dxgi.dll', 'wined3d.dll', 'd2d1.dll']
    for source in list(runtime.glob('*.dll')) + [shim.parent / name for name in native if (shim.parent / name).is_file()]:
        shutil.copyfile(source, output / source.name)
    shutil.copyfile(shim, output / 'appxportable.dll')
    (output / 'AppxManifest.xml').write_text('<Package><Identity Name="Fixture.CoreApplication" Version="1.0.0.0" ProcessorArchitecture="x86" Publisher="CN=Fixture"/><Applications><Application Id="App" Executable="coreapplication.exe" EntryPoint="Fixture.Entry"/></Applications></Package>')
    env = dict(os.environ, WINEPREFIX=str(prefix), WINEDEBUG='-all', MVK_CONFIG_LOG_LEVEL='0', APPX_PORTABLE_LOG='1')
    env['WINEDLLOVERRIDES'] = 'd3dcompiler_47,xaudio2_8,dwrite=n,b;d3d11,dxgi,wined3d,d2d1=n'
    env['WINE_D3D_CONFIG'] = 'renderer=vulkan'
    command = toolchain.compile(output / 'coreapplication.exe', [repo / 'tests/winrt_projection' / {'coreapplication': 'coreapplication.cs', 'storage-buffer': 'storage_buffer.cs', 'task-bridge': 'task_bridge.cs', 'message-dialog': 'message_dialog.cs'}[args.fixture]], ['/r:' + toolchain.path(output / name) for name in ('Windows.dll', 'AppxPortable.WinRT.Interop.dll', 'System.Runtime.WindowsRuntime.dll')])
    with (output / 'compile.log').open('w') as log:
        result = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=60, check=False)
    if result.returncode:
        raise SystemExit((output / 'compile.log').read_text(errors='replace'))
    cases = args.case or (['success', 'cancel-key', 'default-key', 'close-default', 'cancel-operation'] if args.fixture == 'message-dialog' else ['success'] if args.fixture != 'coreapplication' else ['success', 'null-source', 'null-view', 'create', 'initialize', 'set-window', 'load', 'run', 'uninitialize', 'reuse', 'window-apis', 'combined', 'out-failure'])
    failed = False
    for case in cases:
        with (output / (case + '.log')).open('w') as log:
            child = subprocess.Popen([str(wine), 'coreapplication.exe', case], cwd=output, env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
            try:
                code = child.wait(timeout=30)
            except subprocess.TimeoutExpired:
                os.killpg(child.pid, signal.SIGKILL)
                child.wait(timeout=10)
                code = 'timeout'
        text = (output / (case + '.log')).read_text(errors='replace')
        passed = code == 0 and 'PASS:' in text
        print(json.dumps(dict(case=case, exit=code, passed=passed)), flush=True)
        if not passed:
            print(text[-3000:], flush=True)
            failed = True
    return 1 if failed else 0


if __name__ == '__main__':
    raise SystemExit(main())
