using System;

namespace CustomAudioPipeline.Controller
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class AudioControllerMessageSupportAttribute : Attribute
    {
        public Type[] MessageTypes { get; }

        public AudioControllerMessageSupportAttribute(params Type[] messageTypes)
        {
            MessageTypes = messageTypes ?? Array.Empty<Type>();
        }
    }
}