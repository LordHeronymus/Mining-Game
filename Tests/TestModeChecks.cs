// Run with Unity CLI in Play mode. Restores the user's test-settings file.
using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

public static class TestModeChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Call(object target,string method) => target.GetType().GetMethod(method,Private).Invoke(target,null);
    static void Check(bool ok,string message) { if(!ok)throw new Exception(message); }
    static void Reload() => typeof(GameplayTestSettings).GetMethod("ResetSession",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
    public static object Main()
    {
        string path=GameplayTestSettings.FilePath;
        byte[] original=File.Exists(path)?File.ReadAllBytes(path):null;
        var panel=UnityEngine.Object.FindFirstObjectByType<GameplayDebugPanel>();
        var movement=UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        var energy=UnityEngine.Object.FindFirstObjectByType<EnergyManager>();
        var stats=UnityEngine.Object.FindFirstObjectByType<StatsManager>();
        var rb=movement.GetComponent<Rigidbody2D>();
        float oldEnergy=energy.energy;Vector2 oldVelocity=rb.linearVelocity;
        if(!GameplayDebugPanel.IsOpen)panel.Toggle();
        panel.GetComponent<GameplayDebugWindow>().SwitchTab(true);
        var content=panel.transform.Find("Card/WindowViewport/WindowContent");
        var god=content.Find("GodMode").GetComponent<Toggle>();
        var noEnergy=content.Find("NoEnergy").GetComponent<Toggle>();
        var drainEnergy=content.Find("EnergyDrain10").GetComponent<Button>();
        var fly=content.Find("FlyMode").GetComponent<Toggle>();
        try
        {
            god.isOn=true;noEnergy.isOn=true;fly.isOn=true;
            Reload();
            Check(GameplayTestSettings.GodMode && GameplayTestSettings.NoEnergyConsume && GameplayTestSettings.FlyMode,"Toggles were not saved and reloaded.");
            Check(stats.IsInvulnerable && !stats.CanTakeDamage,"God-mode damage gate failed.");
            energy.energy=50;Call(energy,"Update");Check(energy.energy==50,"Energy consumed while disabled.");
            drainEnergy.onClick.Invoke();
            Check(Mathf.Approximately(energy.energy,50-stats.MaxEnergy*.1f),"The debug energy button did not drain 10 percentage points.");
            god.isOn=false;
            Check(!stats.IsInvulnerable && GameplayTestSettings.NoEnergyConsume && GameplayTestSettings.FlyMode,"Toggles are coupled.");
            float gravity=rb.gravityScale;
            Call(movement,"SyncFlyMode");Check(rb.gravityScale==0,"Flight gravity not disabled.");
            typeof(PlayerMovement).GetField("inputY",Private).SetValue(movement,1f);Call(movement,"FixedUpdate");
            Check(Mathf.Approximately(rb.linearVelocity.y,stats.MoveSpeed),"Upward flight failed.");
            typeof(PlayerMovement).GetField("inputY",Private).SetValue(movement,-1f);Call(movement,"FixedUpdate");
            Check(Mathf.Approximately(rb.linearVelocity.y,-stats.MoveSpeed),"Downward flight failed.");
            typeof(PlayerMovement).GetField("inputY",Private).SetValue(movement,0f);Call(movement,"FixedUpdate");
            Check(rb.linearVelocity.y==0,"Idle flight must hover.");
            fly.isOn=false;Call(movement,"SyncFlyMode");Check(rb.gravityScale==gravity,"Original gravity was not restored.");
            noEnergy.isOn=false;Call(energy,"Update");Check(energy.energy<50,"Energy consumption did not resume.");
            Check(movement.GetComponent<Collider2D>().enabled,"Flight disabled collisions.");
            return new {passed=true,persistence=true,independentToggles=true,energy=true,flight=true,godGate=true};
        }
        finally
        {
            if(original!=null)File.WriteAllBytes(path,original);else if(File.Exists(path))File.Delete(path);
            Reload();Call(movement,"SyncFlyMode");energy.energy=oldEnergy;rb.linearVelocity=oldVelocity;
            panel.GetComponent<GameplayDebugWindow>().SwitchTab(false);
            panel.GetComponent<GameplayDebugWindow>().SwitchTab(true);
        }
    }
}
