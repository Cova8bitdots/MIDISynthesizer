using Unity.Burst;
using Unity.IntegerTime;
using UnityEngine;
using UnityEngine.Audio;

namespace CustomAudioPipeline.Nodes
{
    [AudioNodeMessageSupport(typeof(AmMessage))]
    [CreateAssetMenu(fileName = "AmplitudeModulationNode", menuName = "Audio/Node/Effector/AmplitudeModulation")]
    public sealed class AmplitudeModulationNode : BaseAudioNode
    {
        [Header("AM Params")]
        [SerializeField] private bool isActive = true;
        [SerializeField, Range(0.0f, 1.0f)] private float depth = 0.6f;
        [SerializeField, Range(1.0f, 480.0f)] private float bpm = 120.0f;
        [SerializeField, Range(1.0f, 2000.0f)] private float releaseMs = 250.0f;
        [SerializeField, Range(0.0f, 1.0f)] private float phaseOffset01 = 0.0f;

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
                isActive,
                depth,
                bpm,
                releaseMs,
                phaseOffset01
            );
        }

        /// <summary>
        /// Controller から送る AM パラメータメッセージ（unmanaged）。
        /// </summary>
        public readonly struct AmMessage
        {
            public readonly bool IsActive;
            public readonly float Depth;
            public readonly float Bpm;
            public readonly float ReleaseMs;
            public readonly float PhaseOffset01;

            public AmMessage(bool isActive, float depth, float bpm, float releaseMs, float phaseOffset01)
            {
                IsActive = isActive;
                Depth = depth;
                Bpm = bpm;
                ReleaseMs = releaseMs;
                PhaseOffset01 = phaseOffset01;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct Processor : GeneratorInstance.IRealtime
        {
            private bool hasUpstream;
            private GeneratorInstance upstream;

            // Target（受信した値）
            private bool tIsActive;
            private float tDepth;
            private float tBpm;
            private float tReleaseMs;
            private float tPhaseOffset01;

            // State
            private float phase01;
            private float smoothedGain;

            private GeneratorInstance.Setup setup;

            public bool isFinite => false;
            public bool isRealtime => true;
            public DiscreteTime? length => null;

            public static GeneratorInstance Allocate(
                ControlContext context,
                bool hasUpstreamInstance,
                GeneratorInstance upstreamInstance,
                bool initialIsActive,
                float initialDepth,
                float initialBpm,
                float initialReleaseMs,
                float initialPhaseOffset01)
            {
                return context.AllocateGenerator(
                    new Processor(
                        hasUpstreamInstance,
                        upstreamInstance,
                        initialIsActive,
                        initialDepth,
                        initialBpm,
                        initialReleaseMs,
                        initialPhaseOffset01
                    ),
                    new Control()
                );
            }

            private Processor(
                bool hasUpstreamInstance,
                GeneratorInstance upstreamInstance,
                bool initialIsActive,
                float initialDepth,
                float initialBpm,
                float initialReleaseMs,
                float initialPhaseOffset01)
            {
                hasUpstream = hasUpstreamInstance;
                upstream = upstreamInstance;

                tIsActive = initialIsActive;
                tDepth = Mathf.Clamp01(initialDepth);
                tBpm = Mathf.Clamp(initialBpm, 1.0f, 480.0f);
                tReleaseMs = Mathf.Max(0.0f, initialReleaseMs);
                tPhaseOffset01 = initialPhaseOffset01 - Mathf.Floor(initialPhaseOffset01);

                phase01 = 0.0f;
                smoothedGain = 1.0f;

                setup = new GeneratorInstance.Setup();
            }

            public void Update(ProcessorInstance.UpdatedDataContext context, ProcessorInstance.Pipe pipe)
            {
                foreach (var element in pipe.GetAvailableData(context))
                {
                    if (!element.TryGetData(out AmMessage m))
                    {
                        continue;
                    }

                    tIsActive = m.IsActive;
                    tDepth = Mathf.Clamp01(m.Depth);
                    tBpm = Mathf.Clamp(m.Bpm, 1.0f, 480.0f);
                    tReleaseMs = Mathf.Max(0.0f, m.ReleaseMs);
                    tPhaseOffset01 = m.PhaseOffset01 - Mathf.Floor(m.PhaseOffset01);
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

                // 2) AM をクリックレスで掛ける（gain smoothing）
                float sr = setup.sampleRate;
                if (sr <= 0.0f)
                {
                    return frames;
                }

                // gain の追従時間（ms）
                // - すべてのパラメータ変更で「プツ」を止める主砲
                // - releaseMs は OFF 時に“より長く”したいので反映（ON 時は 10ms 固定）
                float smoothMs = tIsActive ? 10.0f : Mathf.Max(10.0f, tReleaseMs);
                float gainAlpha = 1.0f - Mathf.Exp(-1.0f / (sr * (smoothMs * 0.001f)));

                float effectiveDepth = tIsActive ? tDepth : 0.0f;
                float freq = tBpm / 60.0f;
                float phaseInc = freq / sr;

                float offset = tPhaseOffset01;

                for (int frame = 0; frame < frames; frame++)
                {
                    float p = phase01 + offset;
                    p -= Mathf.Floor(p);

                    float lfo01 = 0.5f + 0.5f * Mathf.Sin(p * (Mathf.PI * 2.0f));
                    float targetGain = Mathf.Lerp(1.0f - effectiveDepth, 1.0f, lfo01);

                    smoothedGain += (targetGain - smoothedGain) * gainAlpha;

                    for (int ch = 0; ch < channels; ch++)
                    {
                        buffer[ch, frame] *= smoothedGain;
                    }

                    phase01 += phaseInc;
                    if (phase01 >= 1.0f)
                    {
                        phase01 -= 1.0f;
                    }
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
                    if (message.Is<AmMessage>())
                    {
                        pipe.SendData(context, message.Get<AmMessage>());
                        return ProcessorInstance.Response.Handled;
                    }

                    return ProcessorInstance.Response.Unhandled;
                }
            }
        }
    }
}
