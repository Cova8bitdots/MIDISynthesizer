using System;
using CustomAudioPipeline.Nodes;
using UnityEngine;

namespace CustomAudioPipeline.Controller
{
    [AudioControllerMessageSupport(typeof(WaveformSineNode.SineMessage))]
    public sealed class SineNodeController : BaseNodeController
    {
        [SerializeField] 
        [FrequencyControl(minHz: 20.0f, maxHz: 20000.0f, minMidi: 24, maxMidi: 96)]
        private float frequency = 440.0f;
        [SerializeField] private bool isActive = true;

        private float _lastFreq;
        private bool _lastActive;

        private void OnEnable()
        {
            _lastFreq = float.NaN;
            _lastActive = !isActive;
        }

        private void Update()
        {
            if (Mathf.Approximately(_lastFreq, frequency) &&
                _lastActive == isActive)
            {
                return;
            }

            _lastFreq = frequency;
            _lastActive = isActive;

            TrySend(new CustomAudioPipeline.Nodes.WaveformSineNode.SineMessage(
                frequency,
                isActive
            ));
        }
    }
}