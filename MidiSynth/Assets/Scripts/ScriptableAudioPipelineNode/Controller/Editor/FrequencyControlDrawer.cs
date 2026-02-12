using CustomAudioPipeline.Controller;
using UnityEditor;
using UnityEngine;

namespace CustomAudioPipeline.Controller.Editor
{

    [CustomPropertyDrawer(typeof(FrequencyControlAttribute))]
    public sealed class FrequencyControlDrawer : PropertyDrawer
    {
        private static readonly string[] NoteNames =
        {
            "C", "C#", "D", "D#", "E", "F",
            "F#", "G", "G#", "A", "A#", "B"
        };

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return (EditorGUIUtility.singleLineHeight * 2.0f) + EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.Float)
            {
                EditorGUI.LabelField(position, label.text, "FrequencyControl can be used only with float.");
                return;
            }

            var a = (FrequencyControlAttribute)attribute;

            Rect line1 = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            Rect line2 = new Rect(
                position.x,
                position.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing,
                position.width,
                EditorGUIUtility.singleLineHeight
            );

            EditorGUI.BeginProperty(position, label, property);

            // ===== Line1: Frequency (Hz) =====
            DrawHzLine(line1, property, label, a.MinHz, a.MaxHz);

            // ===== Line2: MIDI =====
            float hz = Mathf.Clamp(property.floatValue, a.MinHz, a.MaxHz);
            DrawMidiLine(line2, property, hz, a.MinHz, a.MaxHz, a.MinMidi, a.MaxMidi);

            EditorGUI.EndProperty();
        }

        private static void DrawHzLine(Rect rect, SerializedProperty hzProp, GUIContent label, float minHz, float maxHz)
        {
            rect = EditorGUI.PrefixLabel(rect, label);

            const float fieldWidth = 72.0f;
            const float gap = 6.0f;

            Rect sliderRect = new Rect(rect.x, rect.y, rect.width - fieldWidth - gap, rect.height);
            Rect fieldRect = new Rect(sliderRect.xMax + gap, rect.y, fieldWidth, rect.height);

            float hz = Mathf.Clamp(hzProp.floatValue, minHz, maxHz);

            // Slider only
            EditorGUI.BeginChangeCheck();
            float sliderHz = GUI.HorizontalSlider(sliderRect, hz, minHz, maxHz);
            if (EditorGUI.EndChangeCheck())
            {
                hz = sliderHz;
            }

            // Field
            EditorGUI.BeginChangeCheck();
            float fieldHz = EditorGUI.FloatField(fieldRect, hz);
            if (EditorGUI.EndChangeCheck())
            {
                hz = fieldHz;
            }

            hzProp.floatValue = Mathf.Clamp(hz, minHz, maxHz);
        }

        private static void DrawMidiLine(Rect rect, SerializedProperty hzProp, float currentHz, float minHz,
            float maxHz, int minMidi, int maxMidi)
        {
            // Left label
            Rect labelRect = new Rect(rect.x, rect.y, EditorGUIUtility.labelWidth, rect.height);
            EditorGUI.LabelField(labelRect, "MIDI");

            Rect contentRect = new Rect(rect.x + EditorGUIUtility.labelWidth, rect.y,
                rect.width - EditorGUIUtility.labelWidth, rect.height);

            int midi = Mathf.Clamp(FrequencyToMidi(currentHz), minMidi, maxMidi);

            const float intFieldWidth = 48.0f;
            const float infoWidth = 120.0f;
            const float gap = 6.0f;

            Rect sliderRect = new Rect(contentRect.x, contentRect.y,
                contentRect.width - intFieldWidth - infoWidth - gap * 2.0f, contentRect.height);
            Rect intRect = new Rect(sliderRect.xMax + gap, contentRect.y, intFieldWidth, contentRect.height);
            Rect infoRect = new Rect(intRect.xMax + gap, contentRect.y, infoWidth, contentRect.height);

            int newMidi = midi;

            // Int slider only (GUI.HorizontalSlider is float)
            EditorGUI.BeginChangeCheck();
            float midiSlider = GUI.HorizontalSlider(sliderRect, midi, minMidi, maxMidi);
            if (EditorGUI.EndChangeCheck())
            {
                newMidi = Mathf.RoundToInt(midiSlider);
            }

            // Int field
            EditorGUI.BeginChangeCheck();
            int midiField = EditorGUI.IntField(intRect, newMidi);
            if (EditorGUI.EndChangeCheck())
            {
                newMidi = midiField;
            }

            newMidi = Mathf.Clamp(newMidi, minMidi, maxMidi);

            // MIDI -> Hz (snap) when changed
            if (newMidi != midi)
            {
                float snappedHz = MidiToFrequency(newMidi);
                hzProp.floatValue = Mathf.Clamp(snappedHz, minHz, maxHz);
            }

            int showMidi = Mathf.Clamp(FrequencyToMidi(Mathf.Clamp(hzProp.floatValue, minHz, maxHz)), minMidi, maxMidi);
            string noteName = MidiToNoteName(showMidi);
            EditorGUI.LabelField(infoRect, $"{noteName} ({Mathf.RoundToInt(hzProp.floatValue)}Hz)");
        }

        private static float MidiToFrequency(int midiNote)
        {
            return 440.0f * Mathf.Pow(2.0f, (midiNote - 69) / 12.0f);
        }

        private static int FrequencyToMidi(float hz)
        {
            hz = Mathf.Max(0.0001f, hz);
            float note = 69.0f + 12.0f * Mathf.Log(hz / 440.0f, 2.0f);
            return Mathf.RoundToInt(note);
        }

        private static string MidiToNoteName(int midiNote)
        {
            int noteIndex = Mathf.Abs(midiNote) % 12;
            int octave = (midiNote / 12) - 1;
            return $"{NoteNames[noteIndex]}{octave}";
        }
    }

}