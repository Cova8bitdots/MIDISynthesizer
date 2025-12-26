using R3;
using UnityEngine;
using UnityEngine.Audio;

namespace TriWave
{

    [RequireComponent(typeof(AudioSource))]
    public class TriWaveGeneratorControl : MonoBehaviour, ISynthsizer
    {
        [SerializeField] AudioSource m_AudioSource;

        ReactiveProperty<float> Frequency = new(432);
        ReactiveProperty<bool> IsActive = new(false);
        void Reset()
        {
            TryGetComponent(out m_AudioSource);
        }

        private void Awake()
        {
            m_AudioSource ??= GetComponent<AudioSource>();
            Observable.Merge(Frequency.Skip(1).Select(_ => 0),
                IsActive.Skip(1).Select(_ => 0))
                .Where(_ => m_AudioSource.isPlaying)
                .Where(_ => ControlContext.builtIn.Exists(m_AudioSource.generatorInstance))
                .Subscribe(_ =>
                    {
                        var handle = m_AudioSource.generatorInstance;
                        var message =
                            new TriangleWaveGenerator.Processor.FrequencyData(Frequency.CurrentValue,
                                IsActive.CurrentValue);
                        ControlContext.builtIn.SendMessage(handle,
                            ref message
                            );
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
    }

}