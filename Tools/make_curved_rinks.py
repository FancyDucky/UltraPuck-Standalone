# Writes the S Rink and the U Rink: a corridor of ice bent into a shape, with the Hockey Rink's nets.
#   S Rink: an S lying on its side, a net standing on the ice near each tip of the S (room to skate behind it), the
#           puck dropped where the two halves meet.
#   U Rink: an upright U (open at the top), a net near the top of each arm with room behind it, both nets open
#           downwards towards the bend, the puck dropped at the bottom of the bend.
# Each map also has a "route": points down the middle of the corridor from team 0's goal to team 1's, which bots
# follow when a wall stands between them and where they want to go. And each goal says which way its mouth faces.
#   python make_curved_rinks.py <maps folder>
import json, math, os, sys

WALL = 0.68                    # wall thickness (Tuning.WallThickness)
GROW = 1.3                     # the nets are 30% bigger than they first were: the opening and the depth, inside the walls
MOUTH = round((1.56 - WALL / 2) * GROW + WALL / 2, 3)   # the Hockey Rink's nets: mouth half height (wall middle)
DEPTH = round((1.9 - WALL / 2) * GROW + WALL / 2, 3)    # and depth (mouth to the back wall's middle)
ROOM = 6.0                     # the ice behind a net: from the end wall to the net's back, like the Hockey Rink


def r3(v): return round(v, 3) + 0.0   # (+ 0.0 so a rounded-away tiny negative is written 0.0, not -0.0)
def add(a, b): return (a[0] + b[0], a[1] + b[1])
def sub(a, b): return (a[0] - b[0], a[1] - b[1])
def mul(a, k): return (a[0] * k, a[1] * k)
def unit(a):
    l = math.hypot(a[0], a[1])
    return (a[0] / l, a[1] / l)
def pt(p): return [r3(p[0]), r3(p[1])]


def goal_box(centre, out, team):
    """The scoring box in a net whose mouth is at centre and whose back is DEPTH further along out (axis-aligned)."""
    u = (-out[1], out[0])
    corners = []
    # front edge at the mouth opening (along=0); sides stop at the walls' inner edge
    for along in (0.0, DEPTH - WALL / 2):
        for side in (-1, 1):
            corners.append(add(add(centre, mul(out, along)), mul(u, side * (MOUTH - WALL / 2))))
    xs = [c[0] for c in corners]
    ys = [c[1] for c in corners]
    return {"team": team, "min": [r3(min(xs)), r3(min(ys))], "max": [r3(max(xs)), r3(max(ys))], "facing": pt(mul(out, -1))}


def standing_net(end, into):
    """
    A net standing on the ice: three walls, open along into. end is the middle of the end wall behind it and into
    the way from there onto the ice (straight along an axis). Returns its walls and the middle of its mouth.
    """
    back = add(end, mul(into, ROOM))
    mouth = add(back, mul(into, DEPTH))
    u = (-into[1], into[0])
    return [add(mouth, mul(u, MOUTH)), add(back, mul(u, MOUTH)), add(back, mul(u, -MOUTH)), add(mouth, mul(u, -MOUTH))], mouth


def along(path, dist):
    """The point dist along a polyline, and the direction there."""
    for k in range(len(path) - 1):
        seg = math.dist(path[k], path[k + 1])
        if dist <= seg or k == len(path) - 2:
            t = min(1.0, dist / seg) if seg > 0 else 0
            return add(path[k], mul(sub(path[k + 1], path[k]), t)), unit(sub(path[k + 1], path[k]))
        dist -= seg
    return path[-1], unit(sub(path[-1], path[-2]))


