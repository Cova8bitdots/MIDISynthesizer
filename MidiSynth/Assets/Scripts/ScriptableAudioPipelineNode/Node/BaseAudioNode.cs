using Unity.IntegerTime;
using UnityEngine;
using UnityEngine.Audio;

namespace CustomAudioPipeline.Nodes
{
    public abstract class BaseAudioNode : ScriptableObject, IAudioGenerator
    {
        [Header("Upstream (Optional)")]
        [SerializeField] private Object upstream;

        /// <summary>Inspector 上で upstream を差し替える。</summary>
        public Object UpstreamObject
        {
            get => upstream;
            set => upstream = value;
        }

        /// <summary>upstream が IAudioGenerator として有効か。</summary>
        protected bool HasUpstream => upstream is IAudioGenerator;

        /// <summary>upstream の定義（無効なら null）。</summary>
        protected IAudioGenerator Upstream => upstream as IAudioGenerator;

        public virtual bool isFinite => Upstream?.isFinite ?? false;
        public virtual bool isRealtime => true;
        public virtual DiscreteTime? length => Upstream?.length;

        public GeneratorInstance CreateInstance(
            ControlContext context,
            AudioFormat? nestedConfiguration,
            ProcessorInstance.CreationParameters creationParameters)
        {
            GeneratorInstance upstreamInstance = default;
            bool hasUpstreamInstance = false;

            var upstreamDef = Upstream;
            if (upstreamDef != null)
            {
                // ★重要: 子(upstream)は常に "nested generator" として作る
                var childNestedFormat = nestedConfiguration ?? GetDefaultNestedFormat();
                upstreamInstance = upstreamDef.CreateInstance(context, childNestedFormat, creationParameters);
                hasUpstreamInstance = true;
            }

            return CreateNodeInstance(context, nestedConfiguration, creationParameters, hasUpstreamInstance, upstreamInstance);
        }

        /// <summary>
        /// Host/Runner が upstream instance を渡して生成するための入口（再帰生成しない）。
        /// </summary>
        internal GeneratorInstance CreateInstanceWithUpstream(
            ControlContext context,
            AudioFormat? nestedConfiguration,
            ProcessorInstance.CreationParameters creationParameters,
            bool hasUpstreamInstance,
            GeneratorInstance upstreamInstance)
        {
            return CreateNodeInstance(context, nestedConfiguration, creationParameters, hasUpstreamInstance, upstreamInstance);
        }

        internal static AudioFormat GetDefaultNestedFormat()
        {
            var cfg = AudioSettings.GetConfiguration();
            return new AudioFormat(cfg.speakerMode, cfg.sampleRate, cfg.dspBufferSize);
        }

        protected abstract GeneratorInstance CreateNodeInstance(
            ControlContext context,
            AudioFormat? nestedConfiguration,
            ProcessorInstance.CreationParameters creationParameters,
            bool hasUpstreamInstance,
            GeneratorInstance upstreamInstance);

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            if (upstream != null && upstream is not IAudioGenerator)
            {
                Debug.LogWarning($"[{name}] UpstreamObject は IAudioGenerator を実装していません: {upstream.name}", this);
            }
        }
#endif
    }
}
