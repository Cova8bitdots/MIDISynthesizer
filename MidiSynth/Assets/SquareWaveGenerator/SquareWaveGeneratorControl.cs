using System;
using R3;
using UnityEngine;
using UnityEngine.Audio;

namespace SqrWaveGenerator
{
    public enum DutyRatio : int
    {
        UNDEFINED = 0,
        Eightth = 8,
        Quarter = 4,
        Half = 2,
    }
    [RequireComponent(typeof(AudioSource))]
    public class SquareWaveGeneratorControl : MonoBehaviour, ISynthsizer
    {
        [SerializeField] AudioSource m_AudioSource;
        public DutyRatio dutyRatio = DutyRatio.Eightth;

        ReactiveProperty<float> Frequency = new(432);
        ReactiveProperty<DutyRatio> Ratio = new(DutyRatio.Eightth);
        ReactiveProperty<bool> IsActive = new(false);

        void Reset()
        {
            TryGetComponent(out m_AudioSource);
        }

        private void Awake()
        {
            m_AudioSource ??= GetComponent<AudioSource>();
            
            Observable.Merge(
                    Frequency.Skip(1).Select(_ => 0),
                    Ratio.Skip(1).Select(_ => 0),
                    IsActive.Skip(1).Select(_ => 0))
                .Where(_ => m_AudioSource.isPlaying)
                .Where(_ => ControlContext.builtIn.Exists(m_AudioSource.generatorInstance))
                .Subscribe(_ =>
                    {
                        var handle = m_AudioSource.generatorInstance;
                        var message = new SquareWaveGenerator.Processor.WaveData(Frequency.CurrentValue,
                            1f/(float)Ratio.CurrentValue,
                            IsActive.CurrentValue);
                        ControlContext.builtIn.SendMessage(handle, ref message);

                    }
                ).AddTo(this);
        }
        

        public void SetFrequency(float frequency)
        {
            Frequency.Value =  Mathf.Clamp(frequency, 20f, 22050f);
        }

        public void SetActive(bool active)
        {
            IsActive.Value = active;
        }
        public void SetRatio(DutyRatio ratio)
        {
            Ratio.Value = ratio;
        }
    }

}