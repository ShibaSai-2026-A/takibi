using UnityEngine;
using UnityEngine.Rendering;

namespace InteractiveBonfire
{
    // 燃焼密度と温度を移流する小型3Dグリッド。圧力を解く完全流体ソルバーではない。
    public sealed class BonfireSimulation
    {
        const int Width=64, Height=96, Depth=48;
        readonly ComputeShader compute;
        readonly int clearKernel, stepKernel, profileKernel;
        RenderTexture current, next, heatProfile;
        public RenderTexture Texture => current;
        public RenderTexture HeatProfile => heatProfile;

        public BonfireSimulation(ComputeShader source)
        {
            compute=Object.Instantiate(source);
            clearKernel=compute.FindKernel("Clear"); stepKernel=compute.FindKernel("Advect");
            profileKernel=compute.FindKernel("BuildHeatProfile");
            try
            {
                current=CreateTexture("Fire density A"); next=CreateTexture("Fire density B");
                heatProfile=new RenderTexture(Height,1,0,RenderTextureFormat.ARGBHalf)
                {name="Fire heat arrival",enableRandomWrite=true,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
                if(!heatProfile.Create()) throw new System.InvalidOperationException("熱の到達テクスチャを作成できませんでした。");
                compute.SetInts("_Resolution",Width,Height,Depth);
                Clear(current); Clear(next);
                UpdateHeatProfile();
            }
            catch { Dispose(); throw; }
        }

        static RenderTexture CreateTexture(string name)
        {
            var texture=new RenderTexture(Width,Height,0,RenderTextureFormat.ARGBHalf)
            {
                name=name, dimension=TextureDimension.Tex3D, volumeDepth=Depth,
                enableRandomWrite=true, wrapMode=TextureWrapMode.Clamp,
                filterMode=FilterMode.Bilinear, useMipMap=false
            };
            if(!texture.Create()) throw new System.InvalidOperationException("炎の3Dテクスチャを作成できませんでした。");
            return texture;
        }

        void Clear(RenderTexture texture)
        {
            compute.SetTexture(clearKernel,"_Write",texture);
            compute.Dispatch(clearKernel,Width/8,Height/8,Depth/4);
        }

        public void Step(float deltaSeconds,float energy,float breath,float time,float speed,float turbulence,Vector4[] sources)
        {
            if(deltaSeconds<=0f || sources==null || sources.Length!=7) return;
            float duration=Mathf.Min(deltaSeconds,0.1f)*Mathf.Clamp(speed,0.25f,2f);
            int steps=Mathf.Clamp(Mathf.CeilToInt(duration*60f),1,12);
            float dt=duration/steps;
            compute.SetVectorArray("_Sources",sources);
            compute.SetFloat("_Energy",Mathf.Clamp01(energy)); compute.SetFloat("_Breath",Mathf.Clamp01(breath));
            compute.SetFloat("_Turbulence",turbulence); compute.SetFloat("_Dt",dt);
            for(int j=0;j<steps;j++)
            {
                compute.SetFloat("_TimeValue",time-(steps-1-j)*dt);
                compute.SetTexture(stepKernel,"_Read",current); compute.SetTexture(stepKernel,"_Write",next);
                compute.Dispatch(stepKernel,Width/8,Height/8,Depth/4);
                var swap=current; current=next; next=swap;
            }
            UpdateHeatProfile();
        }

        void UpdateHeatProfile()
        {
            compute.SetTexture(profileKernel,"_Read",current);
            compute.SetTexture(profileKernel,"_HeatProfileWrite",heatProfile);
            compute.Dispatch(profileKernel,1,Height,1);
        }

        public void Reset() { Clear(current); Clear(next); UpdateHeatProfile(); }

        public void Dispose()
        {
            if(current) current.Release(); if(next) next.Release();
            if(heatProfile) heatProfile.Release();
            Release(current); Release(next); Release(heatProfile); Release(compute);
            current=null; next=null; heatProfile=null;
        }
        static void Release(Object value)
        {
            if(!value) return;
            if(Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value);
        }
    }
}
