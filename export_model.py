# Exports the armature's skeleton and all skinned meshes to player.skin
# and creates a single baked albedo atlas: player.png
#
# The resulting files are:
#
#   player.skin
#   player.png
#
# player.skin keeps the SAME format expected by CustomModel.cs:
#
#   space <model|blender>
#   bone<TAB><name><TAB><parent|-><TAB><x><TAB><y><TAB><z>
#   v <x> <y> <z> <nx> <ny> <nz> <u> <v> <bone0> <w0> ...
#   t <a> <b> <c>
#
# The only intentional change is that the UVs written to player.skin
# are packed atlas UVs corresponding to player.png.
#
# Usage:
#
#   blender --background --python export_model.py -- dragon.glb
#
# Or run it the same way as the old exporter.
#
# Requirements:
#   - exactly one armature
#   - every exported mesh has an Armature modifier pointing to that armature
#   - no modifiers other than Armature
#   - every exported mesh has a UV map
#   - every vertex has at least one valid bone weight
#
# The script:
#   1. Imports the glTF if supplied after "--".
#   2. Finds the armature and skinned meshes.
#   3. Makes a temporary copy of the scene geometry.
#   4. Joins the skinned meshes into one temporary mesh.
#   5. Creates a new atlas UV map.
#   6. Packs the UV islands.
#   7. Bakes the original material colors into player.png.
#   8. Exports the packed geometry using the existing .skin format.
#
# IMPORTANT:
# This creates an ALBEDO atlas only.
# Normal/emission/metallic maps are not baked by this script yet.

import sys
import os
import bpy
from mathutils import Matrix


# ============================================================
# CONFIGURATION
# ============================================================

OUT_DIR = os.path.dirname(
    os.path.abspath(__file__)
)


SKIN_OUT = os.path.join(OUT_DIR, "player.skin")
PNG_OUT = os.path.join(OUT_DIR, "player.png")

ATLAS_SIZE = 2048

# Pixels of padding around UV islands.
# 8-16 is usually good for a 2048 texture.
UV_MARGIN_PIXELS = 12

# Blender bake margin.
BAKE_MARGIN_PIXELS = 16

# Name of the temporary atlas UV map.
ATLAS_UV_NAME = "__PLAYER_ATLAS__"


# ============================================================
# IMPORT GLTF
# ============================================================

if "--" in sys.argv:
  args = sys.argv[sys.argv.index("--") + 1 :]

  if not args:
    raise Exception("expected a glTF filepath after '--'")

  gltf_path = args[0]

  print("Importing:", gltf_path)

  bpy.ops.import_scene.gltf(filepath=gltf_path)


# ============================================================
# FIND ARMATURE
# ============================================================

armatures = [o for o in bpy.data.objects if o.type == "ARMATURE"]

if len(armatures) != 1:
  raise Exception("expected exactly one armature, found %d" % len(armatures))

arm = armatures[0]
bones = arm.data.bones

print("Armature:", arm.name)


# ============================================================
# AXIS SETUP
# ============================================================

# KEEP IDENTICAL to the original exporter.

if "arm_right" in bones and "arm_left" in bones and "head" in bones and "body" in bones:
  side = (bones["arm_right"].head_local - bones["arm_left"].head_local).normalized()

  up = bones["head"].head_local - bones["body"].head_local

  up = (up - side * up.dot(side)).normalized()

  forward = -side.cross(up)

  A = Matrix((side, up, forward))

  space = "model"

else:
  A = Matrix.Identity(3)
  space = "blender"


# ============================================================
# BONE INDICES
# ============================================================

bone_index = {}

for i, b in enumerate(bones):
  if "\t" in b.name:
    raise Exception("bone name '%s' contains a tab" % b.name)

  bone_index[b.name] = i


# ============================================================
# FIND SKINNED MESHES
# ============================================================

meshes = []

for o in bpy.data.objects:
  if o.type != "MESH":
    continue

  armature_modifiers = [m for m in o.modifiers if m.type == "ARMATURE" and m.object == arm]

  if not armature_modifiers:
    continue

  for m in o.modifiers:
    if m.type != "ARMATURE":
      raise Exception("%s has a %s modifier; apply it first" % (o.name, m.type))


  mesh = o.data

  if mesh.uv_layers.active is None:
    raise Exception("%s has no UV map" % o.name)

  meshes.append(o)


if not meshes:
  raise Exception("no mesh has an Armature modifier pointing at %s" % arm.name)


print("Found %d skinned mesh(es):" % len(meshes))

for o in meshes:
  print("  ", o.name)


# ============================================================
# CHECK MATERIALS
# ============================================================

for obj in meshes:
  if len(obj.material_slots) == 0:
    raise Exception("%s has no material" % obj.name)

  for slot in obj.material_slots:
    if slot.material is None:
      raise Exception("%s has an empty material slot" % obj.name)



# ============================================================
# SAVE ORIGINAL SELECTION / MODE
# ============================================================

