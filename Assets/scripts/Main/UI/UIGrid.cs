using UnityEngine;
using UnityEngine.UI;

namespace Main.UI
{
    public class UIGrid : Graphic
    {
        public int horizontalLines = 4;
        public int verticalLines = 5;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect r = rectTransform.rect;
            float w = r.width;
            float h = r.height;

            for (int i = 1; i < horizontalLines; i++)
            {
                float y = h * i / horizontalLines - h * rectTransform.pivot.y;
                DrawLine(vh, new Vector2(-w/2, y), new Vector2(w/2, y));
            }

            for (int i = 1; i < verticalLines; i++)
            {
                float x = w * i / verticalLines - w * rectTransform.pivot.x;
                DrawLine(vh, new Vector2(x, -h/2), new Vector2(x, h/2));
            }
        }

        void DrawLine(VertexHelper vh, Vector2 a, Vector2 b)
        {
            int i = vh.currentVertCount;
            vh.AddVert(a, color, Vector2.zero);
            vh.AddVert(b, color, Vector2.zero);
            vh.AddVert(b, color, Vector2.zero);
            vh.AddTriangle(i, i+1, i+2);
        }
    }
}