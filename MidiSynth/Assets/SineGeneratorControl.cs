using System;
using UnityEngine;
using UnityEngine.Audio;

[RequireComponent(typeof(AudioSource))]
public class SineGeneratorControl : MonoBehaviour
{
    [Range(100f, 5000f)]
    public float frequency = 432;

    AudioSource m_AudioSource;
    float m_PreviousFrequency;

    void Awake()
    {
        m_AudioSource = gameObject.GetComponent<AudioSource>();
    }

    void Update()
    {
        var handle = m_AudioSource.generatorInstance;

        if (!m_AudioSource.isPlaying
            || !ControlContext.builtIn.Exists(handle)
            || Mathf.Approximately(frequency, m_PreviousFrequency))
            return;

        var message = new SineGenerator.Processor.FrequencyData(frequency);
        ControlContext.builtIn.SendMessage(handle, ref message);
        m_PreviousFrequency = frequency;
    }
}
