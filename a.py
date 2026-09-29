import UnityPy

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

print("Successfully saved modified file to:", DST)
