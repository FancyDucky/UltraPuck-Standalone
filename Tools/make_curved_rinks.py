# Writes the S Rink and the U Rink: a corridor of ice bent into a shape, with the Hockey Rink's nets.
#   S Rink: an S lying on its side, a goal in each tip of the S, the puck dropped where the two halves meet.
#   U Rink: a U lying on its side (open to the right), a net near the end of each arm with room behind it, the puck
#           dropped in the middle of the bend.
# Each map also has a "route": points down the middle of the corridor from team 0's goal to team 1's, which bots
# follow when a wall stands between them and where they want to go. And each goal says which way its mouth faces.
#   python make_curved_rinks.py <maps folder>
import json, math, os, sys

WALL = 0.68                    # wall thickness (Tuning.WallThickness)
MOUTH, DEPTH = 1.56, 1.9       # the Hockey Rink's nets: mouth half height (wall middle), depth
LINE = 0.2                     # how far past the mouth the puck has to be to count


def r3(v): return round(v + 0.0, 3)
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
    for along in (LINE, DEPTH - WALL / 2):
        for side in (-1, 1):
            corners.append(add(add(centre, mul(out, along)), mul(u, side * (MOUTH - WALL / 2))))
    xs = [c[0] for c in corners]
    ys = [c[1] for c in corners]
    return {"team": team, "min": [r3(min(xs)), r3(min(ys))], "max": [r3(max(xs)), r3(max(ys))], "facing": pt(mul(out, -1))}


def recess(p1, p2, out):
    """A net set into an end wall running p1 -> p2 (its middle), open towards the ice, going out along out."""
    c = mul(add(p1, p2), 0.5)
    u = unit(sub(p2, p1))
    return [add(c, mul(u, -MOUTH)), add(add(c, mul(u, -MOUTH)), mul(out, DEPTH)),
            add(add(c, mul(u, MOUTH)), mul(out, DEPTH)), add(c, mul(u, MOUTH))], c


def along(path, dist):
    """The point dist along a polyline, and the direction there."""
    for k in range(len(path) - 1):
        seg = math.dist(path[k], path[k + 1])
        if dist <= seg or k == len(path) - 2:
            t = min(1.0, dist / seg) if seg > 0 else 0
            return add(path[k], mul(sub(path[k + 1], path[k]), t)), unit(sub(path[k + 1], path[k]))
        dist -= seg
    return path[-1], unit(sub(path[-1], path[-2]))


def spawns_on(route, puck_index, team):
    """Four spawns along the corridor on a team's side of the puck, facing the puck (route runs team 0 -> team 1)."""
    half = route[:puck_index + 1][::-1] if team == 0 else route[puck_index:]
    out = []
    for dist, side in ((5, 0), (8.5, 3), (8.5, -3), (13, 0)):
        p, d = along(half, dist)
        n = (-d[1], d[0])
        out.append({"team": team, "pos": pt(add(p, mul(n, side))), "facing": pt(mul(d, -1))})
    return out


def thin(route, step=1.5):
    """Route points about step apart."""
    out = [route[0]]
    for p in route[1:]:
        if math.dist(out[-1], p) >= step: out.append(p)
    if out[-1] != route[-1]: out.append(route[-1])
    return out


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

    # end walls with a net set into each: at the start the corridor runs away along d, so the net goes the other way
    (p0, d0), (p1, d1) = centre[0], centre[-1]
    net_start, mouth_start = recess(right[0], left[0], mul(d0, -1))
    net_end, mouth_end = recess(left[-1], right[-1], d1)
    outline = left + net_end + right[::-1] + net_start
    # team 0 defends the net on the left
    start_team = 0 if mouth_start[0] < mouth_end[0] else 1
    goals = [goal_box(mouth_start, mul(d0, -1), start_team), goal_box(mouth_end, d1, 1 - start_team)]
    route = [p for p, d in centre]
    if start_team == 1: route = route[::-1]
    route = thin(route)
    puck = (0.0, 0.0)
    pi = min(range(len(route)), key=lambda k: math.dist(route[k], puck))
    route[pi] = puck
    return {
        "name": "S Rink", "author": "Ultra Puck",
        "walls": [{"closed": True, "points": [pt(p) for p in outline]}],
        "goals": sorted(goals, key=lambda g: g["team"]),
        "spawns": spawns_on(route, pi, 0) + spawns_on(route, pi, 1),
        "puck": pt(puck),
        "route": [pt(p) for p in route],
    }, "// S Rink: an S lying on its side, a net in each tip. Team 0 defends the net on the left. Made by make_curved_rinks.py.\n"


# ------------------------------------------------------------------ U Rink

def u_rink():
    w, gap = 24.0, 6.0         # corridor width, the gap between the arms
    inner, outer = gap / 2, gap / 2 + w
    cx, end = -6.0, 21.0       # the bend's centre, the end walls of the arms
    arm = inner + w / 2        # the middle of each arm, above and below the centre line
    back = end - 6.0           # the back of each net: room behind it, like the Hockey Rink
    steps = 36
    outline = [(end, outer), (cx, outer)]
    outline += [(cx + outer * math.cos(math.radians(90 + 180 * k / steps)), outer * math.sin(math.radians(90 + 180 * k / steps))) for k in range(1, steps)]
    outline += [(cx, -outer), (end, -outer), (end, -inner), (cx, -inner)]
    outline += [(cx + inner * math.cos(math.radians(270 - 180 * k / 12)), inner * math.sin(math.radians(270 - 180 * k / 12))) for k in range(1, 12)]
    outline += [(cx, inner), (end, inner)]

    def net(y):
        return [(back - DEPTH, y + MOUTH), (back, y + MOUTH), (back, y - MOUTH), (back - DEPTH, y - MOUTH)]

    out = (1.0, 0.0)  # both nets open towards the bend (to the left)
    goals = [goal_box((back - DEPTH, arm), out, 0), goal_box((back - DEPTH, -arm), out, 1)]
    # route: from in front of team 0's net (top arm) round the bend to team 1's (bottom arm)
    route = [(x, arm) for x in [back - DEPTH - 2 - 1.5 * k for k in range(int((back - DEPTH - 2 - cx) / 1.5) + 1)]]
    route += [(cx + arm * math.cos(math.radians(90 + 180 * k / 18)), arm * math.sin(math.radians(90 + 180 * k / 18))) for k in range(1, 18)]
    route += [(x, -arm) for x in [cx + 1.5 * k for k in range(int((back - DEPTH - 2 - cx) / 1.5) + 1)]]
    puck = (cx - arm, 0.0)
    pi = min(range(len(route)), key=lambda k: math.dist(route[k], puck))
    route[pi] = puck
    return {
        "name": "U Rink", "author": "Ultra Puck",
        "walls": [{"closed": True, "points": [pt(p) for p in outline]},
                  {"closed": False, "points": [pt(p) for p in net(arm)]},
                  {"closed": False, "points": [pt(p) for p in net(-arm)]}],
        "goals": goals,
        "spawns": spawns_on(route, pi, 0) + spawns_on(route, pi, 1),
        "puck": pt(puck),
        "route": [pt(p) for p in route],
    }, "// U Rink: a U lying on its side, a net near the end of each arm with room to play behind it. Team 0 defends the top net. Made by make_curved_rinks.py.\n"


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
