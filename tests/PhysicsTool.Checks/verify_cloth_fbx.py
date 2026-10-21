"""Run with Blender --background --factory-startup --python this_file -- output_dir."""
import json
import pathlib
import sys

import bpy
from mathutils import Vector

directory = pathlib.Path(sys.argv[sys.argv.index("--") + 1])
for path in sorted(directory.glob("*.fbx")):
    expected = json.loads(path.with_suffix(".fbx.json").read_text())
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(path))
    def check(actual, source, label):
        target = Vector((source[0], -source[2], source[1]))
        assert (actual - target).length < 0.0002, (label, tuple(actual), tuple(target))
    for name, vertices in expected["meshes"].items():
        obj = bpy.data.objects[name]
        assert obj.type == "MESH" and len(obj.data.vertices) == len(vertices), name
        for vertex, source in zip(obj.data.vertices, vertices):
            check(obj.matrix_world @ vertex.co, source, name)
        if name.startswith("Collider_"):
            assert obj.parent is not None and obj.parent_type == "BONE", name
    armature = bpy.data.objects["Skeleton"]
    assert armature.type == "ARMATURE"
    assert len(armature.data.bones) == len(expected["bones"])
    for name, position in expected["bones"].items():
        check(armature.matrix_world @ armature.data.bones[name].head_local, position, name)
    print("PASS Blender:", path.name, "particle order, collider meshes/parents, armature, units and world positions")
