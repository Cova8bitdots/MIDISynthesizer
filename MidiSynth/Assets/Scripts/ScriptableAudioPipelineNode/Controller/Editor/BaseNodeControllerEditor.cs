using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CustomAudioPipeline.Controller.Editor
{
    [CustomEditor(typeof(BaseNodeController), true)]
    public sealed class BaseNodeControllerEditor : UnityEditor.Editor
    {
        private SerializedProperty _audioSourceProp;
        private SerializedProperty _audioGraphHostProp;
        private SerializedProperty _slotGuidProp;

        private bool _onlyCompatible = true;

        private List<SlotView> _slots = new List<SlotView>();
        private int _selectedIndex;
        private bool _needsRebuild = true;

        private void OnEnable()
        {
            _audioSourceProp = serializedObject.FindProperty("audioSource");
            _audioGraphHostProp = serializedObject.FindProperty("audioGraphHost");
            _slotGuidProp = serializedObject.FindProperty("slotGuid");
            _needsRebuild = true;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_audioSourceProp);

            EditorGUILayout.Space(6);

            using (var cc = new EditorGUI.ChangeCheckScope())
            {
                EditorGUILayout.PropertyField(_audioGraphHostProp);

                if (cc.changed)
                {
                    _needsRebuild = true;
                }
            }

            var host = _audioGraphHostProp.objectReferenceValue;
            if (host == null)
            {
                EditorGUILayout.HelpBox("AudioGraphHost が未設定のため、slotGuid は使用されません（AudioSource.generatorInstance へ送信）。", MessageType.Info);
                EditorGUILayout.PropertyField(_slotGuidProp, new GUIContent("Slot Guid"));
                serializedObject.ApplyModifiedProperties();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh Slots", GUILayout.Width(120)))
                {
                    _needsRebuild = true;
                }

                if (GUILayout.Button("Ping Host", GUILayout.Width(90)))
                {
                    EditorGUIUtility.PingObject(host);
                    Selection.activeObject = host;
                }
            }

            _onlyCompatible = EditorGUILayout.ToggleLeft("Only compatible nodes", _onlyCompatible);

            if (_needsRebuild)
            {
                Rebuild(host);
                _needsRebuild = false;
            }

            if (_slots.Count == 0)
            {
                EditorGUILayout.HelpBox("Slot 一覧を取得できませんでした。Definition の構造に合わせて収集ロジックを調整できます。", MessageType.Warning);
                EditorGUILayout.PropertyField(_slotGuidProp, new GUIContent("Slot Guid"));
                serializedObject.ApplyModifiedProperties();
                return;
            }

            DrawDropdown();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Slot Guid", _slotGuidProp.stringValue);
            }

            // ===== ここが重要：派生先の[SerializeField]等を含め、残りを全部描画 =====
            EditorGUILayout.Space(8);
            DrawPropertiesExcluding(
                serializedObject,
                "m_Script",
                _audioSourceProp.name,
                _audioGraphHostProp.name,
                _slotGuidProp.name
            );

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawDropdown()
        {
            var currentGuid = _slotGuidProp.stringValue ?? string.Empty;

            var filtered = ApplyFilter(_slots, _onlyCompatible);
            if (filtered.Count == 0)
            {
                EditorGUILayout.HelpBox("フィルタ結果が 0 件です。Controller 側の送信 Message 型が未宣言の可能性があります。", MessageType.Info);
                return;
            }

            var labels = filtered.Select(x => x.Label).ToArray();
            var guids = filtered.Select(x => x.Guid).ToArray();

            _selectedIndex = Array.IndexOf(guids, currentGuid);
            if (_selectedIndex < 0)
            {
                _selectedIndex = 0;
            }

            using (var cc = new EditorGUI.ChangeCheckScope())
            {
                _selectedIndex = EditorGUILayout.Popup("Slot", _selectedIndex, labels);

                if (cc.changed && _selectedIndex >= 0 && _selectedIndex < guids.Length)
                {
                    _slotGuidProp.stringValue = guids[_selectedIndex];
                }
            }

            var selected = filtered[Mathf.Clamp(_selectedIndex, 0, filtered.Count - 1)];

            if (selected.Compatibility == Compatibility.Unknown && !string.IsNullOrEmpty(selected.Guid))
            {
                EditorGUILayout.HelpBox(
                    "互換性を判定できませんでした（Node が取得できない、または MessageSupport 属性/設定が無い可能性）。\n" +
                    "Node 側に MessageSupport のメタ情報がある場合は、Slot の Node を参照できているか確認してください。",
                    MessageType.Warning
                );
            }

            if (selected.Compatibility == Compatibility.Incompatible && !string.IsNullOrEmpty(selected.Guid))
            {
                EditorGUILayout.HelpBox("この Slot は Controller の Message 要件と互換性がありません。", MessageType.Warning);
            }
        }

        private void Rebuild(UnityEngine.Object host)
        {
            var controller = (BaseNodeController)target;
            var requiredMessageTypes = GetControllerSupportedMessageTypes(controller);

            var raw = SlotReflectionUtility.CollectSlots(host).ToList();

            var list = new List<SlotView>
            {
                new SlotView(string.Empty, "(none) - fallback to AudioSource.generatorInstance", null, Compatibility.Compatible)
            };

            int slotIndex = 0;
            foreach (var r in raw)
            {
                if (string.IsNullOrEmpty(r.Guid))
                {
                    continue;
                }

                var displayName = !string.IsNullOrEmpty(r.Label) ? r.Label : GetFallbackNodeDisplayName(r.Node);
                var nodeTypeName = r.Node != null ? r.Node.GetType().Name : "UnknownNode";
                var shortGuid = r.Guid.Length >= 8 ? r.Guid.Substring(0, 8) : r.Guid;

                var compatibility = EvaluateCompatibility(r.Node, requiredMessageTypes);

                var label = $"{displayName} ({nodeTypeName}) [{shortGuid}] index:{slotIndex}";
                list.Add(new SlotView(r.Guid, label, r.Node, compatibility));

                slotIndex++;
            }

            _slots = list
                .GroupBy(x => x.Guid)
                .Select(g => g.First())
                .OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string GetFallbackNodeDisplayName(UnityEngine.Object node)
        {
            if (node == null)
            {
                return "(empty slot)";
            }

            if (!string.IsNullOrEmpty(node.name))
            {
                return node.name;
            }

            return node.GetType().Name;
        }

        private static List<SlotView> ApplyFilter(List<SlotView> slots, bool onlyCompatible)
        {
            if (!onlyCompatible)
            {
                return slots.ToList();
            }

            return slots
                .Where(x => x.Compatibility != Compatibility.Incompatible || string.IsNullOrEmpty(x.Guid))
                .ToList();
        }

        private static Type[] GetControllerSupportedMessageTypes(BaseNodeController controller)
        {
            // 1) Method-based (recommended if you already have it)
            var method = controller.GetType().GetMethod(
                "GetSupportedMessageTypes",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
            );

            if (method != null)
            {
                try
                {
                    var result = method.Invoke(controller, null) as Type[];
                    if (result != null && result.Length > 0)
                    {
                        return result;
                    }
                }
                catch
                {
                    // ignore
                }
            }

            // 2) Attribute-based (optional)
            var attr = GetCustomAttributeByName(controller.GetType(), "CustomAudioPipeline.Controller.AudioControllerMessageSupportAttribute");
            if (attr == null)
            {
                return Array.Empty<Type>();
            }

            var messageTypesProp = attr.GetType().GetProperty("MessageTypes", BindingFlags.Instance | BindingFlags.Public);
            if (messageTypesProp == null)
            {
                return Array.Empty<Type>();
            }

            return messageTypesProp.GetValue(attr) as Type[] ?? Array.Empty<Type>();
        }

        private static Attribute GetCustomAttributeByName(Type targetType, string attributeFullName)
        {
            if (targetType == null || string.IsNullOrEmpty(attributeFullName))
            {
                return null;
            }

            foreach (var a in targetType.GetCustomAttributes(true))
            {
                if (a is Attribute attr && attr.GetType().FullName == attributeFullName)
                {
                    return attr;
                }
            }

            return null;
        }

        private static Compatibility EvaluateCompatibility(UnityEngine.Object node, Type[] requiredMessageTypes)
        {
            if (requiredMessageTypes == null || requiredMessageTypes.Length == 0)
            {
                // Controller が message を宣言していないなら、フィルタ不能なので全部表示（互換扱い）
                return Compatibility.Compatible;
            }

            if (node == null)
            {
                return Compatibility.Unknown;
            }

            var nodeType = node.GetType();
            var attr = nodeType.GetCustomAttribute<CustomAudioPipeline.Nodes.AudioNodeMessageSupportAttribute>(true);
            if (attr == null || attr.MessageTypes == null || attr.MessageTypes.Length == 0)
            {
                return Compatibility.Unknown;
            }

            var supported = new HashSet<Type>(attr.MessageTypes.Where(x => x != null));
            return requiredMessageTypes.All(supported.Contains)
                ? Compatibility.Compatible
                : Compatibility.Incompatible;
        }

        private enum Compatibility
        {
            Compatible,
            Incompatible,
            Unknown
        }

        private readonly struct SlotView
        {
            public readonly string Guid;
            public readonly string Label;
            public readonly UnityEngine.Object Node;
            public readonly Compatibility Compatibility;

            public SlotView(string guid, string label, UnityEngine.Object node, Compatibility compatibility)
            {
                Guid = guid ?? string.Empty;
                Label = label ?? string.Empty;
                Node = node;
                Compatibility = compatibility;
            }
        }

        private readonly struct SlotRaw
        {
            public readonly string Guid;
            public readonly string Label;
            public readonly UnityEngine.Object Node;

            public SlotRaw(string guid, string label, UnityEngine.Object node)
            {
                Guid = guid ?? string.Empty;
                Label = label ?? string.Empty;
                Node = node;
            }
        }

        private static class SlotReflectionUtility
        {
            private static readonly string[] DefinitionMemberCandidates =
            {
                "AudioGraphDefinition",
                "audioGraphDefinition",
                "Definition",
                "definition",
                "GraphDefinition",
                "graphDefinition",
            };

            private static readonly string[] SlotListMemberCandidates =
            {
                "Slots",
                "slots",
            };

            public static IEnumerable<SlotRaw> CollectSlots(object host)
            {
                if (host == null)
                {
                    yield break;
                }

                var definition = GetFirstMemberValue(host, DefinitionMemberCandidates);
                var slotContainer = GetFirstMemberValue(definition ?? host, SlotListMemberCandidates) ?? definition ?? host;

                foreach (var item in Enumerate(slotContainer))
                {
                    if (item == null)
                    {
                        continue;
                    }

                    // Primary: direct fields on slot item
                    var guid = GetFirstStringMemberValue(item, new[] { "Guid", "guid" });
                    var label = GetFirstStringMemberValue(item, new[] { "Label", "label" });
                    var node = GetFirstUnityObjectMemberValue(item, new[] { "Node", "node" });

                    if (!string.IsNullOrEmpty(guid))
                    {
                        yield return new SlotRaw(guid, label, node);
                        continue;
                    }

                    // Fallback: wrapper objects
                    foreach (var inner in EnumerateMembers(item))
                    {
                        var ig = GetFirstStringMemberValue(inner, new[] { "Guid", "guid" });
                        if (string.IsNullOrEmpty(ig))
                        {
                            continue;
                        }

                        var ilabel = GetFirstStringMemberValue(inner, new[] { "Label", "label" });
                        var inode = GetFirstUnityObjectMemberValue(inner, new[] { "Node", "node" });
                        yield return new SlotRaw(ig, ilabel, inode);
                        break;
                    }
                }
            }

            private static IEnumerable<object> Enumerate(object container)
            {
                if (container == null)
                {
                    yield break;
                }

                if (container is IEnumerable e && container is not string)
                {
                    foreach (var it in e)
                    {
                        yield return it;
                    }
                    yield break;
                }

                foreach (var it in GetFirstEnumerableMember(container))
                {
                    yield return it;
                }
            }

            private static IEnumerable<object> GetFirstEnumerableMember(object obj)
            {
                if (obj == null)
                {
                    yield break;
                }

                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                foreach (var name in SlotListMemberCandidates)
                {
                    var v = GetMemberValue(obj, name, flags);
                    if (v is IEnumerable e && v is not string)
                    {
                        foreach (var it in e)
                        {
                            yield return it;
                        }
                        yield break;
                    }
                }
            }

            private static IEnumerable<object> EnumerateMembers(object obj)
            {
                if (obj == null)
                {
                    yield break;
                }

                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var members = obj.GetType().GetMembers(flags).Where(m => m is FieldInfo or PropertyInfo);

                foreach (var m in members)
                {
                    var v = GetMemberValue(obj, m);
                    if (v == null || v is string)
                    {
                        continue;
                    }

                    yield return v;
                }
            }

            private static object GetFirstMemberValue(object obj, string[] names)
            {
                if (obj == null)
                {
                    return null;
                }

                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                foreach (var name in names)
                {
                    var v = GetMemberValue(obj, name, flags);
                    if (v != null)
                    {
                        return v;
                    }
                }

                return null;
            }

            private static string GetFirstStringMemberValue(object obj, string[] names)
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                foreach (var name in names)
                {
                    var v = GetMemberValue(obj, name, flags);

                    if (v is string s && !string.IsNullOrEmpty(s))
                    {
                        return s;
                    }

                    if (v is Guid g && g != Guid.Empty)
                    {
                        return g.ToString();
                    }
                }

                return string.Empty;
            }

            private static UnityEngine.Object GetFirstUnityObjectMemberValue(object obj, string[] names)
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                foreach (var name in names)
                {
                    var v = GetMemberValue(obj, name, flags);
                    if (v is UnityEngine.Object uo)
                    {
                        return uo;
                    }
                }

                return null;
            }

            private static object GetMemberValue(object obj, string memberName, BindingFlags flags)
            {
                if (obj == null || string.IsNullOrEmpty(memberName))
                {
                    return null;
                }

                var t = obj.GetType();

                var f = t.GetField(memberName, flags);
                if (f != null)
                {
                    return f.GetValue(obj);
                }

                var p = t.GetProperty(memberName, flags);
                if (p != null && p.GetIndexParameters().Length == 0)
                {
                    try
                    {
                        return p.GetValue(obj);
                    }
                    catch
                    {
                        return null;
                    }
                }

                return null;
            }

            private static object GetMemberValue(object obj, MemberInfo member)
            {
                if (obj == null || member == null)
                {
                    return null;
                }

                try
                {
                    if (member is FieldInfo f)
                    {
                        return f.GetValue(obj);
                    }

                    if (member is PropertyInfo p && p.GetIndexParameters().Length == 0)
                    {
                        return p.GetValue(obj);
                    }
                }
                catch
                {
                    return null;
                }

                return null;
            }
        }
    }
}
