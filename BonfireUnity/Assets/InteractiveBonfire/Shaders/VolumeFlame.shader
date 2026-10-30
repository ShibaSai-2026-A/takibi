Shader "InteractiveBonfire/VolumeFlame"
{
    Properties
    {
        _NoiseTex("3D Noise", 3D) = "white" {}
        _Energy("Energy", Range(0,1)) = 0
        _Breath("Breath", Range(0,1)) = 0
        _Exposure("Exposure", Float) = 0.9
        _Density("Density", Float) = 1
        _Steps("Ray steps", Int) = 64
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "VolumeFlame"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE3D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            TEXTURE3D(_SimTex); SAMPLER(sampler_SimTex);
            CBUFFER_START(UnityPerMaterial)
            float _Energy, _Breath, _Exposure, _Density, _AnimationTime, _MotionStrength;
            int _Steps;
            CBUFFER_END
            static const float3 BoxMin=float3(-1.55,-0.15,-1.1);
            static const float3 BoxSize=float3(3.1,3.6,2.2);
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o; o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS); return o;
            }
            float3 ShapeNoise(float3 p) { return SAMPLE_TEXTURE3D_LOD(_NoiseTex,sampler_NoiseTex,p/48.0,0).rgb; }
            float4 Sim(float3 p) { return SAMPLE_TEXTURE3D_LOD(_SimTex,sampler_SimTex,(p-BoxMin)/BoxSize,0); }
            float Field(float3 p, out float heat, out float height)
            {
                // 形は流体の燃焼量 Y の等値面だけで決まる。ここで輪郭や高さを作らない。
                heat=0; height=0;
                if(Sim(p).r<0.004) return 0;
                float t=_AnimationTime;
                float rise=saturate((p.y-0.13)/2.7);
                float3 flow=p*float3(3.7,2.7,4.5)-float3(0.12*t,3.5*t,-0.17*t);
                float3 warp=ShapeNoise(flow)-0.5;
                float3 crinkle=ShapeNoise(flow*float3(2.9,1.7,2.9)+warp*1.5+31.0)-0.5;
                // 格子より細かい襞だけをノイズで足す。大きな動きは流体に任せる。
                float3 q=p;
                q+=(warp*float3(0.10,0.05,0.10)+crinkle*float3(0.055,0.025,0.055))*(0.25+rise)*_MotionStrength;
                float4 s=Sim(q);
                float y=s.r, gradient=max(s.b,0.35);
                float n=ShapeNoise(flow*1.6).r;
                float iso=0.055+0.085*n;
                float distance=(y-iso)/gradient;
                // 炎の反応帯は薄い膜として強く光り、内部は淡く透ける。
                float membrane=exp(-distance*distance/(0.024*0.024));
                float body=smoothstep(-0.006,0.028,distance);
                // 内部の模様は Y の等値線に沿う。流れと一緒に変形する襞になり、形と別に動かない。
                float fold=0.5+0.5*cos((y*2.3+n*1.9+warp.y*1.5)*6.2832);
                fold=fold*fold*smoothstep(iso,iso+0.16,y);
                heat=saturate(saturate(y*1.5)*0.78+membrane*0.15+fold*0.14);
                height=saturate(1-y*1.6);
                return membrane*0.42+body*(0.30+0.34*fold);
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float3 ro = GetCameraPositionWS();
                float3 rd = normalize(i.positionWS-ro);
                float3 lo = TransformWorldToObject(ro);
                float3 ld = mul((float3x3)unity_WorldToObject,rd);
                float3 safeDir = lerp(-1.0,1.0,step(0,ld)) * max(abs(ld),0.00001);
                float3 a = (-0.5-lo)/safeDir, b = (0.5-lo)/safeDir;
                float3 nearV = min(a,b), farV=max(a,b);
                float start = max(0,max(nearV.x,max(nearV.y,nearV.z)));
                float finish = min(farV.x,min(farV.y,farV.z));
                float2 uv = i.positionCS.xy / _ScaledScreenParams.xy;
                float rawDepth = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE,1,rawDepth);
                #endif
                float3 opaqueWS = ComputeWorldSpacePosition(uv,rawDepth,UNITY_MATRIX_I_VP);
                finish = min(finish,dot(opaqueWS-ro,rd));
                // 炎が出ない奥行きを避け、同じ歩数で視線方向の細かさを上げる。
                float slabA=(-0.95-ro.z)/rd.z, slabB=(0.95-ro.z)/rd.z;
                start=max(start,min(slabA,slabB)); finish=min(finish,max(slabA,slabB));
                if (finish<=start) return 0;
                int steps = clamp(_Steps,24,128);
                float ds = (finish-start)/steps;
                float4 result = 0;
                float jitter = frac(sin(dot(i.positionCS.xy,float2(12.9898,78.233)))*43758.5453);
                [loop] for (int k=0;k<128;k++)
                {
                    if(k>=steps || result.a>0.985) break;
                    // 画素ごとに積分開始位置をずらし、薄い層の等高線状エイリアスを抑える。
                    float distance = start+(k+0.2+0.6*jitter)*ds;
                    float3 p=ro+rd*distance;
                    float heat, height;
                    float d = Field(p,heat,height);
                    // 炎はほぼ透明で、光が視線方向に足し合わさる。厚い所ほど明るく黄色くなり、薄い縁は赤橙に残る。
                    float amount=d*ds*_Density;
                    float3 color=lerp(float3(1.0,0.16,0.012),float3(1.0,0.34,0.045),smoothstep(0.1,0.8,heat));
                    color*=_Exposure*(6.0+13.0*heat)*(0.72+0.28*(1-height));
                    result.rgb+=(1-result.a)*amount*color;
                    result.a+=(1-result.a)*(1-exp(-amount*1.3));
                }
                return result;
            }
            ENDHLSL
        }
    }
}