def spawns_on(route, puck_index, team, flip=False):
    """
    Four spawns along the corridor on a team's side of the puck, facing the puck (route runs team 0 -> team 1).
    flip swaps the two side-by-side spawns: for the second team on a rink that's a mirror image of itself, so each
    team's spawns are the other's mirrored, in the same order.
    """
    half = route[:puck_index + 1][::-1] if team == 0 else route[puck_index:]
    out = []
    for dist, side in ((5, 0), (8.5, 3), (8.5, -3), (13, 0)):
        p, d = along(half, dist)
        n = (-d[1], d[0])
        out.append({"team": team, "pos": pt(add(p, mul(n, -side if flip else side))), "facing": pt(mul(d, -1))})
    return out


def thin(route, step=1.5):
    """Route points about step apart."""
    out = [route[0]]
    for p in route[1:]:
        if math.dist(out[-1], p) >= step: out.append(p)
    if out[-1] != route[-1]: out.append(route[-1])
    return out


def trim(path, cut):
    """The path without the first and the last cut of its length."""
    at = [0.0]
    for k in range(1, len(path)): at.append(at[-1] + math.dist(path[k - 1], path[k]))
    return [p for p, t in zip(path, at) if cut <= t <= at[-1] - cut]


# ------------------------------------------------------------------ S Rink

def s_rink():
    r, h = 13.5, 11.0          # centre line radius of each half of the S, half the corridor width (22 wide)
    steps = 54                 # 5 degrees a step
    centre = []                # (point, direction) down the middle, from one tip to the other
    for k in range(steps + 1):  # top half: centre (0, r), from 0 round anticlockwise to 270 degrees
        a = math.radians(270 * k / steps)
        centre.append(((r * math.cos(a), r + r * math.sin(a)), (-math.sin(a), math.cos(a))))
    for k in range(1, steps + 1):  # bottom half: centre (0, -r), from 90 clockwise to -180 degrees
        a = math.radians(90 - 270 * k / steps)
        centre.append(((r * math.cos(a), -r + r * math.sin(a)), (math.sin(a), -math.cos(a))))
    rot = lambda p: (p[1], -p[0])  # lie it on its side
    centre = [(rot(p), rot(d)) for p, d in centre]
    left = [add(p, mul((-d[1], d[0]), h)) for p, d in centre]
    right = [add(p, mul((d[1], -d[0]), h)) for p, d in centre]

    # a plain end wall across each tip, and a net standing on the ice in front of it, open along the corridor:
    # at the start the corridor runs away along d0, at the end it arrives along d1
    (p0, d0), (p1, d1) = centre[0], centre[-1]
    outline = left + right[::-1]
    net_start, mouth_start = standing_net(p0, d0)
    net_end, mouth_end = standing_net(p1, mul(d1, -1))
    # team 0 defends the net on the left
    start_team = 0 if mouth_start[0] < mouth_end[0] else 1
    goals = [goal_box(mouth_start, mul(d0, -1), start_team), goal_box(mouth_end, d1, 1 - start_team)]
    # route: from just in front of one net, down the middle of the corridor, to just in front of the other
    route = [add(mouth_start, mul(d0, 2.0))] + thin(trim([p for p, d in centre], ROOM + DEPTH + 3.0)) + [add(mouth_end, mul(d1, -2.0))]
    if start_team == 1: route = route[::-1]
    puck = (0.0, 0.0)
    pi = min(range(len(route)), key=lambda k: math.dist(route[k], puck))
    route[pi] = puck
    return {
        "name": "S Rink", "author": "Ultra Puck",
        "walls": [{"closed": True, "points": [pt(p) for p in outline]},
                  {"closed": False, "points": [pt(p) for p in net_start]},
                  {"closed": False, "points": [pt(p) for p in net_end]}],
        "goals": sorted(goals, key=lambda g: g["team"]),
        "spawns": spawns_on(route, pi, 0) + spawns_on(route, pi, 1),
        "puck": pt(puck),
        "route": [pt(p) for p in route],
    }, "// S Rink: an S lying on its side, a net standing on the ice near each tip with room to play behind it. Team 0 defends the net on the left. Made by make_curved_rinks.py.\n"


# ------------------------------------------------------------------ U Rink

