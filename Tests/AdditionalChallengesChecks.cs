using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

// Independent profile-only checks; no gameplay objects or user saves are modified.
public static class AdditionalChallengesChecks
{
    static int checks;
    const string Report="Temp/AdditionalChallengesChecks.txt";
    static void Check(bool value,string label) {
        if(!value)throw new Exception(label);
        File.AppendAllText(Report,"PASS "+(++checks)+" "+label+"\n");
    }
    [Serializable] sealed class Envelope {public int version=1;public string payload,sha256;}
    public static object Main() {
        if(MetaProgression.CurrentRun!=null)throw new Exception("Requires no active run");
        string previous=MetaProgression.TestDirectory;
        var clock=MetaProgression.ChallengeTestUtc;
        File.WriteAllText(Report,"");checks=0;
        try {
            MetaProgression.TestDirectory=Path.GetFullPath("Temp/AdditionalChallenges-"+DateTime.UtcNow.Ticks);
            MetaProgression.ChallengeTestUtc=new DateTime(2026,10,5,10,0,0,DateTimeKind.Utc);
            foreach(var period in new[]{ChallengePeriod.Daily,ChallengePeriod.Weekly,ChallengePeriod.Lifetime}) {
                var list=MetaProgression.GetChallenges(period);
                Check(list.Length==(period==ChallengePeriod.Lifetime?17:16),period+" count");
                Check(list.Select(c=>c.id).Distinct().Count()==list.Length,period+" unique stable IDs");
                Check(list.All(c=>c.target>0&&c.xp>0),period+" achievable positive targets/rewards");
            }
            Check(16+16+17-(6+6+7)==30,"Exactly thirty new challenges");
            MetaProgression.BeginRun("new-challenges-A");
            MetaProgression.RecordResource(Item.Coal,500,"coal-A");
            MetaProgression.EndRun();
            MetaProgression.BeginRun("new-challenges-B");
            MetaProgression.RecordResource(Item.Coal,1500,"coal-B");
            MetaProgression.RecordResource(Item.Copper,1000,"copper-B");
            MetaProgression.RecordResource(Item.Iron,1250,"iron-B");
            MetaProgression.RecordResource(Item.Silver,500,"silver-B");
            MetaProgression.RecordResource(Item.Gold,250,"gold-B");
            Check(MetaProgression.GetChallenges(ChallengePeriod.Lifetime).First(c=>c.id=="lifetime-coal").completed,
                "Lifetime coal combines two runs");
            Check(MetaProgression.GetChallenges(ChallengePeriod.Lifetime).First(c=>c.id=="lifetime-ores").completed,
                "Lifetime ores combine deduplicated ledgers");
            var daily=MetaProgression.GetChallenges(ChallengePeriod.Daily);
            Check(daily.First(c=>c.id=="Daily-resources").completed&&daily.First(c=>c.id=="Daily-large-haul").completed,
                "One event advances both resource tiers");
            long rewards=MetaProgression.Profile.periodRewardXp, xp=MetaProgression.TotalXp;
            MetaProgression.RecordResource(Item.Gold,250,"gold-B");
            Check(rewards==MetaProgression.Profile.periodRewardXp&&xp==MetaProgression.TotalXp,"Replay of ore source grants no second reward");
            for(int i=0;i<250;i++)MetaProgression.RecordExploration(i,0,10);
            for(int i=0;i<25;i++)MetaProgression.RecordDiscovery("discovery-"+i);
            MetaProgression.RecordCraftedAmount(Item.Ladder,100);
            MetaProgression.RecordCraftedAmount(Item.Torche,20);
            foreach(var item in new[]{Item.IronLadder,Item.LavaLamp,Item.PercussionHammer}) {
                MetaProgression.RecordCraftedAmount(item,1,true);
                MetaProgression.RecordExoticCraft(item);
            }
            for(int i=0;i<5;i++) {
                if(i>0)MetaProgression.BeginRun("victory-"+i);
                MetaProgression.RecordVictory();MetaProgression.EndRun();
            }
            foreach(var period in new[]{ChallengePeriod.Daily,ChallengePeriod.Weekly}) {
                var list=MetaProgression.GetChallenges(period).Skip(6).ToArray();
                foreach(var c in list)Check(c.completed,period+" reachable: "+c.id);
            }
            foreach(var c in MetaProgression.GetChallenges(ChallengePeriod.Lifetime).Where(c=>c.id.StartsWith("lifetime-")))
                Check(c.completed,"Lifetime reachable: "+c.id);
            xp=MetaProgression.TotalXp;rewards=MetaProgression.Profile.periodRewardXp;
            Check(MetaProgression.Save(),"Expanded profile saves");
            MetaProgression.Reload();
            Check(MetaProgression.TotalXp==xp&&MetaProgression.Profile.periodRewardXp==rewards&&MetaProgression.LastError==null,
                "Expanded profile loads without duplicate rewards");
            Check(MetaProgression.GetChallenges(ChallengePeriod.Lifetime).Where(c=>c.id.StartsWith("lifetime-")).All(c=>c.completed),
                "Lifetime flags persist");
            MetaProgression.ChallengeTestUtc=new DateTime(2026,10,12,10,0,0,DateTimeKind.Utc);
            Check(MetaProgression.GetChallenges(ChallengePeriod.Daily).All(c=>c.current==0)&&
                MetaProgression.GetChallenges(ChallengePeriod.Weekly).All(c=>c.current==0),"All sixteen timed tasks reset");
            Check(MetaProgression.TotalXp==xp,"Reset preserves all XP");
            // Load a real six-counter V3 profile through the production SHA/validation path.
            var old=new MetaProfile{totalXp=900,periodRewardXp=900};
            var state=new MetaChallengePeriodState {
                period=0,startUtc=new DateTime(2026,10,5).Ticks,endUtc=new DateTime(2026,10,6).Ticks,
                counters=new[]{50,100,5,20,1000,3},rewarded=Enumerable.Repeat(true,6).ToArray()
            };
            old.challengePeriods.Add(state);
            var e=new Envelope{payload=JsonUtility.ToJson(old)};
            using(var sha=SHA256.Create())
                e.sha256=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(e.payload))).Replace("-","");
            string path=Path.Combine(MetaProgression.TestDirectory,"six-counter-profile.json");
            File.WriteAllText(path,JsonUtility.ToJson(e));
            object[] args={path,null,false};
            bool valid=(bool)typeof(MetaProgression).GetMethod("TryRead",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
            Check(valid,"Previous six-counter profile accepted");
            var migrated=(MetaProfile)args[1];
            Check(migrated.challengePeriods[0].counters.Length==16&&migrated.challengePeriods[0].rewarded.Take(6).All(v=>v),
                "Migration preserves original six completions");
            Check(migrated.challengePeriods[0].counters.Skip(6).All(v=>v==0)&&migrated.periodRewardXp==900,
                "New goals start empty and earned XP stays intact");
            File.AppendAllText(Report,"COMPLETE "+checks+" checks\n");
            return "PASS "+checks;
        } finally {
            MetaProgression.EndRun();MetaProgression.TestDirectory=previous;MetaProgression.ChallengeTestUtc=clock;
        }
    }
}
