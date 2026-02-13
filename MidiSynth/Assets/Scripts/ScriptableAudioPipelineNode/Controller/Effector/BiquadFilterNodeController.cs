using CustomAudioPipeline.Nodes;
using UnityEngine;

namespace CustomAudioPipeline.Controller
{
    [AudioControllerMessageSupport(typeof(BiquadFilterNode.FilterMessage))]
    public sealed class BiquadFilterNodeController : BaseNodeController
    {
        [Header("Filter Params")]
        [SerializeField] private bool isActive = true;

        [SerializeField] private BiquadFilterNode.FilterMode mode =
            BiquadFilterNode.FilterMode.BandPass;

        // 風用途の初期レンジ（必要なら後で拡張）
        [SerializeField]
        [FrequencyControl(minHz: 20.0f, maxHz: 20000.0f, minMidi: 24, maxMidi: 96)]
        private float frequency = 800.0f;

        // Qは 0.1〜20 くらいで十分（風は 0.3〜3 が主戦場）
        [SerializeField, Range(0.1f, 64.0f)] private float q = 0.8f;

        [Tooltip("Peakモード等で使用。LP/HP/BP/Notchでは通常 0dB 推奨。")]
        [SerializeField, Range(-24.0f, 24.0f)] private float gainDb = 0.0f;
        
        private bool _lastActive;
        private CustomAudioPipeline.Nodes.BiquadFilterNode.FilterMode _lastMode;
        private float _lastFrequency;
        private float _lastQ;
        private float _lastGainDb;

        private void OnEnable()
        {
            Apply(force: true);
        }

        private void Update()
        {
            Apply(force: false);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            Apply(force: false);
        }
#endif

        private void Apply(bool force)
        {
            // 係数計算側に安全ガードはあるが、Controller側でも雑にクランプしておく
            float f = Mathf.Max(1.0f, frequency);
            float qq = Mathf.Max(0.001f, q);

            if (!force
                && _lastActive == isActive
                && _lastMode == mode
                && Mathf.Approximately(_lastFrequency, f)
                && Mathf.Approximately(_lastQ, qq)
                && Mathf.Approximately(_lastGainDb, gainDb))
            {
                return;
            }

            _lastActive = isActive;
            _lastMode = mode;
            _lastFrequency = f;
            _lastQ = qq;
            _lastGainDb = gainDb;

            var msg = new CustomAudioPipeline.Nodes.BiquadFilterNode.FilterMessage(
                mode,
                f,
                qq,
                gainDb,
                isActive
            );

            TrySend(msg);
        }
        
        // ===== Optional: 外部から操作するためのAPI =====

        public void SetActive(bool active)
        {
            isActive = active;
            Apply(force: false);
        }

        public void SetMode(CustomAudioPipeline.Nodes.BiquadFilterNode.FilterMode value)
        {
            mode = value;
            Apply(force: false);
        }

        public void SetFrequency(float hz)
        {
            frequency = hz;
            Apply(force: false);
        }

        public void SetQ(float value)
        {
            q = value;
            Apply(force: false);
        }

        public void SetGainDb(float db)
        {
            gainDb = db;
            Apply(force: false);
        }
    }
}
