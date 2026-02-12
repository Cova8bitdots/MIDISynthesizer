using Unity.Burst;
using Unity.IntegerTime;
using UnityEngine;
using UnityEngine.Audio;

namespace CustomAudioPipeline.Nodes
{
    [AudioNodeMessageSupport(typeof(TriangleWaveMessage))]
    [CreateAssetMenu(fileName = "WaveformTriangleNode", menuName = "Audio/Node/Waveform/Triangle")]
    public sealed class WaveformTriangleNode : BaseAudioNode
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

        [BurstCompile(CompileSynchronously = true)]
        private struct Processor : GeneratorInstance.IRealtime
        {
            private float frequency;
            private float phase;

            private bool isEnabled;

            // de-click
            private float env;
            private float target;
            private int attackSamples;
            private int releaseSamples;

            private GeneratorInstance.Setup setup;

            public bool isFinite => false;
            public bool isRealtime => true;
            public DiscreteTime? length => null;

            public static GeneratorInstance Allocate(ControlContext context, float initialFrequency, bool isInitialActive)
            {
                return context.AllocateGenerator(new Processor(initialFrequency, isInitialActive), new Control());
            }

            private Processor(float initialFrequency, bool isInitialActive, float attackMs = 1.0f, float releaseMs = 3.0f)
            {
                frequency = initialFrequency;
                phase = 0.0f;

                isEnabled = isInitialActive;

                setup = new GeneratorInstance.Setup();

                attackSamples = 1;
                releaseSamples = 1;

                env = isInitialActive ? 1.0f : 0.0f;
                target = env;
            }

            public void Update(ProcessorInstance.UpdatedDataContext context, ProcessorInstance.Pipe pipe)
            {
                foreach (var element in pipe.GetAvailableData(context))
                {
                    if (element.TryGetData(out TriangleWaveMessage d))
                    {
                        frequency = d.Value;
                        isEnabled = d.IsActive;
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

                target = isEnabled ? 1.0f : 0.0f;

                for (int frame = 0; frame < frames; frame++)
                {
                    if (target > env)
                    {
                        env = Mathf.Min(1.0f, env + 1.0f / attackSamples);
                    }
                    else if (target < env)
                    {
                        env = Mathf.Max(0.0f, env - 1.0f / releaseSamples);
                    }

                    float p = phase - Mathf.Floor(phase);
                    float tri = 4.0f * Mathf.Abs(p - 0.5f) - 1.0f;

                    float vOut = tri * env;

                    for (int ch = 0; ch < channels; ch++)
                    {
                        buffer[ch, frame] = vOut;
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
                    if (message.Is<TriangleWaveMessage>())
                    {
                        pipe.SendData(context, message.Get<TriangleWaveMessage>());
                        return ProcessorInstance.Response.Handled;
                    }

                    return ProcessorInstance.Response.Unhandled;
                }
            }
        }

        public readonly struct TriangleWaveMessage
            {
                public readonly float Value;
                public readonly bool IsActive;

                public TriangleWaveMessage(float value, bool isActive)
                {
                    Value = value;
                    IsActive = isActive;
                }
            }
        
    }
}
