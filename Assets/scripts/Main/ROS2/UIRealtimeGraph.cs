using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace ROS2
{
    [RequireComponent(typeof(RectTransform))]
    public class UIRealtimeGraph : Graphic
    {
        [System.Serializable]
        public class GraphSeries
        {
            public string name = "Input";
            public Color color = Color.green;
            public bool enabled = true;
            public bool useThresholdColors = false;

            private float[] values;
            private float[] times;
            private int head;
            private int count;

            public int Count => count;

            public void EnsureCapacity(int capacity)
            {
                capacity = Mathf.Max(2, capacity);
                if (values != null && times != null && values.Length == capacity && times.Length == capacity)
                {
                    return;
                }

                values = new float[capacity];
                times = new float[capacity];
                head = 0;
                count = 0;
            }

            public void AddSample(float value, float time)
            {
                if (values == null || times == null || values.Length == 0)
                {
                    return;
                }

                values[head] = value;
                times[head] = time;

                head = (head + 1) % values.Length;
                if (count < values.Length)
                {
                    count++;
                }
            }

            public void Clear()
            {
                head = 0;
                count = 0;
            }

            public int CollectValidIndices(float now, float timeWindow, int[] indices)
            {
                if (count < 2 || values == null || times == null)
                {
                    return 0;
                }

                int validCount = 0;
                int capacity = values.Length;
                for (int i = 0; i < count; i++)
                {
                    int index = (head - 1 - i + capacity) % capacity;
                    if (now - times[index] <= timeWindow)
                    {
                        indices[validCount++] = index;
                    }
                    else
                    {
                        break;
                    }
                }

                System.Array.Reverse(indices, 0, validCount);
                return validCount;
            }

            public float GetValue(int index)
            {
                return values[index];
            }

            public float GetTime(int index)
            {
                return times[index];
            }
        }

        [Header("Time Window")]
        public float timeWindow = 5f;
        public int maxSamples = 300;

        [Header("Value Range")]
        public float minValue = 0f;
        public float maxValue = 120f;

        [Header("Style")]
        public float lineThickness = 2f;
        public bool fillArea = true;
        [Range(0f, 1f)]
        public float fillAlpha = 0.2f;

        [Header("Inputs")]
        public GraphSeries[] series =
        {
            new GraphSeries
            {
                name = "Input 1",
                color = Color.green,
                enabled = true,
                useThresholdColors = true
            }
        };

        [Header("Threshold")]
        public bool showThresholds = true;
        [FormerlySerializedAs("warnFPS")]
        public float warnValue = 60f;
        [FormerlySerializedAs("dangerFPS")]
        public float dangerValue = 30f;

        private int[] validIndices;

        protected override void Awake()
        {
            base.Awake();
            EnsureInitialized();
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            timeWindow = Mathf.Max(0.01f, timeWindow);
            maxSamples = Mathf.Max(2, maxSamples);
            lineThickness = Mathf.Max(0.1f, lineThickness);
            EnsureInitialized();
            SetVerticesDirty();
        }

        public void AddSample(float value)
        {
            AddSample(0, value);
        }

        public void AddSample(int seriesIndex, float value)
        {
            EnsureInitialized();

            if (seriesIndex < 0 || seriesIndex >= series.Length)
            {
                Debug.LogWarning($"[{nameof(UIRealtimeGraph)}] Series index out of range: {seriesIndex}");
                return;
            }

            series[seriesIndex].AddSample(value, Time.time);
            SetVerticesDirty();
        }

        public void AddSample(string seriesName, float value)
        {
            int index = GetSeriesIndex(seriesName);
            if (index < 0)
            {
                Debug.LogWarning($"[{nameof(UIRealtimeGraph)}] Series not found: {seriesName}");
                return;
            }

            AddSample(index, value);
        }

        public void AddSamples(params float[] values)
        {
            if (values == null)
            {
                return;
            }

            EnsureInitialized();
            float now = Time.time;
            int sampleCount = Mathf.Min(values.Length, series.Length);
            for (int i = 0; i < sampleCount; i++)
            {
                series[i].AddSample(values[i], now);
            }

            SetVerticesDirty();
        }

        public void ClearSamples()
        {
            EnsureInitialized();
            for (int i = 0; i < series.Length; i++)
            {
                series[i].Clear();
            }

            SetVerticesDirty();
        }

        public void SetSeriesCount(int count)
        {
            count = Mathf.Max(1, count);
            EnsureInitialized();

            if (series.Length == count)
            {
                return;
            }

            GraphSeries[] resized = new GraphSeries[count];
            for (int i = 0; i < count; i++)
            {
                resized[i] = i < series.Length ? series[i] : CreateDefaultSeries(i);
            }

            series = resized;
            EnsureInitialized();
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            EnsureInitialized();
            if (series == null || series.Length == 0)
            {
                return;
            }

            Rect rect = rectTransform.rect;
            float width = rect.width;
            float height = rect.height;
            float now = Time.time;
            bool hasAnySeries = false;

            for (int i = 0; i < series.Length; i++)
            {
                GraphSeries graphSeries = series[i];
                if (graphSeries == null || !graphSeries.enabled || graphSeries.Count < 2)
                {
                    continue;
                }

                int validCount = graphSeries.CollectValidIndices(now, timeWindow, validIndices);
                if (validCount < 2)
                {
                    continue;
                }

                hasAnySeries = true;
                DrawSeries(vh, graphSeries, validCount, width, height, now);
            }

            if (!hasAnySeries)
            {
                return;
            }

            if (showThresholds)
            {
                DrawThreshold(vh, warnValue, Color.yellow);
                DrawThreshold(vh, dangerValue, Color.red);
            }
        }

        private void DrawSeries(VertexHelper vh, GraphSeries graphSeries, int validCount, float width, float height, float now)
        {
            Vector2 previous = Vector2.zero;

            for (int i = 0; i < validCount; i++)
            {
                int sampleIndex = validIndices[i];
                float value = graphSeries.GetValue(sampleIndex);
                float timeNormal = 1f - (now - graphSeries.GetTime(sampleIndex)) / timeWindow;
                float x = timeNormal * width;
                float valueNormal = Mathf.InverseLerp(minValue, maxValue, value);
                float y = valueNormal * height;

                Vector2 current = new Vector2(
                    x - width * rectTransform.pivot.x,
                    y - height * rectTransform.pivot.y
                );

                if (i > 0)
                {
                    Color lineColor = graphSeries.useThresholdColors ? GetColor(value) : graphSeries.color;

                    if (fillArea)
                    {
                        Color areaColor = lineColor;
                        areaColor.a *= fillAlpha;
                        DrawArea(vh, previous, current, height, areaColor);
                    }

                    DrawLine(vh, previous, current, lineThickness, lineColor);
                }

                previous = current;
            }
        }

        private Color GetColor(float value)
        {
            if (value < dangerValue) return Color.red;
            if (value < warnValue) return Color.yellow;
            return Color.green;
        }

        private void DrawLine(VertexHelper vh, Vector2 p0, Vector2 p1, float thickness, Color color)
        {
            Vector2 delta = p1 - p0;
            if (delta.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            Vector2 direction = delta.normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x) * thickness * 0.5f;

            int index = vh.currentVertCount;

            vh.AddVert(p0 - normal, color, Vector2.zero);
            vh.AddVert(p0 + normal, color, Vector2.zero);
            vh.AddVert(p1 + normal, color, Vector2.zero);
            vh.AddVert(p1 - normal, color, Vector2.zero);

            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }

        private void DrawArea(VertexHelper vh, Vector2 p0, Vector2 p1, float height, Color color)
        {
            float bottom = -height * rectTransform.pivot.y;

            int index = vh.currentVertCount;

            vh.AddVert(new Vector2(p0.x, bottom), color, Vector2.zero);
            vh.AddVert(new Vector2(p0.x, p0.y), color, Vector2.zero);
            vh.AddVert(new Vector2(p1.x, p1.y), color, Vector2.zero);
            vh.AddVert(new Vector2(p1.x, bottom), color, Vector2.zero);

            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }

        private void DrawThreshold(VertexHelper vh, float value, Color color)
        {
            float height = rectTransform.rect.height;
            float width = rectTransform.rect.width;

            float yNormal = Mathf.InverseLerp(minValue, maxValue, value);
            float y = yNormal * height - height * rectTransform.pivot.y;

            Vector2 p0 = new Vector2(-width * rectTransform.pivot.x, y);
            Vector2 p1 = new Vector2(width * (1 - rectTransform.pivot.x), y);

            DrawLine(vh, p0, p1, 1f, color);
        }

        private int GetSeriesIndex(string seriesName)
        {
            if (string.IsNullOrEmpty(seriesName) || series == null)
            {
                return -1;
            }

            for (int i = 0; i < series.Length; i++)
            {
                if (series[i] != null && series[i].name == seriesName)
                {
                    return i;
                }
            }

            return -1;
        }

        private void EnsureInitialized()
        {
            maxSamples = Mathf.Max(2, maxSamples);

            if (series == null || series.Length == 0)
            {
                series = new[] { CreateDefaultSeries(0) };
            }

            for (int i = 0; i < series.Length; i++)
            {
                if (series[i] == null)
                {
                    series[i] = CreateDefaultSeries(i);
                }

                series[i].EnsureCapacity(maxSamples);
            }

            if (validIndices == null || validIndices.Length != maxSamples)
            {
                validIndices = new int[maxSamples];
            }
        }

        private GraphSeries CreateDefaultSeries(int index)
        {
            Color[] colors =
            {
                Color.green,
                Color.cyan,
                new Color(1f, 0.5f, 0f),
                Color.magenta,
                Color.white
            };

            return new GraphSeries
            {
                name = $"Input {index + 1}",
                color = colors[index % colors.Length],
                enabled = true,
                useThresholdColors = index == 0
            };
        }
    }
}
