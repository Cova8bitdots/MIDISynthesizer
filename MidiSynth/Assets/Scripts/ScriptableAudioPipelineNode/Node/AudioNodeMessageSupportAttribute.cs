using System;
namespace CustomAudioPipeline.Nodes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class AudioNodeMessageSupportAttribute : Attribute
    {
        public Type[] MessageTypes { get; }

        public AudioNodeMessageSupportAttribute(params Type[] messageTypes)
        {
            MessageTypes = messageTypes ?? Array.Empty<Type>();
        }
    }
}