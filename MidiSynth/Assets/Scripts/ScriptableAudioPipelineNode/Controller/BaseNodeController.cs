using System;
using UnityEngine;
using UnityEngine.Audio;
using CustomAudioPipeline.Nodes;

namespace CustomAudioPipeline.Controller
{
    public abstract class BaseNodeController : MonoBehaviour
    {
        [SerializeField] protected AudioSource audioSource;

        [Header("Audio Graph (Optional)")]
        [Tooltip("If set, messages will be sent to the node instance mapped by Slot Guid on this AudioGraphHost.")]
        [SerializeField] private AudioGraphHost audioGraphHost;

        [Tooltip("Slot Guid to target within AudioGraphHost. If empty or not found, falls back to AudioSource.generatorInstance.")]
        [SerializeField] private string slotGuid;

        protected virtual void Reset()
        {
            TryGetComponent(out audioSource);
            TryGetComponent(out audioGraphHost);

            if (audioGraphHost == null)
            {
                audioGraphHost = GetComponentInParent<AudioGraphHost>();
            }
        }

        protected bool TryGetTargetInstance(out ProcessorInstance instance)
        {
            instance = default;

            // 1) Prefer graph slot instance (if available)
            if (audioGraphHost != null && !string.IsNullOrEmpty(slotGuid))
            {
                if (audioGraphHost.TryGetInstance(slotGuid, out var genInstance))
                {
                    // GeneratorInstance can be converted to ProcessorInstance.
                    var pi = (ProcessorInstance)genInstance;
                    if (ControlContext.builtIn.Exists(pi))
                    {
                        instance = pi;
                        return true;
                    }
                }
            }

            // 2) Fallback: target the root generator instance on the AudioSource
            if (audioSource == null)
            {
                return false;
            }

            instance = audioSource.generatorInstance; // ← ProcessorInstance が返る
            return ControlContext.builtIn.Exists(instance);
        }

        protected bool TrySend<T>(in T message) where T : unmanaged
        {
            if (!TryGetTargetInstance(out var instance))
            {
                return false;
            }

            var msg = message;
            ControlContext.builtIn.SendMessage(instance, ref msg);
            return true;
        }

        protected void SetSlotGuid(string guid)
        {
            slotGuid = guid;
        }
    }
}
