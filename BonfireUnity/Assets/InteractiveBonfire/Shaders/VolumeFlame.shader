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
            TEXTURE2D(_HeatProfile); SAMPLER(sampler_HeatProfile);
            TEXTURE2D(_FuelHistory); SAMPLER(sampler_FuelHistory);
            CBUFFER_START(UnityPerMaterial)
            float _Energy, _Breath, _Exposure, _Density, _AnimationTime, _MotionStrength, _UseSimulation, _FuelPeak;
            int _Steps;
            CBUFFER_END
            float4 _Sources[7];
            float4 _SourceMotion[7];
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o; o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS); return o;
            }
            float N(float3 p)
            {
                // 補間の速度も格子境界でつなぎ、炎の方向がカクッと変わるのを抑える。
                float3 cell = floor(p), f = frac(p);
                f = f*f*f*(f*(f*6.0-15.0)+10.0);
                return SAMPLE_TEXTURE3D_LOD(_NoiseTex, sampler_NoiseTex, (cell+f+0.5)/48.0, 0).r;
            }
            // 時間とともに上昇する連続ノイズ。格子を切り替えない。
            float Turbulence(float3 p) { return N(p) * 0.58 + N(p * 2.07 + 7.3) * 0.28 + N(p * 4.13 - 9.1) * 0.14; }
            float ShapeNoise(float3 p) { return SAMPLE_TEXTURE3D_LOD(_NoiseTex,sampler_NoiseTex,p/48.0,0).r; }
            float Fuel(float aboveRoot)
            {
                // 根元から毎秒約1.1mで運ばれる火力。成長と減衰で同じ形を逆再生しない。
                float index=clamp(max(0,aboveRoot)/1.1*30.0,0,127);
                return SAMPLE_TEXTURE2D_LOD(_FuelHistory,sampler_FuelHistory,float2((index+0.5)/128.0,0.5),0).r;
            }
            float PreviousAppearance(float3 p,out float heat,out float height)
            {
                // 一つ前の細い輪郭・重なり・内部の筋を再現。高さを火力で伸ばす処理は戻さない。
                float t=_AnimationTime;
                float3 flow=p*float3(3.7,2.7,4.5)-float3(0.12*t,3.5*t,-0.17*t);
                float3 warp=float3(ShapeNoise(flow+12.8),ShapeNoise(flow+26.2),ShapeNoise(flow+43.7))-0.5;
                float rise=saturate((p.y-0.13)/2.7);
                float3 q=p;
                q.xz+=warp.xz*(0.13+rise*(0.7+_Breath*0.15))*_MotionStrength;
                float2 bend=float2(N(float3(8.1,p.y*1.6-t*0.89,4.7)),N(float3(27.3,p.y*1.1-t*0.61,18.2)))-0.5;
                q.xz+=bend*rise*0.20*_MotionStrength;
                float field=0; height=0;
                [unroll] for(int j=0;j<7;j++)
                {
                    float4 s=_Sources[j];
                    float fuel=Fuel(p.y-s.y);
                    float activation=j==0?smoothstep(0.005,0.12,fuel):smoothstep(0.10+j*0.035,0.40+j*0.055,fuel);
                    float h=2.6*s.w*(0.83+0.24*ShapeNoise(float3(j*6.3,t*1.35,3.1)));
                    float y=(q.y-s.y)/max(0.05,h);
                    float gate=smoothstep(-0.04,0.04,y)*(1-smoothstep(0.72,1.02,y));
                    float r=(0.10+0.115*fuel)*pow(saturate(1-y),0.72)+0.012;
                    float age=p.y-s.y;
                    float2 drift=float2(N(float3(j*6.3+3.1,age*2.4-t*(1.13+j*0.037),12.9)),N(float3(j*4.7+19.2,age*2.1-t*(0.93+j*0.051),37.1)))*2-1;
                    float2 center=s.xz+drift*y*y*(0.07+0.12*fuel)*_MotionStrength;
                    float neck=N(float3(j*8.3+2.1,age*3.8-t*(1.67+j*0.043),23.4));
                    r*=lerp(0.87,1.10,neck);
                    float2 d=(q.xz-center)/float2(r,r*0.72);
                    float breakup=lerp(1.0,smoothstep(0.18,0.48,neck),smoothstep(0.45,0.95,y));
                    field+=exp(-dot(d,d)*1.65)*gate*activation*breakup;
                    height=max(height,saturate(y)*exp(-dot(d,d)));
                }
                float baseBed=exp(-dot(q.xz/float2(0.65,0.32),q.xz/float2(0.65,0.32))*2.4);
                baseBed*=smoothstep(0.04,0.15,p.y)*(1-smoothstep(0.25,0.55,p.y));
                field+=baseBed*_Energy*0.26;
                float3 detailFlow=flow*1.6;
                float n=ShapeNoise(detailFlow)*0.58+ShapeNoise(detailFlow*2.07+7.3)*0.28+ShapeNoise(detailFlow*4.13-9.1)*0.14;
                float fine=ShapeNoise(flow*float3(4,1.3,4)+warp*3);
                float iso=0.19+0.27*n;
                float body=smoothstep(iso-0.045,iso+0.16,field);
                float membrane=exp(-abs(field-iso)*5.5)*smoothstep(0.035,0.18,field);
                float filaments=pow(saturate(1-abs(fine*2-1)),3);
                heat=saturate(0.20+membrane*0.52+filaments*0.20-height*0.20+(1-rise)*0.14);
                return (membrane*(0.65+0.35*filaments)+body*0.085)*smoothstep(0.002,0.04,Fuel(p.y-0.30));
            }
            float Field(float3 p, out float heat, out float height)
            {
                if(_UseSimulation>0.5)
                {
                    // 輪郭と立ち上がりを分離。移流した熱が届いた場所だけ、旧版の見た目で発光する。
                    float profileY=(0.30+(p.y-0.30)*0.68+0.15)/3.6;
                    float temperature=SAMPLE_TEXTURE2D_LOD(_HeatProfile,sampler_HeatProfile,float2(profileY,0.5),0).r;
                    float arrival=smoothstep(0.045,0.17,temperature);
                    if(arrival<0.001) { heat=0; height=0; return 0; }
                    return PreviousAppearance(p,heat,height)*arrival;
                }
                float t = _AnimationTime;
                // 高さに対して時間を遅らせ、根元の変化が上部へ運ばれる流れを作る。
                float3 flow = p * float3(3.4, 2.8, 4.0) - float3(0.08*t, 2.1*t, -0.06*t);
                float3 warp = float3(N(flow+12.8), N(flow+26.2), N(flow+43.7)) - 0.5;
                float rise = saturate((p.y - 0.13) / 2.7);
                float3 q = p;
                float rootAnchor = smoothstep(0.16,0.72,p.y);
                float2 broadBend = float2(N(float3(7.1,p.y*1.3-t*0.72,2.4)),N(float3(23.7,p.y*1.1-t*0.63,19.2)))-0.5;
                q.xz += rootAnchor * _MotionStrength * (broadBend*0.24 + warp.xz*(0.12+rise*0.32+_Breath*0.06));
                float field = 0;
                height = 0;
                [unroll] for (int j=0; j<7; j++)
                {
                    float4 s = _Sources[j];
                    float4 motion = _SourceMotion[j];
                    float fuel=Fuel(p.y-s.y);
                    float activation = j == 0 ? smoothstep(0.005,0.12,fuel) : smoothstep(0.10+j*0.035,0.40+j*0.055,fuel);
                    float h = 2.6 * s.w;
                    h *= motion.x;
                    float aboveRoot = q.y-s.y;
                    float y = aboveRoot / max(0.05,h);
                    float gate = smoothstep(-0.04,0.04,y) * (1-smoothstep(0.80,1.06,y));
                    float r = (0.10 + 0.115*fuel) * pow(saturate(1-y),0.72) + 0.012;
                    // 流れはワールド空間の高さで評価する。火力が変わっても既存の炎を縦に引き伸ばさない。
                    float age = aboveRoot*2.15-t*(1.15+j*0.047);
                    float2 localFlow = float2(N(float3(j*7.3,age,9.2)),N(float3(j*7.3+21.4,age*0.91,31.7)))-0.5;
                    float neck = N(float3(j*5.19+4.7,aboveRoot*3.05-t*(1.52+j*0.033),42.3));
                    r *= motion.y * (0.82+0.32*neck);
                    float2 center = s.xz + localFlow * pow(saturate(aboveRoot/1.8),1.45) * (0.22+0.18*fuel) * _MotionStrength;
                    float2 d = (q.xz-center)/float2(r,r*0.72);
                    // 上部だけにくびれを通すことで、先端が細まり、離れて消える。
                    float breakup = lerp(1.0,smoothstep(0.19,0.52,neck),smoothstep(0.43,0.95,y));
                    float core = exp(-dot(d,d)*1.65);
                    // 分岐は常設の柱にせず、移流する短い反応帯にだけ生じさせる。
                    float branchGate = smoothstep(0.28,0.55,y)*(1-smoothstep(0.83,1.02,y));
                    float2 forkCenter = center + float2(motion.w*(0.055+0.095*y),localFlow.y*0.06);
                    float2 forkD = (q.xz-forkCenter)/float2(r*0.58,r*0.48);
                    float fork = exp(-dot(forkD,forkD)*1.65)*branchGate*motion.z*smoothstep(0.33,0.65,neck);
                    // 小さな燃焼帯が根元から上へ通過する。固定した柱の輪郭を持ち上げる動きにしない。
                    float packet = N(float3(j*13.17+2.9,aboveRoot*3.3-t*(1.74+j*0.041),17.6));
                    float packetDensity = lerp(0.62,1.18,smoothstep(0.24,0.73,packet));
                    field += (core+fork) * gate * activation * breakup * packetDensity;
                    height = max(height, saturate(y)*exp(-dot(d,d)));
                }
                // 薪の隙間に連続する低い燃焼層。高所へは伸びない。
                float baseBed = exp(-dot(q.xz/float2(0.65,0.32),q.xz/float2(0.65,0.32))*2.4);
                baseBed *= smoothstep(0.04,0.15,p.y)*(1-smoothstep(0.25,0.55,p.y));
                field += baseBed * _Energy * 0.26;
                float n = Turbulence(flow*1.6);
                float fine = N(p*float3(11,4.2,12)-float3(0.11*t,2.5*t,-0.09*t)+warp*1.8);
                // 密度を連続変調し、穴を硬く切り抜く処理を避ける。
                float iso = 0.19 + 0.27*n;
                float body = smoothstep(iso-0.045,iso+0.16,field);
                // 炎の反応帯は薄い膜として強く光り、内部は淡く透ける。
                // 空間全体の密度を高くすることで黄色い塊になるのを防ぐ。
                float membrane = exp(-abs(field-iso)*5.5) * smoothstep(0.035,0.18,field);
                float filaments = pow(saturate(1-abs(fine*2-1)),3);
                float density = membrane*(0.65+0.35*filaments) + body*0.085;
                heat = saturate(0.20 + membrane*0.52 + filaments*0.20 - height*0.20 + (1-rise)*0.14);
                return density * smoothstep(0.002,0.04,Fuel(p.y-0.30));
            }
            half4 Frag(Varyings i) : SV_Target
            {
                if (_FuelPeak < 0.002) return 0;
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
                float jitter = frac(sin(dot(i.positionCS.xy,float2(12.9898,78.233)))*43758.5453);
                [loop] for (int k=0;k<128;k++)
                {
                    if(k>=steps || result.a>0.985) break;
                    // 画素ごとに積分開始位置をずらし、薄い層の等高線状エイリアスを抑える。
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