def u_rink():
    # drawn lying on its side (open to the right), then stood up at the end (open at the top)
    w, gap = 24.0, 6.0         # corridor width, the gap between the arms
    inner, outer = gap / 2, gap / 2 + w
    cx, end = -6.0, 21.0       # the bend's centre, the end walls of the arms
    arm = inner + w / 2        # the middle of each arm, either side of the centre line
    steps = 36
    outline = [(end, outer), (cx, outer)]
    outline += [(cx + outer * math.cos(math.radians(90 + 180 * k / steps)), outer * math.sin(math.radians(90 + 180 * k / steps))) for k in range(1, steps)]
    outline += [(cx, -outer), (end, -outer), (end, -inner), (cx, -inner)]
    outline += [(cx + inner * math.cos(math.radians(270 - 180 * k / 12)), inner * math.sin(math.radians(270 - 180 * k / 12))) for k in range(1, 12)]
    outline += [(cx, inner), (end, inner)]

    into = (-1.0, 0.0)  # both nets open towards the bend
    net0, mouth0 = standing_net((end, arm), into)
    net1, mouth1 = standing_net((end, -arm), into)
    # route: from just in front of team 0's net, down its arm, round the bend and up the other arm to team 1's net
    # (the same points on both arms, so each team's half is the other's mirrored)
    front = mouth0[0] - 2
    n = math.ceil((front - cx) / 1.5)
    xs = [cx + (front - cx) * k / n for k in range(n + 1)]
    route = [(x, arm) for x in xs[::-1]]
    route += [(cx + arm * math.cos(math.radians(90 + 180 * k / 18)), arm * math.sin(math.radians(90 + 180 * k / 18))) for k in range(1, 18)]
    route += [(x, -arm) for x in xs]
    puck = (cx - arm, 0.0)
    pi = min(range(len(route)), key=lambda k: math.dist(route[k], puck))
    route[pi] = puck

    up = lambda p: (-p[1], p[0])  # stand it up: the open end at the top, team 0's arm on the left
    out = up(mul(into, -1))
    goals = [goal_box(up(mouth0), out, 0), goal_box(up(mouth1), out, 1)]
    route = [up(p) for p in route]
    return {
        "name": "U Rink", "author": "Ultra Puck",
        "walls": [{"closed": True, "points": [pt(up(p)) for p in outline]},
                  {"closed": False, "points": [pt(up(p)) for p in net0]},
                  {"closed": False, "points": [pt(up(p)) for p in net1]}],
        "goals": goals,
        "spawns": spawns_on(route, pi, 0) + spawns_on(route, pi, 1, flip=True),
        "puck": pt(up(puck)),
        "route": [pt(p) for p in route],
    }, "// U Rink: an upright U, open at the top, a net near the top of each arm with room to play behind it. Team 0 defends the net on the left. Made by make_curved_rinks.py.\n"


def fmt(o, ind=0):
    pad = "  " * ind
    if isinstance(o, dict):
        return "{\n" + ",\n".join(pad + "  " + json.dumps(k) + ": " + fmt(v, ind + 1) for k, v in o.items()) + "\n" + pad + "}"
    if isinstance(o, list) and o and all(isinstance(x, (int, float)) for x in o):
        return "[" + ", ".join(json.dumps(x) for x in o) + "]"
    if isinstance(o, list):
        return "[\n" + ",\n".join(pad + "  " + fmt(v, ind + 1) for v in o) + "\n" + pad + "]"
    return json.dumps(o)


folder = sys.argv[1]
for name, (m, head) in (("s-rink.json", s_rink()), ("u-rink.json", u_rink())):
    open(os.path.join(folder, name), "w", encoding="utf-8", newline="\n").write(head + fmt(m) + "\n")
    print(name, len(m["walls"][0]["points"]), "outline points,", len(m["route"]), "route points, goals", [(g["min"], g["max"], g["facing"]) for g in m["goals"]])
