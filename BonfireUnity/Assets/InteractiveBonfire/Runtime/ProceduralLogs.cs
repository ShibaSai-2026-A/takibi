using System.Collections.Generic;
using UnityEngine;

namespace InteractiveBonfire
{
    public sealed class ProceduralLogs : MonoBehaviour
    {
        readonly List<Mesh> ownedMeshes = new List<Mesh>();
        public void Build(Material material)
        {
            // 薪と火点は同一座標系（メートル）で設計。手前は-z。
            Add(new Vector3(-0.98f,0.14f,0.10f),new Vector3(0.93f,0.19f,-0.10f),0.13f,11,material);
            Add(new Vector3(-0.8f,0.14f,-0.40f),new Vector3(0.76f,0.21f,0.45f),0.14f,23,material);
            Add(new Vector3(-0.57f,0.14f,-0.76f),new Vector3(0.20f,0.48f,0.24f),0.15f,31,material);
            Add(new Vector3(-0.80f,0.39f,0.30f),new Vector3(0.85f,0.35f,0.27f),0.125f,47,material);
            Add(new Vector3(0.37f,0.18f,-0.56f),new Vector3(0.83f,0.46f,0.36f),0.11f,59,material);
            Add(new Vector3(-0.25f,0.21f,0.02f),new Vector3(0.08f,0.68f,0.21f),0.13f,67,material);
            // 小さい破片で根元をつなぐ。乱数は固定し、再起動でも配置を保つ。
            var rng=new System.Random(81);
            for(int k=0;k<22;k++)
            {
                float x=(float)rng.NextDouble()*1.8f-0.9f, z=(float)rng.NextDouble()*1.1f-0.55f;
                float r=0.025f+(float)rng.NextDouble()*0.035f;
                Vector3 a=new Vector3(x,0.035f,z);
                Vector3 b=a+new Vector3(0.10f+(float)rng.NextDouble()*0.12f,0.01f,0.03f);
                Add(a,b,r,100+k,material);
            }
        }

        void Add(Vector3 a,Vector3 b,float radius,int seed,Material material)
        {
            var go=new GameObject("Log_"+seed,typeof(MeshFilter),typeof(MeshRenderer));
            go.transform.SetParent(transform,false); go.transform.localPosition=a;
            go.transform.localRotation=Quaternion.FromToRotation(Vector3.up,(b-a).normalized);
            Mesh mesh=MakeLog((b-a).magnitude,radius,seed); ownedMeshes.Add(mesh);
            go.GetComponent<MeshFilter>().sharedMesh=mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial=material;
        }

        static Mesh MakeLog(float length,float radius,int seed)
        {
            const int sides=36,rings=20;
            var v=new List<Vector3>(); var uv=new List<Vector2>(); var triangles=new List<int>();
            for(int y=0;y<=rings;y++)
            {
                float along=(float)y/rings;
                for(int x=0;x<=sides;x++)
                {
                    float u=(float)x/sides, angle=u*Mathf.PI*2;
                    float noise=Mathf.PerlinNoise(Mathf.Cos(angle)*3+seed,Mathf.Sin(angle)*3+along*4);
                    float bark=0.88f+0.18f*noise+0.055f*Mathf.Sin(angle*13+along*11+seed);
                    float r=radius*bark*(1-0.1f*along);
                    v.Add(new Vector3(Mathf.Cos(angle)*r+Mathf.Sin(along*3+seed)*radius*0.09f,along*length,Mathf.Sin(angle)*r));
                    uv.Add(new Vector2(u,along));
                    if(y<rings && x<sides)
                    {
                        int i=y*(sides+1)+x;
                        triangles.AddRange(new[]{i,i+sides+1,i+1,i+1,i+sides+1,i+sides+2});
                    }
                }
            }
            // 木口には別のUVと頂点を用意して樹皮の継ぎ目を隠す。
            for(int end=0;end<2;end++)
            {
                int center=v.Count; v.Add(new Vector3(Mathf.Sin(end*3+seed)*radius*0.09f,end*length,0)); uv.Add(new Vector2(0.5f,3.5f));
                for(int x=0;x<=sides;x++)
                {
                    int src=end*rings*(sides+1)+x;
                    v.Add(v[src]); float angle=(float)x/sides*Mathf.PI*2;
                    uv.Add(new Vector2(Mathf.Cos(angle)*0.5f+0.5f,3.5f+Mathf.Sin(angle)*0.5f));
                    if(x<sides)
                    {
                        if(end==0) triangles.AddRange(new[]{center,center+x+1,center+x+2});
                        else triangles.AddRange(new[]{center,center+x+2,center+x+1});
                    }
                }
            }
            var mesh=new Mesh {name="Irregular charred log "+seed};
            mesh.SetVertices(v); mesh.SetUVs(0,uv); mesh.SetTriangles(triangles,0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
        void OnDestroy() { foreach(var mesh in ownedMeshes) if(mesh) Destroy(mesh); }
    }
}
