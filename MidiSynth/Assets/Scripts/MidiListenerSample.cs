using UnityEngine;
using UnityEngine.UI;
using R3;
using SqrWaveGenerator;
using TriWave;

public class MidiListenerSample : MonoBehaviour
{
    CompositeDisposable disposables = new();
    [Header("Synth Controllers")]
    [SerializeField] private SquareWaveGeneratorControl sqrWaveController = null;
    [SerializeField] private TriWaveGeneratorControl triWaveController = null;
    
    [Header("Toggle")]
    [SerializeField] private Toggle[] toggles = null;

    
    [SerializeField] private Toggle[] dutyRatioToggles = null;
    private int counter = 0;
    
    ReactiveProperty<ISynthsizer> activeSynth = new(null);
    void Awake()
    {
        var actions = new InputSystem_Actions().AddTo(disposables);
        var midi = new MidiInputObserver(actions).AddTo(disposables);
        activeSynth.Value = sqrWaveController;

        for (int i = 0; i < toggles.Length; i++)
        {
            int index = i;
            toggles[index].OnValueChangedAsObservable()
                .Where(isOn => isOn)
                .Subscribe(_ =>
                {
                    activeSynth.Value = index == 0 ? sqrWaveController : triWaveController;
                })
                .AddTo(disposables);
        }
        for (int i = 0; i < dutyRatioToggles.Length; i++)
        {
            int index = i;
            dutyRatioToggles[index].OnValueChangedAsObservable()
                .Where(isOn => isOn)
                .Subscribe(_ =>
                {
                    int divider = 1 << (3- index);
                    sqrWaveController?.SetRatio( (DutyRatio) divider);
                })
                .AddTo(disposables);
        }
        // すべてのキーをまとめて購読
        midi.AnyKeyDown.Subscribe(e =>
        {
            Debug.Log($"ANY Down {e}");
            OnKeyDown(e, activeSynth.CurrentValue);
        }).AddTo(disposables);
        
        midi.AnyKeyUp.Subscribe(e =>
        {
            Debug.Log($"ANY Up   {e}");
            OnKeyUp(e, activeSynth.CurrentValue);
        }).AddTo(disposables);
        
        activeSynth
            .Skip(1)// SkipOnSubscribe
            .Pairwise()
            .Subscribe(pair =>
            {
                if (pair.Previous != null)
                {
                    pair.Previous.SetActive(false);
                }
            })
            .AddTo(disposables);
    }

    void OnKeyDown(MidiInputObserver.MidiKeyEvent ev, ISynthsizer synth)
    {
        counter++;
        synth.SetFrequency(NoteNumberToFrequency(ev.NoteNumber));
        synth.SetActive(counter> 0);
    }

    void OnKeyUp(MidiInputObserver.MidiKeyEvent ev, ISynthsizer synth)
    {
        counter = Mathf.Max(0, counter - 1);
        synth.SetActive(counter> 0);
    }

    private float NoteNumberToFrequency(int noteNumber)
    {
        return 440f * Mathf.Pow(2f, (noteNumber - 69f) / 12f);
    }
    void OnDestroy()
    {
        disposables.Dispose();
    }
}
