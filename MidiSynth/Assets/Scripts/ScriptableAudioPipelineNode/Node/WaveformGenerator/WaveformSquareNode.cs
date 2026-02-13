using Unity.Burst;
using Unity.IntegerTime;
using UnityEngine;
using UnityEngine.Audio;

namespace CustomAudioPipeline.Nodes
{
    [AudioNodeMessageSupport(typeof(SquareWaveMessage))]
    [CreateAssetMenu(fileName = "WaveformSquareNode", menuName = "Audio/Node/Waveform/Square")]
    public sealed class WaveformSquareNode : BaseAudioNode
    {
        [SerializeField] private float initialFrequency = 440.0f;
        [SerializeField, Range(0.0f, 1.0f)] private float initialDuty = 0.5f;
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
            return Processor.Allocate(context, initialFrequency, Mathf.Clamp01(initialDuty), initialIsActive);

        }

        [BurstCompile(CompileSynchronously = true)]
        private struct Processor : GeneratorInstance.IRealtime
        {
            private float frequency;
            private float duty;
            private float phase;

            private bool isActive;

            // de-click
            private float env;
            private float target;
            private int attackSamples;
            private int releaseSamples;
            private bool zeroCrossStop;
            private float lastSample;

            private GeneratorInstance.Setup setup;

            public bool isFinite => false;
            public bool isRealtime => true;
            public DiscreteTime? length => null;

            public static GeneratorInstance Allocate(ControlContext context, float initialFrequency, float initialDuty, bool initialIsActive)
            {
                return context.AllocateGenerator(new Processor(initialFrequency, initialDuty, initialIsActive), new Control());
            }

            private Processor(float initialFrequency, float initialDuty, bool initialIsActive, float attackMs = 1.0f, float releaseMs = 3.0f,
                bool zeroCrossStopFlag = false)
            {
                frequency = initialFrequency;
                duty = Mathf.Clamp(initialDuty, 0.001f, 0.999f);
                phase = 0.0f;

                isActive = initialIsActive;

                setup = new GeneratorInstance.Setup();

                // attack/release samples are re-calculated in Configure (sampleRate known there).
                attackSamples = 1;
                releaseSamples = 1;

                zeroCrossStop = zeroCrossStopFlag;

                env = initialIsActive ? 1.0f : 0.0f;
                target = env;
                lastSample = 0.0f;
            }

            public void Update(ProcessorInstance.UpdatedDataContext context, ProcessorInstance.Pipe pipe)
            {
                foreach (var element in pipe.GetAvailableData(context))
                {
                    if (element.TryGetData(out SquareWaveMessage d))
                    {
                        frequency = d.Freq;
                        duty = Mathf.Clamp(d.Ratio, 0.001f, 0.999f);
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
                    if (zeroCrossStop && target <= 0.0f && env > 0.0f)
                    {
                        float vDry = (phase < duty) ? 1.0f : -1.0f;
                        bool crossed = (lastSample <= 0.0f && vDry > 0.0f) || (lastSample >= 0.0f && vDry < 0.0f);

                        if (!crossed)
                        {
                            float vOutHold = vDry * env;
                            for (int ch = 0; ch < channels; ch++)
                            {
                                buffer[ch, frame] = vOutHold;
                            }

                            phase += frequency / sr;
                            if (phase >= 1.0f) phase -= 1.0f;

                            lastSample = vDry;
                            continue;
                        }
                    }

                    // envelope
                    if (target > env)
                    {
                        env = Mathf.Min(1.0f, env + 1.0f / attackSamples);
                    }
                    else if (target < env)
                    {
                        env = Mathf.Max(0.0f, env - 1.0f / releaseSamples);
                    }

                    float v = (phase < duty) ? 1.0f : -1.0f;
                    float vOut = v * env;

                    for (int ch = 0; ch < channels; ch++)
                    {
                        buffer[ch, frame] = vOut;
                    }

                    phase += frequency / sr;
                    if (phase >= 1.0f) phase -= 1.0f;

                    lastSample = v;
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

                    // re-calc envelope samples now that sampleRate is known
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
                    if (message.Is<SquareWaveMessage>())
                    {
                        pipe.SendData(context, message.Get<SquareWaveMessage>());
                        return ProcessorInstance.Response.Handled;
                    }

                    return ProcessorInstance.Response.Unhandled;
                }
            }
        }

        public readonly struct SquareWaveMessage
            {
                public readonly float Freq;
                public readonly float Ratio;
                public readonly bool IsActive;

                public SquareWaveMessage(float freq, float ratio, bool isActive)
                {
                    Freq = freq;
                    Ratio = ratio;
                    IsActive = isActive;
                }
            }
        
    }
}
