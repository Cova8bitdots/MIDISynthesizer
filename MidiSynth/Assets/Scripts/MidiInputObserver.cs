using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;

using Minis; // jp.keijiro.minis

/// <summary>
/// Observes MIDI note On/Off in two ways:
/// 1) Input System Action Map ("MIDI" map with 88 keys)
/// 2) Minis.MidiDevice callback events (onWillNoteOn / onWillNoteOff)
///
/// Exposes per-key Down/Up Observables + AnyKey streams.
/// </summary>
public sealed class MidiInputObserver : IDisposable
{
    public readonly struct MidiKeyEvent
    {
        public string Name { get; }            // e.g. "C4" (computed when necessary)
        public int NoteNumber { get; }         // e.g. 60
        public float Value { get; }            // velocity [0..1] or 1/0 for down/up
        public InputActionPhase Phase { get; } // Performed = Down, Canceled = Up
        public double Time { get; }            // Input System time or Time.realtimeSinceStartup

        public MidiKeyEvent(string name, int noteNumber, float value, InputActionPhase phase, double time)
        {
            Name = name;
            NoteNumber = noteNumber;
            Value = value;
            Phase = phase;
            Time = time;
        }

        public override string ToString() => $"{Name}({NoteNumber}) {Phase} v={Value:0.###} t={Time:0.000}";
    }

    private readonly InputSystem_Actions _actions;
    private readonly CompositeDisposable _disposables = new();

    // Per-key streams（キー名で引ける／例："C4", "Cs4"...）
    private readonly Dictionary<string, Subject<MidiKeyEvent>> _keyDown = new();
    private readonly Dictionary<string, Subject<MidiKeyEvent>> _keyUp   = new();

    // 全キー合流
    private readonly Subject<MidiKeyEvent> _anyKeyDown = new();
    private readonly Subject<MidiKeyEvent> _anyKeyUp   = new();

    public Observable<MidiKeyEvent> OnKeyDown(string key) => _keyDown[key];
    public Observable<MidiKeyEvent> OnKeyUp(string key) => _keyUp[key];

    public Observable<MidiKeyEvent> AnyKeyDown => _anyKeyDown;
    public Observable<MidiKeyEvent> AnyKeyUp   => _anyKeyUp;


    // --- Minis device subscriptions bookkeeping ---
    private readonly List<MidiDevice> _attachedDevices = new();

