using System;
using CustomAudioPipeline.Nodes;
using UnityEngine;

namespace CustomAudioPipeline.Controller
{
    [AudioControllerMessageSupport(typeof(AmplitudeModulationNode.AmMessage))]
    public sealed class AmNodeController : BaseNodeController
    {
        [Header("AM Params")]
        [SerializeField] private bool isActive = true;
        [SerializeField, Range(0.0f, 1.0f)] private float depth = 0.6f;
        [SerializeField, Range(1.0f, 480.0f)] private float bpm = 120.0f;
        [SerializeField, Range(1.0f, 2000.0f)] private float releaseMs = 250.0f;
        [SerializeField, Range(0.0f, 1.0f)] private float phaseOffset01 = 0.0f;

        [Header("Send")]
        [Tooltip("値変更時のみ送信。OnEnable で必ず1回送る。")]
        [SerializeField] private bool sendOnEnable = true;

        private bool _lastActive;
        private float _lastDepth;
        private float _lastBpm;
        private float _lastRelease;
        private float _lastPhase;
        
        private void OnEnable()
        {
            // 初回は必ず送るためにわざと不一致にしておく
            _lastActive = !isActive;
            _lastDepth = float.NaN;
            _lastBpm = float.NaN;
            _lastRelease = float.NaN;
            _lastPhase = float.NaN;

            if (sendOnEnable)
            {
                SendIfDirty(force: true);
            }
        }

        private void Update()
        {
            SendIfDirty(force: false);
        }

        private void SendIfDirty(bool force)
        {
            // 正規化
            float d = Mathf.Clamp01(depth);
            float b = Mathf.Clamp(bpm, 1.0f, 480.0f);
            float r = Mathf.Max(0.0f, releaseMs);
            float p = phaseOffset01 - Mathf.Floor(phaseOffset01);

            if (!force &&
                _lastActive == isActive &&
                Mathf.Approximately(_lastDepth, d) &&
                Mathf.Approximately(_lastBpm, b) &&
                Mathf.Approximately(_lastRelease, r) &&
                Mathf.Approximately(_lastPhase, p))
            {
                return;
            }

            _lastActive = isActive;
            _lastDepth = d;
            _lastBpm = b;
            _lastRelease = r;
            _lastPhase = p;

            TrySend(new CustomAudioPipeline.Nodes.AmplitudeModulationNode.AmMessage(
                isActive,
                d,
                b,
                r,
                p
            ));
        }

        // Optional: 外部から叩きたい場合
        public void SetActive(bool v) => isActive = v;
        public void SetDepth(float v) => depth = Mathf.Clamp01(v);
        public void SetBpm(float v) => bpm = Mathf.Clamp(v, 1.0f, 480.0f);
        public void SetReleaseMs(float v) => releaseMs = Mathf.Max(0.0f, v);
        public void SetPhaseOffset01(float v) => phaseOffset01 = v - Mathf.Floor(v);
    }
}
