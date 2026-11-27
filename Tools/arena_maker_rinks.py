# Puts the rinks' walls and nets into Tools/arena_maker.html (between /*RINKS*/ and /*END*/), so the arena maker's
# grid and guides match the game. Run it again after changing a rink in Assets/StreamingAssets/Maps:
#   python Tools/arena_maker_rinks.py
import json
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MAPS = os.path.join(ROOT, 'Assets', 'StreamingAssets', 'Maps')
PAGE = os.path.join(ROOT, 'Tools', 'arena_maker.html')
RINKS = ['hockey-rink', 'backwards-rink', 'big-rink', 's-rink', 'u-rink']


def load(name):
    text = open(os.path.join(MAPS, name + '.json'), encoding='utf-8').read()
    return json.loads('\n'.join(l for l in text.split('\n') if not l.lstrip().startswith('//')))


def r3(v):
    return round(v, 3)


out = {}
for f in RINKS:
    m = load(f)
    out[f] = {
        'name': m['name'],
        'walls': [{'closed': bool(w.get('closed')), 'points': [[r3(p[0]), r3(p[1])] for p in w['points']]} for w in m['walls']],
        'goals': [{'team': g['team'], 'min': g['min'], 'max': g['max']} for g in m.get('goals', [])],
    }

page = open(PAGE, encoding='utf-8').read()
page, n = re.subn(r'/\*RINKS\*/.*?/\*END\*/', lambda _: '/*RINKS*/' + json.dumps(out, separators=(',', ':')) + '/*END*/', page, flags=re.S)
if n != 1:
    raise SystemExit('no /*RINKS*/ ... /*END*/ block in ' + PAGE)
open(PAGE, 'w', encoding='utf-8', newline='\n').write(page)
print('rinks written:', ', '.join(out))
