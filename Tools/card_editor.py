# Ultra Puck card editor: a tiny local web page for Assets/StreamingAssets/Cards/ultrapuck.json.
# Start it with "Card Editor.bat" at the project root (or: python Tools/card_editor.py). It opens your browser.
#
#   Save       writes ultrapuck.json (header comments and layout kept) and copies the cards + art into the
#              local builds (Builds/Windows and Builds/Linux, StreamingAssets/Cards). No Unity compile.
#   Publish    patches the cards into dist/UltraPuck-Windows.zip and dist/UltraPuck-Linux.tar.gz and re-uploads
#              them to the live (latest) GitHub release (gh release upload --clobber). The version number stays the same.
#
# Standard library only. Serves on 127.0.0.1 (this PC only).
import io
import json
import os
import re
import shutil
import socket
import subprocess
import sys
import tarfile
import threading
import time
import urllib.request
import webbrowser
import zipfile
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CARDS = os.path.join(ROOT, 'Assets', 'StreamingAssets', 'Cards')
CARD_FILE = os.path.join(CARDS, 'ultrapuck.json')
ART = os.path.join(CARDS, 'Art')
BUILDS = [os.path.join(ROOT, 'Builds', p, 'UltraPuck_Data', 'StreamingAssets', 'Cards') for p in ('Windows', 'Linux')]
DIST = os.path.join(ROOT, 'dist')
REPO = 'FancyDucky/UltraPuck-Standalone'
ARCHIVE_CARDS = 'UltraPuck/UltraPuck_Data/StreamingAssets/Cards/'
PORT = 8765

TIERS = ['S', 'A', 'B', 'C', 'D', 'F']
VALUE = re.compile(r'^\s*([xX*/+-])?\s*\d+(\.\d+)?\s*$')
job = {'busy': False, 'log': ''}


def version():
    return open(os.path.join(ROOT, 'version.txt')).read().strip()


def names(field):
    """A list of names the game knows, read straight from Core/Cards.cs (StatNames, FinishNames)."""
    src = open(os.path.join(ROOT, 'Assets', 'UltraPuck', 'Core', 'Cards.cs'), encoding='utf-8').read()
    m = re.search(field + r'\s*=\s*\{(.*?)\};', src, re.S)
    return re.findall(r'"(\w+)"', m.group(1)) if m else []


def stat_names():
    return names('StatNames')


def finish_names():
    return names('FinishNames')


def lenient(text):
    """Like the game's reader (Core/Json.cs): allows // and /* */ comments and trailing commas."""
    out, i, n = [], 0, len(text)
    while i < n:
        ch = text[i]
        if ch == '"':
            j = i + 1
            while text[j] != '"':
                j += 2 if text[j] == '\\' else 1
            out.append(text[i:j + 1])
            i = j + 1
        elif text.startswith('//', i):
            i = text.find('\n', i) if '\n' in text[i:] else n
        elif text.startswith('/*', i):
            i = text.index('*/', i) + 2
        else:
            out.append(ch)
            i += 1
    return json.loads(re.sub(r',(\s*[}\]])', r'\1', ''.join(out)))


def load():
    text = open(CARD_FILE, encoding='utf-8').read()
    start = re.search(r'^\{', text, re.M).start()  # everything above the first "{" line is the header comment
    return text[:start], lenient(text[start:])


def art_files():
    return sorted(f for f in os.listdir(ART) if f.lower().endswith(('.jpg', '.jpeg', '.png')))


# ------------------------------------------------------------------ writing (same layout as the hand-made file)

def dump(v):
    return json.dumps(v, ensure_ascii=False)


def inline(obj):
    return '{ ' + ', '.join(dump(k) + ': ' + dump(v) for k, v in obj.items()) + ' }' if obj else '{}'


def card_text(card):
    lines = []
    for k, v in card.items():
        if k == 'face' and isinstance(v, list):
            if not v:
                lines.append('"face": []')
            else:
                rows = ',\n'.join('        ' + inline(r) for r in v)
                lines.append('"face": [\n' + rows + '\n      ]')
        elif k == 'stats' and isinstance(v, dict):
            lines.append('"stats": ' + inline(v))
        else:
            lines.append(dump(k) + ': ' + dump(v))
    return '    {\n' + ',\n'.join('      ' + l for l in lines) + '\n    }'


def file_text(header, data):
    rest = {k: v for k, v in data.items() if k != 'cards'}
    out = header + '{\n  "cards": [\n' + ',\n'.join(card_text(c) for c in data['cards']) + '\n  ]'
    for k, v in rest.items():
        out += ',\n  ' + dump(k) + ': ' + json.dumps(v, ensure_ascii=False, indent=2).replace('\n', '\n  ')
    return out + '\n}\n'


