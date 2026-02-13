using CustomAudioPipeline.Controller;
using CustomAudioPipeline.Nodes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CustomAudioPipeline.UI
{
    public sealed class SpectrumHistogramUGUI : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField] private int analyzerId = 1;
        [SerializeField] private FFTAnalyzeNodeController _controller;
        [Header("UI")]
        [SerializeField] private RectTransform barsRoot;
        [SerializeField] private Image barPrefab;

        [Header("Display")]
        [SerializeField] [UnityEngine.Range(16, 128)] private int barCount = 64;
        [SerializeField] private float maxHeight = 200.0f;
        [SerializeField] private float minDb = -80.0f;
        [SerializeField] private float maxDb = 0.0f;
        [SerializeField] [UnityEngine.Range(0.0f, 1.0f)] private float smoothing = 0.25f;

        [Header("Behavior")]
        [SerializeField] private bool rebuildOnStart = true;

        private RectTransform[] _bars;
        private float[] _smoothed01;

        private void Start()
        {
            Assert.IsNotNull(_controller, "_controller is null");
            if (rebuildOnStart)
            {
                RebuildBars();
            }
        }

        [ContextMenu("RebuildBars")]
        public void RebuildBars()
        {
            if (barsRoot == null || barPrefab == null)
            {
                return;
            }

            for (int i = barsRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(barsRoot.GetChild(i).gameObject);
            }

            _bars = new RectTransform[barCount];
            _smoothed01 = new float[barCount];

            for (int i = 0; i < barCount; i++)
            {
                var img = Instantiate(barPrefab, barsRoot);
                _bars[i] = img.rectTransform;

                var size = _bars[i].sizeDelta;
                size.y = 0.0f;
                _bars[i].sizeDelta = size;
            }
        }

        private void Update()
        {
            if (_bars == null || _bars.Length == 0)
            {
                return;
            }

            if (!FFTAnalyzeNode.SpectrumBus.TryGetLatest(analyzerId, out var bins, out _))
            {
                return;
            }

            int n = Mathf.Min(_bars.Length, bins.Length);

            // 60fps基準で smoothing をフレームレート非依存化（だいたい良い感じ）
            float k = 1.0f - Mathf.Pow(1.0f - Mathf.Clamp01(smoothing), 60.0f * Time.unscaledDeltaTime);

            // windowSize は Analyzer と合わせる必要あり（Controllerで指定してる値と同じにする）
            float windowSize = _controller.WindowSize;
            for (int i = 0; i < n; i++)
            {
                float power = bins[i];
            
                float normalizedPower = power / (windowSize*windowSize);
                float db = 10.0f * Mathf.Log10(normalizedPower + 1e-12f);

                // dB -> 0..1
                float t = Mathf.InverseLerp(minDb, maxDb, db);

                // smoothing
                _smoothed01[i] = Mathf.Lerp(_smoothed01[i], t, k);

                // apply height
                var size = _bars[i].sizeDelta;
                size.y = _smoothed01[i] * maxHeight;
                _bars[i].sizeDelta = size;
            }

            // 余りのバーを0に（binよりバーが多い場合）
            for (int i = n; i < _bars.Length; i++)
            {
                _smoothed01[i] = Mathf.Lerp(_smoothed01[i], 0.0f, k);

                var size = _bars[i].sizeDelta;
                size.y = _smoothed01[i] * maxHeight;
                _bars[i].sizeDelta = size;
            }
        }
    }
}
