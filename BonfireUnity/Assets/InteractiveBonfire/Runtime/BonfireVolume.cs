using UnityEngine;

namespace InteractiveBonfire
{
    public sealed class BonfireVolume : MonoBehaviour
    {
        public Material material;
        [Range(24, 128)] public int raySteps = 64;
        [Range(0.3f, 2f)] public float density = 1f;
        public Vector4[] sources;
        MaterialPropertyBlock block;
        MeshRenderer meshRenderer;
        Texture3D noise;

        void Awake()
        { Initialize(); }

        public void Initialize()
        {
            if (block != null) return;
            meshRenderer = GetComponent<MeshRenderer>(); block = new MaterialPropertyBlock();
            noise = MakeNoise();
            block.SetTexture("_NoiseTex", noise);
            // x,z: 薪接触点の水平位置、y: 埋め込む高さ、w: 舌の相対高さ。
            if (sources != null && sources.Length == 7) block.SetVectorArray("_Sources", sources);
            meshRenderer.SetPropertyBlock(block);
        }

        public void SetFire(float energy, float breath, float exposure)
        {
            Initialize();
            if (!meshRenderer || block == null) return;
            block.SetFloat("_Energy", energy);
            block.SetFloat("_Breath", breath);
            block.SetFloat("_Exposure", exposure);
            block.SetFloat("_Density", density);
            block.SetFloat("_AnimationTime",Time.time);
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
        { Initialize(); block.SetFloat("_AnimationTime",seconds); meshRenderer.SetPropertyBlock(block); }

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
        void OnDestroy() { if (noise) Destroy(noise); }
    }
}
