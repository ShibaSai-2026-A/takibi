using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace InteractiveBonfire.Editor
{
    public static class BonfireValidation
    {
        public static void Recession()
        {
            try
            {
                EditorSceneManager.OpenScene("Assets/InteractiveBonfire/Generated/Bonfire.unity");
                var rig=UnityEngine.Object.FindAnyObjectByType<BonfireBootstrap>();
                rig.microphoneEnabled=false; rig.Construct(); var c=rig.Controller;
                const float dt=1f/30f;
                // 根元が消えた後も、上へ運ばれた履歴が残ることを確認する。
                c.volume.ResetSimulation();
                for(int k=0;k<90;k++) c.volume.AdvanceAnimation(dt,1,1);
                for(int k=0;k<9;k++) c.volume.AdvanceAnimation(dt,0,0);
                if(c.volume.FuelAtAge(0)>0.001f || c.volume.FuelAtAge(2)<0.99f)
                    throw new Exception("Fuel must travel upward instead of shrinking simultaneously");
                for(int k=0;k<150;k++) c.volume.AdvanceAnimation(dt,0,0);
                if(c.volume.FuelAtAge(4)>0.001f) throw new Exception("Fuel tail did not clear");
                c.volume.ResetSimulation();
                for(int k=0;k<210;k++) c.volume.SimulatePreview(dt,1,1,0.9f);
                Directory.CreateDirectory("Verification/Recession"); c.energy=1;
                c.fireLight.intensity=3.9f; c.woodMaterial.SetFloat("_Heat",1);
                Capture(rig,"Recession/maximum",960,540);
                double clock=c.volume.AnimationTime;
                for(int k=1;k<=780;k++)
                {
                    c.AdvanceEnergy(0,dt); c.volume.SimulatePreview(dt,c.energy,0,0.9f);
                    if(c.volume.AnimationTime<=clock) throw new Exception("Animation reversed or stopped");
                    clock=c.volume.AnimationTime;
                    c.fireLight.intensity=0.18f+c.energy*4;
                    c.woodMaterial.SetFloat("_Heat",0.18f+c.energy*0.82f);
                    if(k<=216 && k%6==0) Capture(rig,"Recession/frame-"+(k/6).ToString("D3"),960,540);
                    if(k%120==0 || k==780) Capture(rig,"Recession/decay-"+(k/30).ToString("D2")+"s",960,540);
                }
                if(c.energy>0.004f || c.volume.FuelAtAge(3)>0.004f) throw new Exception("Fire did not settle");
                if(ShaderUtil.ShaderHasError(Shader.Find("InteractiveBonfire/VolumeFlame"))) throw new Exception("Flame shader error");
                File.WriteAllText("Verification/Recession/result.txt","Unity "+Application.unityVersion+"\nGPU "+SystemInfo.graphicsDeviceName+"\nFuel-history assertions passed: root stops first; two-second-old fuel survives; tail clears.\nAnimation clock stays forward through 26-second controller decay.\nShader check and 960x540 Editor renders passed; 36 decay frames at 5fps plus 4/8/12/16/20/24/26-second snapshots.\nNo live Play, microphone, 1080p60 or projector validation. Visual naturalness requires exhibition review.\n");
                EditorApplication.Exit(0);
            }
            catch(Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        public static void Motion()
        {
            try
            {
                // ユーザーの生成済みシーンとURPアセットは保存し直さない。
                EditorSceneManager.OpenScene("Assets/InteractiveBonfire/Generated/Bonfire.unity");
                var rig=UnityEngine.Object.FindAnyObjectByType<BonfireBootstrap>();
                rig.microphoneEnabled=false; rig.Construct();
                var c=rig.Controller;
                c.volume.SetFire(1,1,0.9f);
                c.fireLight.intensity=3.9f; c.woodMaterial.SetFloat("_Heat",1f);
                const string folder="Verification/ShapeRestored";
                Directory.CreateDirectory(folder);
                double previous=c.volume.AnimationTime;
                for(int k=0;k<180;k++)
                {
                    c.volume.AdvanceAnimation(1f/60f,k<90?1f:0.1f,k<90?1f:0f);
                    double next=c.volume.AnimationTime;
                    if(next<=previous || next-previous>0.021) throw new Exception("Animation phase discontinuity");
                    previous=next;
                }
                c.volume.ResetSimulation();
                for(int k=0;k<390;k++) c.volume.SimulatePreview(1f/60f,1f,1f,0.9f);
                const int count=48;
                for(int k=0;k<count;k++)
                {
                    if(k>0) c.volume.SimulatePreview(1f/15f,1f,1f,0.9f);
                    Capture(rig,"ShapeRestored/frame-"+k.ToString("D3"),960,540);
                }
                foreach(float energy in new[]{0.12f,0.35f,0.65f})
                {
                    c.volume.ResetSimulation();
                    for(int k=0;k<240;k++) c.volume.SimulatePreview(1f/60f,energy,0.8f,0.9f);
                    c.fireLight.intensity=0.18f+energy*4; c.woodMaterial.SetFloat("_Heat",0.18f+energy*0.82f);
                    Capture(rig,"ShapeRestored/energy-"+Mathf.RoundToInt(energy*100));
                }
                c.volume.ResetSimulation(); c.energy=0;
                for(int k=1;k<=840;k++)
                {
                    c.AdvanceEnergy(1f,1f/60f);
                    c.volume.SimulatePreview(1f/60f,c.energy,1f,0.9f);
                    if(k==60 || k==180 || k==360 || k==600 || k==840)
                    {
                        c.fireLight.intensity=0.18f+c.energy*4;
                        c.woodMaterial.SetFloat("_Heat",0.18f+c.energy*0.82f);
                        Capture(rig,"ShapeRestored/growth-"+(k/60).ToString("D2")+"s",960,540);
                    }
                }
                if(c.energy<0.90f) throw new Exception("Slow growth regression");
                if(ShaderUtil.ShaderHasError(Shader.Find("InteractiveBonfire/VolumeFlame"))) throw new Exception("Flame shader compilation failed");
                File.WriteAllText(folder+"/result.txt","Unity "+Application.unityVersion+"\nGPU "+SystemInfo.graphicsDeviceName+"\nC# compilation and flame shader check passed.\nAnimation phase remains continuous across energy/breath changes.\nGPU advected density/temperature: 64x96x48 cells, two ARGBHalf textures, 96x1 heat arrival profile.\nAppearance: previous procedural silhouette and fine membranes; rise/fade: advected heat arrival.\n48 Editor render-request frames at 960x540, 6.5 seconds warm-up then 3.133 seconds advance (15 fps preview).\nAdditional 4-second simulations at energy 0.12, 0.35, 0.65.\nSlow growth snapshots at 1, 3, 6, 10 and 14 seconds; final energy="+c.energy+".\nThis is an offline preview; live Play, USB input, 1080p/60fps and projector output remain unverified.\n");
                EditorApplication.Exit(0);
            }
            catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
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
                c.volume.ResetSimulation();
                for(int k=0;k<390;k++) c.volume.SimulatePreview(1f/60f,1f,1f,0.9f);
                c.fireLight.intensity=3.9f; c.woodMaterial.SetFloat("_Heat",1f); Capture(rig,"maximum");
                for(int k=0;k<48;k++) c.volume.SimulatePreview(1f/60f,1f,1f,0.9f);
                Capture(rig,"maximum-motion");
                c.volume.ResetSimulation();
                for(int k=0;k<240;k++) c.volume.SimulatePreview(1f/60f,0.08f,0f,0.9f);
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
        static void Capture(BonfireBootstrap rig,string name,int width=1280,int height=720)
        {
            var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32); rt.Create();
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
