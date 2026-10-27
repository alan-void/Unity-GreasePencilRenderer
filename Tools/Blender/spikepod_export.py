"""
Spike Pod -> Unity exporter.

Run headless (never touches the source .blend; all edits happen in a saved copy):

    blender -b <path>/spikePod-spiral-crown.blend --python Tools/Blender/spikepod_export.py -- <UnityProject>/Assets/SpikePod/Art

Produces in the output folder:
    SpikePod.fbx             meshes (GN evaluated), camera and NPR lights
    spikepod_manifest.json   material / light / outline parameters read from the node data
    AgX_LUT.png              32^3 strip LUT reproducing Blender's AgX view transform (URP Color Lookup)

Baked per-vertex streams (Unity can't reproduce these):
    COLOR      r = AO_vertex (crown spikes only; white elsewhere)
    UV1 (xy)   line_normal.xy   (smooth vertex normal; crown is flat shaded)
    UV2 (xy)   line_normal.z, radius_var (outline radius wobble from Blender's Noise Texture)
    UV3 (x)    polygon index (edge-outlined meshes only; separates real edges from triangulation diagonals)
"""

import json
import math
import os
import sys

import bpy
import bmesh
from mathutils import Vector

# ----------------------------------------------------------------------------------------------
# Configuration: which Blender objects make up the asset
# ----------------------------------------------------------------------------------------------

PARTS = [
    # export name,    source object,                     outline object (or None),     material
    ("SP_Crown",      "spike_spiral",                    "Spiral Crown - Outline",     "spike"),
    ("SP_CrownBase",  "spike_base_viz",                  None,                         "spike_base"),
    ("SP_Stalk",      "Spiral Stalk Procedural Curve",   "Spiral Stalk - Outline",     "MAT_Spiral_Stalk"),
    ("SP_Pod",        "Pink Pod Crown",                  "Pink Pod Crown - Outline",   "MAT_Pod_Pink"),
]
# The crown outline reads the (hidden) GN instancer; its realized geometry equals the baked mesh.
OUTLINE_SOURCE_ALIASES = {"spike_spiral_mod": "SP_Crown"}

CAMERA = ("SP_Camera", "Crown Preview Camera")
LIGHTS = [("SP_KeyLight", "NPR Key", "key"), ("SP_RimLight", "NPR Rim", "rim")]

EXPORT_COLLECTION = "unity_export"
COPY_SUFFIX = ".unity-export.blend"
LUT_SIZE = 32
CURVE_SAMPLES = 256


# ----------------------------------------------------------------------------------------------
# Helpers
# ----------------------------------------------------------------------------------------------

def log(*args):
    print("[spikepod_export]", *args)


def to_json_value(v):
    if v is None:
        return None
    if isinstance(v, (bool, int, str)):
        return v
    if isinstance(v, float):
        return round(v, 6)
    if hasattr(v, "name") and isinstance(getattr(v, "name"), str):
        return v.name
    try:
        return [to_json_value(x) for x in v]
    except TypeError:
        return str(v)


def modifier_inputs(mod):
    """Geometry Nodes modifier input values keyed by interface socket name (Blender 5.x API)."""
    values = {}
    inputs = mod.properties.inputs
    for item in mod.node_group.interface.items_tree:
        if getattr(item, "in_out", None) != "INPUT" or item.socket_type == "NodeSocketGeometry":
            continue
        sock = getattr(inputs, item.identifier, None)
        values[item.name] = to_json_value(sock.value) if sock is not None else None
    return values


def ensure_collection(name):
    coll = bpy.data.collections.get(name)
    if coll is None:
        coll = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(coll)
    return coll


def find_group_node(node_tree, group_name):
    return next((n for n in node_tree.nodes
                 if n.type == "GROUP" and n.node_tree and n.node_tree.name == group_name), None)


# ----------------------------------------------------------------------------------------------
# Geometry baking
# ----------------------------------------------------------------------------------------------

def bake_evaluated_mesh(export_name, source_obj, collection):
    """New mesh object holding the fully evaluated (modifiers / GN applied) geometry of source_obj."""
    dg = bpy.context.evaluated_depsgraph_get()
    evaluated = source_obj.evaluated_get(dg)
    mesh = bpy.data.meshes.new_from_object(evaluated, preserve_all_data_layers=True, depsgraph=dg)
    mesh.name = export_name
    obj = bpy.data.objects.new(export_name, mesh)
    obj.matrix_world = source_obj.matrix_world.copy()
    collection.objects.link(obj)
    if not mesh.materials and source_obj.material_slots:
        for slot in source_obj.material_slots:
            mesh.materials.append(slot.material)
    return obj


