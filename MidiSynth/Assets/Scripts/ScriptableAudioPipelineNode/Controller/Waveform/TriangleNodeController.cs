using System;
using CustomAudioPipeline.Nodes;
using UnityEngine;

namespace CustomAudioPipeline.Controller
{
    [AudioControllerMessageSupport(typeof(WaveformTriangleNode.TriangleWaveMessage))]
    public sealed class TriangleNodeController : BaseNodeController
    {
        [SerializeField, Range(1.0f, 20000.0f)] private float frequency = 440.0f;
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