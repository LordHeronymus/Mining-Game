using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

// Pipeline run_script, entry LegacyMetaSerializationChecks.Main. No scene/play/profile changes.
public static class LegacyMetaSerializationChecks
{
    static int checks;
    static readonly MethodInfo Parser = typeof(GameSaveSystem).GetMethod("ParseRunState", BindingFlags.NonPublic | BindingFlags.Static);
    static RunSaveState Parse(string json)
    {
        if (Parser == null) throw new Exception("Missing optional-section parser.");
        try { return (RunSaveState)Parser.Invoke(null, new object[] { json }); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }
    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        checks++; File.AppendAllText("Temp/LegacyMetaSerializationChecks.txt", "PASS " + label + "\n");
    }
    static void Reject(string json, string label)
    {
        bool rejected = false;
        try { Parse(json); } catch (Exception) { rejected = true; }
        Check(rejected, label);
    }
    public static object Main()
    {
        Directory.CreateDirectory("Temp"); File.WriteAllText("Temp/LegacyMetaSerializationChecks.txt", ""); checks = 0;
        try {
            var absent = Parse("{\"seed\":1,\"width\":100,\"height\":100}");
            Check(absent.progression == null, "missing progression remains absent despite Unity inline initialization");
            Check(absent.exotics == null && absent.placedLights == null, "missing world additions remain absent");
            Check(MetaProgression.ValidateRunState(absent.progression), "legacy progression passes optional validation");
            var explicitNull = Parse("{\"progression\":null,\"exotics\":null,\"placedLights\":null}");
            Check(explicitNull.progression == null && explicitNull.exotics == null && explicitNull.placedLights == null, "explicit optional nulls retained");
            var nested = Parse("{\"inventory\":{\"progression\":{}},\"stats\":{\"exotics\":{}},\"name\":\"\\\"placedLights\\\":[]\"}");
            Check(nested.progression == null && nested.exotics == null && nested.placedLights == null, "nested fields and quoted text never count as root sections");
            var valid = new RunSaveState { progression = new MetaRunState { runId = "valid-run" },
                exotics = new ExoticWorldState { caches = Array.Empty<SavedBlueprintCache>(), boulders = Array.Empty<SavedBoulder>() },
                placedLights = Array.Empty<SavedPlacedLight>() };
            string validJson = JsonUtility.ToJson(valid);
            var present = Parse(validJson);
            Check(present.progression != null && MetaProgression.ValidateRunState(present.progression), "valid explicit progression preserved");
            Check(present.exotics != null && present.placedLights != null && present.placedLights.Length == 0, "valid explicit empty world state and lights preserved");
            var escaped = Parse(validJson.Replace("\"progression\"", "\"\\u0070rogression\"").Replace("\"exotics\"", "\"\\u0065xotics\""));
            Check(escaped.progression != null && MetaProgression.ValidateRunState(escaped.progression) && escaped.exotics != null, "escaped root property names decoded correctly");
            var malformed = Parse("{\"progression\":{}}");
            Check(malformed.progression != null && !MetaProgression.ValidateRunState(malformed.progression), "explicit empty progression cannot masquerade as legacy absence");
            var future = Parse("{\"progression\":{\"version\":2,\"runId\":\"future\"}}");
            Check(future.progression != null && !MetaProgression.ValidateRunState(future.progression), "future progression still rejected by semantic validator");
            var futureExotics = Parse("{\"exotics\":{\"version\":2}}");
            bool rejectedExotics = false;
            try { ExoticWorldContent.ValidateState(futureExotics.exotics, 100, 100); } catch (InvalidDataException) { rejectedExotics = true; }
            Check(rejectedExotics, "future exotic section not normalized away");
            var torchLegacy = Parse("{\"torches\":[{\"x\":1,\"y\":-3,\"z\":0}]}");
            Check(torchLegacy.placedLights == null && torchLegacy.torches.Length == 1 && torchLegacy.torches[0] == new Vector3Int(1, -3, 0), "old torch array survives for light fallback");
            Reject("{\"progression\":[]}", "wrong progression token rejected");
            Reject("{\"exotics\":\"empty\"}", "wrong exotic token rejected");
            Reject("{\"placedLights\":{}}", "wrong lights token rejected");
            Reject("{\"progression\":null,\"\\u0070rogression\":{}}", "duplicate decoded root properties rejected");
            Reject("{\"progression\":{", "truncated JSON rejected");
            Reject("{}{}", "trailing JSON root rejected");
            Reject("[]", "nonobject root rejected");
            var fixture = Parse(ReadFixture("Temp/SelectionEdit-639266992421592508/slot-3.thsave"));
            Check(fixture.width > 0 && fixture.height > 0 && fixture.inventory != null && fixture.stats != null, "actual old save metadata parsed intact");
            Check(fixture.progression == null && fixture.exotics == null && fixture.placedLights == null, "actual pre-progression save retains all three optional absences");
            File.AppendAllText("Temp/LegacyMetaSerializationChecks.txt", "PASS " + checks + " checks\n");
            return new { passed = checks };
        } catch (Exception error) { File.AppendAllText("Temp/LegacyMetaSerializationChecks.txt", "FAIL " + error + "\n"); throw; }
    }
    static string ReadFixture(string path)
    {
        using var file = File.OpenRead(path);
        using var reader = new BinaryReader(file, Encoding.UTF8, true);
        if (reader.ReadInt32() != 0x54484631) throw new InvalidDataException("Fixture magic");
        string header = reader.ReadString(); int size = reader.ReadInt32();
        byte[] checksum = reader.ReadBytes(32), packed = reader.ReadBytes(size);
        using (var sha = SHA256.Create()) {
            byte[] prefix = Encoding.UTF8.GetBytes(header);
            sha.TransformBlock(prefix, 0, prefix.Length, null, 0); sha.TransformFinalBlock(packed, 0, packed.Length);
            for (int i = 0; i < checksum.Length; i++) if (checksum[i] != sha.Hash[i]) throw new InvalidDataException("Fixture checksum");
        }
        using var input = new MemoryStream(packed);
        using var zip = new GZipStream(input, CompressionMode.Decompress);
        using var world = new BinaryReader(zip, Encoding.UTF8, true);
        return world.ReadString();
    }
}
