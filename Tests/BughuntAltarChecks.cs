using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Altar chamber (R2-altar-1): the deposit button stays in view and in the dome for every GPS altar size.
public static class BughuntAltarChecks
{
    const float CellSize = 1.1f; // SampleScene block grid
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        try
        {
            // Deposit button: follows the altar at small sizes, capped inside the dome and in view at large GPS sizes.
            var buttonMethod = typeof(UltroniumAltarChamber).GetMethod("DepositButtonY", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Check(buttonMethod != null, "UltroniumAltarChamber.DepositButtonY vorhanden");
            float ButtonY(float scale) => (float)buttonMethod.Invoke(null, new object[] { CellSize, scale });
            string altarSource = File.ReadAllText("Assets/GameObjects/Map/UltroniumAltarChamber.cs");
            Check(altarSource.Contains("AltarPosition+Vector3.up*DepositButtonY(CellSize,AltarScale)"), "BuildButton setzt den Knopf über DepositButtonY");
            Check(Mathf.Abs(ButtonY(1f) - CellSize * 4.4f) < .0001f, "Standard-Altargrösse 1: Knopfhöhe unverändert");
            float unit = CellSize / 110f, previous = float.MinValue;
            foreach (float scale in new[] { .25f, .5f, 1f, 1.25f, 1.5f, 2f, 2.23f, 2.5f, 2.7f, 3f })
            {
                float y = ButtonY(scale);
                float buttonBottom = y - 61f * unit, labelTop = y + 69f * unit; // button 82 px at -20 px, charge label 28 px at +55 px
                Check(y >= previous - .0001f, "Knopf steigt mit der Altargrösse nicht ab (" + scale + ")");
                Check(buttonBottom > 0f, "Knopf über dem Kammerboden (" + scale + ")");
                Check(labelTop <= UltroniumChamberLayout.Height * CellSize, "Knopf und Ladeanzeige innerhalb der Kuppel (" + scale + ")");
                // Player at the altar: centre 0.43 above the floor; camera centred on the player.
                foreach (float halfHeight in new[] { 10f, 7f })
                    Check(labelTop <= .43f + halfHeight, "Knopf bei halber Bildhöhe " + halfHeight + " sichtbar (" + scale + ", Oberkante " + labelTop.ToString("0.00") + ")");
                previous = y;
            }

            string report = string.Join("\n", checks);
            File.WriteAllText("Temp/BughuntAltarChecks.txt", report); return "PASS (" + checks.Count + ")";
        }
        catch (Exception ex) { File.WriteAllText("Temp/BughuntAltarChecks.txt", "FAIL " + ex + "\n" + string.Join("\n", checks)); throw; }
    }
}
