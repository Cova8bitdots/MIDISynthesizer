using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.IntegerTime;
using UnityEngine;
using UnityEngine.Audio;

namespace CustomAudioPipeline.Nodes
{
    [AudioNodeMessageSupport(typeof(FFTAnalyzeMessage))]
    [CreateAssetMenu(fileName = "FFTAnalyzeNode", menuName = "Audio/Node/Tool/FFTAnalyze")]
    public sealed class FFTAnalyzeNode : BaseAudioNode
    {
        [Header("Identity")]
        [SerializeField] private int analyzerId = 1;

        [Header("Initial Settings")]
        [SerializeField] private bool initialIsActive = true;

        [Header("Spectrum (Goertzel bank)")]
        [SerializeField] [Range(16, 128)] private int initialBinCount = 64;
        [SerializeField] [Range(20.0f, 20000.0f)] private float initialMinHz = 40.0f;
        [SerializeField] [Range(20.0f, 22050.0f)] private float initialMaxHz = 16000.0f;
        [SerializeField] [Range(128, 4096)] private int initialWindowSize = 2048;
        [SerializeField] [Range(1, 8)] private int initialHopDiv = 2;

        [Header("Send Rate (UI)")]
        [SerializeField] [Range(10.0f, 120.0f)] private float initialMaxSendHz = 60.0f;

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
                analyzerId,
                initialIsActive,
                initialBinCount,
                initialMinHz,
                initialMaxHz,
                initialWindowSize,
                initialHopDiv,
                initialMaxSendHz,
                hasUpstreamInstance,
                upstreamInstance
            );
        }

        // =========================================================
        // Messages (Controller -> Node)
        // =========================================================

        public readonly struct FFTAnalyzeMessage
        {
            public readonly bool IsActive;
            public readonly int BinCount;
            public readonly float MinHz;
            public readonly float MaxHz;
            public readonly int WindowSize;
            public readonly int HopDiv;
            public readonly float MaxSendHz;

            public FFTAnalyzeMessage(bool isActive, int binCount, float minHz, float maxHz, int windowSize, int hopDiv, float maxSendHz)
            {
                IsActive = isActive;
                BinCount = binCount;
                MinHz = minHz;
                MaxHz = maxHz;
                WindowSize = windowSize;
                HopDiv = hopDiv;
                MaxSendHz = maxSendHz;
            }
        }

        // =========================================================
        // Realtime -> Control (Spectrum output)
        // =========================================================

        public readonly struct SpectrumFrame
        {
            public readonly int AnalyzerId;
            public readonly float DspTime;
            public readonly int BinCount;
            public readonly FixedList512Bytes<float> Bins; // power (not dB)

            public SpectrumFrame(int analyzerId, float dspTime, int binCount, in FixedList512Bytes<float> bins)
            {
                AnalyzerId = analyzerId;
                DspTime = dspTime;
                BinCount = binCount;
                Bins = bins;
            }
        }

        public static class SpectrumBus
        {
            private struct Entry
            {
                public double DspTime;
                public int BinCount;
                public float[] Bins;
            }

            private static readonly Dictionary<int, Entry> Map = new Dictionary<int, Entry>(8);

            public static bool TryGetLatest(int id, out float[] bins, out double dspTime)
            {
                lock (Map)
                {
                    if (!Map.TryGetValue(id, out var e) || e.Bins == null)
                    {
                        bins = null;
                        dspTime = 0.0;
                        return false;
                    }

                    bins = e.Bins;
                    dspTime = e.DspTime;
                    return true;
                }
            }

            internal static void Publish(in SpectrumFrame frame)
            {
                lock (Map)
                {
                    if (!Map.TryGetValue(frame.AnalyzerId, out var e) ||
                        e.Bins == null ||
                        e.Bins.Length != frame.BinCount)
                    {
                        e = new Entry
                        {
                            BinCount = frame.BinCount,
                            Bins = new float[frame.BinCount]
                        };
                    }

                    int n = Mathf.Min(frame.BinCount, frame.Bins.Length);
                    for (int i = 0; i < n; i++)
                    {
                        e.Bins[i] = frame.Bins[i];
                    }

                    e.DspTime = frame.DspTime;
                    Map[frame.AnalyzerId] = e;
                }
            }
        }

        // =========================================================

        [BurstCompile(CompileSynchronously = true)]
        private struct Processor : GeneratorInstance.IRealtime
        {
            private int analyzerId;
            private bool isActive;

            private int binCount;
            private float minHz;
            private float maxHz;

            private int windowSize;
            private int hopDiv;

            private float maxSendHz;
            private double lastSentDspTime;

            private bool hasUpstream;
            private GeneratorInstance upstream;

            private GeneratorInstance.Setup setup;

            private NativeArray<float> window; // mono ring buffer
            private int writePos;
            private int hopCounter;

            public bool isFinite => false;
            public bool isRealtime => true;
            public DiscreteTime? length => null;

            public static GeneratorInstance Allocate(
                ControlContext context,
                int analyzerId,
                bool isActive,
                int binCount,
                float minHz,
                float maxHz,
                int windowSize,
                int hopDiv,
                float maxSendHz,
                bool hasUpstream,
                GeneratorInstance upstream)
            {
                return context.AllocateGenerator(
                    new Processor(
                        analyzerId,
                        isActive,
                        binCount,
                        minHz,
                        maxHz,
                        windowSize,
                        hopDiv,
                        maxSendHz,
                        hasUpstream,
                        upstream
                    ),
                    new Control()
                );
            }

            private Processor(
                int analyzerId,
                bool isActive,
                int binCount,
                float minHz,
                float maxHz,
                int windowSize,
                int hopDiv,
                float maxSendHz,
                bool hasUpstream,
                GeneratorInstance upstream)
            {
                this.analyzerId = analyzerId;
                this.isActive = isActive;

                this.binCount = Mathf.Clamp(binCount, 16, 128);
                this.minHz = Mathf.Max(1.0f, minHz);
                this.maxHz = Mathf.Max(this.minHz + 1.0f, maxHz);

                this.windowSize = ToPow2(Mathf.Clamp(windowSize, 128, 4096));
                this.hopDiv = Mathf.Clamp(hopDiv, 1, 8);

                this.maxSendHz = Mathf.Clamp(maxSendHz, 10.0f, 120.0f);
                lastSentDspTime = -9999.0;

                this.hasUpstream = hasUpstream;
                this.upstream = upstream;

                setup = new GeneratorInstance.Setup();

                window = default;
                writePos = 0;
                hopCounter = 0;
            }

            // Controller -> Control.OnMessage -> pipe.SendData(ControlContext, msg) -> here
            public void Update(ProcessorInstance.UpdatedDataContext context, ProcessorInstance.Pipe pipe)
            {
                foreach (var element in pipe.GetAvailableData(context))
                {
                    if (!element.TryGetData(out FFTAnalyzeMessage msg))
                    {
                        continue;
                    }

                    isActive = msg.IsActive;

                    binCount = Mathf.Clamp(msg.BinCount, 16, 128);
                    minHz = Mathf.Max(1.0f, msg.MinHz);
                    maxHz = Mathf.Max(minHz + 1.0f, msg.MaxHz);

                    windowSize = ToPow2(Mathf.Clamp(msg.WindowSize, 128, 4096));
                    hopDiv = Mathf.Clamp(msg.HopDiv, 1, 8);

                    maxSendHz = Mathf.Clamp(msg.MaxSendHz, 10.0f, 120.0f);
                }
            }

            public GeneratorInstance.Result Process(
                in RealtimeContext ctx,
                ProcessorInstance.Pipe pipe,
                ChannelBuffer buffer,
                GeneratorInstance.Arguments args)
            {
                int frames = buffer.frameCount;

                // 1) Pass-through upstream into this buffer
                if (hasUpstream)
                {
                    ctx.Process(upstream, buffer, default);
                }
                else
                {
                    for (int ch = 0; ch < buffer.channelCount; ch++)
                    {
                        for (int i = 0; i < frames; i++)
                        {
                            buffer[ch, i] = 0.0f;
                        }
                    }
                }

                if (!isActive)
                {
                    return frames;
                }

                if (!window.IsCreated || window.Length != windowSize)
                {
                    return frames;
                }

                // 2) Mix to mono and write into ring buffer
                int channels = buffer.channelCount;
                float invCh = 1.0f / Mathf.Max(1, channels);

                for (int i = 0; i < frames; i++)
                {
                    float s = 0.0f;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        s += buffer[ch, i];
                    }

                    s *= invCh;

                    window[writePos] = s;
                    writePos++;
                    if (writePos >= windowSize)
                    {
                        writePos = 0;
                    }
                }

                // 3) Hop-based update
                hopCounter += frames;
                int hop = Mathf.Max(1, windowSize / Mathf.Max(1, hopDiv));
                if (hopCounter < hop)
                {
                    return frames;
                }

                hopCounter -= hop;

                // 4) UI send-rate throttle
                double interval = 1.0 / Mathf.Max(1.0f, maxSendHz);
                if (ctx.dspTime - lastSentDspTime < interval)
                {
                    return frames;
                }

                lastSentDspTime = ctx.dspTime;

                // 5) Compute spectrum (Goertzel bank) and send to Control
                var bins = new FixedList512Bytes<float>();
                ComputeSpectrumGoertzel(ref bins);

                pipe.SendData(ctx, new SpectrumFrame(analyzerId, (float)ctx.dspTime, binCount, bins));

                return frames;
            }

            private void ComputeSpectrumGoertzel(ref FixedList512Bytes<float> outBins)
            {
                outBins.Clear();

                int n = windowSize;
                float sr = setup.sampleRate;

                float logMin = Mathf.Log10(minHz);
                float logMax = Mathf.Log10(maxHz);
                float inv = 1.0f / Mathf.Max(1, binCount - 1);

                for (int b = 0; b < binCount; b++)
                {
                    float t = b * inv;
                    float hz = Mathf.Pow(10.0f, Mathf.Lerp(logMin, logMax, t));

                    float w = 2.0f * Mathf.PI * hz / sr;
                    float c = Mathf.Cos(w);
                    float coeff = 2.0f * c;

                    float q0 = 0.0f;
                    float q1 = 0.0f;
                    float q2 = 0.0f;

                    int readPos = writePos; // start from newest point
                    for (int i = 0; i < n; i++)
                    {
                        float x = window[readPos];

                        readPos++;
                        if (readPos >= n)
                        {
                            readPos = 0;
                        }

                        q0 = coeff * q1 - q2 + x;
                        q2 = q1;
                        q1 = q0;
                    }

                    float power = q1 * q1 + q2 * q2 - coeff * q1 * q2;
                    outBins.Add(power);
                }
            }

            private static int ToPow2(int v)
            {
                int p = 1;
                while ((p << 1) <= v)
                {
                    p <<= 1;
                }

                return p;
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

                    if (generator.window.IsCreated)
                    {
                        generator.window.Dispose();
                    }

                    generator.window = new NativeArray<float>(generator.windowSize, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                    generator.writePos = 0;
                    generator.hopCounter = 0;
                    generator.lastSentDspTime = -9999.0;
                }

                public void Dispose(ControlContext context, ref Processor processor)
                {
                    if (processor.window.IsCreated)
                    {
                        processor.window.Dispose();
                    }
                }

                // Realtime -> Control のスペクトル結果を受け取って UI へ共有
                public void Update(ControlContext context, ProcessorInstance.Pipe pipe)
                {
                    foreach (var element in pipe.GetAvailableData(context))
                    {
                        if (element.TryGetData(out SpectrumFrame frame))
                        {
                            SpectrumBus.Publish(frame);
                        }
                    }
                }

                // Controller -> Node (Sine と同じ流儀)
                public ProcessorInstance.Response OnMessage(
                    ControlContext context,
                    ProcessorInstance.Pipe pipe,
                    ProcessorInstance.Message message)
                {
                    if (message.Is<FFTAnalyzeMessage>())
                    {
                        pipe.SendData(context, message.Get<FFTAnalyzeMessage>());
                        return ProcessorInstance.Response.Handled;
                    }

                    return ProcessorInstance.Response.Unhandled;
                }
            }
        }
    }
}
