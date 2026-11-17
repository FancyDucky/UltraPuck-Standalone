# Writes the Test Rink map: a rounded rectangle with a goal notch at each end.
import json, math, sys

W, H, R = 24.0, 13.5, 6.0      # half width, half height, corner radius
MOUTH, DEPTH = 3.0, 2.0        # goal mouth half height, net depth
ARC = 10                       # segments per corner

def r3(v): return round(v + 0.0, 3)

def arc(cx, cy, a0, a1):
    return [[r3(cx + R * math.cos(math.radians(a0 + (a1 - a0) * k / ARC))), r3(cy + R * math.sin(math.radians(a0 + (a1 - a0) * k / ARC)))] for k in range(ARC + 1)]

pts = [[W, MOUTH]]
pts += arc(W - R, H - R, 0, 90)          # top right corner, from (W, H-R) to (W-R, H)
pts += arc(-W + R, H - R, 90, 180)       # top left
pts += [[-W, MOUTH], [-W - DEPTH, MOUTH], [-W - DEPTH, -MOUTH], [-W, -MOUTH]]
pts += arc(-W + R, -H + R, 180, 270)     # bottom left
pts += arc(W - R, -H + R, 270, 360)      # bottom right
pts += [[W, -MOUTH], [W + DEPTH, -MOUTH], [W + DEPTH, MOUTH]]

# drop exact repeats next to each other
clean = []
for p in pts:
    if not clean or clean[-1] != p: clean.append(p)
if clean[0] == clean[-1]: clean.pop()

line = 0.25  # how far into the net the puck has to be to count
spawns = []
for team, sx in ((0, -1), (1, 1)):
    for x, y in ((9, 0), (12.5, 5), (12.5, -5), (17, 0)):
        spawns.append({"team": team, "pos": [sx * x, y], "facing": [-sx, 0]})

m = {
    "name": "Test Rink",
    "author": "Ultra Puck",
    "walls": [{"closed": True, "points": clean}],
    "goals": [
        {"team": 0, "min": [-W - DEPTH, -MOUTH], "max": [-W - line, MOUTH]},
        {"team": 1, "min": [W + line, -MOUTH], "max": [W + DEPTH, MOUTH]},
    ],
    "spawns": spawns,
    "puck": [0, 0],
}

def fmt(o, ind=0):
    pad = "  " * ind
    if isinstance(o, dict):
        return "{\n" + ",\n".join(pad + "  " + json.dumps(k) + ": " + fmt(v, ind + 1) for k, v in o.items()) + "\n" + pad + "}"
    if isinstance(o, list) and o and all(isinstance(x, (int, float)) for x in o):
        return "[" + ", ".join(json.dumps(x) for x in o) + "]"
    if isinstance(o, list):
        return "[\n" + ",\n".join(pad + "  " + fmt(v, ind + 1) for v in o) + "\n" + pad + "]"
    return json.dumps(o)

head = ("// Test Rink: a plain rounded rink with a goal at each end. Units: a player is 1 across.\n"
        "// Team 0 defends the left goal, team 1 the right. 48 x 27, made by a script (make_rink.py).\n")
open(sys.argv[1], "w", encoding="utf-8", newline="\n").write(head + fmt(m) + "\n")
print(len(clean), "wall points")
