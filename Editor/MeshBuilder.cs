using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MeshTools
{
    internal static class MeshBuilder
    {
        public static Mesh BuildMeshFromAttr(MeshSlot slot, string runFolder)
        {
            // Preferred path: if the source mesh is readable, clone it directly.
            // Instantiate() on a Mesh preserves all skinning data including things
            // that are awkward to pull through the public C# API (bone indices,
            // modern-format weight streams, etc).
            if (slot.Source != null && slot.Source.isReadable)
            {
                var cloned = (Mesh)Object.Instantiate(slot.Source);
                cloned.name = slot.Owner.name + "_extracted";

                var assetPath = Path.Combine(runFolder, MeshExtractorUtils.MakeSafe(cloned.name) + ".asset").Replace('\\', '/');
                AssetDatabase.CreateAsset(cloned, assetPath);
                return cloned;
            }

            // Fallback: source isn't readable. Reconstruct from extracted attributes.
            return BuildFromAttributes(slot, runFolder);
        }

        private static Mesh BuildFromAttributes(MeshSlot slot, string runFolder)
        {
            var a = slot.Extracted;
            if (a.vertices == null || a.vertices.Length == 0) return null;
            if (a.submeshTriangles.Count == 0) return null;

            var m = new Mesh { name = slot.Owner.name + "_extracted" };
            if (a.vertices.Length > 65535)
                m.indexFormat = IndexFormat.UInt32;

            m.vertices = a.vertices;

            int vc = a.vertices.Length;
            if (a.normals  != null && a.normals.Length  == vc) m.normals  = a.normals;
            if (a.tangents != null && a.tangents.Length == vc) m.tangents = a.tangents;
            if (a.colors   != null && a.colors.Length   == vc) m.colors   = a.colors;

            for (int ch = 0; ch < a.uvChannels.Count; ch++)
            {
                var uv = a.uvChannels[ch];
                if (uv != null && uv.Length == vc)
                    m.SetUVs(ch, new List<Vector2>(uv));
            }

            m.subMeshCount = a.submeshTriangles.Count;
            for (int s = 0; s < a.submeshTriangles.Count; s++)
            {
                try { m.SetTriangles(a.submeshTriangles[s], s, true); }
                catch { }
            }

            if (slot.IsSkinned)
            {
                var smr = slot.Component as SkinnedMeshRenderer;
                int boneCount = smr != null && smr.bones != null ? smr.bones.Length : 0;

                if (a.bindposes != null && a.bindposes.Length > 0)
                {
                    var bp = a.bindposes;
                    if (boneCount > 0 && bp.Length != boneCount)
                    {
                        var resized = new Matrix4x4[boneCount];
                        for (int i = 0; i < boneCount; i++)
                            resized[i] = i < bp.Length ? bp[i] : Matrix4x4.identity;
                        bp = resized;
                    }
                    m.bindposes = bp;
                }
                else if (boneCount > 0)
                {
                    var bp = new Matrix4x4[boneCount];
                    for (int i = 0; i < boneCount; i++) bp[i] = Matrix4x4.identity;
                    m.bindposes = bp;
                }

                if (a.boneWeights != null && a.boneWeights.Length == vc)
                {
                    if (boneCount > 0)
                    {
                        for (int i = 0; i < a.boneWeights.Length; i++)
                        {
                            var w = a.boneWeights[i];
                            if (w.boneIndex0 >= boneCount) w.boneIndex0 = 0;
                            if (w.boneIndex1 >= boneCount) w.boneIndex1 = 0;
                            if (w.boneIndex2 >= boneCount) w.boneIndex2 = 0;
                            if (w.boneIndex3 >= boneCount) w.boneIndex3 = 0;
                            float sum = w.weight0 + w.weight1 + w.weight2 + w.weight3;
                            if (sum <= 0f) { w.boneIndex0 = 0; w.weight0 = 1f; }
                            a.boneWeights[i] = w;
                        }
                    }
                    m.boneWeights = a.boneWeights;
                }

                foreach (var bs in a.blendShapes)
                {
                    foreach (var f in bs.frames)
                    {
                        if (f.dVerts != null && f.dVerts.Length == vc)
                        {
                            try { m.AddBlendShapeFrame(bs.name, f.weight, f.dVerts, f.dNormals, f.dTangents); }
                            catch { }
                        }
                    }
                }
            }

            m.RecalculateBounds();

            var assetPath = Path.Combine(runFolder, MeshExtractorUtils.MakeSafe(m.name) + ".asset").Replace('\\', '/');
            AssetDatabase.CreateAsset(m, assetPath);
            return m;
        }
    }
}
