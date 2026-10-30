using UnityEngine;
using UnityEngine.Rendering;

namespace InteractiveBonfire
{
    // 炎全体の流れを解く小型の3D流体（速度・圧力・燃焼量・温度）。
    // 燃焼や化学は近似で、見える形は燃焼量の等値面、動きの不規則さは流体と乱数の組み合わせから生まれる。
    public sealed class BonfireSimulation
    {
        const int Width=96, Height=112, Depth=64, PressureIterations=18;
        const float MaxStep=1f/30f;
        readonly ComputeShader compute;
        readonly int clear, advectVelocity, vorticity, forces, divergence, jacobi, project, advectState;
        readonly Texture noise;
        readonly System.Random random;
        RenderTexture velocity, velocityNext, state, stateNext, pressure, pressureNext, aux;
        public RenderTexture Texture => state;

        // 乱数で動く外力。周期のある関数を使わないので、同じ動きが戻ってこない。
        Vector2 gustSlow, gustFast, lurch;
        Vector2 bedOffset;
        Vector3 drift, driftVelocity;
        float surge, flare;

        public BonfireSimulation(ComputeShader source,Texture noiseTexture,int? seed=null)
        {
            compute=Object.Instantiate(source); noise=noiseTexture;
            random=seed.HasValue ? new System.Random(seed.Value) : new System.Random();
            clear=compute.FindKernel("Clear"); advectVelocity=compute.FindKernel("AdvectVelocity");
            vorticity=compute.FindKernel("Vorticity"); forces=compute.FindKernel("ApplyForces");
            divergence=compute.FindKernel("Divergence"); jacobi=compute.FindKernel("Jacobi");
            project=compute.FindKernel("Project"); advectState=compute.FindKernel("AdvectState");
            try
            {
                velocity=CreateTexture("Fire velocity A"); velocityNext=CreateTexture("Fire velocity B");
                state=CreateTexture("Fire state A"); stateNext=CreateTexture("Fire state B");
                pressure=CreateTexture("Fire pressure A"); pressureNext=CreateTexture("Fire pressure B");
                aux=CreateTexture("Fire scratch");
                compute.SetInts("_Resolution",Width,Height,Depth);
                Reset();
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

        void Dispatch(int kernel) { compute.Dispatch(kernel,Width/8,Height/8,Depth/4); }
        void Clear(RenderTexture texture) { compute.SetTexture(clear,"_Write",texture); Dispatch(clear); }

        public void Step(float deltaSeconds,float energy,float breath,float speed,float turbulence)
        {
            if(deltaSeconds<=0f) return;
            float duration=Mathf.Min(deltaSeconds,0.1f)*Mathf.Clamp(speed,0.25f,2f);
            int steps=Mathf.Clamp(Mathf.CeilToInt(duration/MaxStep),1,3);
            float dt=duration/steps;
            compute.SetFloat("_Dt",dt); compute.SetFloat("_Energy",Mathf.Clamp01(energy));
            compute.SetFloat("_Turbulence",turbulence*(1f+0.25f*Mathf.Clamp01(breath)));
            for(int j=0;j<steps;j++) { Drive(dt,energy,breath,turbulence); Solve(); }
        }

        void Drive(float dt,float energy,float breath,float turbulence)
        {
            gustSlow=Wander(gustSlow,2.6f,1f,dt); gustFast=Wander(gustFast,0.55f,1f,dt);
            bedOffset=Wander(bedOffset,3.2f,1f,dt);
            surge=Wander(surge,1.1f,1f,dt);
            // 不意の横風と、燃え上がり・息切れ。いつ来るかは乱数で決まる。
            lurch*=Mathf.Exp(-dt/0.45f);
            if(Chance(0.28f,dt)) lurch+=Direction()*Range(2.5f,6.5f);
            flare*=Mathf.Exp(-dt/0.55f);
            if(Chance(0.35f,dt)) flare+=Range(-0.34f,0.34f);
            driftVelocity=new Vector3(Wander1(driftVelocity.x,1.5f,0.5f,dt),Wander1(driftVelocity.y-1.25f,1.2f,0.45f,dt)+1.25f,Wander1(driftVelocity.z,1.5f,0.5f,dt));
            drift-=driftVelocity*dt;
            // 値が大きくなりすぎるとテクスチャ座標の精度が落ちる。ノイズは48周期なので戻しても連続。
            drift=new Vector3(Mathf.Repeat(drift.x,48f),Mathf.Repeat(drift.y,48f),Mathf.Repeat(drift.z,48f));

            float e=Mathf.Clamp01(energy);
            Vector2 gust=(gustSlow*0.9f+gustFast*0.8f+lurch*0.6f)*turbulence;
            float strength=Mathf.Clamp(1f+0.14f*surge+flare,0.55f,1.45f);
            float lit=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(0.003f,0.03f,e));
            float flame=(0.34f+0.66f*Mathf.Pow(e,0.75f))*strength*lit*(1f+0.08f*Mathf.Clamp01(breath));
            compute.SetVector("_Gust",new Vector3(gust.x,0,gust.y));
            compute.SetVector("_Drift",drift);
            compute.SetVector("_BedCenter",new Vector3(bedOffset.x*0.13f,0,bedOffset.y*0.07f));
            compute.SetFloat("_BedRadius",0.30f+0.70f*Mathf.Pow(e,0.70f));
            compute.SetFloat("_Flame",flame);
        }

        void Solve()
        {
            Bind(advectVelocity,velocity,velocityNext); Dispatch(advectVelocity); Swap(ref velocity,ref velocityNext);
            Bind(vorticity,velocity,aux); Dispatch(vorticity);
            Bind(forces,velocity,velocityNext); compute.SetTexture(forces,"_Aux",aux); compute.SetTexture(forces,"_State",state);
            compute.SetTexture(forces,"_Noise",noise); Dispatch(forces); Swap(ref velocity,ref velocityNext);
            Bind(divergence,velocity,aux); Dispatch(divergence);
            // 前回の圧力から始めるので、少ない反復で済む。
            compute.SetTexture(jacobi,"_Aux",aux);
            for(int k=0;k<PressureIterations;k++) { Bind(jacobi,pressure,pressureNext); Dispatch(jacobi); Swap(ref pressure,ref pressureNext); }
            Bind(project,velocity,velocityNext); compute.SetTexture(project,"_Aux",pressure); Dispatch(project); Swap(ref velocity,ref velocityNext);
            Bind(advectState,state,stateNext); compute.SetTexture(advectState,"_Velocity",velocity);
            compute.SetTexture(advectState,"_Noise",noise); Dispatch(advectState); Swap(ref state,ref stateNext);
        }

        void Bind(int kernel,RenderTexture read,RenderTexture write)
        { compute.SetTexture(kernel,"_Read",read); compute.SetTexture(kernel,"_Write",write); }
        static void Swap(ref RenderTexture a,ref RenderTexture b) { var c=a; a=b; b=c; }

        float Range(float min,float max) => min+(float)random.NextDouble()*(max-min);
        bool Chance(float perSecond,float dt) => random.NextDouble()<perSecond*dt;
        Vector2 Direction() { float a=Range(0f,Mathf.PI*2f); return new Vector2(Mathf.Cos(a),Mathf.Sin(a)*0.6f); }
        float Gaussian()
        {
            double u=1.0-random.NextDouble(), v=random.NextDouble();
            return (float)(System.Math.Sqrt(-2.0*System.Math.Log(u))*System.Math.Cos(2.0*System.Math.PI*v));
        }
        // 平均へ戻りながら乱数で動く連続値（Ornstein-Uhlenbeck過程）。
        float Wander1(float value,float tau,float sigma,float dt)
            => value+(-value/tau)*dt+sigma*Mathf.Sqrt(2f*dt/tau)*Gaussian();
        float Wander(float value,float tau,float sigma,float dt) => Wander1(value,tau,sigma,dt);
        Vector2 Wander(Vector2 value,float tau,float sigma,float dt)
            => new Vector2(Wander1(value.x,tau,sigma,dt),Wander1(value.y,tau,sigma,dt));

        public void Reset()
        {
            Clear(velocity); Clear(velocityNext); Clear(state); Clear(stateNext);
            Clear(pressure); Clear(pressureNext); Clear(aux);
            gustSlow=gustFast=lurch=bedOffset=Vector2.zero; driftVelocity=Vector3.zero; surge=flare=0f;
        }

        public void Dispose()
        {
            foreach(var texture in new[]{velocity,velocityNext,state,stateNext,pressure,pressureNext,aux})
            { if(texture) texture.Release(); Release(texture); }
            Release(compute);
            velocity=velocityNext=state=stateNext=pressure=pressureNext=aux=null;
        }
        static void Release(Object value)
        {
            if(!value) return;
            if(Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value);
        }
    }
}
