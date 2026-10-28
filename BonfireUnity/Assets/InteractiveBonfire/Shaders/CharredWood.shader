Shader "InteractiveBonfire/CharredWood"
{
    Properties { _Heat("Ember heat", Range(0,1)) = 0.2 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
        struct V { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; };
        V Vert(A v) { V o; o.positionWS=TransformObjectToWorld(v.positionOS.xyz); o.positionCS=TransformWorldToHClip(o.positionWS); o.normalWS=TransformObjectToWorldNormal(v.normalOS); o.uv=v.uv; return o; }
        CBUFFER_START(UnityPerMaterial)
        float _Heat;
        CBUFFER_END
        float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
        float Noise(float2 p) { float2 f=frac(p); f=f*f*(3-2*f); float2 i=floor(p); return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y); }
        ENDHLSL
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            half4 Frag(V i):SV_Target
            {
                float2 uv=i.uv;
                float broad=Noise(uv*float2(14,6));
                float grain=Noise(uv*float2(95,9)+broad*2);
                float small=Noise(uv*float2(210,60));
                float cracks=1-smoothstep(0.04,0.11,abs(sin(uv.x*105+Noise(uv*float2(19,8))*4)));
                cracks*=smoothstep(0.30,0.63,Noise(uv*float2(28,11)));
                float cells=Noise(uv*float2(24,13));
                float3 albedo=lerp(float3(0.014,0.008,0.005),float3(0.13,0.061,0.024),broad*0.6+grain*0.4);
                albedo*=0.45+0.7*small;
                albedo*=1-cracks*0.8;
                // UVのv>2は断面。木口に同心円の木目を追加する。
                if(uv.y>2) { float2 q=float2(uv.x,uv.y-3)*2-1; float ring=0.5+0.5*sin(length(q)*72+Noise(q*8)*3); albedo=lerp(float3(0.025,0.012,0.006),float3(0.15,0.072,0.027),ring*0.4); }
                float3 n=normalize(i.normalWS);
                // 手続き樹皮の微細凹凸。シルエットの凹凸はメッシュ側で作る。
                n=normalize(n+float3((grain-.5)*0.15,(small-.5)*0.12,0));
                float3 illumination=float3(0.045,0.035,0.025);
                Light mainLight=GetMainLight();
                illumination+=mainLight.color*saturate(dot(n,mainLight.direction))*mainLight.distanceAttenuation;
                uint count=GetAdditionalLightsCount();
                for(uint k=0;k<count;k++) { Light l=GetAdditionalLight(k,i.positionWS); illumination+=l.color*saturate(dot(n,l.direction))*l.distanceAttenuation; }
                float emberRegion=(1-smoothstep(0.28,0.90,length(i.positionWS.xz)))*smoothstep(0.1,0.42,cells);
                float hot=cracks*emberRegion*(0.18+_Heat*1.4);
                if(uv.y>2) hot*=0.08;
                float3 emission=float3(1.8,0.12,0.008)*hot;
                // URPのPBR応答を利用。非金属で粗い炭化木を想定する。
                InputData lighting=(InputData)0;
                lighting.positionWS=i.positionWS; lighting.normalWS=n;
                lighting.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);
                lighting.shadowCoord=TransformWorldToShadowCoord(i.positionWS);
                lighting.bakedGI=float3(0.015,0.009,0.004);
                lighting.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);
                lighting.shadowMask=half4(1,1,1,1);
                SurfaceData surface=(SurfaceData)0;
                surface.albedo=albedo; surface.metallic=0; surface.specular=half3(0.04,0.04,0.04);
                surface.smoothness=0.12; surface.normalTS=half3(0,0,1);
                surface.emission=emission; surface.occlusion=1; surface.alpha=1;
                return UniversalFragmentPBR(lighting,surface);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Depth
            half4 Depth(V i):SV_Target { return 0; }
            ENDHLSL
        }
    }
}
