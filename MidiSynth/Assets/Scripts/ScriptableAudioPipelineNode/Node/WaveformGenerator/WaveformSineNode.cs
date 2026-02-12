using Unity.Burst;
using Unity.IntegerTime;
using UnityEngine;
using UnityEngine.Audio;

namespace CustomAudioPipeline.Nodes
{
    [AudioNodeMessageSupport(typeof(SineMessage))]
    [CreateAssetMenu(fileName = "WaveformSineNode", menuName = "CustomAudioPipeline/Nodes/Waveform/Sine")]
    public sealed class WaveformSineNode : BaseAudioNode
    {
        [SerializeField] private float initialFrequency = 440.0f;
        [SerializeField] private bool initialIsActive = true;

        public override bool isFinite => false;
        public override DiscreteTime? length => null;

        protected override GeneratorInstance CreateNodeInstance(
            ControlContext context,
            AudioFormat? nestedConfiguration,
            ProcessorInstance.CreationParameters creationParameters,
            bool hasUpstreamInstance,
            GeneratorInstance upstreamInstance)
        {
            return Processor.Allocate(context, initialFrequency, initialIsActive);
        }

        // =========================================================

        [BurstCompile(CompileSynchronously = true)]
        private struct Processor : GeneratorInstance.IRealtime
        {
            private const float Tau = Mathf.PI * 2.0f;

            private float frequency;
            private float phase;

            private bool isActive;

            // de-click envelope
            private float env;
            private float target;
            private int attackSamples;
            private int releaseSamples;

            private GeneratorInstance.Setup setup;

            public bool isFinite => false;
            public bool isRealtime => true;
            public DiscreteTime? length => null;

            public static GeneratorInstance Allocate(ControlContext context, float freq, bool isActive)
            {
                return context.AllocateGenerator(
                    new Processor(freq, isActive),
                    new Control()
                );
            }

            private Processor(float freq, bool active, float attackMs = 1.0f, float releaseMs = 3.0f)
            {
                frequency = freq;
                phase = 0.0f;

                isActive = active;

                env = active ? 1.0f : 0.0f;
                target = env;

                setup = new GeneratorInstance.Setup();

                attackSamples = 1;
                releaseSamples = 1;
            }

            public void Update(ProcessorInstance.UpdatedDataContext context, ProcessorInstance.Pipe pipe)
            {
                foreach (var element in pipe.GetAvailableData(context))
                {
                    if (element.TryGetData(out SineMessage d))
                    {
                        frequency = d.Frequency;
                        isActive = d.IsActive;
                    }
                }
            }

            public GeneratorInstance.Result Process(
                in RealtimeContext ctx,
                ProcessorInstance.Pipe pipe,
                ChannelBuffer buffer,
                GeneratorInstance.Arguments args)
            {
                int frames = buffer.frameCount;
                int channels = buffer.channelCount;
                float sr = setup.sampleRate;

                target = isActive ? 1.0f : 0.0f;

                for (int frame = 0; frame < frames; frame++)
                {
                    // envelope smoothing
                    if (target > env)
                    {
                        env = Mathf.Min(1.0f, env + 1.0f / attackSamples);
                    }
                    else if (target < env)
                    {
                        env = Mathf.Max(0.0f, env - 1.0f / releaseSamples);
                    }

                    float s = Mathf.Sin(phase * Tau) * env;

                    for (int ch = 0; ch < channels; ch++)
                    {
                        buffer[ch, frame] = s;
                    }

                    phase += frequency / sr;
                    if (phase >= 1.0f) phase -= 1.0f;
                }

                return frames;
            }

            private struct Control : GeneratorInstance.IControl<Processor>
            {
                public void Configure(
                    ControlContext context,
                    ref Processor generator,
                    in AudioFormat config,
                    out GeneratorInstance.Setup setup,
                    ref GeneratorInstance.Properties p)
                {
                    generator.setup = new GeneratorInstance.Setup(AudioSpeakerMode.Mono, config.sampleRate);

                    const float attackMs = 1.0f;
                    const float releaseMs = 3.0f;

                    generator.attackSamples =
                        Mathf.Max(1, Mathf.RoundToInt(attackMs * 0.001f * generator.setup.sampleRate));
                    generator.releaseSamples =
                        Mathf.Max(1, Mathf.RoundToInt(releaseMs * 0.001f * generator.setup.sampleRate));

                    setup = generator.setup;
                }

                public void Dispose(ControlContext context, ref Processor processor)
                {
                }

                public void Update(ControlContext context, ProcessorInstance.Pipe pipe)
                {
                }

                public ProcessorInstance.Response OnMessage(
                    ControlContext context,
                    ProcessorInstance.Pipe pipe,
                    ProcessorInstance.Message message)
                {
                    if (message.Is<SineMessage>())
                    {
                        pipe.SendData(context, message.Get<SineMessage>());
                        return ProcessorInstance.Response.Handled;
                    }

                    return ProcessorInstance.Response.Unhandled;
                }
            }
        }

        // ★ public にする（Controller から送れるように）
            public readonly struct SineMessage
            {
                public readonly float Frequency;
                public readonly bool IsActive;

                public SineMessage(float frequency, bool isActive)
                {
                    Frequency = frequency;
                    IsActive = isActive;
                }
            }
        
    }
}
