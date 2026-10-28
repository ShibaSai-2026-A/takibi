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
            CBUFFER_START(UnityPerMaterial)
            float _Energy, _Breath, _Exposure, _Density, _AnimationTime;
            int _Steps;
            CBUFFER_END
            float4 _Sources[7];
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o; o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS); return o;
            }
            float N(float3 p) { return SAMPLE_TEXTURE3D_LOD(_NoiseTex, sampler_NoiseTex, p / 48.0, 0).r; }
            // 時間とともに上昇する連続ノイズ。格子を切り替えない。
            float Turbulence(float3 p) { return N(p) * 0.58 + N(p * 2.07 + 7.3) * 0.28 + N(p * 4.13 - 9.1) * 0.14; }
            float Field(float3 p, out float heat, out float height)
            {
                float t = _AnimationTime;
                float3 flow = p * float3(3.7, 2.7, 4.5) - float3(0.12*t, t*(1.9+_Energy*1.6), -0.17*t);
                float3 warp = float3(N(flow+12.8), N(flow+26.2), N(flow+43.7)) - 0.5;
                float rise = saturate((p.y - 0.13) / 2.7);
                float3 q = p;
                q.xz += warp.xz * (0.13 + rise * (0.7 + _Breath * 0.15));
                q.x += sin(p.y*3.9-t*2.2)*rise*0.10;
                float field = 0;
                height = 0;
                [unroll] for (int j=0; j<7; j++)
                {
                    float4 s = _Sources[j];
                    float activation = j == 0 ? smoothstep(0.005,0.12,_Energy) : smoothstep(0.10+j*0.035,0.40+j*0.055,_Energy);
                    float h = (0.10 + 2.5*pow(_Energy,0.78)) * s.w;
                    h *= 0.83 + 0.24*N(float3(j*6.3, t*1.35, 3.1));
                    float y = (q.y-s.y) / max(0.05,h);
                    float gate = smoothstep(-0.04,0.04,y) * (1-smoothstep(0.72,1.02,y));
                    float r = (0.10 + 0.115*_Energy) * pow(saturate(1-y),0.72) + 0.012;
                    float2 center = s.xz + float2(sin(y*6.0-t*1.6+j),cos(y*4.2-t*1.3+j*1.8)) * y*y * (0.07+0.12*_Energy);
                    float2 d = (q.xz-center)/float2(r,r*0.72);
                    field += exp(-dot(d,d)*1.65) * gate * activation;
                    height = max(height, saturate(y)*exp(-dot(d,d)));
                }
                // 薪の隙間に連続する低い燃焼層。高所へは伸びない。
                float baseBed = exp(-dot(q.xz/float2(0.65,0.32),q.xz/float2(0.65,0.32))*2.4);
                baseBed *= smoothstep(0.04,0.15,p.y)*(1-smoothstep(0.25,0.55,p.y));
                field += baseBed * _Energy * 0.26;
                float n = Turbulence(flow*1.6);
                float fine = N(flow * float3(4.0,1.3,4.0) + warp*3.0);
                // 密度を連続変調し、穴を硬く切り抜く処理を避ける。
                float iso = 0.19 + 0.27*n;
                float body = smoothstep(iso-0.045,iso+0.16,field);
                // 炎の反応帯は薄い膜として強く光り、内部は淡く透ける。
                // 空間全体の密度を高くすることで黄色い塊になるのを防ぐ。
                float membrane = exp(-abs(field-iso)*5.5) * smoothstep(0.035,0.18,field);
                float filaments = pow(saturate(1-abs(fine*2-1)),3);
                float density = membrane*(0.65+0.35*filaments) + body*0.085;
                heat = saturate(0.20 + membrane*0.52 + filaments*0.20 - height*0.20 + (1-rise)*0.14);
                return density * smoothstep(0.002,0.04,_Energy);
            }
            half4 Frag(Varyings i) : SV_Target
            {
                if (_Energy < 0.002) return 0;
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
                if (finish<=start) return 0;
                int steps = clamp(_Steps,24,128);
                float ds = (finish-start)/steps;
                float4 result = 0;
                [loop] for (int k=0;k<128;k++)
                {
                    if(k>=steps || result.a>0.985) break;
                    // 画素ごとに積分開始位置をずらし、薄い層の等高線状エイリアスを抑える。
                    float jitter = frac(sin(dot(i.positionCS.xy,float2(12.9898,78.233)))*43758.5453);
                    float distance = start+(k+0.2+0.6*jitter)*ds;
                    float3 p=ro+rd*distance;
                    float heat, height;
                    float d = Field(p,heat,height);
                    float alpha=1-exp(-d*ds*3.8*_Density);
                    float3 color = lerp(float3(1.0,0.055,0.003),float3(2.6,0.82,0.045),smoothstep(0.18,0.64,heat));
                    color=lerp(color,float3(3.8,2.65,0.86),smoothstep(0.62,0.88,heat));
                    color*= _Exposure*(0.72+0.28*(1-height));
                    result.rgb += (1-result.a)*alpha*color;
                    result.a += (1-result.a)*alpha;
                }
                return result;
            }
            ENDHLSL
        }
    }
}
