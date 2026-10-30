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
        [Tooltip("流れを乱す強さ。大きいほど炎全体が大きくうねります")]
        [Range(0.2f, 1.8f)] public float turbulenceStrength = 1f;
        MaterialPropertyBlock block;
        MeshRenderer meshRenderer;
        Texture3D noise;
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
            if(SystemInfo.supportsComputeShaders && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
            {
                var compute=Resources.Load<ComputeShader>("AdvectedFire");
                if(compute)
                {
                    try { simulation=new BonfireSimulation(compute,noise); }
                    catch(System.Exception error) { Debug.LogError("炎の流体計算を開始できません: "+error.Message,this); }
                }
            }
            if(simulation==null) Debug.LogError("このGPUでは炎の流体計算を実行できないため、炎は表示されません。",this);
            ApplySimulation();
            meshRenderer.SetPropertyBlock(block);
        }

        public void SetFire(float energy, float breath, float exposure)
        {
            Initialize();
            if (!meshRenderer || block == null) return;
            double now = Time.timeAsDouble;
            float dt = lastClock < 0 ? 0f : Mathf.Clamp((float)(now - lastClock), 0f, 0.1f);
            lastClock = now;
            Advance(dt, energy, breath, exposure);
        }

        public void SetAnimationTime(float seconds)
        {
            Initialize(); animationTime = Mathf.Max(0f, seconds); lastClock = Time.timeAsDouble;
            ApplyMotion(); meshRenderer.SetPropertyBlock(block);
        }

        public void SimulatePreview(float deltaSeconds,float energy,float breath,float exposure)
        {
            // Editorの連続フレーム検証で、実行時と同じ流体ステップを進める。
            Initialize(); Advance(deltaSeconds,energy,breath,exposure);
        }

        void Advance(float dt,float energy,float breath,float exposure)
        {
            AdvanceAnimation(dt,energy,breath);
            if(simulation!=null) simulation.Step(dt,energy,smoothedBreath,motionSpeed,turbulenceStrength);
            block.SetFloat("_Energy",energy); block.SetFloat("_Breath",smoothedBreath);
            block.SetFloat("_Exposure",exposure); block.SetFloat("_Density",density); block.SetInt("_Steps",raySteps);
            ApplyMotion(); ApplySimulation(); meshRenderer.SetPropertyBlock(block);
        }

        public void ResetSimulation()
        {
            if(simulation!=null) simulation.Reset(); animationTime=0; smoothedEnergy=smoothedBreath=0; lastClock=Time.timeAsDouble;
        }

        void ApplySimulation()
        {
            // 流体の結果は毎ステップ別のテクスチャへ入れ替わるので、その都度渡す。
            if(simulation!=null) block.SetTexture("_SimTex",simulation.Texture);
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
            block.SetFloat("_AnimationTime", (float)animationTime);
            block.SetFloat("_MotionStrength", turbulenceStrength);
        }

        static Texture3D MakeNoise()
        {
            const int size = 48;
            var pixels = new Color32[size * size * size];
            uint state = 192341u;
            for (int i = 0; i < pixels.Length; i++)
            {
                state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                uint bits = state;
                state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                // 流体の外力に3方向の独立した乱数が要るので、チャンネルごとに別の値を持つ。
                pixels[i] = new Color32((byte)bits, (byte)(bits >> 8), (byte)(bits >> 16), (byte)state);
            }
            var tex = new Texture3D(size, size, size, TextureFormat.RGBA32, false)
            { name = "Runtime periodic 3D noise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(pixels); tex.Apply(false, true);
            return tex;
        }
        void OnDestroy() { if (noise) Destroy(noise); if(simulation!=null) simulation.Dispose(); }
    }
}
