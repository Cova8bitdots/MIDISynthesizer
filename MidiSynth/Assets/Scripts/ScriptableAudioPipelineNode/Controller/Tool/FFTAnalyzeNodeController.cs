using CustomAudioPipeline.Nodes;
using UnityEngine;

namespace CustomAudioPipeline.Controller
{
    [AudioControllerMessageSupport(typeof(FFTAnalyzeNode.FFTAnalyzeMessage))]
    public sealed class FFTAnalyzeNodeController : BaseNodeController
    {
        [Header("Analyzer")]
        [SerializeField] private bool isActive = true;

        [Header("Spectrum")]
        [SerializeField] [Range(16, 128)] private int binCount = 64;
        [SerializeField] [Range(20.0f, 20000.0f)] private float minHz = 40.0f;
        [SerializeField] [Range(20.0f, 22050.0f)] private float maxHz = 16000.0f;
        [SerializeField] [Range(128, 4096)] private int windowSize = 2048;
        public int WindowSize => windowSize;
        [SerializeField] [Range(1, 8)] private int hopDiv = 2;
        [SerializeField] [Range(1, 60)] private int maxSendHz = 30;

        private bool _lastActive;
        private int _lastBinCount;
        private float _lastMinHz;
        private float _lastMaxHz;
        private int _lastWindowSize;
        private int _lastHopDiv;
        private int _lastSendHz;

        private void OnEnable()
        {
            _lastActive = !isActive;
            _lastBinCount = -1;
            _lastMinHz = float.NaN;
            _lastMaxHz = float.NaN;
            _lastWindowSize = -1;
            _lastHopDiv = -1;
            _lastSendHz = 1;
        }

        private void Update()
        {
            if (_lastActive == isActive &&
                _lastBinCount == binCount &&
                Mathf.Approximately(_lastMinHz, minHz) &&
                Mathf.Approximately(_lastMaxHz, maxHz) &&
                _lastWindowSize == windowSize &&
                _lastHopDiv == hopDiv&&
                _lastSendHz == maxSendHz)
            {
                return;
            }

            _lastActive = isActive;
            _lastBinCount = binCount;
            _lastMinHz = minHz;
            _lastMaxHz = maxHz;
            _lastWindowSize = windowSize;
            _lastHopDiv = hopDiv;
            _lastSendHz = maxSendHz;
            TrySend(new FFTAnalyzeNode.FFTAnalyzeMessage(
                isActive,
                binCount,
                minHz,
                maxHz,
                windowSize,
                hopDiv,
                maxSendHz
            ));
        }
    }
}
