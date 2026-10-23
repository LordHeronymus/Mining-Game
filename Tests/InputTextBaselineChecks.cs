using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Checks that text in styled input fields (New Game name, save slot name, searches, GPS) keeps a fixed baseline:
// typing a descender (g, y) or an umlaut must not move the line, and the empty field (TMP renders a zero-width
// space there, which places the caret) sits on the same baseline as typed text. Built like the New Game name field
// in a temporary additive scene; the open SampleScene is not changed.
public static class InputTextBaselineChecks
{
    const float Tolerance = .01f;
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            var canvas = new GameObject("Input Baseline Canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var background = HomeUi.Image("Game Name", canvas.transform, Vector2.zero, new Vector2(660, 76));
            var viewport = HomeUi.Rect("Text Viewport", background.transform, Vector2.zero, new Vector2(614, 60));
            viewport.gameObject.AddComponent<RectMask2D>();
            var text = HomeUi.Label("Text", viewport, "", Vector2.zero, new Vector2(604, 55), 30);
            var input = background.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = viewport; input.textComponent = text; input.targetGraphic = background;
            input.lineType = TMP_InputField.LineType.SingleLine;
            HomeUi.StyleInputField(input);

            var centering = text.GetComponent<ButtonTextCentering>();
            Check(centering, "Eingabetext hat eine feste Grundlinie (ButtonTextCentering)");
            Check(text.verticalAlignment == VerticalAlignmentOptions.Middle, "Eingabetext nicht mehr nach sichtbaren Buchstaben zentriert (Middle statt Midline)");
            Check(text.horizontalAlignment == HorizontalAlignmentOptions.Left, "Eingabetext bleibt linksbündig");
            // Outside Play Mode Unity does not call OnEnable on this component; subscribe like at runtime.
            typeof(ButtonTextCentering).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(centering, null);

            float Baseline(string value)
            {
                text.text = value; text.ForceMeshUpdate(true, true);
                return text.textInfo.characterInfo[0].baseLine;
            }
            float hallo = Baseline("Hallo");
            float descender = Baseline("Hallo hallo hade kg");
            float umlaut = Baseline("Äpfel");
            float empty = Baseline("​");
            checks.Add($"INFO Grundlinie Hallo {hallo:0.00}, mit g {descender:0.00}, Ä {umlaut:0.00}, leer {empty:0.00}");
            Check(Mathf.Abs(hallo - descender) < Tolerance, "Buchstabe mit Unterlänge verschiebt den Text nicht");
            Check(Mathf.Abs(hallo - umlaut) < Tolerance, "Umlaut verschiebt den Text nicht");
            Check(Mathf.Abs(hallo - empty) < Tolerance, "Leeres Feld (Schreibmarke) auf derselben Grundlinie wie Text");

            // Optical middle (between baseline and cap height) sits on the middle of the text box, ink inside the viewport.
            Baseline("Äg");
            var face = text.font.faceInfo; var head = text.textInfo.characterInfo[0];
            float middle = head.baseLine + face.capLine * head.scale / 2;
            var box = text.rectTransform.rect;
            float target = (box.yMin + text.margin.w + box.yMax - text.margin.y) / 2 + GpsSettings.Preferences.buttonTextOffset * head.pointSize;
            Check(Mathf.Abs(middle - target) < .05f, "Mitte zwischen Grundlinie und Grossbuchstabenhöhe liegt in der Feldmitte");
            float top = float.MinValue, bottom = float.MaxValue;
            for (int i = 0; i < text.textInfo.characterCount; i++)
            {
                var c = text.textInfo.characterInfo[i];
                if (!c.isVisible) continue;
                var m = c.textElement.glyph.metrics;
                top = Mathf.Max(top, c.baseLine + m.horizontalBearingY * c.scale);
                bottom = Mathf.Min(bottom, c.baseLine + (m.horizontalBearingY - m.height) * c.scale);
            }
            var view = viewport.rect;
            float viewTop = text.rectTransform.InverseTransformPoint(viewport.TransformPoint(new Vector3(0, view.yMax))).y;
            float viewBottom = text.rectTransform.InverseTransformPoint(viewport.TransformPoint(new Vector3(0, view.yMin))).y;
            checks.Add($"INFO Ä/g von {bottom:0.0} bis {top:0.0}, sichtbarer Bereich {viewBottom:0.0} bis {viewTop:0.0}");
            Check(top <= viewTop + .5f && bottom >= viewBottom - .5f, "Ä und g bleiben im sichtbaren Bereich des Felds");
            return string.Join("\n", checks);
        }
        catch (Exception error)
        {
            checks.Add("FAIL " + error.Message);
            return string.Join("\n", checks);
        }
        finally
        {
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
