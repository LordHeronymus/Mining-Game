using UnityEngine;
using UnityEngine.UI;

public sealed class WorkbenchPreviewOrnament : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var center = rectTransform.rect.center;
        var radius = rectTransform.rect.size * .45f;
        for (int i = 0; i < 80; i++)
        {
            float a = (15 + i * 150f / 80) * Mathf.Deg2Rad;
            float b = (15 + (i + 1) * 150f / 80) * Mathf.Deg2Rad;
            Line(mesh, center + Vector2.Scale(new Vector2(Mathf.Cos(a), Mathf.Sin(a)), radius),
                center + Vector2.Scale(new Vector2(Mathf.Cos(b), Mathf.Sin(b)), radius), .8f);
        }
        Diamond(mesh, center + new Vector2(0, radius.y), 5);
        Diamond(mesh, center + new Vector2(-radius.x, -15), 4);
        Diamond(mesh, center + new Vector2(radius.x, -15), 4);
    }
    void Diamond(VertexHelper mesh, Vector2 p, float size)
    {
        Line(mesh,p+Vector2.up*size,p+Vector2.right*size,1);
        Line(mesh,p+Vector2.right*size,p+Vector2.down*size,1);
        Line(mesh,p+Vector2.down*size,p+Vector2.left*size,1);
        Line(mesh,p+Vector2.left*size,p+Vector2.up*size,1);
    }
    void Line(VertexHelper mesh, Vector2 a, Vector2 b, float width)
    {
        var n = new Vector2(-(b-a).y,(b-a).x).normalized * width * .5f;
        int start = mesh.currentVertCount;
        mesh.AddVert(a-n,color,Vector2.zero); mesh.AddVert(a+n,color,Vector2.zero);
        mesh.AddVert(b+n,color,Vector2.zero); mesh.AddVert(b-n,color,Vector2.zero);
        mesh.AddTriangle(start,start+1,start+2); mesh.AddTriangle(start,start+2,start+3);
    }
}
