"""Finds what the keyboard cannot reach: the studio's --keyboard-report over its pages (empty, and with a design open on
the schematic and the PCB). Each finding is a control a mouse can use (button, toggle, box, list, slider, tab) that the
window's Tab order never lands on, with why when it is plain; a closing line says where the Tab walk stopped.

    python tools/scripts/keyboard_check.py <RobotWinStudio.exe> [--out report.txt]

Exits 1 when there is any, and prints them grouped by page (see docs/gelistirici/ARACLAR.md).
"""
import io
import os
import subprocess
import sys
import tempfile

PAGES = ['home', 'settings', 'library', 'schematic', 'pcb', 'code', 'model', 'assembly', 'world']


def main(argv):
    if not argv:
        print(__doc__)
        return 2
    exe = argv[0]
    out = argv[argv.index('--out') + 1] if '--out' in argv else os.path.join(tempfile.gettempdir(), 'robotwin-keyboard.txt')
    if os.path.exists(out):
        os.remove(out)
    shot = os.path.join(tempfile.gettempdir(), 'robotwin-keyboard.png')
    states = [['--page', p] for p in PAGES] + [['--page', p, '--example', 'motor-driver'] for p in ('schematic', 'pcb')]
    for a in states:
        cmd = [exe, '--snapshot', shot, '--keyboard-report', out, '--size', '1600x1000', '--wait', '2'] + a
        try:
            subprocess.run(cmd, timeout=180, check=False, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        except subprocess.TimeoutExpired:
            print('timeout:', ' '.join(a))
        print(' '.join(a), 'done')
    lines = io.open(out, encoding='utf-8').read().splitlines() if os.path.exists(out) else []
    if not lines:
        print('every control in reach of the keyboard')
        return 0
    groups = {}
    for line in lines:
        parts = line.split('\t')
        groups.setdefault(parts[0], []).append(parts)
    for page, rows in sorted(groups.items()):
        print(page)
        for r in rows:
            print('   ', r[1], '|', r[4] if len(r) > 4 else '', '|', r[2], '|', r[3][:120])
    print(len(lines), 'findings')
    return 1


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