def noise_node_settings(outline_obj):
    """Noise Texture node of the outline GN group (copied so the bake uses identical settings)."""
    for mod in outline_obj.modifiers:
        if mod.type != "NODES" or mod.node_group is None:
            continue
        node = next((n for n in mod.node_group.nodes if n.type == "TEX_NOISE"), None)
        if node is not None:
            return node
    return None


def sample_noise_factor(world_positions, scale, template):
    """Evaluate Blender's Noise Texture 'Factor' at world positions (exactly as the GN outline does)."""
    mesh = bpy.data.meshes.new("_noise_probe")
    mesh.vertices.add(len(world_positions))
    mesh.vertices.foreach_set("co", [c for p in world_positions for c in p])
    probe = bpy.data.objects.new("_noise_probe", mesh)
    bpy.context.scene.collection.objects.link(probe)

    tree = bpy.data.node_groups.new("_noise_probe_tree", "GeometryNodeTree")
    tree.interface.new_socket("Geometry", in_out="INPUT", socket_type="NodeSocketGeometry")
    tree.interface.new_socket("Geometry", in_out="OUTPUT", socket_type="NodeSocketGeometry")
    n_in = tree.nodes.new("NodeGroupInput")
    n_out = tree.nodes.new("NodeGroupOutput")
    noise = tree.nodes.new("ShaderNodeTexNoise")
    if template is not None:
        for prop in ("noise_dimensions", "noise_type", "normalize"):
            if hasattr(template, prop):
                setattr(noise, prop, getattr(template, prop))
        for sock in template.inputs:
            if sock.name in noise.inputs and hasattr(sock, "default_value") and not sock.is_linked:
                try:
                    noise.inputs[sock.name].default_value = sock.default_value
                except (TypeError, AttributeError):
                    pass
    noise.inputs["Scale"].default_value = scale
    store = tree.nodes.new("GeometryNodeStoreNamedAttribute")
    store.data_type = "FLOAT"
    store.domain = "POINT"
    store.inputs["Name"].default_value = "noise_fac"
    tree.links.new(n_in.outputs[0], store.inputs["Geometry"])
    tree.links.new(noise.outputs["Fac"] if "Fac" in noise.outputs else noise.outputs[0], store.inputs["Value"])
    tree.links.new(store.outputs[0], n_out.inputs[0])

    mod = probe.modifiers.new("noise", "NODES")
    mod.node_group = tree
    dg = bpy.context.evaluated_depsgraph_get()
    ev = probe.evaluated_get(dg)
    ev_mesh = ev.to_mesh()
    attr = ev_mesh.attributes["noise_fac"]
    values = [d.value for d in attr.data]
    ev.to_mesh_clear()

    bpy.data.objects.remove(probe)
    bpy.data.meshes.remove(mesh)
    bpy.data.node_groups.remove(tree)
    return values


