# Writes the Hockey Rink map: a rounded rink like the Test Rink, but the goals stand out on the ice in front of
# each end, inside a little net (a wall on three sides, open towards the middle). You can skate and pass behind them.
import json, math, sys

W, H, R = 24.0, 13.5, 6.0      # half width, half height, corner radius
ARC = 10                       # segments per corner
NET_X = 18.5                   # where the back of each net stands (from the middle)
DEPTH, MOUTH = 2.0, 2.6        # net depth, mouth half height
CHAMFER = 0.5                  # the back corners of the net are cut off a little, so nothing snags there

def r3(v): return round(v + 0.0, 3)

def arc(cx, cy, a0, a1):
    return [[r3(cx + R * math.cos(math.radians(a0 + (a1 - a0) * k / ARC))), r3(cy + R * math.sin(math.radians(a0 + (a1 - a0) * k / ARC)))] for k in range(ARC + 1)]

pts = []
pts += arc(W - R, H - R, 0, 90)
pts += arc(-W + R, H - R, 90, 180)
pts += arc(-W + R, -H + R, 180, 270)
pts += arc(W - R, -H + R, 270, 360)
clean = []
for p in pts:
    if not clean or clean[-1] != p: clean.append(p)
if clean[0] == clean[-1]: clean.pop()

def net(side):
    # side -1: the left net (team 0 defends it), +1: the right one. Open towards the middle.
    back = side * NET_X
    mouth = side * (NET_X - DEPTH)
    return [
        [r3(mouth), r3(MOUTH)],
        [r3(back + -side * CHAMFER), r3(MOUTH)],
        [r3(back), r3(MOUTH - CHAMFER)],
        [r3(back), r3(-MOUTH + CHAMFER)],
        [r3(back + -side * CHAMFER), r3(-MOUTH)],
        [r3(mouth), r3(-MOUTH)],
    ]

line = 0.3  # how far past the mouth the puck has to be to count
spawns = []
for team, sx in ((0, -1), (1, 1)):
    for x, y in ((8, 0), (11, 5), (11, -5), (14.5, 0)):
        spawns.append({"team": team, "pos": [sx * x, y], "facing": [-sx, 0]})

goal_in = NET_X - DEPTH + line   # mouth line, plus a little
m = {
    "name": "Hockey Rink",
    "author": "Ultra Puck",
    "walls": [
        {"closed": True, "points": clean},
        {"closed": False, "points": net(-1)},
        {"closed": False, "points": net(1)},
    ],
    "goals": [
        {"team": 0, "min": [-NET_X, -MOUTH], "max": [-goal_in, MOUTH]},
        {"team": 1, "min": [goal_in, -MOUTH], "max": [NET_X, MOUTH]},
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

head = ("// Hockey Rink: the goals stand out on the ice inside little nets, open towards the middle, so you can go behind\n"
        "// them. Team 0 defends the left net, team 1 the right. 48 x 27, made by a script (make_hockey_rink.py).\n")
open(sys.argv[1], "w", encoding="utf-8", newline="\n").write(head + fmt(m) + "\n")
print(len(clean), "outline points")
