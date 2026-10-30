using System;
using UnityEngine;

namespace InteractiveBonfire
{
    // 音声は録音ファイルに保存せず、直近のサンプルだけを解析する。
    public sealed class BreathMicrophone : MonoBehaviour
    {
        [Tooltip("空欄なら一覧の先頭。USBマイク名の一部を指定すると、その機器だけを使用します")]
        public string deviceNameContains = "";
        public bool useMicrophone = true;
        [Range(0.0001f, 0.3f)] public float noiseGate = 0.012f;
        [Range(0.01f, 0.8f)] public float fullBreathRms = 0.12f;
        [Range(0.1f, 8f)] public float sensitivity = 1f;
        [Range(0.01f, 0.5f)] public float attackSeconds = 0.08f;
        [Range(0.05f, 1f)] public float releaseSeconds = 0.25f;
        [Tooltip("短い物音を弾くため、ゲートを超える入力がこの秒数続いてから反応します")]
        [Range(0f, 1f)] public float minimumInputSeconds = 0.18f;
        [Tooltip("解析値。実行中のInspectorで確認")]
        public float rms;
        public float breath;
        public string status = "停止";
        public string activeDevice = "";
        AudioClip clip;
        float[] samples;
        bool gateOpen;
        float inputDuration;
        float calibrationTime, calibrationSum;
        int calibrationCount;
        float lastPositionTime;
        int lastPosition = -1;

        void OnEnable() { RestartMicrophone(); }

        [ContextMenu("マイク一覧をConsoleに表示")]
        public void ListDevices()
        {
            Debug.Log("利用可能なマイク: " + string.Join(" / ", Microphone.devices), this);
        }

        [ContextMenu("マイクを再接続")]
        public void RestartMicrophone()
        {
            StopMicrophone();
            if (!useMicrophone) { status = "マイク無効・手動操作可"; return; }
            var devices = Microphone.devices;
            if (devices.Length == 0) { status = "マイクなし・Spaceで操作可"; return; }
            activeDevice = devices[0];
            if (!string.IsNullOrWhiteSpace(deviceNameContains))
            {
                activeDevice = Array.Find(devices, d => d.IndexOf(deviceNameContains, StringComparison.OrdinalIgnoreCase) >= 0);
                if (activeDevice == null) { activeDevice = ""; status = "指定マイクが見つかりません"; return; }
            }
            try
            {
                Microphone.GetDeviceCaps(activeDevice, out int min, out int max);
                int rate = max == 0 ? 48000 : Mathf.Clamp(48000, Mathf.Max(8000, min), max);
                clip = Microphone.Start(activeDevice, true, 2, rate);
                samples = clip ? new float[1024 * clip.channels] : null;
                lastPosition = -1;
                lastPositionTime = Time.unscaledTime;
                status = clip ? "マイク起動待ち" : "マイク開始失敗";
            }
            catch (Exception e) { status = "マイク開始失敗: " + e.Message; }
        }

        [ContextMenu("無音を2秒測定してゲート調整")]
        public void CalibrateNoise()
        {
            if (!clip) return;
            calibrationTime = 2f; calibrationSum = 0f; calibrationCount = 0;
        }

        void Update()
        {
            float target = 0f;
            if (useMicrophone && clip && samples != null)
            {
                int position = Microphone.GetPosition(activeDevice);
                if (position != lastPosition) { lastPosition = position; lastPositionTime = Time.unscaledTime; }
                bool fresh = Time.unscaledTime - lastPositionTime < 1.5f;
                if (position >= 1024 && fresh && Microphone.IsRecording(activeDevice))
                {
                    // offsetはチャンネル数を掛けないサンプルフレーム単位。
                    if (clip.GetData(samples, position - 1024))
                    {
                        double mean = 0, power = 0;
                        for (int i = 0; i < samples.Length; i++) mean += samples[i];
                        mean /= samples.Length;
                        for (int i = 0; i < samples.Length; i++) { double v = samples[i] - mean; power += v * v; }
                        rms = Mathf.Sqrt((float)(power / samples.Length));
                        status = "入力中: " + activeDevice;
                        gateOpen = rms > noiseGate * (gateOpen ? 0.75f : 1.1f);
                        // 別のマイクを混ぜず、選択した機器の持続入力だけを採用する。
                        inputDuration = gateOpen ? inputDuration + Time.unscaledDeltaTime : 0f;
                        if (gateOpen && inputDuration >= minimumInputSeconds)
                            target = Mathf.Clamp01((rms - noiseGate * 0.75f) / Mathf.Max(0.001f, fullBreathRms - noiseGate * 0.75f) * sensitivity);
                        if (calibrationTime > 0f)
                        {
                            calibrationTime -= Time.unscaledDeltaTime;
                            calibrationSum += rms; calibrationCount++;
                            target = 0; gateOpen = false; inputDuration = 0;
                            status = "周囲の音を測定中・息を吹かないでください";
                            if (calibrationTime <= 0) noiseGate = Mathf.Clamp(calibrationSum / Mathf.Max(1, calibrationCount) * 2.5f + 0.002f, 0.002f, 0.25f);
                        }
                    }
                }
                else { rms = 0; gateOpen = false; inputDuration = 0; status = fresh ? "マイク起動待ち" : "入力停止・権限やUSB接続を確認"; }
            }
            else { rms = 0; gateOpen = false; inputDuration = 0; }
            breath = Mathf.Lerp(breath, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime / (target > breath ? attackSeconds : releaseSeconds)));
        }

        void StopMicrophone()
        {
            if (clip && !string.IsNullOrEmpty(activeDevice)) Microphone.End(activeDevice);
            if (clip) Destroy(clip);
            clip = null; samples = null; breath = rms = 0; gateOpen = false;
            inputDuration = 0; calibrationTime = 0;
        }
        void OnDisable() { StopMicrophone(); }
    }
}