def check(cards):
    """The same checks the game's loader makes (a broken file means no cards at all in game)."""
    known, finishes = set(stat_names()), finish_names()
    ids, errors = set(), []
    for i, c in enumerate(cards):
        what = c.get('name') or c.get('id') or 'card %d' % (i + 1)
        cid = c.get('id')
        if not cid:
            errors.append('%s: needs an id' % what)
        elif cid in ids:
            errors.append('%s: the id "%s" is used twice' % (what, cid))
        ids.add(cid)
        if c.get('tier', 'C') not in TIERS:
            errors.append('%s: tier must be S, A, B, C, D or F' % what)
        if c.get('finish') and str(c['finish']).lower() not in finishes:
            errors.append('%s: finish must be one of: %s' % (what, ', '.join(finishes)))
        for k in ('max', 'maxTeam', 'minTeam'):
            if k in c and not isinstance(c[k], int):
                errors.append('%s: %s must be a whole number' % (what, k))
        for k, v in (c.get('stats') or {}).items():
            base = k[8:] if k.startswith('Opponent') else k
            if base not in known:
                errors.append('%s: unknown stat "%s"' % (what, k))
            if not isinstance(v, str) or not VALUE.match(v):
                errors.append('%s: %s should look like "x1.5", "/1.6" or "+1"' % (what, k))
    for c in cards:
        if c.get('requires') and c['requires'] not in ids:
            errors.append('%s: requires "%s", which isn\'t a card' % (c.get('name'), c['requires']))
    return errors


def sync_builds():
    done = []
    for dst in BUILDS:
        if not os.path.isdir(os.path.dirname(dst)):
            continue
        os.makedirs(os.path.join(dst, 'Art'), exist_ok=True)
        for f in os.listdir(CARDS):
            if f.endswith('.json'):
                shutil.copy2(os.path.join(CARDS, f), os.path.join(dst, f))
        for f in os.listdir(ART):
            if not f.endswith('.meta'):
                shutil.copy2(os.path.join(ART, f), os.path.join(dst, 'Art', f))
        done.append(os.path.relpath(dst, ROOT))
    return done


# ------------------------------------------------------------------ publishing to the current GitHub release

def card_files(folder):
    """{path inside the archive: bytes} for the cards folder of a build."""
    out = {}
    for base, _, files in os.walk(folder):
        for f in files:
            if f.endswith('.meta'):
                continue
            full = os.path.join(base, f)
            rel = os.path.relpath(full, folder).replace(os.sep, '/')
            out[ARCHIVE_CARDS + rel] = open(full, 'rb').read()
    return out


def patch_zip(path, files):
    tmp = path + '.tmp'
    with zipfile.ZipFile(path) as src, zipfile.ZipFile(tmp, 'w', zipfile.ZIP_DEFLATED) as dst:
        for info in src.infolist():
            if info.filename.startswith(ARCHIVE_CARDS) and not info.is_dir() and not info.filename.endswith('.meta'):
                continue  # replaced below
            dst.writestr(info, src.read(info))
        for name, data in sorted(files.items()):
            dst.writestr(zipfile.ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0)), data, zipfile.ZIP_DEFLATED)
    os.replace(tmp, path)


def patch_tar(path, files):
    tmp = path + '.tmp'
    with tarfile.open(path, 'r:gz') as src, tarfile.open(tmp, 'w:gz') as dst:
        for m in src.getmembers():
            if m.name.startswith(ARCHIVE_CARDS) and m.isfile():
                continue
            dst.addfile(m, src.extractfile(m) if m.isfile() else None)
        for name, data in sorted(files.items()):
            info = tarfile.TarInfo(name)
            info.size, info.mode = len(data), 0o644
            dst.addfile(info, io.BytesIO(data))
    os.replace(tmp, path)


def publish():
    def log(s):
        job['log'] += s + '\n'
    try:
        r = subprocess.run(['gh', 'release', 'view', '--repo', REPO, '--json', 'tagName', '-q', '.tagName'],
                           capture_output=True, text=True)
        if r.returncode != 0 or not r.stdout.strip():
            raise RuntimeError("couldn't find the live release: " + (r.stderr.strip() or 'no releases'))
        tag = r.stdout.strip()
        ver = tag.lstrip('v')
        dist_ver = open(os.path.join(DIST, 'version.txt')).read().strip()
        if dist_ver != ver:
            raise RuntimeError('the live release is %s but dist/ holds the %s zips. Make the release first (Release.bat).' % (tag, dist_ver))
        zip_path = os.path.join(DIST, 'UltraPuck-Windows.zip')
        tar_path = os.path.join(DIST, 'UltraPuck-Linux.tar.gz')
        sync_builds()
        log('Putting the cards into UltraPuck-Windows.zip ...')
        patch_zip(zip_path, card_files(BUILDS[0]))
        if os.path.isdir(os.path.dirname(BUILDS[1])):
            log('Putting the cards into UltraPuck-Linux.tar.gz ...')
            patch_tar(tar_path, card_files(BUILDS[1]))
        loose = os.path.join(DIST, 'UltraPuck', 'UltraPuck_Data', 'StreamingAssets', 'Cards')
        if os.path.isdir(loose):
            shutil.copytree(BUILDS[0], loose, dirs_exist_ok=True)
        log('Uploading to the live GitHub release, %s (about 130 MB) ...' % tag)
        r = subprocess.run(['gh', 'release', 'upload', tag, zip_path, tar_path, '--clobber', '--repo', REPO],
                           capture_output=True, text=True)
        if r.returncode != 0:
            raise RuntimeError(r.stderr.strip() or 'gh release upload failed')
        log('Done: https://github.com/%s/releases/tag/%s now has these cards.' % (REPO, tag))
    except Exception as ex:
        log('Failed: %s' % ex)
    finally:
        job['busy'] = False


