using System;
using UnityEngine;

namespace CustomAudioPipeline.Controller
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FrequencyControlAttribute : PropertyAttribute
    {
        public readonly float MinHz;
        public readonly float MaxHz;
        public readonly int MinMidi;
        public readonly int MaxMidi;

        public FrequencyControlAttribute(float minHz, float maxHz, int minMidi = 24, int maxMidi = 96)
        {
            MinHz = minHz;
            MaxHz = maxHz;
            MinMidi = minMidi;
            MaxMidi = maxMidi;
        }
    }
}