    public MidiInputObserver(InputSystem_Actions actions)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));

        // 1) InputAction 経由のフック
        EnableActionMapBindings();

        // 2) Minis の生コールバックにフック（既存デバイス＋以後追加）
        AttachToExistingMidiDevices();
        InputSystem.onDeviceChange += OnDeviceChange;
    }

    private void EnableActionMapBindings()
    {
        _actions.MIDI.Enable();

        // すべてのアクションを列挙して Down/Up に接続
        var midiMap = _actions.MIDI.Get();
        foreach (var act in midiMap.actions)
        {
            if (act == null) continue;

            var actionName = act.name; // 例: "C4"
            var noteNumber = TryGetNoteNumber(act, out var n) ? n : GuessNoteNumberByName(actionName);

            // Subject を用意
            var down = new Subject<MidiKeyEvent>().AddTo(_disposables);
            var up   = new Subject<MidiKeyEvent>().AddTo(_disposables);
            _keyDown[actionName] = down;
            _keyUp[actionName]   = up;

            // イベント登録
            act.performed += ctx =>
            {
                var val = ReadValueSafe(ctx);
                var e = new MidiKeyEvent(actionName, noteNumber, val, InputActionPhase.Performed, ctx.time);
                down.OnNext(e);
                _anyKeyDown.OnNext(e);
            };
            act.canceled += ctx =>
            {
                var val = ReadValueSafe(ctx);
                var e = new MidiKeyEvent(actionName, noteNumber, val, InputActionPhase.Canceled, ctx.time);
                up.OnNext(e);
                _anyKeyUp.OnNext(e);
            };

            act.Enable();
        }
    }

    // --- Minis: MidiDevice へのフック ---
    private void AttachToExistingMidiDevices()
    {
        foreach (var dev in InputSystem.devices)
        {
            if (dev is MidiDevice md) AttachToMidiDevice(md);
        }
    }

    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (device is not MidiDevice md) return;

        switch (change)
        {
            case InputDeviceChange.Added:
            case InputDeviceChange.Reconnected:
                AttachToMidiDevice(md);
                break;
            case InputDeviceChange.Removed:
            case InputDeviceChange.Disconnected:
                DetachFromMidiDevice(md);
                break;
        }
    }

    private void AttachToMidiDevice(MidiDevice md)
    {
        if (md == null || _attachedDevices.Contains(md)) return;

        // Minis 公式: onWillNoteOn / onWillNoteOff はコントロール更新前に呼ばれる
        // 引数は (MidiNoteControl note, float velocity) / (MidiNoteControl note)
        md.onWillNoteOn += OnWillNoteOn;
        md.onWillNoteOff += OnWillNoteOff;

        _attachedDevices.Add(md);
    }

    private void DetachFromMidiDevice(MidiDevice md)
    {
        if (md == null) return;
        try
        {
            md.onWillNoteOn -= OnWillNoteOn;
            md.onWillNoteOff -= OnWillNoteOff;
        }
        catch { /* ignore */ }
        _attachedDevices.Remove(md);
    }

    private void OnWillNoteOn(MidiNoteControl note, float velocity)
    {
        int num = note.noteNumber;
        string name = TryGetNoteName(note) ?? NoteNumberToName(num);
        var e = new MidiKeyEvent(name, num, Mathf.Clamp01(velocity), InputActionPhase.Performed, Time.realtimeSinceStartupAsDouble);

        // 個別ストリーム（存在しない場合は遅延作成）
        if (!_keyDown.TryGetValue(name, out var down))
        {
            down = new Subject<MidiKeyEvent>().AddTo(_disposables);
            _keyDown[name] = down;
        }
        down.OnNext(e);
        _anyKeyDown.OnNext(e);
    }

    private void OnWillNoteOff(MidiNoteControl note)
    {
        int num = note.noteNumber;
        string name = TryGetNoteName(note) ?? NoteNumberToName(num);
        var e = new MidiKeyEvent(name, num, 0f, InputActionPhase.Canceled, Time.realtimeSinceStartupAsDouble);

        if (!_keyUp.TryGetValue(name, out var up))
        {
            up = new Subject<MidiKeyEvent>().AddTo(_disposables);
            _keyUp[name] = up;
        }
        up.OnNext(e);
        _anyKeyUp.OnNext(e);
    }

    // --- Helpers ---
    private static string TryGetNoteName(MidiNoteControl note)
    {
        // Minis の NoteControl には displayName が入ることがある（例: "C4"）。
        // 無ければ null を返し、数値から生成する。
        var s = note?.displayName;
        return string.IsNullOrEmpty(s) ? null : s;
    }

    private static float ReadValueSafe(InputAction.CallbackContext ctx)
    {
        try { return ctx.ReadValue<float>(); }
        catch { return ctx.phase == InputActionPhase.Performed ? 1f : 0f; }
    }

    private static readonly Regex NoteRegex = new(@"note(?<num>\d{3})", RegexOptions.Compiled);

    private static bool TryGetNoteNumber(InputAction action, out int noteNumber)
    {
        foreach (var b in action.bindings)
        {
            if (string.IsNullOrEmpty(b.path)) continue;
            var m = NoteRegex.Match(b.path);    // "<MidiDevice>/note060" など
            if (m.Success && int.TryParse(m.Groups["num"].Value, out var n))
            {
                noteNumber = n;
                return true;
            }
        }
        noteNumber = -1;
        return false;
    }

    private static int GuessNoteNumberByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return -1;

        // 末尾の桁をオクターブとして抽出
        int i = name.Length - 1;
        while (i >= 0 && char.IsDigit(name[i])) i--;
        var notePart = name.Substring(0, i + 1); // "Cs"
        var octPart  = name.Substring(i + 1);    // "4"
        if (!int.TryParse(octPart, out var octave)) return -1;

        int semitone = notePart switch
        {
            "C"  => 0,  "Cs" => 1,
            "D"  => 2,  "Ds" => 3,
            "E"  => 4,
            "F"  => 5,  "Fs" => 6,
            "G"  => 7,  "Gs" => 8,
            "A"  => 9,  "As" => 10,
            "B"  => 11,
            _    => -1,
        };
        if (semitone < 0) return -1;

        // 本プロジェクトの命名前提: C1=24 -> C0=12
        const int C0 = 12;
        return C0 + octave * 12 + semitone;
    }

    private static string NoteNumberToName(int noteNumber)
    {
        // 12 = C0 として逆変換
        int x = Mathf.Max(0, noteNumber - 12);
        int octave = x / 12;
        int semitone = x % 12;
        return semitone switch
        {
            0  => $"C{octave}",
            1  => $"Cs{octave}",
            2  => $"D{octave}",
            3  => $"Ds{octave}",
            4  => $"E{octave}",
            5  => $"F{octave}",
            6  => $"Fs{octave}",
            7  => $"G{octave}",
            8  => $"Gs{octave}",
            9  => $"A{octave}",
            10 => $"As{octave}",
            11 => $"B{octave}",
            _  => noteNumber.ToString(),
        };
    }

    public void Dispose()
    {
        // 完了通知
        foreach (var s in _keyDown.Values) s.OnCompleted();
        foreach (var s in _keyUp.Values) s.OnCompleted();
        _anyKeyDown.OnCompleted();
        _anyKeyUp.OnCompleted();

        // Minis デバイスからデタッチ
        InputSystem.onDeviceChange -= OnDeviceChange;
        foreach (var md in _attachedDevices)
        {
            DetachFromMidiDevice(md);
        }
        _attachedDevices.Clear();

        _disposables.Dispose();
    }
}
