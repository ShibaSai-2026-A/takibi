using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace InteractiveBonfire.Editor
{
    // 動きを目で確認するための撮影。成長・最大火力の連続・減衰を1枚の一覧画像にまとめる。
    // 出力先は環境変数 BONFIRE_REVIEW_DIR（未指定なら Logs/Review）。
    public static class BonfireReview
    {
        const float Step=1f/60f;
        // 炎の周りだけを切り出す
        const int CameraWidth=1280, CameraHeight=720, FrameWidth=480, FrameHeight=540, CropX=400, CropY=80, Columns=9;

        public static void Run()
        {
            try
            {
                string folder=Environment.GetEnvironmentVariable("BONFIRE_REVIEW_DIR");
                if(string.IsNullOrEmpty(folder)) folder="Logs/Review";
                Directory.CreateDirectory(folder);
                EditorSceneManager.OpenScene("Assets/InteractiveBonfire/Generated/Bonfire.unity");
                var rig=UnityEngine.Object.FindAnyObjectByType<BonfireBootstrap>();
                rig.microphoneEnabled=false; rig.Construct();
                var c=rig.Controller;

                // 成長：何もない状態から2秒ごと
                c.volume.ResetSimulation(); c.energy=0;
                var growth=new List<Texture2D>();
                for(int k=1;k<=18;k++) { Advance(c,1,2f); growth.Add(Shot(rig,c)); }
                Sheet(growth,Path.Combine(folder,"growth-2s.png"));

                // 最大火力：0.1秒ごとの連続（予測できない動きか、一体で変形するかを見る）
                Advance(c,1,6f);
                var steady=new List<Texture2D>();
                for(int k=0;k<36;k++) { if(k>0) Advance(c,1,0.1f); steady.Add(Shot(rig,c)); }
                Sheet(steady,Path.Combine(folder,"steady-0.1s.png"));

                // 全画面の1枚（細部の質感を見る）
                SaveFull(rig,Path.Combine(folder,"full.png"));

                // 最大火力：1秒ごと（大きな形の変化を見る）
                var slow=new List<Texture2D>();
                for(int k=0;k<18;k++) { Advance(c,1,1f); slow.Add(Shot(rig,c)); }
                Sheet(slow,Path.Combine(folder,"steady-1s.png"));

                // 減衰：息を止めてから1秒ごと（巻き戻しに見えないかを見る）
                var decay=new List<Texture2D>();
                for(int k=0;k<24;k++) { Advance(c,0,1f); decay.Add(Shot(rig,c)); }
                Sheet(decay,Path.Combine(folder,"decay-1s.png"));

                // 減衰の途中：0.2秒ごとの連続
                c.volume.ResetSimulation(); c.energy=0;
                Advance(c,1,16f); Advance(c,0,4f);
                var decayMotion=new List<Texture2D>();
                for(int k=0;k<36;k++) { if(k>0) Advance(c,0,0.2f); decayMotion.Add(Shot(rig,c)); }
                Sheet(decayMotion,Path.Combine(folder,"decay-0.2s.png"));
                Debug.Log("Bonfire frame cost at 1080p: "+MeasureFrame(rig,c).ToString("F2")+" ms");
                Debug.Log("Bonfire review captured: "+folder+" GPU="+SystemInfo.graphicsDeviceName);
                EditorApplication.Exit(0);
            }
            catch(Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        // 成長→維持→減衰の通しを連番画像で書き出す（動画の素材）。
        public static void Movie()
        {
            try
            {
                string folder=Environment.GetEnvironmentVariable("BONFIRE_REVIEW_DIR");
                if(string.IsNullOrEmpty(folder)) folder="Logs/Review";
                Directory.CreateDirectory(folder);
                EditorSceneManager.OpenScene("Assets/InteractiveBonfire/Generated/Bonfire.unity");
                var rig=UnityEngine.Object.FindAnyObjectByType<BonfireBootstrap>();
                rig.microphoneEnabled=false; rig.Construct();
                var c=rig.Controller;
                c.volume.ResetSimulation(); c.energy=0;
                const float frame=1f/30f;
                int index=0;
                for(float t=0;t<44f;t+=frame)
                {
                    Advance(c,t<20f?1:0,frame);
                    var texture=Shot(rig,c);
                    File.WriteAllBytes(Path.Combine(folder,"f"+(index++).ToString("D5")+".png"),texture.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(texture);
                }
                EditorApplication.Exit(0);
            }
            catch(Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        static void Advance(BonfireController c,float input,float seconds)
        {
            int steps=Mathf.RoundToInt(seconds/Step);
            for(int k=0;k<steps;k++)
            {
                c.AdvanceEnergy(input,Step);
                c.volume.SimulatePreview(Step,c.energy,input,c.exposure);
                if(k%20==0) GL.Flush();
            }
            c.fireLight.intensity=0.18f+c.energy*4f;
            c.woodMaterial.SetFloat("_Heat",0.18f+c.energy*0.82f);
        }

        static Texture2D Shot(BonfireBootstrap rig,BonfireController c)
        {
            var rt=new RenderTexture(CameraWidth,CameraHeight,24,RenderTextureFormat.ARGB32); rt.Create();
            var request=new UniversalRenderPipeline.SingleCameraRequest { destination=rt };
            RenderPipeline.SubmitRenderRequest(rig.ViewCamera,request);
            var old=RenderTexture.active; RenderTexture.active=rt;
            var texture=new Texture2D(FrameWidth,FrameHeight,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(CropX,CropY,FrameWidth,FrameHeight),0,0); texture.Apply();
            RenderTexture.active=old; rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            return texture;
        }

        // 最大火力で流体1ステップと1080p描画の合計時間を測る。ReadPixelsでGPUの完了を待つ。
        static double MeasureFrame(BonfireBootstrap rig,BonfireController c)
        {
            Advance(c,1,8f);
            var rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32); rt.Create();
            var pixel=new Texture2D(1,1,TextureFormat.RGB24,false);
            var old=RenderTexture.active; RenderTexture.active=rt;
            var clock=new System.Diagnostics.Stopwatch();
            const int frames=120;
            for(int k=0;k<frames;k++)
            {
                if(k==20) clock.Start();
                c.AdvanceEnergy(1,Step); c.volume.SimulatePreview(Step,c.energy,1,c.exposure);
                RenderPipeline.SubmitRenderRequest(rig.ViewCamera,new UniversalRenderPipeline.SingleCameraRequest { destination=rt });
                RenderTexture.active=rt;
                pixel.ReadPixels(new Rect(0,0,1,1),0,0);
            }
            clock.Stop();
            RenderTexture.active=old; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(pixel);
            return clock.Elapsed.TotalMilliseconds/(frames-20);
        }

        static void SaveFull(BonfireBootstrap rig,string path)
        {
            var rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32); rt.Create();
            var request=new UniversalRenderPipeline.SingleCameraRequest { destination=rt };
            RenderPipeline.SubmitRenderRequest(rig.ViewCamera,request);
            var old=RenderTexture.active; RenderTexture.active=rt;
            var texture=new Texture2D(760,1000,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(580,60,760,1000),0,0); texture.Apply();
            File.WriteAllBytes(path,texture.EncodeToPNG());
            RenderTexture.active=old; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(texture);
        }

        static void Sheet(List<Texture2D> frames,string path)
        {
            int rows=Mathf.CeilToInt(frames.Count/(float)Columns);
            var sheet=new Texture2D(Columns*FrameWidth,rows*FrameHeight,TextureFormat.RGB24,false);
            for(int k=0;k<frames.Count;k++)
            {
                int x=k%Columns*FrameWidth, y=(rows-1-k/Columns)*FrameHeight;
                sheet.SetPixels(x,y,FrameWidth,FrameHeight,frames[k].GetPixels());
                UnityEngine.Object.DestroyImmediate(frames[k]);
            }
            sheet.Apply();
            File.WriteAllBytes(path,sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
        }
    }
}
