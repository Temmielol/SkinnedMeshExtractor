using System.Collections.Generic;
using UnityEngine;

namespace MeshTools
{
    internal class MeshSlot
    {
        public Component Component;
        public GameObject Owner;
        public Mesh Source;
        public bool IsSkinned;
        public Attr Extracted = new Attr();
        public Mesh Rebuilt;
    }

    internal class Attr
    {
        public Vector3[] vertices;
        public Vector3[] normals;
        public Vector4[] tangents;
        public Color[] colors;
        public List<Vector2[]> uvChannels = new List<Vector2[]>();
        public List<int[]> submeshTriangles = new List<int[]>();
        public BoneWeight[] boneWeights;
        public Matrix4x4[] bindposes;
        public List<BlendShapeCapture> blendShapes = new List<BlendShapeCapture>();
    }

    internal class BlendShapeCapture
    {
        public string name;
        public List<BlendShapeFrame> frames = new List<BlendShapeFrame>();
    }

    internal class BlendShapeFrame
    {
        public float weight;
        public Vector3[] dVerts;
        public Vector3[] dNormals;
        public Vector3[] dTangents;
    }
}
