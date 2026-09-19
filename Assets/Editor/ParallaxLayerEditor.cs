using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ParallaxLayer)), CanEditMultipleObjects]
public sealed class ParallaxLayerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("Vertical Offset verschiebt diese Ebene nach oben/unten. Height umfasst auch transparente Bildbereiche. Parallax: 0 = bildschirmfest, 1 = weltfest.", MessageType.Info);
        DrawDefaultInspector();
        foreach (Object item in targets)
        {
            var layer = (ParallaxLayer)item;
            if (!layer.GetComponentInParent<SurfaceBackgroundController>())
                EditorGUILayout.HelpBox("Die Ebene benötigt einen SurfaceBackgroundController auf einem übergeordneten Objekt.", MessageType.Warning);
            if (!layer.material)
                EditorGUILayout.HelpBox("Bitte ein Unlit-Sprite-Material zuweisen.", MessageType.Warning);
            if (Quaternion.Angle(layer.transform.rotation, Quaternion.identity) > 0.01f ||
                layer.transform.lossyScale.x <= 0f || layer.transform.lossyScale.y <= 0f)
                EditorGUILayout.HelpBox("Ebenen benötigen positive Skalierung und dürfen nicht gedreht sein.", MessageType.Warning);
            layer.Refresh();
        }
    }
}
