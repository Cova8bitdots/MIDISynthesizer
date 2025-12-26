using System;
using Unity.Burst;
using Unity.IntegerTime;
using UnityEngine;
using UnityEngine.Audio;

[CreateAssetMenu(fileName = "TriangleWaveGenerator", menuName = "Audio/Generator/TriangleWaveGenerator")]
public class TriangleWaveGenerator : ScriptableObject, IAudioGenerator
{
    public float initialFrequency;

    public bool isFinite => false;
    public bool isRealtime => true;
    public DiscreteTime? length => null;

    public GeneratorInstance CreateInstance(ControlContext context, AudioFormat? nestedConfiguration,
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
        bool isEnabled;
        // de-click用
        float m_env;                        // 0..1 出力ゲイン（エンベロープ）
        private float m_target;             // 0 or 1
        private int m_attackSamples;        // アタック長
        private int m_releaseSamples;       // リリース長
        private bool m_zeroCrossStop;       // trueでNoteOffはゼロクロス優先
        private float m_lastSample;         // 直前サンプル（ゼロクロス判定用）
        // スイッチ
        private bool m_useBandLimitedTriangle;
        
        public static GeneratorInstance Allocate(ControlContext context, float frequency)
        {
            return context.AllocateGenerator(new Processor(frequency), new Control());
        }

        public bool isFinite => false;
        public bool isRealtime => true;
        public DiscreteTime? length => null;

        GeneratorInstance.Setup m_Setup;

        Processor(float frequency,float attackMs = 1.0f, float releaseMs = 3.0f, bool zeroCrossStop = false)
        {
            m_Frequency = frequency;
            m_Phase = 0.0f;
            m_Setup = new GeneratorInstance.Setup();
            isEnabled = false;
            m_attackSamples  = Mathf.Max(1, Mathf.RoundToInt(attackMs  * 0.001f * m_Setup.sampleRate));
            m_releaseSamples = Mathf.Max(1, Mathf.RoundToInt(releaseMs * 0.001f * m_Setup.sampleRate));
            m_zeroCrossStop  = zeroCrossStop;
            m_env = 0f;
            m_target = 0f;
            m_lastSample = 0f;
            m_useBandLimitedTriangle = false;
        }
        // Called when the real-time side of the graph updates (e.g., new control data available).
        // Keep this method allocation-free and exception-free.
        public void Update(ProcessorInstance.UpdatedDataContext context, ProcessorInstance.Pipe pipe)
        {
            var enumerator = pipe.GetAvailableData(context);

			foreach (var element in enumerator)
			{
                if (element.TryGetData(out FrequencyData data))
                {
                    m_Frequency = data.Value;
                    isEnabled = data.IsActive;
                }
                else
            	    Debug.Log("DataAvailable: unknown data."); 
			}
        }

        // PolyBLEP（標準形）
        static float PolyBLEP(float t, float dt)
        {
            if (t < dt)
            {
                float x = t / dt;
                return x + x - x * x - 1f;
            }
            if (t > 1f - dt)
            {
                float x = (t - 1f) / dt;
                return x * x + x + x + 1f;
            }
            return 0f;
        }

        public GeneratorInstance.Result Process(in RealtimeContext ctx,
            ProcessorInstance.Pipe pipe,
            ChannelBuffer buffer, 
            GeneratorInstance.Arguments args)
        {
            int frames = buffer.frameCount;
            int channels = buffer.channelCount;
            float sr = m_Setup.sampleRate;

            m_target = isEnabled ? 1f : 0f;

            for (int frame = 0; frame < frames; frame++)
            {
                // ---- 1) エンベロープ ----
                if (m_target > m_env)      m_env = Mathf.Min(1f, m_env + 1f / m_attackSamples);
                else if (m_target < m_env) m_env = Mathf.Max(0f, m_env - 1f / m_releaseSamples);

                // ---- 2) 波形 ----
                float tri;

               // naive triangle
               float phase = m_Phase - Mathf.Floor(m_Phase);
               tri = 4f * Mathf.Abs(phase - 0.5f) - 1f;

                // ---- 3) エンベロープ適用 ----
                float vOut = tri * m_env;

                for (int ch = 0; ch < channels; ch++)
                    buffer[ch, frame] = vOut;

                // ---- 4) 位相進行 ----
                m_Phase += m_Frequency / sr;
                if (m_Phase >= 1f) m_Phase -= 1f;
            }
            return frames;
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
            public readonly bool IsActive;

            public FrequencyData(float value, bool isActive)
            {
                Value = value;
                IsActive = isActive;
            }
        }
    }
}
