﻿
using ObjLoader.Loader.Data.VertexData;
using ObjLoader.Loader.Data.Elements;
using ObjLoader.Loader.Loaders;
using ObjParser;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Diagnostics;

namespace Converter
{
    class Program
    {
        static VertexModel v;
        static void Main(string[] args)
        {
            try {
                v = new VertexModel(args[0]);
            } catch (FileNotFoundException) {
                return;
            }
        }
    }

    class Distancer
    {
    }

    public class VertexModel
    {
        public LoadResult RawData;
        //public Obj Raw;// { get; private set; }
        public DetailVertex[] xSorted; // TODO privatify
        public float DetailScale = 0; // longest edge
        //public Vector3[] xSortedNormals;
        protected float Scale;
        protected Vector3 Offset;
        const float padding = 1.1f;
        KdTree verts;

        public VertexModel(string fileName) : this(fileName, (new ObjLoaderFactory()).Create())
        {

        }
        public VertexModel(string fileName, IObjLoader loader)
        {
            using (var file = new FileStream(fileName, FileMode.Open))
                RawData = loader.Load(file);
            FindDimensions();
            Sort();
        }

        public virtual float DistanceAt(Vector3 v, List<int> possible)
        {
            v = Transform(v);
            int closest = 0;
            float minDistance = Single.PositiveInfinity;

            foreach (int i in possible) {
                if (Vector3.DistanceSquared(xSorted[i].Pos, v) < minDistance) {
                    minDistance = Vector3.DistanceSquared(xSorted[i].Pos, v);
                    closest = i;
                }
                if ((xSorted[i].Pos.X - xSorted[closest].Pos.X) * (xSorted[i].Pos.X - xSorted[closest].Pos.X) > minDistance)
                    break;
            }

            if (float.IsPositiveInfinity(minDistance))
                Debug.WriteLine("Did not find ");
            if (float.IsInfinity(minDistance) || float.IsNaN(minDistance))
                Debug.WriteLine("NaN distance, Oh noes.");

            minDistance = (float) Math.Sqrt(minDistance);

            if (Inside(v, closest))
                minDistance *= -1;

            return minDistance / Scale;
        }

        public virtual bool Inside(Vector3 v, int closest)
        {
            return Vector3.Dot(xSorted[closest].Normal, xSorted[closest].Pos - v) > 0;
            /**
            bool outside = false;
            foreach (var n in xSorted[closest].Normals) {
                if (Vector3.Dot(n, v - xSorted[closest].Pos) > 0)
                    outside = true;
            }
            if (Vector3.Dot(xSorted[closest].Normal, v - xSorted[closest].Pos) > 0)
                outside = true;
            if (!outside)
                minDistance *= -1;
            */
        }

        public virtual List<int> GetPossible(Vector3 pos, float scale, List<int> possible)
        {
            return GetPossiblePrecomp(pos, FacelessDistanceAt(pos, possible) + scale, possible);
        }
        public List<int> GetPossiblePrecomp(Vector3 pos, float minDistance, List<int> possible)
        {
            pos = Transform(pos);
            minDistance *= Scale;
            List<int> next = new List<int>();
            foreach (int i in possible) {
                if (Vector3.Distance(xSorted[i].Pos, pos) < minDistance)
                    next.Add(i);
            }
            return next;
        }
        float FacelessDistanceAt(Vector3 v, List<int> possible)
        {
            v = Transform(v);
            float minDistance = Single.PositiveInfinity;

            foreach (int i in possible) {
                minDistance = Math.Min(minDistance, Vector3.DistanceSquared(xSorted[i].Pos, v));
            }
            return (float) Math.Sqrt(minDistance);
        }
        public List<int> All()
        {
            List<int> x = new List<int>();
            for (int i = 0; i < xSorted.Length; i++) {
                x.Add(i);
            }
            return x;
        }

        private void FindDimensions()
        {
            Vector3 lower = new Vector3(float.PositiveInfinity);
            Vector3 higher = new Vector3(float.NegativeInfinity);
            foreach (var vert in RawData.Vertices) {
                lower.X = Math.Min(lower.X, vert.X);
                lower.Y = Math.Min(lower.Y, vert.Y);
                lower.Z = Math.Min(lower.Z, vert.Z);
                higher.X = Math.Max(higher.X, vert.X);
                higher.Y = Math.Max(higher.Y, vert.Y);
                higher.Z = Math.Max(higher.Z, vert.Z);
            }
            Offset = (lower + higher) / 2 + new Vector3(0.003f);
            float lowest = Math.Min(lower.X, Math.Min(lower.Y, lower.Z));
            float highest = Math.Max(higher.X, Math.Max(higher.Y, higher.Z));
            Scale = (highest - lowest) * padding;
        }
        protected Vector3 Transform(Vector3 worldPos)
        {
            worldPos.Y = 1 - worldPos.Y;
            Vector3 modelPos = (worldPos - new Vector3(0.5f)) * Scale + Offset; //(.5f, .625f, .5f)
            return modelPos;
        }

