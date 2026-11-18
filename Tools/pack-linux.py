# Packs Builds/Linux as dist/UltraPuck-Linux.tar.gz: an "UltraPuck" folder with a read-me, and the game file marked
# executable (a zip made on Windows would lose that). Run by Tools/release.ps1.
import io
import os
import tarfile

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
src = os.path.join(root, 'Builds', 'Linux')
dist = os.path.join(root, 'dist')
os.makedirs(dist, exist_ok=True)
out = os.path.join(dist, 'UltraPuck-Linux.tar.gz')
version = open(os.path.join(root, 'version.txt')).read().strip()

readme = f"""Ultra Puck {version} for Linux / Steam Deck

Steam Deck:
  1. In Desktop Mode, extract this archive somewhere (for example ~/Games), keeping the UltraPuck folder together.
  2. In Steam: Games > Add a Non-Steam Game to My Library > Browse, pick UltraPuck/UltraPuck.x86_64.
  3. Play it from your library (Game Mode works too). Don't set a Proton compatibility tool: this is the native
     Linux version.
  Started from Steam, the Steam overlay and invites work.

Any Linux PC: run ./UltraPuck.x86_64 from this folder.

The game updates itself: when a new version is out it says so, downloads it and restarts. Keep the UltraPuck
folder somewhere you can write to (like ~/Games). Online play needs everyone on the same version.
"""

executables = {'UltraPuck.x86_64'}


def add(tar, path, arc):
    info = tar.gettarinfo(path, arc)
    info.uid = info.gid = 0
    info.uname = info.gname = ''
    if info.isdir():
        info.mode = 0o755
    else:
        name = os.path.basename(path)
        info.mode = 0o755 if name in executables or name.endswith('.so') or '.so.' in name else 0o644
    if info.isfile():
        with open(path, 'rb') as f:
            tar.addfile(info, f)
    else:
        tar.addfile(info)


with tarfile.open(out, 'w:gz') as tar:
    add(tar, src, 'UltraPuck')
    for base, dirs, files in os.walk(src):
        dirs.sort()
        for d in dirs:
            p = os.path.join(base, d)
            add(tar, p, 'UltraPuck/' + os.path.relpath(p, src).replace(os.sep, '/'))
        for f in sorted(files):
            p = os.path.join(base, f)
            add(tar, p, 'UltraPuck/' + os.path.relpath(p, src).replace(os.sep, '/'))
    data = readme.encode('utf-8')
    info = tarfile.TarInfo('UltraPuck/README.txt')
    info.size = len(data)
    info.mode = 0o644
    tar.addfile(info, io.BytesIO(data))

print('Made', out, '(' + str(round(os.path.getsize(out) / 1e6, 1)) + ' MB)')
