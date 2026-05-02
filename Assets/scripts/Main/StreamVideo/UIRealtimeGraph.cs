using UnityEngine;
using UnityEngine.UI;

namespace StreamVideo
{
    [RequireComponent(typeof(RectTransform))]
    public class UIRealtimeGraph : Graphic
    {
        [Header("Time Window")]
        public float timeWindow = 5f;     // 顯示最近幾秒
        public int maxSamples = 300;      // 解析度（越高越平滑）

        [Header("Value Range")]
        public float minValue = 0f;
        public float maxValue = 120f;

        [Header("Style")]
        public float lineThickness = 2f;
        public bool fillArea = true;

        [Header("Threshold")]
        public float warnFPS = 60f;
        public float dangerFPS = 30f;

        // 環形 buffer
        private float[] values;
        private float[] times;
        private int head = 0;
        private int count = 0;

        private float smoothed;

        protected override void Awake()
        {
            base.Awake();
            values = new float[maxSamples];
            times  = new float[maxSamples];
        }

        // void Update()
        // {
        //     // 範例：FPS
        //     float raw = 1f / Time.deltaTime;

        //     // 平滑（EMA）
        //     smoothed = Mathf.Lerp(smoothed, raw, 0.1f);

        //     AddSample(smoothed);
        //     SetVerticesDirty();
        // }

        public void AddSample(float v)
        {
            float t = Time.time;

            values[head] = v;
            times[head] = t;

            head = (head + 1) % maxSamples;
            if (count < maxSamples) count++;

            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (count < 2) return;

            Rect r = rectTransform.rect;
            float width = r.width;
            float height = r.height;

            float now = Time.time;

            // 收集有效點（在 timeWindow 內）
            int validCount = 0;
            int[] idx = new int[count];

            for (int i = 0; i < count; i++)
            {
                int index = (head - 1 - i + maxSamples) % maxSamples;
                if (now - times[index] <= timeWindow)
                {
                    idx[validCount++] = index;
                }
                else break;
            }

            if (validCount < 2) return;

            // 反轉順序（時間由舊→新）
            System.Array.Reverse(idx, 0, validCount);

            Vector2 prev = Vector2.zero;

            for (int i = 0; i < validCount; i++)
            {
                float tNorm = 1f - (now - times[idx[i]]) / timeWindow;
                float x = tNorm * width;

                float vNorm = Mathf.InverseLerp(minValue, maxValue, values[idx[i]]);
                float y = vNorm * height;

                Vector2 cur = new Vector2(
                    x - width * rectTransform.pivot.x,
                    y - height * rectTransform.pivot.y
                );

                if (i > 0)
                {
                    Color c = GetColor(values[idx[i]]);
                    DrawLine(vh, prev, cur, lineThickness, c);

                    if (fillArea)
                        DrawArea(vh, prev, cur, height, c * new Color(1,1,1,0.2f));
                }

                prev = cur;
            }

            DrawThreshold(vh, warnFPS, Color.yellow);
            DrawThreshold(vh, dangerFPS, Color.red);
        }

        Color GetColor(float v)
        {
            if (v < dangerFPS) return Color.red;
            if (v < warnFPS) return Color.yellow;
            return Color.green;
        }

        void DrawLine(VertexHelper vh, Vector2 p0, Vector2 p1, float t, Color c)
        {
            Vector2 dir = (p1 - p0).normalized;
            Vector2 n = new Vector2(-dir.y, dir.x) * t * 0.5f;

            int i = vh.currentVertCount;

            vh.AddVert(p0 - n, c, Vector2.zero);
            vh.AddVert(p0 + n, c, Vector2.zero);
            vh.AddVert(p1 + n, c, Vector2.zero);
            vh.AddVert(p1 - n, c, Vector2.zero);

            vh.AddTriangle(i, i+1, i+2);
            vh.AddTriangle(i, i+2, i+3);
        }

        void DrawArea(VertexHelper vh, Vector2 p0, Vector2 p1, float height, Color c)
        {
            float bottom = -height * rectTransform.pivot.y;

            int i = vh.currentVertCount;

            vh.AddVert(new Vector2(p0.x, bottom), c, Vector2.zero);
            vh.AddVert(new Vector2(p0.x, p0.y), c, Vector2.zero);
            vh.AddVert(new Vector2(p1.x, p1.y), c, Vector2.zero);
            vh.AddVert(new Vector2(p1.x, bottom), c, Vector2.zero);

            vh.AddTriangle(i, i+1, i+2);
            vh.AddTriangle(i, i+2, i+3);
        }

        void DrawThreshold(VertexHelper vh, float value, Color c)
        {
            float height = rectTransform.rect.height;
            float width = rectTransform.rect.width;

            float yNorm = Mathf.InverseLerp(minValue, maxValue, value);
            float y = yNorm * height - height * rectTransform.pivot.y;

            Vector2 p0 = new Vector2(-width * rectTransform.pivot.x, y);
            Vector2 p1 = new Vector2(width * (1 - rectTransform.pivot.x), y);

            DrawLine(vh, p0, p1, 1f, c);
        }
    }
}