using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
public static class ChallengeMigrationChecks {
 [Serializable] class Envelope {public int version=1; public string payload,sha256;}
 public static object Main(){
  {
   string directory=Path.GetFullPath("Temp/ChallengeLegacy-"+DateTime.UtcNow.Ticks);
   Directory.CreateDirectory(directory);
   // An actual version-1 payload with none of the new period fields.
   string payload="{\"version\":1,\"revision\":1,\"totalXp\":250,\"upgrades\":[],\"completedMilestones\":[\"depth-100\"],\"runs\":[]}";
   using var sha=SHA256.Create();
   var e=new Envelope{payload=payload,sha256=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(payload))).Replace("-","")};
   File.WriteAllText(Path.Combine(directory,"meta-progression.json"),JsonUtility.ToJson(e));
   var read=typeof(MetaProgression).GetMethod("TryRead",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
   object[] args={Path.Combine(directory,"meta-progression.json"),null,false};
   if(!(bool)read.Invoke(null,args))throw new Exception("Legacy read");
   var profile=(MetaProfile)args[1];
   if(profile.totalXp!=250)throw new Exception("Legacy XP");
   if(!profile.completedMilestones.Contains("depth-100"))throw new Exception("Legacy milestone");
   if(profile.periodRewardXp!=0||profile.challengePeriods.Count!=0)throw new Exception("New fields initialize empty");
   e.payload=JsonUtility.ToJson(profile);
   e.sha256=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(e.payload))).Replace("-","");
   File.WriteAllText(Path.Combine(directory,"meta-progression.json"),JsonUtility.ToJson(e));
   args[1]=null;if(!(bool)read.Invoke(null,args)||((MetaProfile)args[1]).totalXp!=250)throw new Exception("Migrated roundtrip");
   File.WriteAllText("Temp/ChallengeMigrationChecks.txt","PASS 5: legacy read, XP, milestone, empty period fields, serialized roundtrip");
   return "PASS 5";
  }
 }
}