def write_vertex_streams(obj, outline_obj, outline_params, polygon_ids=False):
    """AO -> color, line_normal + radius_var -> UV1/UV2, optional polygon index -> UV3.

    The polygon index lets Unity tell real polygon edges from its own triangulation diagonals, which edge
    based outlines (Silhouette Edges) must ignore. It splits shared vertices per polygon, so it is only
    written for meshes outlined by edges (those are flat shaded and split per polygon anyway)."""
    mesh = obj.data
    n_verts = len(mesh.vertices)

    # radius_var = 1 + V * (2 * noise(P_world * S) - 1); 1 when the outline has no variation input.
    variation = (outline_params or {}).get("Radius Variation")
    scale = (outline_params or {}).get("Variation Scale")
    if variation is not None and scale is not None and outline_obj is not None:
        world = [obj.matrix_world @ v.co for v in mesh.vertices]
        fac = sample_noise_factor([tuple(p) for p in world], scale, noise_node_settings(outline_obj))
        radius_var = [1.0 + variation * (2.0 * f - 1.0) for f in fac]
    else:
        radius_var = [1.0] * n_verts

    normals = [v.vector.copy() for v in mesh.vertex_normals]

    # UV0 stays the authored UVMap (create one if missing so channel indices are stable).
    while len(mesh.uv_layers) > 1:
        mesh.uv_layers.remove(mesh.uv_layers[-1])
    if len(mesh.uv_layers) == 0:
        mesh.uv_layers.new(name="UVMap")
    uv1 = mesh.uv_layers.new(name="line_normal_xy")
    uv2 = mesh.uv_layers.new(name="line_normal_z_radius_var")
    for loop in mesh.loops:
        vi = loop.vertex_index
        n = normals[vi]
        uv1.data[loop.index].uv = (n.x, n.y)
        uv2.data[loop.index].uv = (n.z, radius_var[vi])

    if polygon_ids:
        uv3 = mesh.uv_layers.new(name="polygon_id")
        for poly in mesh.polygons:
            for li in poly.loop_indices:
                uv3.data[li].uv = (float(poly.index), 0.0)

    # Single color attribute: AO_vertex if present, white otherwise.
    ao = mesh.color_attributes.get("AO_vertex")
    ao_values = [tuple(d.color) for d in ao.data] if ao is not None and ao.domain == "POINT" else None
    for attr in list(mesh.color_attributes):
        mesh.color_attributes.remove(attr)
    col = mesh.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    for i, d in enumerate(col.data):
        d.color = ao_values[i] if ao_values else (1.0, 1.0, 1.0, 1.0)
    mesh.color_attributes.active_color = col
    mesh.color_attributes.render_color_index = 0

    return {
        "radiusVarMin": round(min(radius_var), 4),
        "radiusVarMax": round(max(radius_var), 4),
        "hasAo": ao_values is not None,
    }


# ----------------------------------------------------------------------------------------------
# Manifest extraction
# ----------------------------------------------------------------------------------------------

def toon_params(material):
    node = find_group_node(material.node_tree, "NPR • Toon")
    params = {}
    if node is not None:
        for sock in node.inputs:
            if hasattr(sock, "default_value") and not sock.is_linked and sock.name != "Normal":
                params[sock.name] = to_json_value(sock.default_value)
    result = {"name": material.name, "toon": params}

    # Optional AO mask: Mix(A = dark colour, B = toon colour, Factor = ConstantRamp(attribute)).
    for mix in material.node_tree.nodes:
        if mix.type != "MIX" or mix.data_type != "RGBA":
            continue
        fac = mix.inputs["Factor"]
        if not fac.is_linked or not mix.outputs["Result"].is_linked:
            continue
        ramp = fac.links[0].from_node
        if ramp.type != "VALTORGB" or not ramp.inputs[0].is_linked:
            continue
        attr = ramp.inputs[0].links[0].from_node
        if attr.type != "ATTRIBUTE":
            continue
        threshold = next((e.position for e in ramp.color_ramp.elements if e.color[0] > 0.5), 0.5)
        result["ao_mask"] = {
            "attribute": attr.attribute_name,
            "threshold": round(threshold, 6),
            "dark_color": to_json_value(mix.inputs[6].default_value),  # A (RGBA)
        }
    return result


def gp_material_params(material):
    gp = material.grease_pencil
    return {"name": material.name, "color": to_json_value(gp.color)}


def sample_profile_curve(curve_obj):
    """Y(x) of a 2D profile curve sampled at CURVE_SAMPLES evenly spaced x in [0,1]."""
    if curve_obj is None:
        return None
    dg = bpy.context.evaluated_depsgraph_get()
    ev = curve_obj.evaluated_get(dg)
    mesh = ev.to_mesh()
    pts = sorted(((v.co.x, v.co.y) for v in mesh.vertices), key=lambda p: p[0])
    ev.to_mesh_clear()
    out = []
    for i in range(CURVE_SAMPLES):
        x = i / (CURVE_SAMPLES - 1)
        if x <= pts[0][0]:
            out.append(pts[0][1])
            continue
        if x >= pts[-1][0]:
            out.append(pts[-1][1])
            continue
        for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
            if x0 <= x <= x1:
                t = 0.0 if x1 == x0 else (x - x0) / (x1 - x0)
                out.append(y0 + (y1 - y0) * t)
                break
    return [round(y, 6) for y in out]


def outline_params(outline_obj):
    stack = []
    curves = {}
    for mod in outline_obj.modifiers:
        if mod.type != "NODES" or mod.node_group is None or not mod.show_render:
            continue
        values = modifier_inputs(mod)
        for key, val in list(values.items()):
            obj = bpy.data.objects.get(val) if isinstance(val, str) else None
            if obj is not None and obj.type == "CURVE" and key.endswith("Curve"):
                curves[val] = sample_profile_curve(obj)
        if "Source" in values:
            values["Source"] = OUTLINE_SOURCE_ALIASES.get(values["Source"], values["Source"])
            for export_name, src, _, _ in PARTS:
                if values["Source"] == src:
                    values["Source"] = export_name
        stack.append({"modifier": mod.name, "group": mod.node_group.name, "inputs": values})
    return stack, curves