if bpy.context.object is not None:
  try:
    bpy.ops.object.mode_set(mode="OBJECT")

  except:
    pass


old_selected = list(bpy.context.selected_objects)
old_active = bpy.context.view_layer.objects.active


# ============================================================
# DUPLICATE MESHES FOR ATLAS CREATION
# ============================================================

duplicates = []

for source in meshes:
  dup = source.copy()
  dup.data = source.data.copy()

  bpy.context.collection.objects.link(dup)

  dup.name = "__ATLAS_" + source.name

  duplicates.append(dup)


# ============================================================
# SELECT DUPLICATES
# ============================================================

bpy.ops.object.select_all(action="DESELECT")

for obj in duplicates:
  obj.select_set(True)

bpy.context.view_layer.objects.active = duplicates[0]


# ============================================================
# CREATE ATLAS UV LAYER
# ============================================================

for obj in duplicates:
  mesh = obj.data

  # Remove an old temporary atlas layer if present.
  if ATLAS_UV_NAME in mesh.uv_layers:
    mesh.uv_layers.remove(mesh.uv_layers[ATLAS_UV_NAME])

  atlas_uv = mesh.uv_layers.new(name=ATLAS_UV_NAME)

  # Copy the original UVs.
  original_uv = mesh.uv_layers.active

  for i in range(len(atlas_uv.data)):
    atlas_uv.data[i].uv = original_uv.data[i].uv

  mesh.uv_layers.active = mesh.uv_layers[ATLAS_UV_NAME]


# ============================================================
# PACK UV ISLANDS
# ============================================================

print("Packing UV islands...")

# Blender's pack operation works on selected mesh objects.
#
# Enter Edit Mode so the UV operator can pack all islands.

bpy.ops.object.mode_set(mode="EDIT")

bpy.ops.mesh.select_all(action="SELECT")

bpy.ops.uv.select_all(action="SELECT")

try:
  bpy.ops.uv.pack_islands(rotate=True, margin=UV_MARGIN_PIXELS / float(ATLAS_SIZE))

except TypeError:
  # Compatibility fallback for Blender versions where
  # the pack_islands signature differs.

  bpy.ops.uv.pack_islands(rotate=True)

bpy.ops.object.mode_set(mode="OBJECT")


# ============================================================
# CREATE BAKE IMAGE
# ============================================================

print("Creating %dx%d atlas..." % (ATLAS_SIZE, ATLAS_SIZE))

if "PLAYER_ALBEDO_ATLAS" in bpy.data.images:
  atlas = bpy.data.images["PLAYER_ALBEDO_ATLAS"]

  # Do not reuse an image of the wrong size.
  if atlas.size[0] != ATLAS_SIZE or atlas.size[1] != ATLAS_SIZE:
    bpy.data.images.remove(atlas)
    atlas = None

else:
  atlas = None


if atlas is None:
  atlas = bpy.data.images.new(name="PLAYER_ALBEDO_ATLAS", width=ATLAS_SIZE, height=ATLAS_SIZE, alpha=True)


# ============================================================
# PREPARE MATERIAL IMAGE NODES
# ============================================================

# Baking writes into the image associated with the ACTIVE
# Image Texture node.
#
# Every material used by the source meshes gets an image
# texture node pointing at the same atlas.
#
# The node is NOT made the material's visible color input.
# It is only the baking target.

bake_nodes = []

for obj in duplicates:
  for slot in obj.material_slots:
    mat = slot.material

    if mat is None:
      continue

    mat.use_nodes = True

    nodes = mat.node_tree.nodes

    # Remove an old temporary node.
    old = nodes.get("__PLAYER_BAKE_TARGET__")

    if old:
      nodes.remove(old)

    node = nodes.new("ShaderNodeTexImage")

    node.name = "__PLAYER_BAKE_TARGET__"
    node.label = "__PLAYER_BAKE_TARGET__"

    node.image = atlas

    # IMPORTANT:
    # Selecting this node tells Blender where to bake.
    for n in nodes:
      n.select = False

    node.select = True
    nodes.active = node

    bake_nodes.append(node)


# ============================================================
# PREPARE RENDER ENGINE
# ============================================================

scene = bpy.context.scene

old_engine = scene.render.engine

# Cycles is the safest baking engine for material baking.
#
# New Blender versions use:
#   'CYCLES'
#
# If unavailable, fall back to the current engine.

try:
  scene.render.engine = "CYCLES"

except:
  print("WARNING: Could not switch render engine to CYCLES.")


# ============================================================
# SELECT DUPLICATES
# ============================================================

bpy.ops.object.select_all(action="DESELECT")

for obj in duplicates:
  obj.select_set(True)

bpy.context.view_layer.objects.active = duplicates[0]


# ============================================================
# BAKE DIFFUSE COLOR
# ============================================================

print("Baking albedo...")

try:
  bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"}, use_clear=True, margin=BAKE_MARGIN_PIXELS, target="IMAGE_TEXTURES", uv_layer=ATLAS_UV_NAME)

