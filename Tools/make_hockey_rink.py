# Writes the Hockey Rink map: a rounded rink like the Test Rink, but the goals stand out on the ice in front of
# each end, inside a little net (a wall on three sides, open towards the middle). You can skate and pass behind them.
# Usage: make_hockey_rink.py out.json            the Hockey Rink
#        make_hockey_rink.py out.json --big      the Big Rink: the same, 70% bigger every way (for 4v4); nets the same size
#        make_hockey_rink.py out.json --backwards  the Backwards Rink: the Hockey Rink with each net turned round where it
#                                                  stands, so it opens towards the end wall behind it (go round to score)
import json, math, sys

BIG = "--big" in sys.argv
BACKWARDS = "--backwards" in sys.argv
SIZE = 1.7 if BIG else 1.0     # the Big Rink: 70% bigger (length, width, corners, where things stand)
STRETCH = 1.35                 # 35% longer than the Test Rink, end to end
W, H, R = 24.0 * STRETCH * SIZE, 13.5 * SIZE, 6.0 * SIZE      # half width, half height, corner radius
ARC = 14 if BIG else 10        # segments per corner
NET_X = 18.5 * STRETCH * SIZE  # where the back of each net stands (from the middle)
NET_SCALE = 0.6                # the nets were made 40% smaller than they first were...
GROW = 1.3                     # ...and then 30% bigger again: the opening and the depth, measured inside the walls
WALL = 0.68                    # wall thickness (the game's Tuning.WallThickness): the goal area stops at the walls' inner edge
DEPTH = round((1.9 - WALL / 2) * GROW + WALL / 2, 3)                 # net depth: mouth to the back wall's middle
WIDEN = 1.1                    # then the opening 10% wider again (easier to score)
MOUTH = round((2.6 * NET_SCALE - WALL / 2) * GROW * WIDEN + WALL / 2, 3)     # mouth half height (to the side walls' middle)

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
    # side -1: the left net (team 0 defends it), +1: the right one. Open towards the middle
    # (backwards: turned round in the same place, open towards the end wall behind it).
    back = side * NET_X
    mouth = side * (NET_X - DEPTH)
    if BACKWARDS: back, mouth = mouth, back
    return [
        [r3(mouth), r3(MOUTH)],
        [r3(back), r3(MOUTH)],
        [r3(back), r3(-MOUTH)],
        [r3(mouth), r3(-MOUTH)],
    ]

spawns = []
for team, sx in ((0, -1), (1, 1)):
    for x, y in ((8, 0), (11, 5), (11, -5), (14.5, 0)):
        spawns.append({"team": team, "pos": [r3(sx * x * STRETCH * SIZE), r3(y * SIZE)], "facing": [-sx, 0]})

mouth_x = NET_X - DEPTH  # front of the goal box at the net opening
goals = [
    # front edge at the mouth opening; sides stop at the walls' inner edge
    # "facing": which way each net opens (bots skate round to that side, and round the net to a puck behind it)
    {"team": 0, "min": [r3(-NET_X + WALL / 2), r3(-MOUTH + WALL / 2)], "max": [r3(-mouth_x), r3(MOUTH - WALL / 2)], "facing": [1.0, 0.0]},
    {"team": 1, "min": [r3(mouth_x), r3(-MOUTH + WALL / 2)], "max": [r3(NET_X - WALL / 2), r3(MOUTH - WALL / 2)], "facing": [-1.0, 0.0]},
]
if BACKWARDS:
    # the same boxes turned round: the open side (no inset) at the end-wall side, the back wall's inner edge towards
    # the middle. "faceoff": the face-off group still stands on the middle side of the net (behind its back wall).
    goals = [
        {"team": 0, "min": [r3(-NET_X), r3(-MOUTH + WALL / 2)], "max": [r3(-mouth_x - WALL / 2), r3(MOUTH - WALL / 2)], "facing": [-1.0, 0.0], "faceoff": [1.0, 0.0]},
        {"team": 1, "min": [r3(mouth_x + WALL / 2), r3(-MOUTH + WALL / 2)], "max": [r3(NET_X), r3(MOUTH - WALL / 2)], "facing": [1.0, 0.0], "faceoff": [-1.0, 0.0]},
    ]
m = {
    "name": "Big Rink" if BIG else "Backwards Rink" if BACKWARDS else "Hockey Rink",
    "author": "Ultra Puck",
    "walls": [
        {"closed": True, "points": clean},
        {"closed": False, "points": net(-1)},
        {"closed": False, "points": net(1)},
    ],
    "goals": goals,
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
if BIG:
    head = ("// Big Rink: the Hockey Rink 70%% bigger every way (the nets the same size), room for 4 against 4.\n"
            "// Team 0 defends the left net, team 1 the right. %.1f x %.1f, made by a script (make_hockey_rink.py --big).\n" % (W * 2, H * 2))
if BACKWARDS:
    head = ("// Backwards Rink: the Hockey Rink with each net turned round where it stands, open towards the end wall behind\n"
            "// it, so you go round the net to score. Team 0 defends the left net, team 1 the right. 64.8 x 27, made by a\n"
            "// script (make_hockey_rink.py --backwards).\n")
open(sys.argv[1], "w", encoding="utf-8", newline="\n").write(head + fmt(m) + "\n")
print(len(clean), "outline points")