def light_params(light_obj, role):
    data = light_obj.data
    direction = light_obj.matrix_world.to_3x3() @ Vector((0.0, 0.0, -1.0))
    return {
        "role": role,
        "type": data.type,
        "color": to_json_value(data.color),
        "energy": round(data.energy, 6),
        "directionBlender": to_json_value(direction.normalized()),
    }


def world_color():
    world = bpy.context.scene.world
    if world and world.node_tree:
        bg = next((n for n in world.node_tree.nodes if n.type == "BACKGROUND"), None)
        if bg is not None:
            c = bg.inputs["Color"].default_value
            s = bg.inputs["Strength"].default_value
            return [round(c[0] * s, 6), round(c[1] * s, 6), round(c[2] * s, 6), 1.0]
    return [0.05, 0.05, 0.05, 1.0]


# ----------------------------------------------------------------------------------------------
# Typed (Unity JsonUtility friendly) views of the raw node data
# ----------------------------------------------------------------------------------------------

def typed_material(raw):
    t = raw["toon"]
    ao = raw.get("ao_mask")
    return {
        "name": raw["name"],
        "baseColor": t["Base Color"], "shadowTint": t["Shadow Tint"],
        "bandThreshold": t["Band Threshold"], "bandSoftness": t["Band Softness"],
        "receiveShadows": t["Receive Shadows"], "shadowThreshold": t["Shadow Threshold"],
        "rimStrength": t["Rim Strength"], "rimTint": t["Rim Tint"], "rimWidth": t["Rim Width"],
        "rimSoftness": t["Rim Softness"], "rimFacing": t["Rim Facing"],
        "hasAoMask": ao is not None,
        "aoThreshold": ao["threshold"] if ao else 0.5,
        "aoDarkColor": ao["dark_color"] if ao else [0.0, 0.0, 0.0, 1.0],
    }


def typed_outline(outline_name, target, stack):
    """Flatten a GN outline modifier stack into the parameters the Unity line art components take."""
    o = {
        "name": outline_name, "target": target, "method": "", "material": "",
        "strokeRadius": 0.02, "cornerType": "Round", "miterAngle": 0.0,
        "includeBoundary": True, "skipConcave": False, "concaveTolerance": 0.001,
        "outlineFalloff": 0.0, "undersideFalloff": False, "scaleBySpan": False, "fullSpan": 0.785398,
        "scaleByPolar": False, "joinBranches": False, "straightnessWeight": 0.1,
        "useRadiusVariation": False,
        "profileEnabled": False, "taperStartCurve": "", "taperEndCurve": "",
        "taperStartDistance": 40.0, "taperEndDistance": 40.0, "profileStrength": 1.0,
        "pixelsPerUnit": 1000.0, "profileApplyToRadius": True, "profileApplyToPush": True,
        "profileResolution": 256,
        "pushEnabled": False, "push": 0.9, "pushOffset": 0.0, "maxCornerPush": 2.5,
        "spikeThinning": False, "maxSpikeLength": 3.0,
        "warnings": [],
    }
    warn = o["warnings"]
    for entry in stack:
        group, i = entry["group"], entry["inputs"]
        if group in ("Silhouetteness Transfer", "Grease Pencil Outline • Smooth"):
            o["method"] = "edge" if group == "Silhouetteness Transfer" else "smooth"
            o["material"] = i.get("Material") or ""
            o["strokeRadius"] = i["Stroke Radius"]
            o["cornerType"] = i["Corner Type"]
            o["miterAngle"] = i["Miter Angle"]
            o["includeBoundary"] = i["Include Boundary"]
            o["skipConcave"] = i["Skip Concave Edges"]
            o["concaveTolerance"] = i["Concave Tolerance"]
            if abs(i.get("Camera Offset") or 0.0) > 1e-6:
                warn.append("Camera Offset is not supported")
            if o["method"] == "edge":
                o["outlineFalloff"] = i["Outline Falloff"]
                o["undersideFalloff"] = i["Underside Falloff"]
                o["scaleBySpan"] = i["Scale by Span"]
                o["fullSpan"] = i["Full Span"]
                o["scaleByPolar"] = i["Scale by Polar Angle"]
                o["straightnessWeight"] = i["Straightness Weight"]
                o["joinBranches"] = True  # Silhouetteness Transfer always joins branches
                o["miterAngle"] = max(i["Miter Angle"], 0.002)
            else:
                o["useRadiusVariation"] = (i.get("Radius Variation") or 0.0) > 0.0
                if (i.get("Smoothing") or 0) != 0 or abs(i.get("Normal Push") or 0.0) > 1e-6:
                    warn.append("Smoothing / Normal Push are not supported")
        elif group == "Stroke Profile":
            if i["Profile Along"] != "Start and End Tapers":
                warn.append(f"Profile Along '{i['Profile Along']}' is not supported")
            o["profileEnabled"] = True
            o["taperStartCurve"] = i.get("Taper Start Curve") or ""
            o["taperEndCurve"] = i.get("Taper End Curve") or ""
            o["taperStartDistance"] = i["Taper Start Distance"]
            o["taperEndDistance"] = i["Taper End Distance"]
            o["profileStrength"] = i["Strength"]
            o["pixelsPerUnit"] = i["Pixels per Unit"]
            o["profileResolution"] = i["Profile Resolution"]
            apply_to = i["Apply To"]
            o["profileApplyToRadius"] = "Radius" in apply_to.replace("Push Radius", "")
            o["profileApplyToPush"] = "Push Radius" in apply_to
        elif group in ("Stroke Push", "Edge Lines Push"):
            o["pushEnabled"] = True
            o["push"] = i["Push"]
            o["pushOffset"] = i["Offset"]
            o["maxCornerPush"] = i["Max Corner Push"]
            if group == "Edge Lines Push":
                if i.get("Screen Space"):
                    warn.append("Edge Lines Push screen space mode is not supported")
                o["spikeThinning"] = True
                o["maxSpikeLength"] = i["Max Spike Length"]
        else:
            warn.append(f"GN group '{group}' is not supported")
    return o


