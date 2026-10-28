using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace InteractiveBonfire
{
    public sealed class BonfireBootstrap : MonoBehaviour
    {
        public Material flameMaterial, woodMaterial, sparkMaterial;
        public VolumeProfile volumeProfile;
        public bool microphoneEnabled = true;
        public int initialRaySteps = 64;
        [Range(0.3f,2f)] public float initialDensity = 1f;
        public bool exhibitionFullscreen = true;
        public BonfireController Controller { get; private set; }
        public Camera ViewCamera { get; private set; }
        Material runtimeWood;
        bool built;

        void Start() { Construct(); }
        public void Construct()
        {
            if(built) return; built=true;
            if(!flameMaterial || !woodMaterial || !sparkMaterial)
            { Debug.LogError("Bonfire: 素材不足。Tools > Interactive Bonfire > Generate Scene を実行してください。",this); return; }
            Application.targetFrameRate=60;
            if(!Application.isEditor && exhibitionFullscreen) Screen.SetResolution(1920,1080,FullScreenMode.FullScreenWindow);
            RenderSettings.skybox=null; RenderSettings.ambientMode=AmbientMode.Flat;
            RenderSettings.ambientLight=Color.black; RenderSettings.fog=false;
            runtimeWood=new Material(woodMaterial);
            var logs=new GameObject("Logs (opaque depth)").AddComponent<ProceduralLogs>();
            logs.transform.SetParent(transform,false); logs.Build(runtimeWood);

            var cameraObject=new GameObject("Exhibition Camera",typeof(Camera),typeof(AudioListener));
            cameraObject.transform.SetParent(transform,false);
            ViewCamera=cameraObject.GetComponent<Camera>(); ViewCamera.tag="MainCamera";
            ViewCamera.transform.position=new Vector3(0,1.55f,-5.6f);
            ViewCamera.transform.LookAt(new Vector3(0,1.42f,0));
            ViewCamera.fieldOfView=44; ViewCamera.nearClipPlane=0.05f; ViewCamera.farClipPlane=25;
            ViewCamera.clearFlags=CameraClearFlags.SolidColor; ViewCamera.backgroundColor=Color.black;
            ViewCamera.allowHDR=true;
            var cameraData=ViewCamera.GetUniversalAdditionalCameraData();
            cameraData.requiresDepthTexture=true; cameraData.renderPostProcessing=true;
            cameraData.antialiasing=AntialiasingMode.FastApproximateAntialiasing;
            if(volumeProfile)
            {
                var post=new GameObject("Subtle bloom and tonemapping").AddComponent<Volume>();
                post.transform.SetParent(transform,false); post.isGlobal=true; post.sharedProfile=volumeProfile;
            }

            // 箱の表面は見えない。内部の発光密度だけを積分して描画する。
            var box=GameObject.CreatePrimitive(PrimitiveType.Cube); box.name="Flame Volume (3D)";
            box.transform.SetParent(transform,false); box.transform.localPosition=new Vector3(0,1.65f,0);
            box.transform.localScale=new Vector3(3.1f,3.6f,2.2f);
            if(Application.isPlaying) Destroy(box.GetComponent<Collider>()); else DestroyImmediate(box.GetComponent<Collider>());
            box.GetComponent<MeshRenderer>().sharedMaterial=flameMaterial;
            box.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
            var volume=box.AddComponent<BonfireVolume>(); volume.material=flameMaterial; volume.raySteps=initialRaySteps; volume.density=initialDensity;
            // Awake後にも反映できるよう、sourcesはUpdateSources経由で設定。
            volume.SetSources(new [] {
                new Vector4(0f,0.40f,0.08f,1.0f), new Vector4(-0.38f,0.23f,-0.06f,0.72f),
                new Vector4(0.38f,0.25f,0.06f,0.62f), new Vector4(-0.16f,0.26f,-0.29f,0.66f),
                new Vector4(0.20f,0.30f,0.27f,0.81f),new Vector4(-0.62f,0.18f,0.10f,0.39f),
                new Vector4(0.62f,0.21f,-0.18f,0.43f) });

            var lightObject=new GameObject("Fire light"); lightObject.transform.SetParent(transform,false);
            lightObject.transform.localPosition=new Vector3(0,0.8f,-0.15f);
            var light=lightObject.AddComponent<Light>(); light.type=LightType.Point;
            light.color=new Color(1,0.38f,0.075f); light.range=4f; light.shadows=LightShadows.None;

            var mic=GetComponent<BreathMicrophone>();
            if(!mic) mic=gameObject.AddComponent<BreathMicrophone>();
            if(!microphoneEnabled) { mic.useMicrophone=false; mic.RestartMicrophone(); }
            var ps=CreateSparks();
            Controller=GetComponent<BonfireController>();
            if(!Controller) Controller=gameObject.AddComponent<BonfireController>();
            Controller.microphone=mic; Controller.volume=volume; Controller.fireLight=light;
            Controller.sparks=ps; Controller.woodMaterial=runtimeWood;
        }

        ParticleSystem CreateSparks()
        {
            var go=new GameObject("Sparks"); go.transform.SetParent(transform,false);
            go.transform.localPosition=new Vector3(0,0.32f,0);
            var ps=go.AddComponent<ParticleSystem>(); ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main; main.duration=5; main.loop=true; main.playOnAwake=false;
            main.maxParticles=140; main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.startLifetime=new ParticleSystem.MinMaxCurve(1.0f,2.9f);
            main.startSpeed=new ParticleSystem.MinMaxCurve(0.35f,0.85f);
            main.startSize=new ParticleSystem.MinMaxCurve(0.008f,0.021f);
            main.startColor=new Color(1,0.35f,0.04f,1); main.gravityModifier=-0.015f;
            var shape=ps.shape; shape.shapeType=ParticleSystemShapeType.Box; shape.scale=new Vector3(0.85f,0.38f,0.04f);
            shape.rotation=new Vector3(-90,0,0);
            var emission=ps.emission; emission.rateOverTime=0;
            var velocity=ps.velocityOverLifetime; velocity.enabled=true; velocity.space=ParticleSystemSimulationSpace.World;
            // x,y,zはすべてTwoConstants。同じモードに統一しUnityの警告を防ぐ。
            velocity.x=new ParticleSystem.MinMaxCurve(-0.18f,0.18f);
            velocity.y=new ParticleSystem.MinMaxCurve(0.35f,0.75f);
            velocity.z=new ParticleSystem.MinMaxCurve(-0.12f,0.12f);
            var noise=ps.noise; noise.enabled=true; noise.strength=0.32f; noise.frequency=0.65f;
            noise.scrollSpeed=0.4f; noise.quality=ParticleSystemNoiseQuality.Medium; noise.octaveCount=2;
            var color=ps.colorOverLifetime; color.enabled=true;
            var gradient=new Gradient(); gradient.SetKeys(new[]{new GradientColorKey(new Color(1,0.55f,0.08f),0),new GradientColorKey(new Color(0.65f,0.025f,0.002f),1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,0.1f),new GradientAlphaKey(0,1)});
            color.color=gradient;
            var size=ps.sizeOverLifetime; size.enabled=true; size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,1),new Keyframe(1,0.1f)));
            var renderer=go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial=sparkMaterial;
            renderer.renderMode=ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
            ps.Play(); return ps;
        }
        void OnDestroy() { if(runtimeWood) Destroy(runtimeWood); }
    }
}
