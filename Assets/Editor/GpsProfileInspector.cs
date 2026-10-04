using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GpsProfile))]
public sealed class GpsProfileInspector : Editor
{
    public override void OnInspectorGUI()
    { if(GUILayout.Button("Gameplay Settings")) GameplaySettingsWindow.Open(); }
}
