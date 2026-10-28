using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace InteractiveBonfire.Editor
{
    public static class BonfireSceneBuilder
    {
        const string Root="Assets/InteractiveBonfire";
        [MenuItem("Tools/Interactive Bonfire/Generate Scene")]
        public static void GenerateScene()
        {
            if(!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Generate();
        }
        public static void Generate()
        {
            Directory.CreateDirectory(Root+"/Generated");
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            // 別のUniversal 3Dプロジェクトへ導入した場合は既存URPを使用する。
            if(!pipeline) pipeline=GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if(!pipeline) throw new InvalidOperationException("URPがありません。Unity HubのUniversal 3Dテンプレートを使ってください。");
            pipeline.supportsCameraDepthTexture=true; pipeline.supportsHDR=true;
            pipeline.renderScale=0.85f;
            GraphicsSettings.defaultRenderPipeline=pipeline;
            QualitySettings.renderPipeline=pipeline;
            // Forward+のライト走査を必要としないForwardを明示。
            var pipelineSettings=new SerializedObject(pipeline);
            var renderers=pipelineSettings.FindProperty("m_RendererDataList");
            var renderer=renderers != null && renderers.arraySize>0 ? renderers.GetArrayElementAtIndex(0).objectReferenceValue as UniversalRendererData : null;
            if(renderer)
            {
                var serialized=new SerializedObject(renderer); serialized.FindProperty("m_RenderingMode").intValue=0;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                foreach(var feature in renderer.rendererFeatures) if(feature && feature.GetType().Name.Contains("ScreenSpaceAmbientOcclusion")) feature.SetActive(false);
                EditorUtility.SetDirty(renderer);
            }
            EditorUtility.SetDirty(pipeline);
            var flame=MaterialAsset("VolumeFlame","InteractiveBonfire/VolumeFlame");
            var wood=MaterialAsset("CharredWood","InteractiveBonfire/CharredWood");
            var sparks=MaterialAsset("EmberParticle","InteractiveBonfire/EmberParticle");
            string profilePath=Root+"/Generated/BonfirePost.asset";
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if(!profile)
            {
                profile=ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile,profilePath);
                var tone=profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
                var bloom=profile.Add<Bloom>(true); bloom.intensity.Override(0.12f); bloom.threshold.Override(1.1f); bloom.scatter.Override(0.55f);
                foreach(var component in profile.components) AssetDatabase.AddObjectToAsset(component,profile);
            }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var rig=new GameObject("Bonfire Rig (press Play)").AddComponent<BonfireBootstrap>();
            rig.flameMaterial=flame; rig.woodMaterial=wood; rig.sparkMaterial=sparks; rig.volumeProfile=profile;
            EditorSceneManager.SaveScene(scene,Root+"/Generated/Bonfire.unity");
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(Root+"/Generated/Bonfire.unity",true)};
            PlayerSettings.companyName="University Exhibition"; PlayerSettings.productName="Breath Bonfire";
            PlayerSettings.defaultScreenWidth=1920; PlayerSettings.defaultScreenHeight=1080;
            PlayerSettings.fullScreenMode=FullScreenMode.FullScreenWindow;
            PlayerSettings.runInBackground=true; PlayerSettings.colorSpace=ColorSpace.Linear;
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Selection.activeGameObject=rig.gameObject;
            Debug.Log("Bonfireシーン生成完了。Play → Game画面をクリック → Spaceを長押し。");
        }
        static Material MaterialAsset(string name,string shaderName)
        {
            string path=Root+"/Generated/"+name+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat) return mat;
            Shader shader=Shader.Find(shaderName);
            if(!shader || !shader.isSupported) throw new InvalidOperationException("シェーダーが利用できません: "+shaderName);
            mat=new Material(shader); AssetDatabase.CreateAsset(mat,path); return mat;
        }

        [MenuItem("Tools/Interactive Bonfire/Quality/Low (32 steps, 65 percent)")]
        public static void Low() { Quality(32,0.65f); }
        [MenuItem("Tools/Interactive Bonfire/Quality/Medium (64 steps, 85 percent)")]
        public static void Medium() { Quality(64,0.85f); }
        [MenuItem("Tools/Interactive Bonfire/Quality/High (96 steps, 100 percent)")]
        public static void High() { Quality(96,1f); }
        static void Quality(int steps,float scale)
        {
            var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if(pipeline) { pipeline.renderScale=scale; EditorUtility.SetDirty(pipeline); }
            var rig=UnityEngine.Object.FindAnyObjectByType<BonfireBootstrap>();
            if(rig) { rig.initialRaySteps=steps; EditorUtility.SetDirty(rig); }
            var volume=UnityEngine.Object.FindAnyObjectByType<BonfireVolume>();
            if(volume) volume.raySteps=steps;
            if(!Application.isPlaying) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }
    }
}
