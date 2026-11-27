"""[tokens] Hard-coded colours and font sizes in the interface, against the design tokens.

The interface takes its colours from the theme's tokens (B.* / C.* in Theme/Tokens.xaml, set per theme by
Shell/Themes.cs) and its text sizes from the type scale (T.Caption 12, T.Body 13, T.Subtitle 14, T.Title 20,
T.Hero 30). This lists what bypasses them in the pages, the shell and the editors' panels: colour literals in XAML
("#RRGGBB" on a Foreground / Background / BorderBrush / Fill / Stroke), Color.FromRgb / FromArgb and Brushes.<Name>
in C# outside the drawing code, and FontSize values off the scale. Drawing code (canvases, 3D, symbols, plots) draws
technical colours on purpose and is left out.

    python tools/scripts/token_scan.py              # the counts per file and the total
    python tools/scripts/token_scan.py --list       # every place
    python tools/scripts/token_scan.py --check      # exit 1 when a count grew past tools/tokens/baseline.json
    python tools/scripts/token_scan.py --update     # write the baseline from what is there now
"""
import io
import json
import os
import re
import sys

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), '..', '..'))
STUDIO = os.path.join(ROOT, 'Studio', 'RobotWin.Studio')
BASELINE = os.path.join(ROOT, 'tools', 'tokens', 'baseline.json')
SCALE = {'12', '13', '14', '17', '20', '24', '30'}   # Theme/Tokens.xaml T.Caption … T.Hero

# the drawing code: technical colours are its job
DRAWING = ('Editor' + os.sep + 'SchematicCanvas', 'Editor' + os.sep + 'Symbol', 'Pcb' + os.sep, 'World' + os.sep, 'Render' + os.sep,
           'Models3D' + os.sep, 'Assembly3D' + os.sep, 'Modeling' + os.sep, 'Simulation' + os.sep + 'Scope', 'Firmware' + os.sep + 'Timeline',
           'Analysis' + os.sep, 'Theme' + os.sep, 'Shell' + os.sep + 'Themes.cs', 'Shell' + os.sep + 'ViewCube.cs', 'Shell' + os.sep + 'StartupSplash',
           'Simulation' + os.sep + 'MultimeterView', 'Simulation' + os.sep + 'HistogramView', 'Input' + os.sep + 'VirtualGamepad', 'Branding' + os.sep,
           'Editor' + os.sep + 'SchematicEffects', 'Editor' + os.sep + 'CodeEditor', 'Shell' + os.sep + 'FileAssociations', 'Shell' + os.sep + 'FlowRail')

XAML_COLOR = re.compile(r'(Foreground|Background|BorderBrush|Fill|Stroke)="#[0-9A-Fa-f]{6,8}"')
XAML_SIZE = re.compile(r'FontSize="([0-9.]+)"')
CS_COLOR = re.compile(r'Color\.From(?:A?Rgb)\(|\bBrushes\.(?!Transparent\b)[A-Z][a-zA-Z]+')
CS_SIZE = re.compile(r'FontSize\s*=\s*([0-9.]+)')


def files():
    for dirpath, dirnames, filenames in os.walk(STUDIO):
        dirnames[:] = [d for d in dirnames if d not in ('obj', 'bin')]
        for f in filenames:
            if f.endswith('.xaml') or f.endswith('.cs'):
                yield os.path.join(dirpath, f)


def scan():
    found = {}
    for path in files():
        rel = os.path.relpath(path, STUDIO)
        if rel.startswith(DRAWING):
            continue
        text = io.open(path, encoding='utf-8').read()
        hits = []
        for n, line in enumerate(text.split('\n'), 1):
            if path.endswith('.xaml'):
                hits += [(n, 'renk', m.group(0)) for m in XAML_COLOR.finditer(line)]
                hits += [(n, 'boyut', m.group(0)) for m in XAML_SIZE.finditer(line) if m.group(1) not in SCALE]
            else:
                if line.strip().startswith('//'):
                    continue
                hits += [(n, 'renk', m.group(0)) for m in CS_COLOR.finditer(line)]
                hits += [(n, 'boyut', m.group(0)) for m in CS_SIZE.finditer(line) if m.group(1) not in SCALE]
        if hits:
            found[rel.replace(os.sep, '/')] = hits
    return found


def main():
    found = scan()
    counts = {f: len(h) for f, h in found.items()}
    total = sum(counts.values())
    if '--list' in sys.argv:
        for f in sorted(found):
            for n, kind, what in found[f]:
                print(f'{f}:{n}: {kind}: {what}')
    if '--update' in sys.argv:
        os.makedirs(os.path.dirname(BASELINE), exist_ok=True)
        with io.open(BASELINE, 'w', encoding='utf-8', newline='\n') as fh:
            json.dump({'total': total, 'files': counts}, fh, ensure_ascii=False, indent=1, sort_keys=True)
        print(f'baseline written: {total} places in {len(counts)} files')
        return 0
    if '--check' in sys.argv:
        base = json.load(io.open(BASELINE, encoding='utf-8')) if os.path.exists(BASELINE) else {'files': {}}
        grew = [(f, c, base['files'].get(f, 0)) for f, c in counts.items() if c > base['files'].get(f, 0)]
        for f, c, b in grew:
            print(f'{f}: {c} (was {b}): use a token (B.*, T.*) or CanvasColors for technical colours')
        print(f'{total} places in {len(counts)} files' + (f', {len(grew)} files grew' if grew else ', none grew'))
        return 1 if grew else 0
    for f in sorted(counts, key=lambda k: -counts[k])[:25]:
        print(f'{counts[f]:4}  {f}')
    print(f'{total} places in {len(counts)} files')
    return 0


if __name__ == '__main__':
    sys.exit(main())
