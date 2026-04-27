using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MeshTools
{
    internal static class MeshAttributeExtractor
    {
        public static void ExtractAllAttributes(MeshSlot slot)
        {
            var mesh = slot.Source;
            var a = slot.Extracted;

            TryAttr(() => {
                var r = mesh.vertices;
                if (MeshExtractorUtils.IsRealVec3(r)) a.vertices = r;
            });

            TryAttr(() => {
                var r = mesh.normals;
                if (MeshExtractorUtils.IsRealVec3(r)) a.normals = r;
            });

            TryAttr(() => {
                var r = mesh.tangents;
                if (r != null && r.Length > 0) a.tangents = r;
            });

            TryAttr(() => {
                var r = mesh.colors;
                if (r != null && r.Length > 0) a.colors = r;
            });

            for (int ch = 0; ch < 8; ch++)
            {
                int channel = ch;
                TryAttr(() => {
                    var list = new List<Vector2>();
                    mesh.GetUVs(channel, list);
                    if (list.Count > 0)
                    {
                        while (a.uvChannels.Count <= channel) a.uvChannels.Add(null);
                        a.uvChannels[channel] = list.ToArray();
                    }
                });
            }

            TryAttr(() => {
                int subCount = mesh.subMeshCount;
                for (int s = 0; s < subCount; s++)
                {
                    try
                    {
                        var tris = mesh.GetTriangles(s);
                        if (tris != null && tris.Length >= 3)
                            a.submeshTriangles.Add(tris);
                    }
                    catch { }
                }
            });

            if (slot.IsSkinned)
            {
                TryAttr(() => {
                    var r = mesh.boneWeights;
                    if (r != null && r.Length > 0 &&
                        MeshExtractorUtils.HasAnyWeight(r) &&
                        MeshExtractorUtils.HasVariedBoneIndices(r))
                    {
                        a.boneWeights = r;
                    }
                });

                TryAttr(() => {
                    if (a.boneWeights != null && a.boneWeights.Length > 0) return;
                    var perVertex = mesh.GetBonesPerVertex();
                    var all = mesh.GetAllBoneWeights();
                    if (perVertex.Length == 0 || all.Length == 0) return;

                    int vc = mesh.vertexCount;
                    var bw = new BoneWeight[vc];
                    int cursor = 0;
                    for (int v = 0; v < vc; v++)
                    {
                        int count = perVertex[v];
                        var w = new BoneWeight();
                        for (int k = 0; k < count && k < 4; k++)
                        {
                            var bw1 = all[cursor + k];
                            switch (k)
                            {
                                case 0: w.boneIndex0 = bw1.boneIndex; w.weight0 = bw1.weight; break;
                                case 1: w.boneIndex1 = bw1.boneIndex; w.weight1 = bw1.weight; break;
                                case 2: w.boneIndex2 = bw1.boneIndex; w.weight2 = bw1.weight; break;
                                case 3: w.boneIndex3 = bw1.boneIndex; w.weight3 = bw1.weight; break;
                            }
                        }
                        float sum = w.weight0 + w.weight1 + w.weight2 + w.weight3;
                        if (sum > 0f)
                        {
                            w.weight0 /= sum; w.weight1 /= sum;
                            w.weight2 /= sum; w.weight3 /= sum;
                        }
                        bw[v] = w;
                        cursor += count;
                    }
                    a.boneWeights = bw;
                });

                TryAttr(() => {
                    var r = mesh.bindposes;
                    if (r != null && r.Length > 0) a.bindposes = r;
                });

                TryAttr(() => {
                    if (a.bindposes != null && a.bindposes.Length > 0) return;
                    var smr = slot.Component as SkinnedMeshRenderer;
                    if (smr == null || smr.bones == null || smr.bones.Length == 0) return;
                    var bp = new Matrix4x4[smr.bones.Length];
                    var rootToWorld = smr.transform.localToWorldMatrix;
                    for (int i = 0; i < smr.bones.Length; i++)
                    {
                        if (smr.bones[i] == null) { bp[i] = Matrix4x4.identity; continue; }
                        bp[i] = smr.bones[i].worldToLocalMatrix * rootToWorld;
                    }
                    a.bindposes = bp;
                });

                TryAttr(() => {
                    int count = mesh.blendShapeCount;
                    int vc = mesh.vertexCount;
                    for (int i = 0; i < count; i++)
                    {
                        var bs = new BlendShapeCapture { name = mesh.GetBlendShapeName(i) };
                        int frames = mesh.GetBlendShapeFrameCount(i);
                        for (int f = 0; f < frames; f++)
                        {
                            var dv = new Vector3[vc];
                            var dn = new Vector3[vc];
                            var dt = new Vector3[vc];
                            mesh.GetBlendShapeFrameVertices(i, f, dv, dn, dt);
                            bs.frames.Add(new BlendShapeFrame {
                                weight = mesh.GetBlendShapeFrameWeight(i, f),
                                dVerts = dv, dNormals = dn, dTangents = dt
                            });
                        }
                        a.blendShapes.Add(bs);
                    }
                });
            }

            // Vertex fallbacks — only run if the primary vertex read failed
            TryAttr(() => {
                if (a.vertices != null) return;
                var pi = typeof(Mesh).GetProperty("vertices",
                    BindingFlags.Public | BindingFlags.Instance);
                var r = (Vector3[])pi.GetValue(mesh);
                if (MeshExtractorUtils.IsRealVec3(r)) a.vertices = r;
            });

            TryAttr(() => {
                if (a.vertices != null) return;
                var list = new List<Vector3>();
                mesh.GetVertices(list);
                if (list.Count > 0 && !MeshExtractorUtils.AllSameOrDegenerate(list.ToArray()))
                    a.vertices = list.ToArray();
            });

            TryAttr(() => {
                if (a.vertices != null) return;
                using (var data = Mesh.AcquireReadOnlyMeshData(mesh))
                {
                    if (data.Length == 0) return;
                    var md = data[0];
                    var verts = new Vector3[md.vertexCount];
                    var na = new Unity.Collections.NativeArray<Vector3>(
                        md.vertexCount, Unity.Collections.Allocator.Temp);
                    md.GetVertices(na);
                    na.CopyTo(verts);
                    na.Dispose();
                    if (MeshExtractorUtils.IsRealVec3(verts)) a.vertices = verts;
                }
            });
        }

        private static void TryAttr(Action fn)
        {
            try { fn(); }
            catch (Exception e) { Debug.LogWarning("Mesh attribute extraction threw: " + e.GetType().Name + " — " + e.Message); }
        }
    }
}
