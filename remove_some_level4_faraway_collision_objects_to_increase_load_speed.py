import struct
import UnityPy

SRC = "mathbreakers/Mathbreakers_Data/level3.bak"
DST = "mathbreakers/Mathbreakers_Data/level3.a"

env1 = UnityPy.load(SRC)
sf1 = env1.file

target_parent_x = -840.5


def get_parent_local_pos(transform_pathid):
  t = sf1.objects[transform_pathid].read_typetree()
  parent_ptr = t["m_Father"]
  if parent_ptr["m_PathID"] == 0:
    return None

  p = sf1.objects[parent_ptr["m_PathID"]].read_typetree()["m_LocalPosition"]
  return p


mesh_collider_pathids = []
for obj in env1.objects:
  if obj.type.name != "GameObject":
    continue

  tree = obj.read_typetree()
  if tree["m_Name"] != "Combined":
    continue

  transform_pathid = None
  mc_pathid = None
  for class_id, ptr in tree["m_Component"]:
    if class_id == 4:
      transform_pathid = ptr["m_PathID"]

    if class_id == 64:
      mc_pathid = ptr["m_PathID"]


  if transform_pathid is None or mc_pathid is None:
    continue

  parent_pos = get_parent_local_pos(transform_pathid)
  if parent_pos is None:
    continue

  if abs(parent_pos["x"] - target_parent_x) < 0.1:
    mesh_collider_pathids.append(mc_pathid)


  # print(parent_pos["x"])
print(f"matched {len(mesh_collider_pathids)} MeshColliders with parent x={target_parent_x}")

env2 = UnityPy.load(SRC)
sf2 = env2.file
raw = bytearray(open(SRC, "rb").read())

target_set = set(mesh_collider_pathids)
patched = 0
failures = []

for pid in target_set:
  obj = sf2.objects[pid]
  off = obj.byte_start
  size = obj.byte_size
  window = bytes(raw[off : off + size])

  tree = obj.read_typetree()
  mesh_pathid = tree["m_Mesh"]["m_PathID"]
  if mesh_pathid == 0:
    continue

  aligned_matches = [lo for lo in range(0, size - 3, 4) if struct.unpack_from("<i", window, lo)[0] == mesh_pathid]

  if len(aligned_matches) != 1:
    failures.append((pid, aligned_matches))
    continue

  struct.pack_into("<i", raw, off + aligned_matches[0], 0)
  patched += 1

print(f"patched {patched} of {len(target_set)}, failures: {failures}")

if not failures:
  with open(DST, "wb") as f:
    f.write(raw)

  print("wrote", DST)
