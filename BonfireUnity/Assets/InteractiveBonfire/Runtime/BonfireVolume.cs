using UnityEngine;

namespace InteractiveBonfire
{
    public sealed class BonfireVolume : MonoBehaviour
    {
        public Material material;
        [Range(24, 128)] public int raySteps = 64;
        [Range(0.3f, 2f)] public float density = 1f;
        [Tooltip("炎内部の流れの速さ。火力が育つ時間とは別の設定です")]
        [Range(0.25f, 2f)] public float motionSpeed = 1f;
        [Tooltip("根元を固定したまま、上部を曲げる強さ")]
        [Range(0.2f, 1.8f)] public float turbulenceStrength = 1f;
        [Tooltip("密度と温度をGPU上で移流する。非対応GPUでは手続き方式へ戻ります")]
        public bool useAdvectedSimulation = true;
        public Vector4[] sources;
        MaterialPropertyBlock block;
        MeshRenderer meshRenderer;
        Texture3D noise;
        readonly Vector4[] sourceMotion = new Vector4[7];
        double animationTime;
        double lastClock = -1;
        float smoothedEnergy, smoothedBreath;
        BonfireSimulation simulation;
        public double AnimationTime => animationTime;

        void Awake()
        { Initialize(); }

        public void Initialize()
        {
            if (block != null) return;
            meshRenderer = GetComponent<MeshRenderer>(); block = new MaterialPropertyBlock();
            noise = MakeNoise();
            block.SetTexture("_NoiseTex", noise);
            if(useAdvectedSimulation && SystemInfo.supportsComputeShaders && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
            {
                var compute=Resources.Load<ComputeShader>("AdvectedFire");
                if(compute)
                {
                    try { simulation=new BonfireSimulation(compute); }
                    catch(System.Exception error) { Debug.LogWarning("3D移流を開始できないため手続き炎を使用します: "+error.Message,this); }
                }
            }
            // xyz: 薪に埋め込む固定火点。w: 熱量の比率（手続き版では相対高さ）。
            if (sources != null && sources.Length == 7) block.SetVectorArray("_Sources", sources);
            meshRenderer.SetPropertyBlock(block);
        }

        public void SetFire(float energy, float breath, float exposure)
        {
            Initialize();
            if (!meshRenderer || block == null) return;
            double now = Time.timeAsDouble;
            float dt = lastClock < 0 ? 0f : Mathf.Clamp((float)(now - lastClock), 0f, 0.1f);
            lastClock = now;
            AdvanceAnimation(dt, energy, breath);
            if(simulation!=null) simulation.Step(dt,energy,smoothedBreath,(float)animationTime,motionSpeed,turbulenceStrength,sources);
            block.SetFloat("_Energy", energy);
            block.SetFloat("_Breath", smoothedBreath);
            block.SetFloat("_Exposure", exposure);
            block.SetFloat("_Density", density);
            ApplyMotion();
            ApplySimulation();
            block.SetInt("_Steps", raySteps);
            meshRenderer.SetPropertyBlock(block);
        }

        public void SetSources(Vector4[] values)
        {
            Initialize();
            sources = values;
            if (block != null && values.Length == 7)
            { block.SetVectorArray("_Sources",values); meshRenderer.SetPropertyBlock(block); }
        }

        public void SetAnimationTime(float seconds)
        {
            Initialize(); animationTime = Mathf.Max(0f, seconds); lastClock = Time.timeAsDouble;
            ApplyMotion(); meshRenderer.SetPropertyBlock(block);
        }

        public void SimulatePreview(float deltaSeconds,float energy,float breath,float exposure)
        {
            // Editorの連続フレーム検証で、実行時と同じ移流ステップを進める。
            Initialize(); AdvanceAnimation(deltaSeconds,energy,breath);
            if(simulation!=null) simulation.Step(deltaSeconds,energy,smoothedBreath,(float)animationTime,motionSpeed,turbulenceStrength,sources);
            block.SetFloat("_Energy",energy); block.SetFloat("_Breath",smoothedBreath);
            block.SetFloat("_Exposure",exposure); block.SetFloat("_Density",density); block.SetInt("_Steps",raySteps);
            ApplyMotion(); ApplySimulation(); meshRenderer.SetPropertyBlock(block);
        }

        public void ResetSimulation()
        { if(simulation!=null) simulation.Reset(); animationTime=0; smoothedEnergy=smoothedBreath=0; lastClock=Time.timeAsDouble; }

        void ApplySimulation()
        {
            block.SetFloat("_UseSimulation",simulation!=null?1f:0f);
            if(simulation!=null)
            {
                block.SetTexture("_SimTex",simulation.Texture);
                block.SetTexture("_HeatProfile",simulation.HeatProfile);
            }
        }

        public void AdvanceAnimation(float deltaSeconds, float energy, float breath)
        {
            // t×火力ではなく速度を積算する。息を止めた瞬間にも模様を巻き戻さない。
            float dt = Mathf.Clamp(deltaSeconds, 0f, 0.1f);
            smoothedEnergy = Mathf.Lerp(smoothedEnergy, Mathf.Clamp01(energy), 1f - Mathf.Exp(-dt / 0.65f));
            smoothedBreath = Mathf.Lerp(smoothedBreath, Mathf.Clamp01(breath), 1f - Mathf.Exp(-dt / 0.55f));
            animationTime += dt * motionSpeed * Mathf.Lerp(0.72f, 1.12f, smoothedEnergy);
        }

        void ApplyMotion()
        {
            float t = (float)animationTime;
            for (int j = 0; j < sourceMotion.Length; j++)
            {
                float seed = 8.37f + j * 5.19f;
                // GPUの各レイで共通する低速の揺らぎはCPUで一度だけ求める。
                float height = 0.87f + 0.20f * Mathf.PerlinNoise(seed, t * 0.53f);
                float thickness = 0.88f + 0.20f * Mathf.PerlinNoise(seed + 17f, t * 0.39f);
                float fork = Mathf.SmoothStep(0f, 0.52f, Mathf.InverseLerp(0.42f, 0.72f, Mathf.PerlinNoise(seed + 33f, t * 0.67f)));
                sourceMotion[j] = new Vector4(height, thickness, fork, j % 2 == 0 ? 1f : -1f);
            }
            block.SetFloat("_AnimationTime", t);
            block.SetFloat("_MotionStrength", turbulenceStrength);
            block.SetVectorArray("_SourceMotion", sourceMotion);
        }

        static Texture3D MakeNoise()
        {
            const int size = 48;
            var pixels = new Color32[size * size * size];
            uint state = 192341u;
            for (int i = 0; i < pixels.Length; i++)
            {
                state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                byte r = (byte)(state & 255);
                pixels[i] = new Color32(r, r, r, 255);
            }
            var tex = new Texture3D(size, size, size, TextureFormat.RGBA32, false)
            { name = "Runtime periodic 3D noise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(pixels); tex.Apply(false, true);
            return tex;
        }
        void OnDestroy() { if (noise) Destroy(noise); if(simulation!=null) simulation.Dispose(); }
    }
}
