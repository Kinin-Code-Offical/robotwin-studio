"""Finds text that cannot be read, in every theme: the studio's --contrast-report over its pages, the guide's picture
states (dialogs, popups, tuners) and every Help chapter.

    python tools/scripts/contrast_check.py <RobotWinStudio.exe> [--quick] [--out report.txt]

Every page goes through all 16 themes; the picture states and Help chapters through the four most unlike each other
(graphite, light, black, pink). --quick: the pages only, in those four. Each finding is a text under 3:1 contrast
against what lies behind it (see docs/TOOLS.md, --contrast-report). Exits 1 when there is any, and prints them grouped.
"""
import collections, io, json, os, subprocess, sys, tempfile

here = os.path.dirname(os.path.abspath(__file__))
repo = os.path.normpath(os.path.join(here, '..', '..'))

THEMES = ['koyu', 'siyah', 'gece', 'okyanus', 'orman', 'murdum', 'gul-gece', 'kahve', 'karsitlik',
          'acik', 'kagit', 'pembe', 'seftali', 'lavanta', 'nane', 'gok']
FULL = ['koyu', 'acik', 'siyah', 'pembe']
PAGES = ['home', 'settings', 'library', 'schematic', 'pcb', 'code', 'model', 'assembly']


def states(quick):
    pages = [['--page', p] for p in PAGES]
    # with a design open too: the text the canvases draw themselves (designators, pad names, values) is judged as well
    pages += [['--page', p, '--example', 'motor-driver'] for p in ('schematic', 'pcb')]
    if quick:
        return pages, []
    more = []
    shots = json.load(io.open(os.path.join(repo, 'docs', 'images', 'shots.json'), encoding='utf-8'))['shots']
    for spec in shots.values():
        a = list(spec if isinstance(spec, list) else spec['args'])
        if a[a.index('--page') + 1] == 'splash':
            continue
        # colours, not results: short runs
        for key, cap in (('--simulate', 1.0), ('--wait', 2.0)):
            if key in a:
                i = a.index(key)
                a[i + 1] = str(min(float(a[i + 1]), cap))
        more.append(a)
    for chapter in [str(n) for n in range(1, 13)] + ['news']:
        more.append(['--page', 'help', '--fault', chapter, '--wait', '1'])
    return pages, more


def main(argv):
    if not argv:
        print(__doc__)
        return 2
    exe = argv[0]
    quick = '--quick' in argv
    out = argv[argv.index('--out') + 1] if '--out' in argv else os.path.join(tempfile.gettempdir(), 'robotwin-contrast.txt')
    if os.path.exists(out):
        os.remove(out)
    pages, more = states(quick)
    shot = os.path.join(tempfile.gettempdir(), 'robotwin-contrast.png')
    for theme in (FULL if quick else THEMES):
        for a in pages + (more if theme in FULL else []):
            cmd = [exe, '--snapshot', shot, '--theme', theme, '--contrast-report', out] + a
            if '--size' not in a:
                cmd += ['--size', '1600x1000']
            try:
                subprocess.run(cmd, timeout=300, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            except subprocess.TimeoutExpired:
                print('timeout:', theme, ' '.join(a))
        print('theme', theme, 'done', flush=True)
    for leftover in (shot, os.path.splitext(shot)[0] + '.txt'):
        if os.path.exists(leftover):
            os.remove(leftover)
    rows = [l.rstrip('\n').split('\t') for l in io.open(out, encoding='utf-8')] if os.path.exists(out) else []
    if not rows:
        print('every text readable')
        return 0
    grouped = collections.defaultdict(list)
    for page, theme, ratio, colours, where, text in (r[:6] for r in rows if len(r) >= 6):
        grouped[(where, text)].append((theme, ratio, colours, page))
    for (where, text), hits in sorted(grouped.items(), key=lambda kv: -len(kv[1])):
        theme, ratio, colours, page = hits[0]
        print(f'{ratio}:1  {colours}  "{text}"  ({page}; {", ".join(sorted({h[0] for h in hits}))})  {where}')
    print(f'{len(grouped)} texts unreadable, report: {out}')
    return 1


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
