using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace InteractiveBonfire
{
    public sealed class BonfireController : MonoBehaviour
    {
        public BreathMicrophone microphone;
        public BonfireVolume volume;
        public ParticleSystem sparks;
        public Light fireLight;
        public Material woodMaterial;
        [Range(0, 1)] public float manualBreath;
        public bool enableKeyboard = true;
        // 展示でゆっくり育ち、余韻を残して収まるよう、従来の約2倍の時間に設定。
        [Min(0.2f)] public float growthSeconds = 9f;
        [Min(0.2f)] public float decaySeconds = 12f;
        [Range(0.02f, 1f)] public float maximumGrowthPerSecond = 0.11f;
        [Range(0, 1)] public float energy;
        [Range(0, 1)] public float input;
        [Range(0.1f, 2f)] public float exposure = 0.9f;
        public bool showDiagnostics;
        float fps;

        void Update()
        {
            bool held = false, toggle = false;
            if (enableKeyboard)
            {
#if ENABLE_INPUT_SYSTEM
                if (Keyboard.current != null) { held = Keyboard.current.spaceKey.isPressed; toggle = Keyboard.current.f3Key.wasPressedThisFrame; }
#elif ENABLE_LEGACY_INPUT_MANAGER
                held = Input.GetKey(KeyCode.Space); toggle = Input.GetKeyDown(KeyCode.F3);
#endif
            }
            if (toggle) showDiagnostics = !showDiagnostics;
            input = Mathf.Max(manualBreath, held ? 1f : 0f, microphone ? microphone.breath : 0f);
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            AdvanceEnergy(input,dt);
            if (volume) volume.SetFire(energy, input, exposure);
            if (sparks)
            {
                var emission = sparks.emission;
                emission.rateOverTime = energy < 0.015f ? 0f : 1.5f + energy * 13f + input * energy * 8f;
            }
            float flicker = 0.86f + 0.12f * Mathf.PerlinNoise(Time.time * 7.3f, 1.2f) + 0.1f * Mathf.PerlinNoise(5.3f, Time.time * 13f);
            if (fireLight) fireLight.intensity = (0.18f + energy * 4f) * flicker;
            if (woodMaterial) woodMaterial.SetFloat("_Heat", 0.18f + energy * 0.82f);
            fps = Mathf.Lerp(fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 0.001f), 0.03f);
        }

        void OnGUI()
        {
            if (!showDiagnostics) return;
            GUI.Box(new Rect(12, 12, 540, 150), "Bonfire diagnostics (F3)");
            GUI.Label(new Rect(24, 40, 520, 25), $"FPS {fps:F0}  | RMS {(microphone ? microphone.rms : 0):F4}  | input {input:F2} | energy {energy:F2}");
            GUI.Label(new Rect(24, 67, 520, 25), microphone ? microphone.status : "Manual mode");
            GUI.Label(new Rect(24, 94, 520, 25), "Space: blow / Inspector: manualBreath / F3: hide");
        }

        public void AdvanceEnergy(float normalizedInput,float deltaSeconds)
        {
            // 成長と減衰を別々の係数で計算し、急な成長を制限する。
            float dt=Mathf.Clamp(deltaSeconds,0,0.1f);
            float desired=normalizedInput<0.025f ? 0f : Mathf.Pow(Mathf.Clamp01(normalizedInput),0.7f);
            float tau=desired>energy ? growthSeconds/3f : decaySeconds/3f;
            float next=Mathf.Lerp(energy,desired,1f-Mathf.Exp(-dt/Mathf.Max(0.05f,tau)));
            energy=desired>energy ? Mathf.Min(next,energy+maximumGrowthPerSecond*dt) : next;
            if(desired==0f && energy<0.004f) energy=0;
        }
    }
}
