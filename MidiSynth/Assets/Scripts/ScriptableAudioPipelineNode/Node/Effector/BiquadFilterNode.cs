using Unity.Burst;
using Unity.IntegerTime;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Audio;

namespace CustomAudioPipeline.Nodes
{
    [AudioNodeMessageSupport(typeof(FilterMessage))]
    [CreateAssetMenu(fileName = "BiquadFilterNode", menuName = "Audio/Node/Effector/Biquad")]
    public sealed class BiquadFilterNode : BaseAudioNode
    {
        public enum FilterMode
        {
            LowPass,
            HighPass,
            BandPass,
            Notch,
            Peak
        }

        [SerializeField] private FilterMode initialMode = FilterMode.BandPass;
        [SerializeField] private float initialFrequency = 1000.0f;
        [SerializeField] private float initialQ = 0.707f;
        [SerializeField] private float initialGainDb = 0.0f;
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
            return Processor.Allocate(
                context,
                hasUpstreamInstance,
                upstreamInstance,
                initialMode,
                initialFrequency,
                initialQ,
                initialGainDb,
                initialIsActive);
        }

        // =========================================================

        [BurstCompile(CompileSynchronously = true)]
        private struct Processor : GeneratorInstance.IRealtime
        {
            private bool hasUpstream;
            private GeneratorInstance upstream;

            private const int MaxChannels = 8;

            private FilterMode mode;
            private float frequency;
            private float q;
            private float gainDb;
            private bool isActive;

            // DF2T coefficients (normalized)
            private float c0;
            private float c1;
            private float c2;
            private float d1;
            private float d2;

            // per-channel states (z1,z2) up to 8ch
            private ChannelState z1;
            private ChannelState z2;
            private int channelCount;

            // de-click envelope
            private float env;
            private float target;
            private int attackSamples;
            private int releaseSamples;

            private GeneratorInstance.Setup setup;

            public bool isFinite => false;
            public bool isRealtime => true;
            public DiscreteTime? length => null;

            public static GeneratorInstance Allocate(
                ControlContext context,
                bool hasUpstreamInstance,
                GeneratorInstance upstreamInstance,
                FilterMode mode,
                float frequency,
                float q,
                float gainDb,
                bool isActive)
            {
                return context.AllocateGenerator(
                    new Processor(hasUpstreamInstance, upstreamInstance, mode, frequency, q, gainDb, isActive),
                    new Control()
                );
            }

            private Processor(
                bool hasUpstreamInstance,
                GeneratorInstance upstreamInstance,
                FilterMode mode,
                float frequency,
                float q,
                float gainDb,
                bool isActive)
            {
                hasUpstream = hasUpstreamInstance;
                upstream = upstreamInstance;

                this.mode = mode;
                this.frequency = frequency;
                this.q = q;
                this.gainDb = gainDb;
                this.isActive = isActive;

                c0 = c1 = c2 = 0.0f;
                d1 = d2 = 0.0f;

                z1 = default;
                z2 = default;
                channelCount = 0;

                env = isActive ? 1.0f : 0.0f;
                target = env;
                attackSamples = 1;
                releaseSamples = 1;

                setup = new GeneratorInstance.Setup();
            }