except TypeError:
  # Some Blender releases don't accept uv_layer.
  bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"}, use_clear=True, margin=BAKE_MARGIN_PIXELS, target="IMAGE_TEXTURES")


# ============================================================
# SAVE PLAYER.PNG
# ============================================================

print("Saving:", PNG_OUT)

atlas.filepath_raw = os.path.abspath(PNG_OUT)
atlas.file_format = "PNG"

print("Saving PNG to:", atlas.filepath_raw)

atlas.save()


print("Wrote player.png (%dx%d)" % (atlas.size[0], atlas.size[1]))


# ============================================================
# REMOVE BAKE NODES
# ============================================================

for obj in duplicates:
  for slot in obj.material_slots:
    mat = slot.material

    if mat is None:
      continue

    nodes = mat.node_tree.nodes

    node = nodes.get("__PLAYER_BAKE_TARGET__")

    if node:
      nodes.remove(node)



# ============================================================
# BUILD EXPORT DATA
# ============================================================


def influences(obj, vert, group_bone):

  ws = [(group_bone[g.group], g.weight) for g in vert.groups if g.group in group_bone and g.weight > 0]

  if not ws:
    raise Exception("vertex %d of %s has no bone weights" % (vert.index, obj.name))

  ws.sort(key=lambda t: -t[1])

  ws = ws[:4]

  total = sum(w for _, w in ws)

  ws = [(bone_index[name], w / total) for name, w in ws]

  return ws + [(0, 0.0) for _ in range(4 - len(ws))]


# ============================================================
# WRITE PLAYER.SKIN
# ============================================================

print("Writing:", SKIN_OUT)

with open(SKIN_OUT, "w") as f:
  f.write("# generated by export_model.py\n")

  f.write("space %s\n" % space)

  # --------------------------------------------------------
  # BONES
  # --------------------------------------------------------

  for b in bones:
    p = A @ b.head_local

    f.write("bone\t%s\t%s\t%.6f\t%.6f\t%.6f\n" % (b.name, b.parent.name if b.parent else "-", p.x, p.y, p.z))

  # --------------------------------------------------------
  # MESHES
  # --------------------------------------------------------

  base = 0

  for obj in duplicates:
    mesh = obj.data

    mesh.calc_loop_triangles()

    # Find atlas UV layer.
    uv_layer = mesh.uv_layers.get(ATLAS_UV_NAME)

    if uv_layer is None:
      raise Exception("%s has no atlas UV map" % obj.name)

    group_bone = {g.index: g.name for g in obj.vertex_groups if g.name in bone_index}

    to_arm = arm.matrix_world.inverted() @ obj.matrix_world

    rot = to_arm.to_3x3()

    weights = {}

    written = {}

    count = 0

    # ----------------------------------------------------
    # TRIANGLES
    # ----------------------------------------------------

    for tri in mesh.loop_triangles:
      poly = mesh.polygons[tri.polygon_index]

      ids = []

      for loop_index, vi in zip(tri.loops, tri.vertices):
        vert = mesh.vertices[vi]

        # IMPORTANT:
        # Use the NEW atlas UV.
        uv = uv_layer.data[loop_index].uv

        normal = vert.normal if poly.use_smooth else poly.normal

        key = (vi, round(uv.x, 5), round(uv.y, 5), None if poly.use_smooth else tuple(round(c, 4) for c in normal))

        if key not in written:
          if vi not in weights:
            weights[vi] = influences(obj, vert, group_bone)

          p = A @ (to_arm @ vert.co)

          n = (A @ (rot @ normal)).normalized()

          line = "v %.6f %.6f %.6f %.5f %.5f %.5f %.5f %.5f" % (p.x, p.y, p.z, n.x, n.y, n.z, uv.x, uv.y)

          for b, w in weights[vi]:
            line += " %d %.5f" % (b, w)

          f.write(line + "\n")

          written[key] = base + count

          count += 1

        ids.append(written[key])

      # CustomModel.cs reverses winding.
      f.write("t %d %d %d\n" % tuple(ids))

    base += count


# ============================================================
# CLEAN TEMPORARY OBJECTS
# ============================================================

print("Cleaning temporary atlas objects...")

for obj in duplicates:
  bpy.data.objects.remove(obj, do_unlink=True)


# ============================================================
# RESTORE RENDER ENGINE
# ============================================================

try:
  scene.render.engine = old_engine

except:
  pass


# ============================================================
# RESTORE ORIGINAL SELECTION
# ============================================================

bpy.ops.object.select_all(action="DESELECT")

for obj in old_selected:
  if obj and obj.name in bpy.data.objects:
    try:
      obj.select_set(True)

    except:
      pass



try:
  bpy.context.view_layer.objects.active = old_active

except:
  pass


# ============================================================
# DONE
# ============================================================

print("")
print("==========================================")
print("EXPORT COMPLETE")
print("==========================================")
print("Skin:  ", SKIN_OUT)
print("Image: ", PNG_OUT)
print("Size:  ", ATLAS_SIZE, "x", ATLAS_SIZE)
print("==========================================")
