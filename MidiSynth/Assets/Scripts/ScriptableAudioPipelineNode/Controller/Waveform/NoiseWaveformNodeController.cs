using CustomAudioPipeline.Nodes;
using UnityEngine;

namespace CustomAudioPipeline.Controller
{
    [AudioControllerMessageSupport(typeof(NoiseWaveformNode.NoiseMessage))]
    public sealed class NoiseWaveformNodeController : BaseNodeController
    {
        [Header("Noise Params")]
        [SerializeField] private bool isActive = true;
        [SerializeField] private CustomAudioPipeline.Nodes.NoiseWaveformNode.NoiseType noiseType =
            CustomAudioPipeline.Nodes.NoiseWaveformNode.NoiseType.White;
        [SerializeField, Range(0.0f, 1.0f)] private float gain = 0.2f;

        [Header("Seed")]
        [SerializeField] private int seed = 22222;

        [Tooltip("True の間だけ reseed を送信し、その後自動で false に戻します。")]
        [SerializeField] private bool reseed;

        private bool _lastActive;
        private CustomAudioPipeline.Nodes.NoiseWaveformNode.NoiseType _lastType;
        private float _lastGain;
        private int _lastSeed;
        private bool _lastReseed;

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
            // Play中のみ即反映（Edit中に送ると再生系が居ないのでノイズになる）
            if (!Application.isPlaying)
            {
                return;
            }

            Apply(force: false);
        }
#endif

        private void Apply(bool force)
        {
            // reseed は「トグルが立った瞬間だけ送る」運用にする
            bool shouldSendReseed = reseed && (!_lastReseed || force);

            if (!force
                && _lastActive == isActive
                && _lastType == noiseType
                && Mathf.Approximately(_lastGain, gain)
                && _lastSeed == seed
                && !shouldSendReseed)
            {
                return;
            }

            _lastActive = isActive;
            _lastType = noiseType;
            _lastGain = gain;
            _lastSeed = seed;
            _lastReseed = reseed;

            var msg = new CustomAudioPipeline.Nodes.NoiseWaveformNode.NoiseMessage(
                noiseType,
                Mathf.Clamp01(gain),
                isActive,
                seed,
                shouldSendReseed
            );

            TrySend(msg);

            // reseed はワンショットに戻す
            if (shouldSendReseed)
            {
                reseed = false;
                _lastReseed = false;
            }
        }

        // ゲーム側API（Inspector以外から叩く用）
        public void SetActive(bool value)
        {
            isActive = value;
            Apply(force: false);
        }

        public void SetNoiseType(CustomAudioPipeline.Nodes.NoiseWaveformNode.NoiseType value)
        {
            noiseType = value;
            Apply(force: false);
        }

        public void SetGain(float value01)
        {
            gain = Mathf.Clamp01(value01);
            Apply(force: false);
        }

        public void Reseed(int newSeed)
        {
            seed = newSeed;
            reseed = true;
            Apply(force: false);
        }
        
    }
}
