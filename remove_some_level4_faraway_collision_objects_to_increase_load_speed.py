import os
import struct

import UnityPy

if not os.path.isfile("mathbreakers/Mathbreakers_Data/level3.bak"):
  import shutil

  _ = shutil.copy(src="mathbreakers/Mathbreakers_Data/level3", dst="mathbreakers/Mathbreakers_Data/level3.bak")

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


SRC = "mathbreakers/Mathbreakers_Data/level3.a"
DST = "mathbreakers/Mathbreakers_Data/level3"

target_parent_x = -840.5

env = UnityPy.load(SRC)
sf = env.file


def get_parent_local_pos(transform_pathid):
  if transform_pathid not in sf.objects:
    return None

  t = sf.objects[transform_pathid].read_typetree()
  parent_ptr = t["m_Father"]
  if parent_ptr["m_PathID"] == 0:
    return None

  p = sf.objects[parent_ptr["m_PathID"]].read_typetree()["m_LocalPosition"]
  return p


renderer_pathids = []

# Find all GameObjects named "Combined" matching the target parent X coordinate
for obj in env.objects:
  if obj.type.name != "GameObject":
    continue

  tree = obj.read_typetree()
  if tree.get("m_Name") != "Combined":
    continue

  transform_pathid = None
  mesh_renderer_pathid = None

  # Iterate over components attached to the GameObject
  # Component tuple format can be (class_id, ptr) or structured dict depending on Unity version
  for comp in tree["m_Component"]:
    # Unwrap component pointer
    ptr = comp[1] if isinstance(comp, tuple) else comp["component"]
    class_id = comp[0] if isinstance(comp, tuple) else comp.get("classID", 0)

    # Class ID 4 = Transform, Class ID 23 = MeshRenderer
    if class_id == 4:
      transform_pathid = ptr["m_PathID"]
    elif class_id == 23: # MeshRenderer
      mesh_renderer_pathid = ptr["m_PathID"]


  if transform_pathid is None or mesh_renderer_pathid is None:
    continue

  parent_pos = get_parent_local_pos(transform_pathid)
  if parent_pos and abs(parent_pos["x"] - target_parent_x) < 0.1:
    renderer_pathids.append(mesh_renderer_pathid)


print(f"Matched {len(renderer_pathids)} MeshRenderers with parent x={target_parent_x}")

# Modify the TypeTree directly using UnityPy and save
patched = 0
for pid in renderer_pathids:
  if pid in sf.objects:
    obj = sf.objects[pid]
    tree = obj.read_typetree()

    # Set enabled state to False to hide rendering
    tree["m_Enabled"] = False

    # Save modifications back to the object
    obj.save_typetree(tree)
    patched += 1


print(f"Patched {patched} MeshRenderers.")

# Save modified asset bundle / level file
with open(DST, "wb") as f:
  f.write(env.file.save())

os.remove("mathbreakers/Mathbreakers_Data/level3.a")
print("Successfully saved modified file to:", DST)
