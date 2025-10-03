using System;
using Unity.Burst;
using Unity.IntegerTime;
using UnityEngine;
using UnityEngine.Audio;

[CreateAssetMenu(fileName = "TriangleWaveGenerator", menuName = "Audio/Generator/TriangleWaveGenerator")]
public class TriangleWaveGenerator : ScriptableObject, IGeneratorDefinition
{
    public float initialFrequency;

    public bool isFinite => false;
    public bool isRealtime => true;
    public DiscreteTime? length => null;

    public Generator CreateRuntime(ControlContext context, DSPConfiguration? nestedConfiguration,
        ControlContext.ProcessorCreationParameters creationParameters)
    {
        return Processor.Allocate(context, initialFrequency);
    }

    [BurstCompile(CompileSynchronously = true)]
    internal struct Processor : Generator.IProcessor
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
        
        public static Generator Allocate(ControlContext context, float frequency)
        {
            return context.AllocateGenerator(new Processor(frequency), new Control());
        }

        public bool isFinite => false;
        public bool isRealtime => true;
        public DiscreteTime? length => null;

        Generator.Setup m_Setup;

        Processor(float frequency,float attackMs = 1.0f, float releaseMs = 3.0f, bool zeroCrossStop = false)
        {
            m_Frequency = frequency;
            m_Phase = 0.0f;
            m_Setup = new Generator.Setup();
            isEnabled = false;
            m_attackSamples  = Mathf.Max(1, Mathf.RoundToInt(attackMs  * 0.001f * m_Setup.sampleRate));
            m_releaseSamples = Mathf.Max(1, Mathf.RoundToInt(releaseMs * 0.001f * m_Setup.sampleRate));
            m_zeroCrossStop  = zeroCrossStop;
            m_env = 0f;
            m_target = 0f;
            m_lastSample = 0f;
            m_useBandLimitedTriangle = false;
        }

        public void Update(UnityEngine.Audio.Processor.UpdatedDataContext context, UnityEngine.Audio.Processor.Pipe pipe)
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

        public Generator.Result Process(in ProcessingContext ctx,
            UnityEngine.Audio.Processor.Pipe pipe, ChannelBuffer buffer, Generator.Arguments args)
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

                if (!m_useBandLimitedTriangle)
                {
                    // naive triangle
                    float phase = m_Phase - Mathf.Floor(m_Phase);
                    tri = 4f * Mathf.Abs(phase - 0.5f) - 1f;
                }
                else
                {
                    // PolyBLEP saw -> triangle 変換
                    float t  = m_Phase - Mathf.Floor(m_Phase);   // [0,1)
                    float dt = Mathf.Min(0.5f, m_Frequency / sr);

                    // band-limited sawtooth (-1..+1)
                    float saw = 2f * t - 1f;
                    saw -= PolyBLEP(t, dt);

                    // saw -> triangle（対称化）
                    // tri ≈ 1 - 2*|saw| で -1..+1 の三角に写像（穏やかな角になり高域がマイルド）
                    tri = 1f - 2f * Mathf.Abs(saw);
                }

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

        struct Control : Generator.IControl<Processor>
        {
            public void Configure(ControlContext context, ref Processor generator, in DSPConfiguration config, out Generator.Setup setup, ref Generator.Properties p)
            {
                generator.m_Setup = new Generator.Setup(AudioSpeakerMode.Mono, config.sampleRate);
                setup = generator.m_Setup;
            }

            public void Dispose(ControlContext context, ref Processor processor) { }

            public void Update(ControlContext context, UnityEngine.Audio.Processor.Pipe pipe) { }

            public UnityEngine.Audio.Processor.MessageStatus OnMessage(ControlContext context, UnityEngine.Audio.Processor.Pipe pipe, UnityEngine.Audio.Processor.Message message)
            {
                return UnityEngine.Audio.Processor.MessageStatus.Unhandled;
            }
        }

        internal struct FrequencyData
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
