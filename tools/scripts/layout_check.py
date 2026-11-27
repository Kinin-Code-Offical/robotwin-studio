"""Finds what breaks at large display scales: the studio's --layout-report over its pages at the window sizes a 1920 x 1080
screen gives at 100 %, 125 %, 150 % and 200 % (1920 x 1080, 1536 x 864, 1280 x 720, 960 x 540 device-independent
pixels).

    python tools/scripts/layout_check.py <RobotWinStudio.exe> [--sizes 1280x720,960x540] [--out report.txt]

Each finding is a control the user acts on (a button, a toggle, a box) cut off by the window or by a clipping panel
that does not scroll, or lying over another one (see Shell/LayoutAudit.cs). Exits 1 when there is any, and prints them
grouped by control.
"""
import collections, io, os, subprocess, sys, tempfile

PAGES = ['home', 'settings', 'library', 'schematic', 'pcb', 'code', 'model', 'world']
SIZES = ['1920x1080', '1536x864', '1280x720', '960x540']


def main(argv):
    if not argv:
        print(__doc__)
        return 2
    exe = argv[0]
    sizes = argv[argv.index('--sizes') + 1].split(',') if '--sizes' in argv else SIZES
    out = argv[argv.index('--out') + 1] if '--out' in argv else os.path.join(tempfile.gettempdir(), 'robotwin-layout.txt')
    if os.path.exists(out):
        os.remove(out)
    shot = os.path.join(tempfile.gettempdir(), 'robotwin-layout.png')
    for size in sizes:
        for page in PAGES:
            cmd = [exe, '--snapshot', shot, '--page', page, '--size', size, '--layout-report', out]
            if page in ('schematic', 'pcb', 'model'):
                cmd += ['--example', 'motor-driver']
            try:
                subprocess.run(cmd, timeout=300, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            except subprocess.TimeoutExpired:
                print('timeout:', size, page)
        print('size', size, 'done', flush=True)
    for leftover in (shot, os.path.splitext(shot)[0] + '.txt'):
        if os.path.exists(leftover):
            os.remove(leftover)
    rows = [l.rstrip('\n').split('\t') for l in io.open(out, encoding='utf-8')] if os.path.exists(out) else []
    if not rows:
        print('every control in reach at every size')
        return 0
    grouped = collections.defaultdict(list)
    for page, size, kind, what, where, detail in (r[:6] for r in rows if len(r) >= 6):
        grouped[(page, kind, what)].append((size, detail, where))
    for (page, kind, what), hits in sorted(grouped.items()):
        sizes_hit = ', '.join(sorted({h[0] for h in hits}, key=lambda s: -int(s.split('x')[0])))
        print(f'{page}  {kind}  {what}  [{sizes_hit}]  {hits[0][1]}  {hits[0][2]}')
    print(f'{len(grouped)} findings, report: {out}')
    return 1


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
