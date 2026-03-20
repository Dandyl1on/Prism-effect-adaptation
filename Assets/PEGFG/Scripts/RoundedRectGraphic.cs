using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public class RoundedRectGraphic : MaskableGraphic
{
    [SerializeField] private float cornerRadius = 10f;
    [SerializeField] private int cornerSegments = 6;

    public float CornerRadius
    {
        get => cornerRadius;
        set
        {
            cornerRadius = Mathf.Max(0f, value);
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect rect = GetPixelAdjustedRect();
        float width = rect.width;
        float height = rect.height;
        if (width <= 0f || height <= 0f)
            return;

        float radius = Mathf.Min(cornerRadius, width * 0.5f, height * 0.5f);
        if (radius <= 0.01f)
        {
            AddQuad(vh, rect.min, rect.max, color);
            return;
        }

        int segments = Mathf.Max(1, cornerSegments);
        Vector2 center = rect.center;
        AddVertex(vh, center, color);

        AddCornerArc(vh, new Vector2(rect.xMax - radius, rect.yMax - radius), radius, 0f, 90f, segments, true);
        AddCornerArc(vh, new Vector2(rect.xMin + radius, rect.yMax - radius), radius, 90f, 180f, segments, false);
        AddCornerArc(vh, new Vector2(rect.xMin + radius, rect.yMin + radius), radius, 180f, 270f, segments, false);
        AddCornerArc(vh, new Vector2(rect.xMax - radius, rect.yMin + radius), radius, 270f, 360f, segments, false);

        for (int i = 1; i < vh.currentVertCount - 1; i++)
            vh.AddTriangle(0, i, i + 1);

        vh.AddTriangle(0, vh.currentVertCount - 1, 1);
    }

    static void AddVertex(VertexHelper vh, Vector2 position, Color32 vertexColor)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = vertexColor;
        vertex.position = position;
        vh.AddVert(vertex);
    }

    static void AddQuad(VertexHelper vh, Vector2 min, Vector2 max, Color32 vertexColor)
    {
        int startIndex = vh.currentVertCount;
        AddVertex(vh, new Vector2(min.x, min.y), vertexColor);
        AddVertex(vh, new Vector2(min.x, max.y), vertexColor);
        AddVertex(vh, new Vector2(max.x, max.y), vertexColor);
        AddVertex(vh, new Vector2(max.x, min.y), vertexColor);
        vh.AddTriangle(startIndex + 0, startIndex + 1, startIndex + 2);
        vh.AddTriangle(startIndex + 0, startIndex + 2, startIndex + 3);
    }

    void AddCornerArc(VertexHelper vh, Vector2 arcCenter, float radius, float startDegrees, float endDegrees, int segments, bool includeFirstVertex)
    {
        for (int i = 0; i <= segments; i++)
        {
            if (!includeFirstVertex && i == 0)
                continue;

            float t = i / (float)segments;
            float angleRadians = Mathf.Lerp(startDegrees, endDegrees, t) * Mathf.Deg2Rad;
            Vector2 point = new Vector2(
                arcCenter.x + Mathf.Cos(angleRadians) * radius,
                arcCenter.y + Mathf.Sin(angleRadians) * radius);
            AddVertex(vh, point, color);
        }
    }
}