# ----------------------------------------------------------------------------------------------
# AgX LUT bake
# ----------------------------------------------------------------------------------------------

def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def bake_view_transform_lut(path):
    """URP strip LUT (N*N x N). Input coordinates are sRGB encoded (URP applies user LUTs on
    LinearToSRGB(color)); output is the display-encoded colour Blender's view transform produces."""
    n = LUT_SIZE
    img = bpy.data.images.new("_agx_lut", width=n * n, height=n, alpha=False, float_buffer=True)
    img.colorspace_settings.name = "Linear Rec.709"
    px = [0.0] * (n * n * n * 4)
    for y in range(n):
        g = srgb_to_linear(y / (n - 1))
        for x in range(n * n):
            r = srgb_to_linear((x % n) / (n - 1))
            b = srgb_to_linear((x // n) / (n - 1))
            i = (y * n * n + x) * 4
            px[i:i + 4] = (r, g, b, 1.0)
    img.pixels.foreach_set(px)

    scene = bpy.context.scene
    settings = scene.render.image_settings
    settings.file_format = "PNG"
    settings.color_mode = "RGB"
    settings.color_depth = "16"
    img.save_render(path, scene=scene)
    bpy.data.images.remove(img)
    return {"size": n, "viewTransform": scene.view_settings.view_transform,
            "look": scene.view_settings.look, "exposure": scene.view_settings.exposure,
            "gamma": scene.view_settings.gamma}


# ----------------------------------------------------------------------------------------------
# Main
# ----------------------------------------------------------------------------------------------

def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        raise SystemExit("usage: blender -b file.blend --python spikepod_export.py -- <output dir>")
    out_dir = os.path.abspath(argv[0])
    os.makedirs(out_dir, exist_ok=True)

    source_path = bpy.data.filepath
    copy_path = os.path.splitext(source_path)[0] + COPY_SUFFIX
    bpy.ops.wm.save_as_mainfile(filepath=copy_path)
    log("working on copy", copy_path)

    # Reset any previous export collection so the script is re-runnable on the copy.
    old = bpy.data.collections.get(EXPORT_COLLECTION)
    if old is not None:
        for o in list(old.objects):
            bpy.data.objects.remove(o)
        bpy.data.collections.remove(old)
    collection = ensure_collection(EXPORT_COLLECTION)

    manifest = {
        "source_blend": os.path.basename(source_path),
        "export_blend": os.path.basename(copy_path),
        "parts": [], "materials": [], "gpMaterials": [], "outlines": [], "curves": [],
        "lights": [], "camera": {}, "render": {}, "lut": {},
        "raw": {"materials": {}, "outlines": []},
    }
    curves = {}
    gp_materials = {}

    export_objects = []
    for export_name, src_name, outline_name, mat_name in PARTS:
        src = bpy.data.objects[src_name]
        outline = bpy.data.objects.get(outline_name) if outline_name else None
        stack, curves_for_outline = outline_params(outline) if outline else ([], {})
        curves.update(curves_for_outline)

        first_inputs = stack[0]["inputs"] if stack else None
        obj = bake_evaluated_mesh(export_name, src, collection)
        edge_outline = bool(stack) and stack[0]["group"] == "Silhouetteness Transfer"
        stream_info = write_vertex_streams(obj, outline, first_inputs, polygon_ids=edge_outline)
        export_objects.append(obj)

        raw_material = toon_params(bpy.data.materials[mat_name])
        manifest["raw"]["materials"][mat_name] = raw_material
        manifest["materials"].append(typed_material(raw_material))
        manifest["parts"].append({
            "name": export_name, "source": src_name, "material": mat_name,
            "vertices": len(obj.data.vertices), "faces": len(obj.data.polygons),
            "flatShaded": not any(p.use_smooth for p in obj.data.polygons), **stream_info,
        })
        if outline is not None:
            for entry in stack:
                gp_mat_name = entry["inputs"].get("Material")
                gp_mat = bpy.data.materials.get(gp_mat_name) if isinstance(gp_mat_name, str) else None
                if gp_mat is not None and gp_mat.is_grease_pencil:
                    gp_materials[gp_mat.name] = gp_material_params(gp_mat)
            manifest["raw"]["outlines"].append({"name": outline_name, "target": export_name, "stack": stack})
            typed = typed_outline(outline_name, export_name, stack)
            for w in typed["warnings"]:
                log("WARNING", outline_name, w)
            manifest["outlines"].append(typed)
        log("baked", export_name, len(obj.data.vertices), "verts", stream_info)

    cam_name, cam_src = CAMERA
    cam_src_obj = bpy.data.objects[cam_src]
    cam = bpy.data.objects.new(cam_name, cam_src_obj.data)
    cam.matrix_world = cam_src_obj.matrix_world.copy()
    collection.objects.link(cam)
    export_objects.append(cam)
    cd = cam_src_obj.data
    manifest["camera"] = {
        "name": cam_name, "orthographic": cd.type == "ORTHO", "orthoScale": round(cd.ortho_scale, 6),
        "lens": cd.lens, "sensorWidth": cd.sensor_width, "sensorFit": cd.sensor_fit,
        "clipStart": cd.clip_start, "clipEnd": cd.clip_end,
    }

    for light_name, light_src, role in LIGHTS:
        src = bpy.data.objects[light_src]
        light = bpy.data.objects.new(light_name, src.data)
        light.matrix_world = src.matrix_world.copy()
        collection.objects.link(light)
        export_objects.append(light)
        manifest["lights"].append({"name": light_name, **light_params(src, role)})

    manifest["curves"] = [{"name": k, "samples": v} for k, v in curves.items()]
    manifest["gpMaterials"] = list(gp_materials.values())

    scene = bpy.context.scene
    manifest["render"] = {
        "resolution": [scene.render.resolution_x, scene.render.resolution_y],
        "background": world_color(),
        "viewTransform": scene.view_settings.view_transform,
    }

    # Export.
    bpy.ops.object.select_all(action="DESELECT")
    for obj in export_objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = export_objects[0]
    fbx_path = os.path.join(out_dir, "SpikePod.fbx")
    bpy.ops.export_scene.fbx(
        filepath=fbx_path, use_selection=True,
        object_types={"MESH", "CAMERA", "LIGHT"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=False,
        use_mesh_modifiers=False, mesh_smooth_type="OFF",
        colors_type="LINEAR", prioritize_active_color=True,
        add_leaf_bones=False, bake_anim=False, path_mode="STRIP",
    )
    log("wrote", fbx_path)

    manifest["lut"] = bake_view_transform_lut(os.path.join(out_dir, "AgX_LUT.png"))

    with open(os.path.join(out_dir, "spikepod_manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
    log("wrote manifest")

    bpy.ops.wm.save_mainfile()
    log("saved copy", copy_path)


if __name__ == "__main__":
    main()
