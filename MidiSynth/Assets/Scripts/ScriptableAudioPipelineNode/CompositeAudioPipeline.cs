using Unity.IntegerTime;
using UnityEngine;
using UnityEngine.Audio;

namespace CustomAudioPipeline.Nodes
{
    [CreateAssetMenu(fileName = "CompositeAudioPipeline", menuName = "Audio/Pipeline/CompositeAudioPipeline")]
    public sealed class CompositeAudioPipeline : ScriptableObject, IAudioGenerator
    {
        [SerializeField] private BaseAudioNode[] nodes;

        public BaseAudioNode[] Nodes => nodes;

        public bool isFinite
        {
            get
            {
                var last = GetLastNode();
                return last != null && last.isFinite;
            }
        }

        public bool isRealtime => true;

        public DiscreteTime? length
        {
            get
            {
                var last = GetLastNode();
                return last?.length;
            }
        }

        /// <summary>最終出力ノード（配線後の末端）。</summary>
        public IAudioGenerator Output => GetLastNode();

        public GeneratorInstance CreateInstance(
            ControlContext context,
            AudioFormat? nestedConfiguration,
            ProcessorInstance.CreationParameters creationParameters)
        {
            var last = GetLastNode();
            if (last == null)
            {
                Debug.LogWarning("[CompositeAudioPipeline] nodes is empty -> silence");
                return default;
            }

            // ランタイムでも安全のため配線（Editor OnValidate だけに依存しない）
            WireUpstream();

            // 最終ノードの instance を返すだけ（Composite）
            return last.CreateInstance(context, nestedConfiguration, creationParameters);
        }

        private BaseAudioNode GetLastNode()
        {
            if (nodes == null || nodes.Length == 0)
            {
                return null;
            }

            return nodes[^1];
        }

        private void WireUpstream()
        {
            if (nodes == null || nodes.Length == 0)
            {
                return;
            }

            // null が混じっていても落ちないように「直前の有効ノード」を採用する
            BaseAudioNode prev = null;

            for (int i = 0; i < nodes.Length; i++)
            {
                var n = nodes[i];
                if (n == null)
                {
                    continue;
                }

                n.UpstreamObject = prev;
                prev = n;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            WireUpstream();
        }
#endif
    }
}
