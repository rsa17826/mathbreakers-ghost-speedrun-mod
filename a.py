import struct
import UnityPy

SRC = "mathbreakers/Mathbreakers_Data/level3.bak"
DST = "mathbreakers/Mathbreakers_Data/level3"

env1 = UnityPy.load(SRC)
mesh_collider_pathids = []
for obj in env1.objects:
    if obj.type.name != "GameObject":
        continue
    tree = obj.read_typetree()
    if tree["m_Name"] != "Combined":
        continue
    for class_id, ptr in tree["m_Component"]:
        if class_id == 64:
            mesh_collider_pathids.append(ptr["m_PathID"])

env2 = UnityPy.load(SRC)
sf = env2.file
raw = bytearray(open(SRC, "rb").read())

target_set = set(mesh_collider_pathids)
patched = 0
failures = []

for pid in target_set:
    obj = sf.objects[pid]
    off = obj.byte_start
    size = obj.byte_size
    window = bytes(raw[off:off+size])

    tree = obj.read_typetree()
    mesh_pathid = tree["m_Mesh"]["m_PathID"]
    if mesh_pathid == 0:
        continue

    aligned_matches = []
    for local_off in range(0, size - 3, 4):
        val = struct.unpack_from("<i", window, local_off)[0]
        if val == mesh_pathid:
            aligned_matches.append(local_off)

    if len(aligned_matches) != 1:
        failures.append((pid, off, size, mesh_pathid, aligned_matches, window.hex()))
        continue

    real_off = off + aligned_matches[0]
    struct.pack_into("<i", raw, real_off, 0)
    patched += 1

print(f"patched {patched} of {len(target_set)}, failures: {len(failures)}")
for f in failures[:10]:
    print(f)

if not failures:
    with open(DST, "wb") as f:
        f.write(raw)
    print("wrote", DST)
else:
    print("NOT writing file — resolve failures first")
