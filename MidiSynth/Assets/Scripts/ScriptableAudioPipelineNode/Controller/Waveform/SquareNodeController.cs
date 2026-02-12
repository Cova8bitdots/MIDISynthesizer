using System;
using CustomAudioPipeline.Nodes;
using UnityEngine;

namespace CustomAudioPipeline.Controller
{
    [AudioControllerMessageSupport(typeof(WaveformSquareNode.SquareWaveMessage))]
    public sealed class SquareNodeController : BaseNodeController
    {
        [SerializeField, Range(1.0f, 20000.0f)] private float frequency = 440.0f;
        [SerializeField, Range(0.01f, 0.99f)] private float duty = 0.5f;
        [SerializeField] private bool isActive = true;

        private float _lastFrequency;
        private float _lastDuty;
        private bool _lastActive;

        private void OnEnable()
        {
            _lastFrequency = float.NaN;
            _lastDuty = float.NaN;
            _lastActive = !isActive;
        }

        private void Update()
        {
            if (Mathf.Approximately(_lastFrequency, frequency) &&
                Mathf.Approximately(_lastDuty, duty) &&
                _lastActive == isActive)
            {
                return;
            }

            _lastFrequency = frequency;
            _lastDuty = duty;
            _lastActive = isActive;

            TrySend(new CustomAudioPipeline.Nodes.WaveformSquareNode.SquareWaveMessage(
                frequency,
                duty,
                isActive
            ));
        }
    }
}