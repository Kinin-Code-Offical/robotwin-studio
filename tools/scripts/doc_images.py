"""Renders the guide's pictures from docs/images/shots.json.

    python tools/scripts/doc_images.py <RobotWinStudio.exe> [--lang en] [name ...]

Turkish pictures go to docs/images/<name>.png, others to docs/images/<lang>/<name>.png. With names, only those.
Each picture is the app's own --snapshot of a page (see docs/TOOLS.md); "crop" and "resize" are applied after.
"""
import io, json, os, subprocess, sys

here = os.path.dirname(os.path.abspath(__file__))
repo = os.path.normpath(os.path.join(here, '..', '..'))
images = os.path.join(repo, 'docs', 'images')


def main(argv):
    if not argv:
        print(__doc__)
        return 2
    exe = argv[0]
    lang = None
    names = []
    i = 1
    while i < len(argv):
        if argv[i] == '--lang' and i + 1 < len(argv):
            lang = argv[i + 1]
            i += 2
            continue
        names.append(argv[i])
        i += 1
    shots = json.load(io.open(os.path.join(images, 'shots.json'), encoding='utf-8'))['shots']
    out_dir = images if lang in (None, 'tr') else os.path.join(images, lang)
    os.makedirs(out_dir, exist_ok=True)
    failed = []
    for name, spec in shots.items():
        if names and name not in names:
            continue
        args = spec if isinstance(spec, list) else spec['args']
        target = os.path.join(out_dir, name + '.png')
        raw = target if isinstance(spec, list) else target + '.raw.png'
        cmd = [exe, '--snapshot', raw] + args + (['--lang', lang] if lang else [])
        try:
            subprocess.run(cmd, timeout=900, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        except subprocess.TimeoutExpired:
            failed.append(name + ' (timeout)')
            continue
        report = os.path.splitext(raw)[0] + '.txt'   # an end-to-end check's report beside its picture: not a doc file
        if os.path.exists(report):
            os.remove(report)
        if not os.path.exists(raw):
            failed.append(name)
            continue
        if not isinstance(spec, list):
            from PIL import Image
            im = Image.open(raw)
            if 'crop' in spec:
                im = im.crop(tuple(spec['crop']))
            if 'resize' in spec:
                im = im.resize(tuple(spec['resize']), Image.LANCZOS)
            im.save(target, optimize=True)
            os.remove(raw)
        print('ok', name)
    for f in failed:
        print('FAILED', f)
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
