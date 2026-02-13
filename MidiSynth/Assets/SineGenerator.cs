using System;
using Unity.Burst;
using Unity.IntegerTime;
using UnityEngine;
using UnityEngine.Audio;

[CreateAssetMenu(fileName = "SineGenerator", menuName = "Sample/Create SineGenerator asset", order = 2)]
public class SineGenerator : ScriptableObject, IAudioGenerator
{
    public float initialFrequency;

    public bool isFinite => false;
    public bool isRealtime => true;
    public DiscreteTime? length => null;

    public GeneratorInstance CreateInstance(ControlContext context, AudioFormat? nestedFormat,
        ProcessorInstance.CreationParameters creationParameters)
    {
        return Processor.Allocate(context, initialFrequency);
    }

    [BurstCompile(CompileSynchronously = true)]
    internal struct Processor : GeneratorInstance.IRealtime
    {
        const float k_Tau = Mathf.PI * 2;

        float m_Frequency;
        float m_Phase;

        public static GeneratorInstance Allocate(ControlContext context, float frequency)
        {
            return context.AllocateGenerator(new Processor(frequency), new Control());
        }

        public bool isFinite => false;
        public bool isRealtime => true;
        public DiscreteTime? length => null;

        GeneratorInstance.Setup m_Setup;

        Processor(float frequency)
        {
            m_Frequency = frequency;
            m_Phase = 0.0f;
            m_Setup = new GeneratorInstance.Setup();
        }

        public void Update(ProcessorInstance.UpdatedDataContext context, ProcessorInstance.Pipe pipe)
        {
            var enumerator = pipe.GetAvailableData(context);

			foreach (var element in enumerator)
			{
	            if (element.TryGetData(out FrequencyData data))
    	            m_Frequency = data.Value;
        	    else
            	    Debug.Log("DataAvailable: unknown data."); 
			}
        }

        public GeneratorInstance.Result Process(in RealtimeContext ctx, ProcessorInstance.Pipe pipe, ChannelBuffer buffer, GeneratorInstance.Arguments args)
        {
            for (var frame = 0; frame < buffer.frameCount; frame++)
            {
                for (var channel = 0; channel < buffer.channelCount; channel++)
                    buffer[channel, frame] = Mathf.Sin(m_Phase * k_Tau);

                m_Phase += m_Frequency / m_Setup.sampleRate;

                // if (m_Phase > 1.0f) m_Phase -= 1.0f;
            }

            return buffer.frameCount;
        }

        struct Control : GeneratorInstance.IControl<Processor>
        {
            public void Configure(ControlContext context, ref Processor generator, in AudioFormat config, out GeneratorInstance.Setup setup, ref GeneratorInstance.Properties p)
            {
                generator.m_Setup = new GeneratorInstance.Setup(AudioSpeakerMode.Mono, config.sampleRate);
                setup = generator.m_Setup;
            }

            public void Dispose(ControlContext context, ref Processor processor) { }

            public void Update(ControlContext context, ProcessorInstance.Pipe pipe) { }

            public ProcessorInstance.Response OnMessage(ControlContext context, ProcessorInstance.Pipe pipe, ProcessorInstance.Message message)
            {
                return ProcessorInstance.Response.Unhandled;
            }
        }

        internal readonly struct FrequencyData
        {
            public readonly float Value;

            public FrequencyData(float value)
            {
                Value = value;
            }
        }
    }
}
