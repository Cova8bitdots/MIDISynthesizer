using System.Collections.Generic;
using Unity.IntegerTime;
using UnityEngine;
using UnityEngine.Audio;

namespace CustomAudioPipeline.Nodes
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class AudioGraphHost : MonoBehaviour, IAudioGenerator
    {
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioGraphDefinition definition;

        private readonly Dictionary<string, AudioGraphDefinition.Slot> _slotByGuid = new Dictionary<string, AudioGraphDefinition.Slot>(64);
        private readonly Dictionary<string, GeneratorInstance> _instanceByGuid = new Dictionary<string, GeneratorInstance>(64);

        public bool isFinite => false;
        public bool isRealtime => true;
        public DiscreteTime? length => null;

        public AudioGraphDefinition Definition => definition;

        private void Reset()
        {
            TryGetComponent(out audioSource);
        }

        private void Awake()
        {
            if (audioSource == null)
            {
                Debug.LogError("[AudioGraphHost] AudioSource is null.", this);
                return;
            }

            // AudioSource には常に Host を刺す（単体/Composite判定問題を消す）
            audioSource.generator = this;
        }
        private void OnEnable()
        {
            if (audioSource == null)
            {
                Debug.LogError("[AudioGraphHost] AudioSource is null.", this);
                return;
            }

            // AudioSource には常に Host を刺す
            audioSource.generator = this;

            // playOnAwake を自前で保証（generator 差し替え順のズレを潰す）
            if (audioSource.playOnAwake && !audioSource.isPlaying)
            {
                audioSource.Play();
            }
        }


        public bool TryGetInstance(string slotGuid, out GeneratorInstance instance)
            => _instanceByGuid.TryGetValue(slotGuid, out instance);

        public GeneratorInstance CreateInstance(
            ControlContext context,
            AudioFormat? nestedConfiguration,
            ProcessorInstance.CreationParameters creationParameters)
        {
            _slotByGuid.Clear();
            _instanceByGuid.Clear();

            if (definition == null || definition.Slots == null || definition.Slots.Length == 0)
            {
                Debug.LogWarning("[AudioGraphHost] definition is empty.", this);
                return default;
            }

            foreach (var s in definition.Slots)
            {
                if (s == null || string.IsNullOrEmpty(s.Guid))
                {
                    continue;
                }

                _slotByGuid[s.Guid] = s;
            }

            if (string.IsNullOrEmpty(definition.OutputGuid) || !_slotByGuid.ContainsKey(definition.OutputGuid))
            {
                Debug.LogError("[AudioGraphHost] OutputGuid is invalid.", this);
                return default;
            }

            var defaultNested = BaseAudioNode.GetDefaultNestedFormat();

            // root(出力)だけは nestedConfiguration をそのまま（null可）
            return BuildInstance(
                definition.OutputGuid,
                isRoot: true,
                context: context,
                nestedConfiguration: nestedConfiguration,
                defaultNested: defaultNested,
                creationParameters: creationParameters
            );
        }

        private GeneratorInstance BuildInstance(
            string slotGuid,
            bool isRoot,
            ControlContext context,
            AudioFormat? nestedConfiguration,
            AudioFormat defaultNested,
            ProcessorInstance.CreationParameters creationParameters)
        {
            if (_instanceByGuid.TryGetValue(slotGuid, out var cached))
            {
                return cached;
            }

            if (!_slotByGuid.TryGetValue(slotGuid, out var slot) || slot.Node == null)
            {
                Debug.LogError($"[AudioGraphHost] Slot not found or node null. guid={slotGuid}", this);
                return default;
            }

            // root以外は必ず nested として生成（null を渡さない）
            AudioFormat? fmt = isRoot ? nestedConfiguration : (nestedConfiguration ?? defaultNested);

            var inputs = slot.InputGuids;

            GeneratorInstance instance;

            if (inputs == null || inputs.Length == 0)
            {
                // 起点
                instance = slot.Node.CreateInstanceWithUpstream(
                    context,
                    fmt,
                    creationParameters,
                    false,
                    default
                );
            }
            else if (inputs.Length == 1)
            {
                // 直列（今はここまで）
                var up = BuildInstance(inputs[0], isRoot: false, context, nestedConfiguration, defaultNested, creationParameters);

                instance = slot.Node.CreateInstanceWithUpstream(
                    context,
                    fmt,
                    creationParameters,
                    true,
                    up
                );
            }
            else
            {
                // ★将来の合流(Mixer)対応のための拡張ポイント
                Debug.LogError($"[AudioGraphHost] Multi-input is not supported yet. slot={slot.Label} inputs={inputs.Length}", this);
                return default;
            }

            _instanceByGuid[slotGuid] = instance;
            return instance;
        }
    }
}
