"""Render Ahu/Yaman portrait stills from Ready Player Me GLBs via Blender."""
import bpy
import math
from pathlib import Path

OUT_DIR = Path(r"D:\LongLake\LongLake\Assets\Art\UI\Portraits")
OUT_DIR.mkdir(parents=True, exist_ok=True)

JOBS = [
    {
        "name": "Ahu",
        "glb": r"D:\LongLake\LongLake\Assets\ReadyPlayerMe\Avatars\Ahu\2fac66e374c947c41bc74325c6e3d934\69503453403c000063195f31.glb",
        "bg": (0.10, 0.07, 0.08, 1.0),
    },
    {
        "name": "Yaman",
        "glb": r"D:\LongLake\LongLake\Assets\ReadyPlayerMe\Avatars\Yaman\2fac66e374c947c41bc74325c6e3d934\695139d3220569853f86a35f.glb",
        "bg": (0.06, 0.09, 0.11, 1.0),
    },
]


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in bpy.data.meshes:
        bpy.data.meshes.remove(block)
    for block in bpy.data.materials:
        bpy.data.materials.remove(block)
    for block in bpy.data.images:
        bpy.data.images.remove(block)
    for block in bpy.data.armatures:
        bpy.data.armatures.remove(block)


def bounds_world(obj):
    corners = [obj.matrix_world @ v.co for v in obj.data.vertices] if obj.type == "MESH" else []
    return corners


def scene_bounds():
    coords = []
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH":
            continue
        for v in obj.data.vertices:
            coords.append(obj.matrix_world @ v.co)
    if not coords:
        return None
    xs = [c.x for c in coords]
    ys = [c.y for c in coords]
    zs = [c.z for c in coords]
    min_v = (min(xs), min(ys), min(zs))
    max_v = (max(xs), max(ys), max(zs))
    center = ((min_v[0] + max_v[0]) * 0.5, (min_v[1] + max_v[1]) * 0.5, (min_v[2] + max_v[2]) * 0.5)
    size = (max_v[0] - min_v[0], max_v[1] - min_v[1], max_v[2] - min_v[2])
    return center, size, min_v, max_v


def find_head_focus(center, size, min_v, max_v):
    # Prefer bone named Head if armature present
    for obj in bpy.context.scene.objects:
        if obj.type == "ARMATURE":
            for bone in obj.pose.bones:
                if bone.name.lower() in ("head", "headtop_end", "mixamorig:head"):
                    return obj.matrix_world @ bone.head
    # Fallback: upper chest/head region
    return (
        center[0],
        center[1],
        min_v[2] + size[2] * 0.82,
    )


def setup_camera(focus, bg):
    cam_data = bpy.data.cameras.new("PortraitCam")
    cam_data.lens = 85
    cam_data.clip_start = 0.05
    cam_data.clip_end = 50
    cam = bpy.data.objects.new("PortraitCam", cam_data)
    bpy.context.collection.objects.link(cam)

    # RPM avatars usually Y-up in glTF → Blender converts to Z-up
    cam.location = (focus[0] + 0.22, focus[1] - 1.05, focus[2] + 0.05)
    direction = (
        focus[0] - cam.location.x,
        focus[1] - cam.location.y,
        focus[2] - cam.location.z,
    )
    # Point camera at focus
    rot_quat = direction_to_quat(direction)
    cam.rotation_euler = rot_quat.to_euler()

    bpy.context.scene.camera = cam

    world = bpy.data.worlds.new("PortraitWorld")
    bpy.context.scene.world = world
    world.use_nodes = True
    nodes = world.node_tree.nodes
    links = world.node_tree.links
    nodes.clear()
    bg_node = nodes.new(type="ShaderNodeBackground")
    bg_node.inputs[0].default_value = bg
    bg_node.inputs[1].default_value = 1.0
    out = nodes.new(type="ShaderNodeOutputWorld")
    links.new(bg_node.outputs[0], out.inputs[0])

    # Lights
    key = bpy.data.lights.new("Key", type="AREA")
    key.energy = 250
    key.size = 1.2
    key_obj = bpy.data.objects.new("Key", key)
    bpy.context.collection.objects.link(key_obj)
    key_obj.location = (focus[0] + 1.2, focus[1] - 0.8, focus[2] + 1.0)
    key_obj.rotation_euler = (math.radians(35), 0, math.radians(40))

    fill = bpy.data.lights.new("Fill", type="AREA")
    fill.energy = 80
    fill.size = 1.5
    fill_obj = bpy.data.objects.new("Fill", fill)
    bpy.context.collection.objects.link(fill_obj)
    fill_obj.location = (focus[0] - 1.0, focus[1] - 0.6, focus[2] + 0.4)

    return cam


def direction_to_quat(direction):
    import mathutils
    d = mathutils.Vector(direction)
    if d.length < 1e-6:
        return mathutils.Quaternion()
    d.normalize()
    # Camera looks down -Z
    return d.to_track_quat("-Z", "Y")


def render_job(job):
    clear_scene()
    bpy.ops.import_scene.gltf(filepath=job["glb"])

    bb = scene_bounds()
    if bb is None:
        print("No mesh for", job["name"])
        return
    center, size, min_v, max_v = bb
    focus = find_head_focus(center, size, min_v, max_v)
    # mathutils Vector or tuple
    if hasattr(focus, "x"):
        focus_t = (focus.x, focus.y, focus.z)
    else:
        focus_t = focus

    setup_camera(focus_t, job["bg"])

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items.keys() else "BLENDER_EEVEE"
    # Fallback
    try:
        scene.render.engine = "BLENDER_EEVEE_NEXT"
    except Exception:
        scene.render.engine = "CYCLES"
        scene.cycles.samples = 32

    scene.render.resolution_x = 768
    scene.render.resolution_y = 768
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.film_transparent = False

    out = OUT_DIR / f"{job['name']}.png"
    scene.render.filepath = str(out)
    bpy.ops.render.render(write_still=True)
    print("Wrote", out)


def main():
    for job in JOBS:
        print("=== Rendering", job["name"], "===")
        render_job(job)
    print("DONE")


if __name__ == "__main__":
    main()