            public void Update(ProcessorInstance.UpdatedDataContext context, ProcessorInstance.Pipe pipe)
            {
                bool dirty = false;

                foreach (var element in pipe.GetAvailableData(context))
                {
                    if (!element.TryGetData(out FilterMessage msg))
                    {
                        continue;
                    }

                    mode = msg.Mode;
                    frequency = msg.Frequency;
                    q = msg.Q;
                    gainDb = msg.GainDb;
                    isActive = msg.IsActive;

                    dirty = true;
                }

                if (dirty)
                {
                    ComputeCoefficients(setup.sampleRate);
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

                // 1) Upstream を先に処理して buffer を埋める
                if (hasUpstream)
                {
                    ctx.Process(upstream, buffer, args);
                }
                else
                {
                    for (int frame = 0; frame < frames; frame++)
                    {
                        for (int ch = 0; ch < channels; ch++)
                        {
                            buffer[ch, frame] = 0.0f;
                        }
                    }
                }

                EnsureState(channels);

                target = isActive ? 1.0f : 0.0f;

                for (int frame = 0; frame < frames; frame++)
                {
                    // envelope smoothing（クリック防止）
                    if (target > env)
                    {
                        env = math.min(1.0f, env + 1.0f / attackSamples);
                    }
                    else if (target < env)
                    {
                        env = math.max(0.0f, env - 1.0f / releaseSamples);
                    }

                    for (int ch = 0; ch < channels; ch++)
                    {
                        float x = buffer[ch, frame];

                        // 8ch を超える場合は破綻回避でパススルー（必要なら後で拡張）
                        if (ch >= MaxChannels)
                        {
                            buffer[ch, frame] = x;
                            continue;
                        }

                        float s1 = z1.Get(ch);
                        float s2 = z2.Get(ch);

                        // DF2T
                        float y = c0 * x + s1;
                        s1 = c1 * x - d1 * y + s2;
                        s2 = c2 * x - d2 * y;

                        z1.Set(ch, s1);
                        z2.Set(ch, s2);

                        buffer[ch, frame] = x + (y - x) * env;
                    }
                }

                return frames;
            }

            private void EnsureState(int channels)
            {
                int clamped = math.min(channels, MaxChannels);

                if (channelCount == clamped)
                {
                    return;
                }

                channelCount = clamped;

                z1.ClearAll();
                z2.ClearAll();
            }

            private void ComputeCoefficients(int sampleRate)
            {
                float sr = math.max(1.0f, sampleRate);
                float f = math.clamp(frequency, 1.0f, 0.49f * sr);
                float qq = math.max(0.001f, q);

                float omega = 2.0f * math.PI * f / sr;
                float s = math.sin(omega);
                float c = math.cos(omega);
                float alpha = s / (2.0f * qq);
                float A = math.pow(10.0f, gainDb / 40.0f);

                float b0, b1, b2, a0, a1, a2;

                switch (mode)
                {
                    case FilterMode.LowPass:
                        b0 = (1.0f - c) * 0.5f;
                        b1 = 1.0f - c;
                        b2 = (1.0f - c) * 0.5f;
                        a0 = 1.0f + alpha;
                        a1 = -2.0f * c;
                        a2 = 1.0f - alpha;
                        break;

                    case FilterMode.HighPass:
                        b0 = (1.0f + c) * 0.5f;
                        b1 = -(1.0f + c);
                        b2 = (1.0f + c) * 0.5f;
                        a0 = 1.0f + alpha;
                        a1 = -2.0f * c;
                        a2 = 1.0f - alpha;
                        break;

                    case FilterMode.BandPass:
                        // RBJ BandPass (constant skirt gain, peak gain = Q)
                        b0 = s * 0.5f;
                        b1 = 0.0f;
                        b2 = -s * 0.5f;
                        a0 = 1.0f + alpha;
                        a1 = -2.0f * c;
                        a2 = 1.0f - alpha;
                        break;

                    case FilterMode.Notch:
                        b0 = 1.0f;
                        b1 = -2.0f * c;
                        b2 = 1.0f;
                        a0 = 1.0f + alpha;
                        a1 = -2.0f * c;
                        a2 = 1.0f - alpha;
                        break;

                    case FilterMode.Peak:
                        b0 = 1.0f + alpha * A;
                        b1 = -2.0f * c;
                        b2 = 1.0f - alpha * A;
                        a0 = 1.0f + alpha / A;
                        a1 = -2.0f * c;
                        a2 = 1.0f - alpha / A;
                        break;

                    default:
                        return;
                }

                float invA0 = 1.0f / a0;

                // normalized for DF2T
                c0 = b0 * invA0;
                c1 = b1 * invA0;
                c2 = b2 * invA0;
                d1 = a1 * invA0;
                d2 = a2 * invA0;
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
                    // AM と同じく Mono Setup（内部は buffer.channelCount を見る）
                    generator.setup = new GeneratorInstance.Setup(AudioSpeakerMode.Mono, config.sampleRate);

                    const float attackMs = 2.0f;
                    const float releaseMs = 10.0f;

                    generator.attackSamples =
                        Mathf.Max(1, Mathf.RoundToInt(attackMs * 0.001f * config.sampleRate));
                    generator.releaseSamples =
                        Mathf.Max(1, Mathf.RoundToInt(releaseMs * 0.001f * config.sampleRate));

                    generator.ComputeCoefficients(config.sampleRate);

                    setup = generator.setup;
                }

                public void Dispose(ControlContext context, ref Processor processor)
                {
                    // ★ ネスト child を破棄（リーク防止）
                    if (processor.hasUpstream)
                    {
                        context.Destroy(processor.upstream);
                    }
                }

                public void Update(ControlContext context, ProcessorInstance.Pipe pipe)
                {
                }

                public ProcessorInstance.Response OnMessage(
                    ControlContext context,
                    ProcessorInstance.Pipe pipe,
                    ProcessorInstance.Message message)
                {
                    if (message.Is<FilterMessage>())
                    {
                        pipe.SendData(context, message.Get<FilterMessage>());
                        return ProcessorInstance.Response.Handled;
                    }

                    return ProcessorInstance.Response.Unhandled;
                }
            }

            // Burst/Blittable 用：固定長8ch状態
            private struct ChannelState
            {
                private float v0;
                private float v1;
                private float v2;
                private float v3;
                private float v4;
                private float v5;
                private float v6;
                private float v7;

                public float Get(int index)
                {
                    switch (index)
                    {
                        case 0: return v0;
                        case 1: return v1;
                        case 2: return v2;
                        case 3: return v3;
                        case 4: return v4;
                        case 5: return v5;
                        case 6: return v6;
                        case 7: return v7;
                        default: return 0.0f;
                    }
                }

                public void Set(int index, float value)
                {
                    switch (index)
                    {
                        case 0: v0 = value; break;
                        case 1: v1 = value; break;
                        case 2: v2 = value; break;
                        case 3: v3 = value; break;
                        case 4: v4 = value; break;
                        case 5: v5 = value; break;
                        case 6: v6 = value; break;
                        case 7: v7 = value; break;
                    }
                }

                public void ClearAll()
                {
                    v0 = v1 = v2 = v3 = v4 = v5 = v6 = v7 = 0.0f;
                }
            }
        }

        // 1 message 方式
        public readonly struct FilterMessage
        {
            public readonly FilterMode Mode;
            public readonly float Frequency;
            public readonly float Q;
            public readonly float GainDb;
            public readonly bool IsActive;

            public FilterMessage(FilterMode mode, float frequency, float q, float gainDb, bool isActive)
            {
                Mode = mode;
                Frequency = frequency;
                Q = q;
                GainDb = gainDb;
                IsActive = isActive;
            }
        }
    }
}
