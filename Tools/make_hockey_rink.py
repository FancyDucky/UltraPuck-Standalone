# Writes the Hockey Rink map: a rounded rink like the Test Rink, but the goals stand out on the ice in front of
# each end, inside a little net (a wall on three sides, open towards the middle). You can skate and pass behind them.
import json, math, sys

STRETCH = 1.35                 # 35% longer than the Test Rink, end to end
W, H, R = 24.0 * STRETCH, 13.5, 6.0      # half width, half height, corner radius
ARC = 10                       # segments per corner
NET_X = 18.5 * STRETCH         # where the back of each net stands (from the middle); the nets themselves keep their size
NET_SCALE = 0.6                # the nets are 40% smaller than they first were
DEPTH, MOUTH = 1.9, 2.6 * NET_SCALE   # net depth (deeper than the first 1.2), mouth half height
WALL = 0.68                    # wall thickness (the game's Tuning.WallThickness): the goal area stops at the walls' inner edge

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
        [r3(back), r3(MOUTH)],
        [r3(back), r3(-MOUTH)],
        [r3(mouth), r3(-MOUTH)],
    ]

line = 0.2  # how far past the mouth the puck has to be to count
spawns = []
for team, sx in ((0, -1), (1, 1)):
    for x, y in ((8, 0), (11, 5), (11, -5), (14.5, 0)):
        spawns.append({"team": team, "pos": [r3(sx * x * STRETCH), y], "facing": [-sx, 0]})

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
        {"team": 0, "min": [r3(-NET_X + WALL / 2), r3(-MOUTH + WALL / 2)], "max": [r3(-goal_in), r3(MOUTH - WALL / 2)]},
        {"team": 1, "min": [r3(goal_in), r3(-MOUTH + WALL / 2)], "max": [r3(NET_X - WALL / 2), r3(MOUTH - WALL / 2)]},
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
        "// them. Team 0 defends the left net, team 1 the right. 64.8 x 27, made by a script (make_hockey_rink.py).\n")
open(sys.argv[1], "w", encoding="utf-8", newline="\n").write(head + fmt(m) + "\n")
print(len(clean), "outline points")
