"""Convert licensed GLB rest geometry to a portable original swimming morph loop.
The source's nonuniform armature hierarchy is omitted; materials and geometry remain.
Run using Blender --background --factory-startup --python this.py -- in.glb out.glb.
"""
import json
import math
import struct
import sys
from pathlib import Path
import bpy
from mathutils import Vector, Matrix, Quaternion


def main():
    source, output = map(Path, sys.argv[sys.argv.index('--') + 1:])
    blob = source.read_bytes()
    length = struct.unpack_from('<I', blob, 12)[0]
    document = json.loads(blob[20:20 + length])
    binary = blob[28 + length:]
    def read(index):
        accessor = document['accessors'][index]
        view = document['bufferViews'][accessor['bufferView']]
        fmt = {5121: 'B', 5123: 'H', 5125: 'I', 5126: 'f'}[accessor['componentType']]
        count = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}[accessor['type']]
        stride = view.get('byteStride', struct.calcsize(fmt) * count)
        offset = view.get('byteOffset', 0) + accessor.get('byteOffset', 0)
        return [struct.unpack_from('<' + fmt * count, binary, offset + i * stride)
                for i in range(accessor['count'])]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    materials = []
    for source_material in document['materials']:
        material = bpy.data.materials.new(source_material['name'])
        color = source_material['pbrMetallicRoughness']['baseColorFactor']
        peak = max(color[:3])
        multiplier = max(1, .35 / peak) if peak > 0 else 1
        material.diffuse_color = tuple(value * multiplier for value in color[:3]) + (1,)
        materials.append(material)
    root_rotation = Matrix([document['nodes'][0]['matrix'][i:i + 4] for i in range(0, 16, 4)]).transposed()
    pieces = []
    for source_mesh in document['meshes']:
        for primitive in source_mesh['primitives']:
            transformed = [root_rotation @ Vector(point) for point in read(primitive['attributes']['POSITION'])]
            points = [Vector((point.x, -point.z, point.y)) for point in transformed]
            indices = [value[0] for value in read(primitive['indices'])]
            pieces.append((points, [indices[i:i + 3] for i in range(0, len(indices), 3)], primitive.get('material', 0)))
    low = Vector(tuple(min(point[i] for points, _, _ in pieces for point in points) for i in range(3)))
    high = Vector(tuple(max(point[i] for points, _, _ in pieces for point in points) for i in range(3)))
    size = high - low
    scale = min(8 / size.z, 30 / max(size.x, size.y))
    offset = Vector(((low.x + high.x) / 2, (low.y + high.y) / 2, low.z))
    for number, (points, faces, material_index) in enumerate(pieces):
        base = [(point - offset) * scale for point in points]
        mesh = bpy.data.meshes.new(f'Leviathan{number}')
        mesh.from_pydata(base, [], faces)
        mesh.materials.append(materials[material_index])
        obj = bpy.data.objects.new(mesh.name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.shape_key_add(name='Basis')
        for index in range(17):
            key = obj.shape_key_add(name=f'Swim{index:02}')
            phase = index / 16 * math.tau
            for vertex, point in zip(key.data, base):
                vertex.co = point + Vector((math.sin(phase + point.y * .22) * .15 * abs(point.y), 0, 0))
            for frame_index in range(17):
                key.value = float(frame_index == index)
                key.keyframe_insert(data_path='value', frame=1 + frame_index * 6)
        action = mesh.shape_keys.animation_data.action
        action.name = 'LeviathanSwim'
        for curve in action.fcurves:
            for keyframe in curve.keyframe_points:
                keyframe.interpolation = 'LINEAR'
    bpy.context.scene.frame_end = 97
    bpy.context.scene.frame_set(1)
    bpy.ops.export_scene.gltf(filepath=str(output.resolve()), export_format='GLB',
                             export_animations=True, export_cameras=False, export_lights=False)
    print(f'LEVIATHAN REST GEOMETRY pieces={len(pieces)} size={size} scale={scale}')


if __name__ == '__main__':
    main()



