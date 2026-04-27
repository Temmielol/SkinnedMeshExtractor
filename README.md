# Skinned Mesh Extractor

A Unity Editor tool for extracting meshes out of a GameObject hierarchy and saving them as standalone assets. Works with both `MeshFilter` and `SkinnedMeshRenderer` components, preserves bone weights and blendshapes, and can optionally rebuild the hierarchy as a prefab or export to FBX.

## Installation

Drop all seven `.cs` files into an `Editor/` folder anywhere inside your Unity project's `Assets/`. They share the `MeshTools` namespace and don't need to be in any particular subfolder.

Requires Unity 2020.1+ (uses `Mesh.AcquireReadOnlyMeshData`, `GetAllBoneWeights`, and `GetBonesPerVertex`).

## Usage

1. Open the window via **Tools → Skinned Mesh Extractor** in the Unity menu bar.
2. Drag a GameObject from the scene or a prefab into the drop zone.
3. Configure options:
   - **Output Folder** — where extracted `.asset` meshes, prefabs, and (optionally) FBX files are written. Defaults to `Assets/Mesh`.
   - **Rebuild In Scene** — leave the rebuilt hierarchy as a new GameObject in the scene after extraction.
   - **Save Prefab Asset** — save the rebuilt hierarchy as a `.prefab` in the output folder.
   - **Export FBX** — also write a `.fbx` of the rebuilt hierarchy. Requires the `com.unity.formats.fbx` package; if it isn't installed, the tool will install it automatically via the Package Manager when you click Extract.
4. Click **Extract**.

Each run writes to a timestamped subfolder like `Assets/Mesh/run_20260421_143022_MyCharacter/` so runs don't overwrite each other.

## How it works

For each mesh in the hierarchy, the tool picks the simplest path that will work:

- If the source mesh is marked **Read/Write enabled**, it clones the mesh directly via `Object.Instantiate`. This preserves all skinning data including things that are awkward to pull through the public C# API (bone indices, modern-format weight streams).
- If the source is **not** readable, the tool temporarily flips the `ModelImporter.isReadable` flag, reimports, extracts, and restores the original setting afterward.
- As a last resort, attributes are extracted individually (vertices, normals, UVs, tangents, colors, submeshes, bone weights via both legacy and modern APIs, bindposes, blendshapes) and a new `Mesh` is built from them.

Bone weights use a two-stage approach: the legacy `mesh.boneWeights` API first, falling back to `GetAllBoneWeights` / `GetBonesPerVertex` when the legacy API returns collapsed indices (which happens with meshes imported using "Unlimited" bones-per-vertex). Bindposes fall back to reconstruction from the SMR's `bones` array if the mesh doesn't expose them.

When rebuilding the hierarchy, bones are remapped by their path relative to the root (`Armature/Hips/Spine`, etc.), and `bones` and `rootBone` are set on the SMR **before** `sharedMesh` — Unity binds skinning at the moment `sharedMesh` is assigned, so ordering matters.

## File layout

| File | Responsibility |
|------|----------------|
| `SkinnedMeshExtractor.cs` | `EditorWindow` — UI, drag-drop, coroutine runner, Read/Write toggle orchestration |
| `MeshSlot.cs` | Shared data classes (`MeshSlot`, `Attr`, `BlendShapeCapture`, `BlendShapeFrame`) |
| `MeshAttributeExtractor.cs` | Pulls every attribute out of a source mesh, with fallbacks |
| `MeshBuilder.cs` | Turns an `Attr` into a saved `.asset` Mesh |
| `HierarchyRebuilder.cs` | Clones the GameObject hierarchy, remaps bones, saves prefab |
| `FbxExporter.cs` | Reflection-based wrapper around the Unity FBX Exporter package |
| `MeshExtractorUtils.cs` | Small static helpers (path building, vertex validation, folder/name utilities) |

Only `SkinnedMeshExtractorWindow` is `public`. Everything else is `internal` to keep the surface area small.

## Output

Inside each run folder:

- `<MeshName>_extracted.asset` — one per mesh in the hierarchy, always written
- `<RootName>_extracted.prefab` — if **Save Prefab Asset** is on
- `<RootName>_extracted.fbx` — if **Save Prefab Asset** *and* **Export FBX** are both on, and the FBX Exporter package is installed

Note: **Export FBX** only runs when **Save Prefab Asset** is also enabled. If both **Rebuild In Scene** and **Save Prefab Asset** are off, the hierarchy rebuild step is skipped entirely — you'll only get `.asset` files for the individual meshes.

## Notes and caveats

- The tool flips Read/Write on source `.fbx`/`.asset` importers during a run and restores the original value in a `finally` block. If Unity crashes mid-extraction, you may need to check your import settings manually.
- FBX export is best-effort via reflection on `UnityEditor.Formats.Fbx.Exporter.ModelExporter`. If the Unity FBX Exporter package's API changes, export will log a warning and skip — the `.asset` and `.prefab` outputs will still succeed.
- Blendshape weights from the original SMR are copied onto the rebuilt SMR when the rebuilt mesh has matching blendshapes.
- Submesh triangles that throw on `GetTriangles` are silently skipped rather than aborting the whole extraction.
- Meshes with more than 65535 vertices are automatically switched to `IndexFormat.UInt32` during the fallback rebuild path.
