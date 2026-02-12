using System;
using UnityEngine;

namespace CustomAudioPipeline.Nodes
{
    [CreateAssetMenu(fileName = "AudioGraphDefinition", menuName = "Audio/Pipeline/AudioGraphDefinition")]
    public sealed class AudioGraphDefinition : ScriptableObject
    {
        [Serializable]
        public sealed class Slot
        {
            [SerializeField] private string guid;
            [SerializeField] private string label;
            [SerializeField] private BaseAudioNode node;

            // 将来の分岐/合流に備えて配列にしておく（今は0 or 1のみ許可）
            [SerializeField] private string[] inputGuids;

            public string Guid => guid;
            public string Label => label;
            public BaseAudioNode Node => node;
            public string[] InputGuids => inputGuids;

#if UNITY_EDITOR
            public void EnsureGuid()
            {
                if (!string.IsNullOrEmpty(guid))
                {
                    return;
                }

                guid = System.Guid.NewGuid().ToString("N");
            }
#endif
        }

        [SerializeField] private Slot[] slots;
        [SerializeField] private string outputGuid;

        public Slot[] Slots => slots;
        public string OutputGuid => outputGuid;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (slots == null)
            {
                outputGuid = string.Empty;
                return;
            }

            foreach (var s in slots)
            {
                if (s == null) continue;
                s.EnsureGuid();
            }

            // 直列モード：常に「最後の有効スロット」を Output にする
            string last = string.Empty;

            for (int i = slots.Length - 1; i >= 0; i--)
            {
                var s = slots[i];
                if (s == null || s.Node == null) continue;
                if (string.IsNullOrEmpty(s.Guid)) continue;

                last = s.Guid;
                break;
            }

            outputGuid = last;
        }
#endif
    }
}
