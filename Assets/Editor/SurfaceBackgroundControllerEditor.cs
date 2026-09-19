using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SurfaceBackgroundController))]
public sealed class SurfaceBackgroundControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script", "layers");
        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("layers"), new GUIContent("Ebenen"), true);
        serializedObject.ApplyModifiedProperties();
    }
}

// The array exposes the existing layer components inline instead of copying their data.
// SerializedObject preserves prefab overrides, Undo and asset references on those components.
[CustomPropertyDrawer(typeof(SurfaceBackgroundController.LayerEntry))]
public sealed class SurfaceBackgroundLayerEntryDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorGUIUtility.singleLineHeight;
        if (!property.isExpanded) return height;
        height += EditorGUIUtility.singleLineHeight + 4;
        var layer = property.FindPropertyRelative("layer").objectReferenceValue as ParallaxLayer;
        if (!layer) return height;
        height += 3 * (EditorGUIUtility.singleLineHeight + 4);
        using (var data = new SerializedObject(layer))
        {
            var field = data.GetIterator();
            if (field.NextVisible(true))
                while (field.NextVisible(false)) height += EditorGUI.GetPropertyHeight(field, true) + 4;
        }
        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        var reference = property.FindPropertyRelative("layer");
        var layer = reference.objectReferenceValue as ParallaxLayer;
        var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(row, property.isExpanded,
            layer ? layer.name : label.text, true);
        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            row.y += row.height + 4;
            EditorGUI.PropertyField(row, reference, new GUIContent("Ebene"));
            layer = reference.objectReferenceValue as ParallaxLayer;
            if (layer)
            {
                using (var transformData = new SerializedObject(layer.transform))
                {
                    Draw(ref row, transformData.FindProperty("m_LocalPosition"), "Position");
                    Draw(ref row, transformData.FindProperty("m_LocalScale"), "Skalierung");
                    transformData.ApplyModifiedProperties();
                }
                using (var data = new SerializedObject(layer))
                {
                    Draw(ref row, data.FindProperty("m_Enabled"), "Aktiv");
                    var field = data.GetIterator();
                    if (field.NextVisible(true))
                        while (field.NextVisible(false))
                            Draw(ref row, field, field.name == "segments" ? "Bilder" : field.displayName);
                    if (data.ApplyModifiedProperties()) layer.Refresh();
                }
            }
            EditorGUI.indentLevel--;
        }
        EditorGUI.EndProperty();
    }

    static void Draw(ref Rect row, SerializedProperty field, string label)
    {
        row.y += row.height + 4;
        row.height = EditorGUI.GetPropertyHeight(field, true);
        EditorGUI.PropertyField(row, field, new GUIContent(label), true);
    }
}
