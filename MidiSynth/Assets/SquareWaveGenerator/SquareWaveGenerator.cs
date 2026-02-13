using System;
using Unity.Burst;
using Unity.IntegerTime;
using UnityEngine;
using UnityEngine.Audio;

[CreateAssetMenu(fileName = "SquareWaveGenerator", menuName = "Audio/Generator/SquareWaveGenerator")]
public class SquareWaveGenerator : ScriptableObject, IAudioGenerator
{
    public float initialFrequency;
    public float initRatio;

    public bool isFinite => false;
    public bool isRealtime => true;
    public DiscreteTime? length => null;

    public GeneratorInstance CreateInstance(ControlContext context, AudioFormat? nestedFormat,
        ProcessorInstance.CreationParameters creationParameters)
    {
        return Processor.Allocate(context, initialFrequency, Mathf.Clamp01(initRatio));
    }

    [BurstCompile(CompileSynchronously = true)]
    internal struct Processor : GeneratorInstance.IRealtime
    {
        const float k_Tau = Mathf.PI * 2;

        float m_Frequency;
        private float m_DutyRatio;
        float m_Phase;
        private bool m_IsActive;
        // de-click用
        float m_env;                  // 0..1 出力ゲイン（エンベロープ）
        float m_target;               // 0 or 1
        int m_attackSamples;           // アタック長
        int m_releaseSamples;          // リリース長
        bool m_zeroCrossStop ;      // trueでNoteOffはゼロクロス優先
        float m_lastSample;           // 直前サンプル（ゼロクロス判定用）
        
        public static GeneratorInstance Allocate(ControlContext context, float frequency, float dutyRatio)
        {
            return context.AllocateGenerator(new Processor(frequency, dutyRatio), new Control());
        }

        public bool isFinite => false;
        public bool isRealtime => true;
        public DiscreteTime? length => null;

        GeneratorInstance.Setup m_Setup;

        Processor(float frequency, float dutyRatio,float attackMs = 1.0f, float releaseMs = 3.0f, bool zeroCrossStop = false)
        {
            m_Frequency = frequency;
            m_Phase = 0.0f;
            m_DutyRatio = dutyRatio;
            m_Setup = new GeneratorInstance.Setup();
            m_IsActive = false;
            m_attackSamples  = Mathf.Max(1, Mathf.RoundToInt(attackMs  * 0.001f * m_Setup.sampleRate));
            m_releaseSamples = Mathf.Max(1, Mathf.RoundToInt(releaseMs * 0.001f * m_Setup.sampleRate));
            m_zeroCrossStop  = zeroCrossStop;
            m_env = 0f;
            m_target = 0f;
            m_lastSample = 0f;
        }

        public void Update(ProcessorInstance.UpdatedDataContext context, ProcessorInstance.Pipe pipe)
        {
            var enumerator = pipe.GetAvailableData(context);

			foreach (var element in enumerator)
			{
                if (element.TryGetData(out WaveData data))
                {
                    m_Frequency = data.Freq;
                    m_DutyRatio = data.Ratio;
                    m_IsActive = data.IsActive;
                }
        	    else
            	    Debug.Log("DataAvailable: unknown data."); 
			}
        }

        public GeneratorInstance.Result Process(in RealtimeContext ctx,
            ProcessorInstance.Pipe pipe, ChannelBuffer buffer, GeneratorInstance.Arguments args)
        {
            int frames = buffer.frameCount;
            int channels = buffer.channelCount;
            float sr = m_Setup.sampleRate;

            // 外部フラグ→目標値に変換
            m_target = m_IsActive ? 1f : 0f;

            // Dutyは安全クランプ
            float d = Mathf.Clamp01(m_DutyRatio);
            d = Mathf.Clamp(d, 0.001f, 0.999f);

            for (int frame = 0; frame < frames; frame++)
            {
                // ===== 1) ゼロクロス優先のNoteOff（任意） =====
                // NoteOff要求(m_target=0) かつ env>0 のとき、
                // ゼロクロスを待ってからリリースを始めたい場合。
                if (m_zeroCrossStop && m_target <= 0f && m_env > 0f)
                {
                    // 現フレームの理想値（エンベロープなし）
                    float phase = m_Phase;
                    float vDry = (phase < d) ? 1f : -1f;

                    // 直前サンプルと符号が異なり、かつ小さい方が0ならゼロクロス
                    bool crossed = (m_lastSample <= 0f && vDry > 0f) || (m_lastSample >= 0f && vDry < 0f);
                    if (!crossed)
                    {
                        // まだゼロクロスしていない：envは維持して先に音を出す
                        float vOut = vDry * m_env;
                        for (int ch = 0; ch < channels; ch++) buffer[ch, frame] = vOut;

                        // フェーズ進行
                        m_Phase += m_Frequency / sr;
                        if (m_Phase >= 1f) m_Phase -= 1f;

                        m_lastSample = vDry;
                        continue; // 次フレームへ
                    }
                    // crossed==true の瞬間に以降は通常のリリース処理へ移行
                }

                // ===== 2) エンベロープ更新 =====
                // targetへ線形追従（1サンプルあたりの増分）
                float step = 0f;
                if (m_target > m_env)
                {
                    step = 1f / m_attackSamples;
                    m_env = Mathf.Min(1f, m_env + step);
                }
                else if (m_target < m_env)
                {
                    step = 1f / m_releaseSamples;
                    m_env = Mathf.Max(0f, m_env - step);
                }
                // ここで m_env は 0..1 の滑らかな値

                // ===== 3) 波形生成（矩形 ±1） =====
                float phaseNow = m_Phase;
                float v = (phaseNow < d) ? 1f : -1f;

                // ===== 4) エンベロープ適用 =====
                float vOutFinal = v * m_env;

                // 書き込み
                for (int ch = 0; ch < channels; ch++)
                {
                    buffer[ch, frame] = vOutFinal;
                }

                // 位相進行
                m_Phase += m_Frequency / sr;
                if (m_Phase >= 1f) m_Phase -= 1f;

                m_lastSample = v;
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
                if (message.Is<WaveData>())
                {
                    pipe.SendData(context, message.Get<WaveData>());

                    return ProcessorInstance.Response.Handled;
                }
                
                return ProcessorInstance.Response.Unhandled;
            }
        }

        internal readonly struct WaveData
        {
            public readonly float Freq;
            public readonly float Ratio;
            public readonly bool IsActive;

            public WaveData(float freq, float ratio, bool isActive)
            {
                Freq = freq;
                Ratio = ratio;
                IsActive = isActive;
            }
        }
    }
}
