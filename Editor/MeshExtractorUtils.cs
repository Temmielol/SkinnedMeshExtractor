using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MeshTools
{
    internal static class MeshExtractorUtils
    {
        public static string GetPath(Transform t, Transform root)
        {
            if (t == root) return "";
            var sb = new StringBuilder();
            var cur = t;
            while (cur != null && cur != root)
            {
                if (sb.Length > 0) sb.Insert(0, '/');
                sb.Insert(0, cur.name);
                cur = cur.parent;
            }
            return sb.ToString();
        }

        public static bool IsRealVec3(Vector3[] v)
        {
            if (v == null || v.Length < 3) return false;
            return !AllSameOrDegenerate(v);
        }

        public static bool HasAnyWeight(BoneWeight[] bw)
        {
            if (bw == null || bw.Length == 0) return false;
            for (int i = 0; i < bw.Length; i++)
            {
                if (bw[i].weight0 > 0f || bw[i].weight1 > 0f ||
                    bw[i].weight2 > 0f || bw[i].weight3 > 0f)
                    return true;
            }
            return false;
        }

        public static bool HasVariedBoneIndices(BoneWeight[] bw)
        {
            // Detects legacy API returning collapsed indices (all 0), which happens
            // when the source mesh was imported with Unlimited bones-per-vertex.
            if (bw == null || bw.Length == 0) return false;
            // Sample up to 200 verts to keep this cheap on dense meshes
            int step = Math.Max(1, bw.Length / 200);
            var seen = new HashSet<int>();
            for (int i = 0; i < bw.Length; i += step)
            {
                if (bw[i].weight0 > 0f) seen.Add(bw[i].boneIndex0);
                if (bw[i].weight1 > 0f) seen.Add(bw[i].boneIndex1);
                if (bw[i].weight2 > 0f) seen.Add(bw[i].boneIndex2);
                if (bw[i].weight3 > 0f) seen.Add(bw[i].boneIndex3);
                if (seen.Count > 1) return true;
            }
            return false;
        }

        public static bool AllSameOrDegenerate(Vector3[] v)
        {
            if (v.Length == 0) return true;
            var first = v[0];
            bool allSame = true;
            for (int i = 1; i < v.Length; i++)
            {
                if (float.IsNaN(v[i].x) || float.IsNaN(v[i].y) || float.IsNaN(v[i].z))
                    continue;
                if (v[i] != first) { allSame = false; break; }
            }
            return allSame;
        }

        public static void EnsureFolder(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }

        public static string MakeSafe(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }
    }
}
