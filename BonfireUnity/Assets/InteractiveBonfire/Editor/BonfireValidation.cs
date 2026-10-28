using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace InteractiveBonfire.Editor
{
    public static class BonfireValidation
    {
        // Editorの直接描画。Playモードや実際のUSB入力の検証とは区別する。
        public static void Begin()
        {
            try
            {
                BonfireSceneBuilder.Generate();
                var rig=UnityEngine.Object.FindAnyObjectByType<BonfireBootstrap>();
                rig.microphoneEnabled=false; rig.Construct();
                var c=rig.Controller;
                const float dt=1f/60f;
                float previous=0;
                for(int k=0;k<840;k++)
                {
                    c.AdvanceEnergy(1,dt);
                    if(c.energy-previous>c.maximumGrowthPerSecond*dt+0.00001f) throw new Exception("Growth rate limit failed");
                    if(c.energy<previous) throw new Exception("Growth monotonicity failed");
                    previous=c.energy;
                }
                float grown=c.energy;
                if(grown<0.90f) throw new Exception("Growth failed: "+grown);
                for(int k=0;k<1440;k++)
                {
                    c.AdvanceEnergy(0,dt);
                    if(c.energy>previous) throw new Exception("Decay monotonicity failed");
                    previous=c.energy;
                }
                if(c.energy>0.004f) throw new Exception("Decay failed: "+c.energy);
                Directory.CreateDirectory("Verification");
                c.volume.SetFire(0,0,0.9f); c.fireLight.intensity=0.18f; c.woodMaterial.SetFloat("_Heat",0.18f);
                Capture(rig,"embers");
                c.volume.SetFire(1,1,0.9f); c.volume.SetAnimationTime(6.5f);
                c.fireLight.intensity=3.9f; c.woodMaterial.SetFloat("_Heat",1f); Capture(rig,"maximum");
                c.volume.SetAnimationTime(7.3f); Capture(rig,"maximum-motion");
                c.volume.SetFire(0.08f,0,0.9f); c.volume.SetAnimationTime(13f);
                c.fireLight.intensity=0.48f; c.woodMaterial.SetFloat("_Heat",0.25f); Capture(rig,"decay");
                foreach(string name in new[]{"VolumeFlame","CharredWood","EmberParticle"})
                {
                    var shader=Shader.Find("InteractiveBonfire/"+name);
                    if(ShaderUtil.ShaderHasError(shader)) throw new Exception("Shader compilation failed: "+name);
                }
                File.WriteAllText("Verification/runtime-result.txt","Unity "+Application.unityVersion+"\nGraphics: "+SystemInfo.graphicsDeviceName+"\nAPI: "+SystemInfo.graphicsDeviceType+"\nC# compiled; scene/material generation; scene object construction; shader error checks; 1280x720 Editor render requests passed.\nEnvelope fixed-step assertions passed: 14 seconds growth="+grown+", 24 seconds decay="+c.energy+"\nPlay mode, USB microphone, keyboard hardware, projector, standalone build and 60fps are not verified by this run.\n");
                EditorApplication.Exit(0);
            }
            catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void Capture(BonfireBootstrap rig,string name)
        {
            var rt=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32); rt.Create();
            var request=new UniversalRenderPipeline.SingleCameraRequest { destination=rt };
            RenderPipeline.SubmitRenderRequest(rig.ViewCamera,request);
            var old=RenderTexture.active; RenderTexture.active=rt;
            var texture=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); texture.Apply();
            File.WriteAllBytes("Verification/"+name+".png",texture.EncodeToPNG());
            RenderTexture.active=old; UnityEngine.Object.DestroyImmediate(texture); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
