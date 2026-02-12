using System;
using CustomAudioPipeline.Nodes;
using UnityEngine;

namespace CustomAudioPipeline.Controller
{
    [AudioControllerMessageSupport(typeof(WaveformTriangleNode.TriangleWaveMessage))]
    public sealed class TriangleNodeController : BaseNodeController
    {
        [SerializeField]
        [FrequencyControl(minHz: 20.0f, maxHz: 20000.0f, minMidi: 24, maxMidi: 96)]
        private float frequency = 440.0f;
        [SerializeField] private bool isActive = true;

        private float _lastFrequency;
        private bool _lastActive;

        private void OnEnable()
        {
            _lastFrequency = float.NaN;
            _lastActive = !isActive;
        }

        private void Update()
        {
            if (Mathf.Approximately(_lastFrequency, frequency) && _lastActive == isActive)
            {
                return;
            }

            _lastFrequency = frequency;
            _lastActive = isActive;

            TrySend(new CustomAudioPipeline.Nodes.WaveformTriangleNode.TriangleWaveMessage(
                frequency,
                isActive
            ));
        }
    }
}