# ------------------------------------------------------------------ the web server

class Handler(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def send(self, body, kind='application/json', code=200):
        if isinstance(body, (dict, list)):
            body = json.dumps(body, ensure_ascii=False)
        if isinstance(body, str):
            body = body.encode('utf-8')
        self.send_response(code)
        self.send_header('Content-Type', kind)
        self.send_header('Content-Length', str(len(body)))
        self.send_header('Cache-Control', 'no-store')
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        path = self.path.split('?')[0]
        if path == '/':
            self.send(open(os.path.join(ROOT, 'Tools', 'card_editor.html'), 'rb').read(), 'text/html; charset=utf-8')
        elif path == '/api/cards':
            header, data = load()
            self.send({'cards': data['cards'], 'stats': stat_names(), 'finishes': finish_names(), 'art': art_files(), 'version': version(),
                       'header': header})
        elif path == '/api/status':
            self.send(job)
        elif path.startswith('/art/'):
            name = os.path.basename(path[5:])
            full = os.path.join(ART, name)
            if not os.path.isfile(full):
                return self.send('no such art', 'text/plain', 404)
            self.send(open(full, 'rb').read(), 'image/png' if name.lower().endswith('.png') else 'image/jpeg')
        else:
            self.send('not found', 'text/plain', 404)

    def do_POST(self):
        if self.headers.get('Origin') not in (None, 'http://127.0.0.1:%d' % PORT, 'http://localhost:%d' % PORT):
            return self.send({'error': 'wrong origin'}, code=403)
        body = json.loads(self.rfile.read(int(self.headers.get('Content-Length', 0))) or b'{}')
        if self.path == '/api/save':
            errors = check(body['cards'])
            if errors:
                return self.send({'error': '\n'.join(errors)})
            header, data = load()
            data['cards'] = body['cards']
            with open(CARD_FILE, 'w', encoding='utf-8', newline='\n') as f:
                f.write(file_text(header, data))
            self.send({'ok': True, 'synced': sync_builds()})
        elif self.path == '/api/publish':
            if job['busy']:
                return self.send({'error': 'already publishing'})
            job.update(busy=True, log='')
            threading.Thread(target=publish, daemon=True).start()
            self.send({'ok': True})
        elif self.path == '/api/quit':  # a newer copy of the editor is starting: make way for it
            self.send({'ok': True})
            threading.Thread(target=self.server.shutdown, daemon=True).start()
        else:
            self.send({'error': 'not found'}, code=404)


class Server(ThreadingHTTPServer):
    # Only one editor on the port: Windows otherwise lets two share it, and requests go to either one at random.
    allow_reuse_address = False

    def server_bind(self):
        if hasattr(socket, 'SO_EXCLUSIVEADDRUSE'):
            self.socket.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        super().server_bind()


def main():
    if '--check' in sys.argv:  # round-trip test: does saving an unchanged file give the same bytes?
        header, data = load()
        same = file_text(header, data) == open(CARD_FILE, encoding='utf-8').read()
        print('round trip identical' if same else 'round trip DIFFERS', '|', len(data['cards']), 'cards |',
              'problems:', check(data['cards']) or 'none')
        return
    url = 'http://127.0.0.1:%d/' % PORT
    try:  # an editor already open (an older window)? ask it to close so only this one answers
        urllib.request.urlopen(urllib.request.Request(url + 'api/quit', b'{}', method='POST'), timeout=1)
        time.sleep(0.5)
    except Exception:
        pass
    try:
        server = Server(('127.0.0.1', PORT), Handler)
    except OSError:
        print('Another card editor is already running. Close its window, then start this again.')
        sys.exit(1)
    print('Ultra Puck card editor on', url, '(close this window to stop it)')
    if '--no-browser' not in sys.argv:
        webbrowser.open(url)
    server.serve_forever()


if __name__ == '__main__':
    main()
