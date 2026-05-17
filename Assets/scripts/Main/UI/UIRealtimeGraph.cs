using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Main.UI
{
    /// <summary>
    /// 實時圖表UI組件
    /// 用於在Unity UI上繪製動態的實時數據曲線，支持多個數據序列、閾值線、填充區域等功能
    /// 可以用於顯示即時的性能指標（如FPS、延遲等）
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class UIRealtimeGraph : Graphic
    {
        /// <summary>
        /// 圖表數據序列類
        /// 用於儲存單個數據序列的所有樣本值、時間戳和顯示設置
        /// </summary>
        [System.Serializable]
        public class GraphSeries
        {
            // 序列的顯示名稱
            public string name = "Input";
            // 線條顏色
            public Color color = Color.green;
            // 是否啟用此序列（不啟用則不會被繪製）
            public bool enabled = true;
            // 是否使用閾值顏色（根據值大小自動改變顏色：綠色→黃色→紅色）
            public bool useThresholdColors = false;

            // 儲存樣本值的環形緩衝區
            private float[] values;
            // 儲存對應的時間戳的環形緩衝區
            private float[] times;
            // 環形緩衝區的寫入位置（下一個樣本將寫入此位置）
            private int head;
            // 當前儲存的有效樣本數量
            private int count;

            /// <summary>取得當前儲存的樣本數量</summary>
            public int Count => count;

            /// <summary>
            /// 確保緩衝區有足夠容量
            /// 如果容量不足，會重新初始化數組
            /// </summary>
            public void EnsureCapacity(int capacity)
            {
                // 容量最小為2
                capacity = Mathf.Max(2, capacity);
                // 如果已有相同容量的數組，直接返回
                if (values != null && times != null && values.Length == capacity && times.Length == capacity)
                {
                    return;
                }

                // 創建新的數組並重置寫入位置和計數
                values = new float[capacity];
                times = new float[capacity];
                head = 0;
                count = 0;
            }

            /// <summary>添加一個新的數據樣本</summary>
            public void AddSample(float value, float time)
            {
                // 數組未初始化時直接返回
                if (values == null || times == null || values.Length == 0)
                {
                    return;
                }

                // 在環形緩衝區的head位置寫入新樣本
                values[head] = value;
                times[head] = time;

                // 移動head指針到下一位置（使用模運算實現環形）
                head = (head + 1) % values.Length;
                // 增加計數（直到滿容量）
                if (count < values.Length)
                {
                    count++;
                }
            }

            /// <summary>清空所有樣本數據</summary>
            public void Clear()
            {
                // 重置寫入位置和計數
                head = 0;
                count = 0;
            }

            /// <summary>
            /// 收集在指定時間窗口內的有效樣本索引
            /// 返回在timeWindow秒內的樣本索引，按時間順序排列
            /// </summary>
            public int CollectValidIndices(float now, float timeWindow, int[] indices)
            {
                // 需要至少2個樣本才能繪製線條
                if (count < 2 || values == null || times == null)
                {
                    return 0;
                }

                int validCount = 0;
                int capacity = values.Length;
                // 從最新的樣本開始，向後遍歷
                for (int i = 0; i < count; i++)
                {
                    // 計算環形緩衝區中的實際索引
                    int index = (head - 1 - i + capacity) % capacity;
                    // 檢查此樣本是否在時間窗口內
                    if (now - times[index] <= timeWindow)
                    {
                        indices[validCount++] = index;
                    }
                    else
                    {
                        // 因為是從新到舊遍歷，一旦超出時間窗口就可以停止
                        break;
                    }
                }

                // 反轉索引數組，使其按時間順序（從舊到新）排列
                System.Array.Reverse(indices, 0, validCount);
                return validCount;
            }

            /// <summary>取得指定索引的樣本值</summary>
            public float GetValue(int index)
            {
                return values[index];
            }

            /// <summary>取得指定索引的時間戳</summary>
            public float GetTime(int index)
            {
                return times[index];
            }
        }

        // ========== 時間窗口設置 ==========
        [Header("Time Window")]
        /// <summary>顯示的時間範圍（秒），只有在此範圍內的樣本才會被顯示</summary>
        public float timeWindow = 5f;
        /// <summary>最多儲存的樣本數量，每個序列都會有此數量的環形緩衝區</summary>
        public int maxSamples = 300;

        // ========== 值的範圍設置 ==========
        [Header("Value Range")]
        /// <summary>圖表Y軸的最小值</summary>
        public float minValue = 0f;
        /// <summary>圖表Y軸的最大值</summary>
        public float maxValue = 120f;

        // ========== 樣式設置 ==========
        [Header("Style")]
        /// <summary>線條的寬度（像素）</summary>
        public float lineThickness = 2f;
        /// <summary>是否填充曲線下方的區域</summary>
        public bool fillArea = true;
        /// <summary>填充區域的透明度（0-1）</summary>
        [Range(0f, 1f)]
        public float fillAlpha = 0.2f;

        // ========== 數據輸入設置 ==========
        [Header("Data Input")]
        /// <summary>所有要顯示的數據序列</summary>
        public GraphSeries[] series =
        {
            new GraphSeries
            {
                name = "Input 1",
                color = Color.green,
                enabled = true,
                // 此序列使用閾值顏色（綠->黃->紅）
                useThresholdColors = true
            }
        };

        // ========== 閾值設置 ==========
        [Header("Thresholds")]
        /// <summary>是否顯示警告和危險閾值線</summary>
        public bool showThresholds = true;
        /// <summary>警告閾值（黃色線），低於此值顯示為黃色</summary>
        [FormerlySerializedAs("warnFPS")]
        public float warnValue = 60f;
        /// <summary>危險閾值（紅色線），低於此值顯示為紅色</summary>
        [FormerlySerializedAs("dangerFPS")]
        public float dangerValue = 30f;

        // 臨時陣列，用於儲存有效的樣本索引，避免每幀都分配新內存
        private int[] validIndices;

        /// <summary>Unity生命週期：初始化</summary>
        protected override void Awake()
        {
            base.Awake();
            // 確保所有數據結構已初始化
            EnsureInitialized();
        }

        /// <summary>Unity編輯器中修改屬性時調用</summary>
        protected override void OnValidate()
        {
            base.OnValidate();
            // 確保參數有效
            timeWindow = Mathf.Max(0.01f, timeWindow);
            maxSamples = Mathf.Max(2, maxSamples);
            lineThickness = Mathf.Max(0.1f, lineThickness);
            EnsureInitialized();
            // 標記需要重新繪製
            SetVerticesDirty();
        }

        /// <summary>為第一個序列添加一個新樣本</summary>
        public void AddSample(float value)
        {
            // 委派給索引版本
            AddSample(0, value);
        }

        /// <summary>為指定序列添加一個新樣本</summary>
        public void AddSample(int seriesIndex, float value)
        {
            EnsureInitialized();

            // 驗證序列索引
            if (seriesIndex < 0 || seriesIndex >= series.Length)
            {
                Debug.LogWarning($"[{nameof(UIRealtimeGraph)}] Series index out of range: {seriesIndex}");
                return;
            }

            // 添加樣本並標記需要重新繪製
            series[seriesIndex].AddSample(value, Time.time);
            SetVerticesDirty();
        }

        /// <summary>根據序列名稱為指定序列添加一個新樣本</summary>
        public void AddSample(string seriesName, float value)
        {
            // 查找序列索引
            int index = GetSeriesIndex(seriesName);
            if (index < 0)
            {
                Debug.LogWarning($"[{nameof(UIRealtimeGraph)}] Series not found: {seriesName}");
                return;
            }

            AddSample(index, value);
        }

        /// <summary>為多個序列同時添加樣本（按順序對應序列）</summary>
        public void AddSamples(params float[] values)
        {
            if (values == null)
            {
                return;
            }

            EnsureInitialized();
            // 使用同一時間戳確保所有樣本對齊
            float now = Time.time;
            // 只處理存在的序列數
            int sampleCount = Mathf.Min(values.Length, series.Length);
            for (int i = 0; i < sampleCount; i++)
            {
                series[i].AddSample(values[i], now);
            }

            SetVerticesDirty();
        }

        /// <summary>清空所有序列的所有樣本</summary>
        public void ClearSamples()
        {
            EnsureInitialized();
            // 清空所有序列
            for (int i = 0; i < series.Length; i++)
            {
                series[i].Clear();
            }

            SetVerticesDirty();
        }

        /// <summary>動態改變序列數量</summary>
        public void SetSeriesCount(int count)
        {
            // 至少保留1個序列
            count = Mathf.Max(1, count);
            EnsureInitialized();

            // 如果數量未改變直接返回
            if (series.Length == count)
            {
                return;
            }

            // 創建新的數組並複製或創建序列
            GraphSeries[] resized = new GraphSeries[count];
            for (int i = 0; i < count; i++)
            {
                // 保留既有序列或創建新的默認序列
                resized[i] = i < series.Length ? series[i] : CreateDefaultSeries(i);
            }

            series = resized;
            EnsureInitialized();
            SetVerticesDirty();
        }

        /// <summary>Unity繪製系統：生成網格頂點</summary>
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            // 清空現有的頂點
            vh.Clear();
            EnsureInitialized();
            if (series == null || series.Length == 0)
            {
                return;
            }

            // 取得RectTransform的尺寸
            Rect rect = rectTransform.rect;
            float width = rect.width;
            float height = rect.height;
            float now = Time.time;
            bool hasAnySeries = false;

            // 繪製所有啟用的序列
            for (int i = 0; i < series.Length; i++)
            {
                GraphSeries graphSeries = series[i];
                // 跳過未啟用或樣本不足的序列
                if (graphSeries == null || !graphSeries.enabled || graphSeries.Count < 2)
                {
                    continue;
                }

                // 收集在時間窗口內的有效樣本索引
                int validCount = graphSeries.CollectValidIndices(now, timeWindow, validIndices);
                if (validCount < 2)
                {
                    continue;
                }

                hasAnySeries = true;
                DrawSeries(vh, graphSeries, validCount, width, height, now);
            }

            // 如果沒有任何序列可繪製就直接返回
            if (!hasAnySeries)
            {
                return;
            }

            // 繪製閾值線
            if (showThresholds)
            {
                DrawThreshold(vh, warnValue, Color.yellow);
                DrawThreshold(vh, dangerValue, Color.red);
            }
        }

        /// <summary>繪製單個數據序列的曲線</summary>
        private void DrawSeries(VertexHelper vh, GraphSeries graphSeries, int validCount, float width, float height, float now)
        {
            Vector2 previous = Vector2.zero;

            // 遍歷時間窗口內的所有樣本
            for (int i = 0; i < validCount; i++)
            {
                int sampleIndex = validIndices[i];
                // 取得樣本值和時間
                float value = graphSeries.GetValue(sampleIndex);
                float time = graphSeries.GetTime(sampleIndex);
                
                // 計算X坐標（時間軸）：從左到右代表從舊到新
                // timeNormal：0（最舊）到1（現在）
                float timeNormal = 1f - (now - time) / timeWindow;
                float x = timeNormal * width;
                
                // 計算Y坐標（值軸）：根據minValue和maxValue進行正規化
                // valueNormal：0（最小值）到1（最大值）
                float valueNormal = Mathf.InverseLerp(minValue, maxValue, value);
                float y = valueNormal * height;

                // 應用pivot偏移，使坐標相對於RectTransform的pivot點
                Vector2 current = new Vector2(
                    x - width * rectTransform.pivot.x,
                    y - height * rectTransform.pivot.y
                );

                // 繪製從前一個點到當前點的線段
                if (i > 0)
                {
                    // 決定線條顏色：使用閾值顏色或序列的預設顏色
                    Color lineColor = graphSeries.useThresholdColors ? GetColor(value) : graphSeries.color;

                    // 繪製填充區域（曲線下方）
                    if (fillArea)
                    {
                        Color areaColor = lineColor;
                        areaColor.a *= fillAlpha;
                        DrawArea(vh, previous, current, height, areaColor);
                    }

                    // 繪製線條
                    DrawLine(vh, previous, current, lineThickness, lineColor);
                }

                previous = current;
            }
        }

        /// <summary>根據值返回對應的閾值顏色：紅色（危險）→ 黃色（警告）→ 綠色（正常）</summary>
        private Color GetColor(float value)
        {
            if (value < dangerValue) return Color.red;  // 紅色：低於危險閾值
            if (value < warnValue) return Color.yellow; // 黃色：低於警告閾值
            return Color.green;                          // 綠色：正常
        }

        /// <summary>繪製一條矩形線條（線寬由thickness控制）</summary>
        private void DrawLine(VertexHelper vh, Vector2 p0, Vector2 p1, float thickness, Color color)
        {
            // 計算兩點之間的向量
            Vector2 delta = p1 - p0;
            // 如果兩點過於接近則跳過
            if (delta.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            // 計算線段方向的垂直向量（法線），用於在線段兩側各延伸thickness/2的距離
            Vector2 direction = delta.normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x) * thickness * 0.5f;

            // 記錄開始頂點的索引
            int index = vh.currentVertCount;

            // 添加4個頂點構成一個矩形：
            // p0-normal   p0+normal
            //     \      /
            //      \    /
            //       \  /
            //     p1-normal   p1+normal
            vh.AddVert(p0 - normal, color, Vector2.zero);
            vh.AddVert(p0 + normal, color, Vector2.zero);
            vh.AddVert(p1 + normal, color, Vector2.zero);
            vh.AddVert(p1 - normal, color, Vector2.zero);

            // 添加2個三角形組成矩形
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }

        /// <summary>繪製曲線下方的填充區域</summary>
        private void DrawArea(VertexHelper vh, Vector2 p0, Vector2 p1, float height, Color color)
        {
            // 計算底部的Y坐標（X軸位置）
            float bottom = -height * rectTransform.pivot.y;

            int index = vh.currentVertCount;

            // 添加4個頂點構成填充梯形：
            //   p0   p1
            //    |   |
            //    |   |
            //  bottom
            vh.AddVert(new Vector2(p0.x, bottom), color, Vector2.zero);
            vh.AddVert(new Vector2(p0.x, p0.y), color, Vector2.zero);
            vh.AddVert(new Vector2(p1.x, p1.y), color, Vector2.zero);
            vh.AddVert(new Vector2(p1.x, bottom), color, Vector2.zero);

            // 添加2個三角形
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }

        /// <summary>繪製水平閾值線</summary>
        private void DrawThreshold(VertexHelper vh, float value, Color color)
        {
            float height = rectTransform.rect.height;
            float width = rectTransform.rect.width;

            // 計算閾值在圖表上的Y坐標
            float yNormal = Mathf.InverseLerp(minValue, maxValue, value);
            float y = yNormal * height - height * rectTransform.pivot.y;

            // 從左到右繪製一條完整的水平線
            Vector2 p0 = new Vector2(-width * rectTransform.pivot.x, y);
            Vector2 p1 = new Vector2(width * (1 - rectTransform.pivot.x), y);

            DrawLine(vh, p0, p1, 1f, color);
        }

        /// <summary>根據序列名稱查找序列索引</summary>
        private int GetSeriesIndex(string seriesName)
        {
            if (string.IsNullOrEmpty(seriesName) || series == null)
            {
                return -1;
            }

            // 遍歷尋找名稱匹配的序列
            for (int i = 0; i < series.Length; i++)
            {
                if (series[i] != null && series[i].name == seriesName)
                {
                    return i;
                }
            }

            // 未找到返回-1
            return -1;
        }

        /// <summary>確保所有數據結構正確初始化，避免空參考異常</summary>
        private void EnsureInitialized()
        {
            // 確保maxSamples有效
            maxSamples = Mathf.Max(2, maxSamples);

            // 如果沒有序列，創建一個默認序列
            if (series == null || series.Length == 0)
            {
                series = new[] { CreateDefaultSeries(0) };
            }

            // 初始化所有序列
            for (int i = 0; i < series.Length; i++)
            {
                // 如果序列為空，創建默認序列
                if (series[i] == null)
                {
                    series[i] = CreateDefaultSeries(i);
                }

                // 確保序列的緩衝區容量足夠
                series[i].EnsureCapacity(maxSamples);
            }

            // 初始化臨時索引陣列
            if (validIndices == null || validIndices.Length != maxSamples)
            {
                validIndices = new int[maxSamples];
            }
        }

        /// <summary>創建一個具有默認設置的新序列</summary>
        private GraphSeries CreateDefaultSeries(int index)
        {
            // 預設的顏色循環池
            Color[] colors =
            {
                Color.green,
                Color.cyan,
                new Color(1f, 0.5f, 0f),  // 橙色
                Color.magenta,
                Color.white
            };

            return new GraphSeries
            {
                name = $"Input {index + 1}",
                color = colors[index % colors.Length],  // 按索引循環選擇顏色
                enabled = true,
                useThresholdColors = index == 0  // 只有第一個序列預設使用閾值顏色
            };
        }
    }
}
