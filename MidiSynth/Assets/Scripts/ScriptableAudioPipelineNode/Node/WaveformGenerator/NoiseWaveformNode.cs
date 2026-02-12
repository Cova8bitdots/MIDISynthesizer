using Unity.Burst;
using Unity.IntegerTime;
using UnityEngine;
using UnityEngine.Audio;

namespace CustomAudioPipeline.Nodes
{
    [AudioNodeMessageSupport(typeof(NoiseMessage))]
    [CreateAssetMenu(fileName = "NoiseWaveformNode", menuName = "Audio/Node/Waveform/Noise")]
    public sealed class NoiseWaveformNode : BaseAudioNode
    {
        public enum NoiseType
        {
            White,
            Pink,
            Red
        }

        [SerializeField] private NoiseType initialNoiseType = NoiseType.White;
        [SerializeField, Range(0.0f, 1.0f)] private float initialGain = 0.2f;
        [SerializeField] private bool initialIsActive = true;
        [SerializeField] private int initialSeed = 22222;

        public override bool isFinite => false;
        public override DiscreteTime? length => null;

        protected override GeneratorInstance CreateNodeInstance(
            ControlContext context,
            AudioFormat? nestedConfiguration,
            ProcessorInstance.CreationParameters creationParameters,
            bool hasUpstreamInstance,
            GeneratorInstance upstreamInstance)
        {
            return Processor.Allocate(context, initialNoiseType, initialGain, initialIsActive, initialSeed);
        }

        // =========================================================

        [BurstCompile(CompileSynchronously = true)]
        private struct Processor : GeneratorInstance.IRealtime
        {
            private NoiseType noiseType;
            private float gain;
            private bool isActive;

            private uint rngState;

            // Pink noise state (Paul Kellet approximation)
            private float b0;
            private float b1;
            private float b2;
            private float b3;
            private float b4;
            private float b5;
            private float b6;

            // Red/Brown state
            private float brown;

            // de-click envelope
            private float env;
            private float target;
            private int attackSamples;
            private int releaseSamples;

            private GeneratorInstance.Setup setup;

            public bool isFinite => false;
            public bool isRealtime => true;
            public DiscreteTime? length => null;

            public static GeneratorInstance Allocate(ControlContext context, NoiseType type, float gain, bool isActive, int seed)
            {
                return context.AllocateGenerator(
                    new Processor(type, gain, isActive, seed),
                    new Control()
                );
            }

            private Processor(NoiseType type, float gain, bool active, int seed)
            {
                noiseType = type;
                this.gain = Mathf.Clamp01(gain);
                isActive = active;

                rngState = MakeSeed(seed);

                b0 = b1 = b2 = b3 = b4 = b5 = b6 = 0.0f;
                brown = 0.0f;

                env = active ? this.gain : 0.0f;
                target = env;

                setup = new GeneratorInstance.Setup();

                attackSamples = 1;
                releaseSamples = 1;
            }

            public void Update(ProcessorInstance.UpdatedDataContext context, ProcessorInstance.Pipe pipe)
            {
                foreach (var element in pipe.GetAvailableData(context))
                {
                    if (!element.TryGetData(out NoiseMessage d))
                    {
                        continue;
                    }

                    noiseType = d.Type;
                    gain = Mathf.Clamp01(d.Gain);
                    isActive = d.IsActive;

                    if (d.Reseed)
                    {
                        rngState = MakeSeed(d.Seed);
                        b0 = b1 = b2 = b3 = b4 = b5 = b6 = 0.0f;
                        brown = 0.0f;
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

                target = isActive ? gain : 0.0f;

                for (int frame = 0; frame < frames; frame++)
                {
                    // envelope smoothing (gain 포함)
                    if (target > env)
                    {
                        env = Mathf.Min(target, env + 1.0f / attackSamples);
                    }
                    else if (target < env)
                    {
                        env = Mathf.Max(target, env - 1.0f / releaseSamples);
                    }

                    float n = GenerateNoiseSample();
                    float s = n * env;

                    for (int ch = 0; ch < channels; ch++)
                    {
                        buffer[ch, frame] = s;
                    }
                }

                return frames;
            }

            private float GenerateNoiseSample()
            {
                switch (noiseType)
                {
                    case NoiseType.White:
                        return NextWhite();

                    case NoiseType.Pink:
                        return NextPink();

                    case NoiseType.Red:
                        return NextRed();

                    default:
                        return 0.0f;
                }
            }

            private static uint MakeSeed(int seed)
            {
                // xorshift32 は 0 を避けたい
                unchecked
                {
                    uint s = (uint)seed;
                    if (s == 0) s = 1u;
                    // 少し拡散
                    s ^= 0xA3C59AC3u;
                    return s;
                }
            }

            private float NextWhite()
            {
                // xorshift32
                rngState ^= rngState << 13;
                rngState ^= rngState >> 17;
                rngState ^= rngState << 5;

                // [0,1] -> [-1,1]
                float u = rngState * (1.0f / uint.MaxValue);
                return u * 2.0f - 1.0f;
            }

            private float NextPink()
            {
                // Paul Kellet pink noise approximation
                float white = NextWhite();

                b0 = 0.99886f * b0 + white * 0.0555179f;
                b1 = 0.99332f * b1 + white * 0.0750759f;
                b2 = 0.96900f * b2 + white * 0.1538520f;
                b3 = 0.86650f * b3 + white * 0.3104856f;
                b4 = 0.55000f * b4 + white * 0.5329522f;
                b5 = -0.7616f * b5 - white * 0.0168980f;

                float pink = b0 + b1 + b2 + b3 + b4 + b5 + b6 + white * 0.5362f;
                b6 = white * 0.115926f;

                // normalization (rough)
                return pink * 0.11f;
            }

            private float NextRed()
            {
                // Brownian(=Red) noise: integrate white with leakage to avoid drift
                float white = NextWhite();

                // leak + integrate
                brown = brown * 0.995f + white * 0.02f;

                // clamp to keep stable
                brown = Mathf.Clamp(brown, -1.0f, 1.0f);

                // gain compensation (rough)
                return brown * 3.5f;
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
                    // SineNode と同様に Mono setup（出力は Process 側で全chに複製）
                    generator.setup = new GeneratorInstance.Setup(AudioSpeakerMode.Mono, config.sampleRate);

                    const float attackMs = 2.0f;
                    const float releaseMs = 10.0f;

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
                    if (message.Is<NoiseMessage>())
                    {
                        pipe.SendData(context, message.Get<NoiseMessage>());
                        return ProcessorInstance.Response.Handled;
                    }

                    return ProcessorInstance.Response.Unhandled;
                }
            }
        }

        // ★ public にする（Controller から送れるように）
        public readonly struct NoiseMessage
        {
            public readonly NoiseType Type;
            public readonly float Gain;
            public readonly bool IsActive;

            public readonly int Seed;
            public readonly bool Reseed;

            public NoiseMessage(NoiseType type, float gain, bool isActive, int seed, bool reseed)
            {
                Type = type;
                Gain = gain;
                IsActive = isActive;
                Seed = seed;
                Reseed = reseed;
            }
        }
    }
}
