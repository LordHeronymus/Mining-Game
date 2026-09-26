using System;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(OreOnlyAttribute))]
public sealed class OreOnlyDrawer : PropertyDrawer
{
    static readonly BlockType[] OreTypes =
    {
        BlockType.IronOre,
        BlockType.CopperOre,
        BlockType.SilverOre,
        BlockType.GoldOre,
        BlockType.PlatinumOre,
        BlockType.Coal,
        BlockType.DiamondOre,
        BlockType.UltroniumOre
    };

    static readonly string[] Names = Array.ConvertAll(OreTypes,
        type => ObjectNames.NicifyVariableName(type.ToString()));

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.Enum)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }
        EditorGUI.BeginProperty(position, label, property);
        int selected = Array.IndexOf(OreTypes, (BlockType)property.intValue);
        EditorGUI.BeginChangeCheck();
        int next = EditorGUI.Popup(position, label.text, selected, Names);
        if (EditorGUI.EndChangeCheck() && next >= 0)
            property.intValue = (int)OreTypes[next];
        EditorGUI.EndProperty();
    }
}