        protected virtual void Sort()
        {
            List<DetailVertex> ordered = new List<DetailVertex>(RawData.Vertices.Count);

            for (int i = 0; i < RawData.Vertices.Count; i++) {
                Vertex x = RawData.Vertices[i];
                ordered.Add(new DetailVertex(new Vector3(x.X, x.Y, x.Z)));
            }

            foreach (var face in RawData.Groups[0].Faces) {
                for (int i = 0; i < face.Count; i++) {
                    Normal n = RawData.Normals[face[i].NormalIndex - 1];
                    ordered[face[i].VertexIndex - 1].Normal = (new Vector3(n.X, n.Y, n.Z));
                    for (int j = 0; j < face.Count - 2; j++) {
                        var a = RawData.Vertices[face[0].VertexIndex-1];
                        var b = RawData.Vertices[face[j+1].VertexIndex-1];
                        var c = RawData.Vertices[face[j+2].VertexIndex-1];
                        ordered[face[i].VertexIndex - 1].Add(a, b, c);
                        DetailScale = Math.Max(DetailScale, LongestEdge(a, b, c));
                    }
                }
            }
            ordered.Sort((x, y) => x.Pos.X.CompareTo(y.Pos.X));
            xSorted = ordered.ToArray();

            float LongestEdge(Vertex a, Vertex b, Vertex c)
            {
                return 0;
            }
            #region deprecated
            /*
            for (int i = 0; ordered.Count > 0; i++) {
                int vertex = ordered.Dequeue();
                int normal;
                if (NormalMapping.ContainsKey(vertex))
                    normal = NormalMapping[vertex];
                else
                    normal = vertex;
                var v = RawData.Vertices[vertex];
                Normal n = RawData.Normals[normal];
                xSorted[i] = new DetailVertex(new Vector3(v.X, v.Y, v.Z), new Vector3(n.X, n.Y, n.Z));
            }*/
            //*/
            #endregion
        }
        List<VertexN> Vertices()
        {
            VertexN[] elems = new VertexN[RawData.Vertices.Count];

            Dictionary<int, int> NormalMapping = new Dictionary<int, int>();
            foreach (var face in RawData.Groups[0].Faces) {
                for (int i = 0; i < face.Count; i++) {
                    NormalMapping[face[i].VertexIndex - 1] = face[i].NormalIndex - 1;
                }
            }

            for (int i = 0; i < xSorted.Length; i++) {
                var v = RawData.Vertices[i];
                Normal n = RawData.Normals[NormalMapping[i]];
                elems[i] = new VertexN(new Vector3(v.X, v.Y, v.Z), new Vector3(n.X, n.Y, n.Z));
            }
            return new List<VertexN>(elems);
        }
    }

    public class DetailVertex
    {
        public Vector3 Pos;
        public Vector3 Normal;
        public List<Matrix4x4> Faces;

        public DetailVertex(Vector3 pos)
        {
            Pos = pos;
            Faces = new List<Matrix4x4>();
            //Normals = new List<Vector3>();
        }

        public void Add(Vertex a, Vertex b, Vertex c)
        {
            Faces.Add(Trify(new Vector3(a.X, a.Y, a.Z), new Vector3(b.X, b.Y, b.Z), new Vector3(c.X, c.Y, c.Z)));
        }
        static Matrix4x4 Trify(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 p = b - a;
            Vector3 q = c - a;
            Vector3 n = Vector3.Normalize(Vector3.Cross(p, q));

            Matrix4x4 mat = new Matrix4x4(
                p.X, q.X, n.X, 0,
                p.Y, q.Y, n.Y, 0,
                p.Z, q.Z, n.Z, 0,
                0, 0, 0, 1);
            if (!Matrix4x4.Invert(mat, out mat))
                Debug.WriteLine("OH NOES! Could not invert");
            mat.Translation = Vector3.Transform(-a, mat);
            return mat;
        }
        public static float FaceDist(Vector3 pos, Matrix4x4 face)
        {
            pos = Vector3.Transform(pos, face);
            if (pos.X >= 0 && pos.Y >= 0 && pos.X + pos.Y <= 1)
                return Math.Abs(pos.Z);
            else
                return float.PositiveInfinity;
        }
    }
}