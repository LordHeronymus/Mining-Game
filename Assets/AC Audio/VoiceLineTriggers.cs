using System;

// One-shot threshold latch: fires once when value crosses threshold (falling: goes <= threshold; rising: goes > threshold) while
// armed; re-arms after value moved back beyond threshold +/- rearmMargin. Rebaseline sets armed from the current value without firing.
public sealed class VoiceThresholdLatch
{
    readonly bool falling;
    bool armed;

    public VoiceThresholdLatch(bool falling) => this.falling = falling;

    public bool Armed => armed;

    public void Rebaseline(double value, double threshold) => armed = falling ? value > threshold : value <= threshold;

    public bool Step(double value, double threshold, double rearmMargin)
    {
        rearmMargin = Math.Max(0.0, rearmMargin);
        if (falling)
        {
            if (armed && value <= threshold) { armed = false; return true; }
            if (!armed && value > threshold + rearmMargin) armed = true;
        }
        else
        {
            if (armed && value > threshold) { armed = false; return true; }
            if (!armed && value <= threshold - rearmMargin) armed = true;
        }
        return false;
    }

    // Disarms without firing (the value must move back beyond the re-arm margin first).
    internal void Consume() => armed = false;
}

// Watchers keep a fired event OPEN until the player acknowledges it, so a warning that could not be spoken yet (busy, global pause,
// lower priority, expired) is re-submitted on the next poll. Open is dropped automatically when the condition no longer holds
// (value back above threshold for energy / below for carry).
public sealed class VoiceEnergyWatcher
{
    readonly VoiceThresholdLatch low = new(true), critical = new(true);
    bool hasBaseline;
    VoiceLineEvent? open;

    public bool HasBaseline => hasBaseline;
    public VoiceLineEvent? Open => open;

    public void Rebaseline(double fraction, double low, double critical)
    {
        this.low.Rebaseline(fraction, low);
        this.critical.Rebaseline(fraction, critical);
        hasBaseline = true;
    }

    public void Invalidate() { hasBaseline = false; open = null; }

    public VoiceLineEvent? Step(double fraction, double low, double critical, double rearmMargin)
    {
        if (!hasBaseline) { Rebaseline(fraction, low, critical); return null; }
        bool c = this.critical.Step(fraction, critical, rearmMargin), l = this.low.Step(fraction, low, rearmMargin);
        if (c) { open = VoiceLineEvent.EnergyCritical; this.low.Consume(); }
        else if (l && open != VoiceLineEvent.EnergyCritical) open = VoiceLineEvent.EnergyLow;
        if (open == VoiceLineEvent.EnergyLow && fraction > low) open = null;
        else if (open == VoiceLineEvent.EnergyCritical && fraction > critical) open = null;
        return open;
    }

    public void Acknowledge(VoiceLineEvent e) { if (open == e) open = null; }
}

// Rising latches on ratio = weight / capacity.
public sealed class VoiceCarryWatcher
{
    // Same stages as InventoryManager.CarryStage / inventory colours; stage 2 follows GPS Bewegung → Traglast.
    public const double Stage1Ratio = 1.0;
    public static double Stage2Ratio => InventoryManager.RedStageRatio;
    readonly VoiceThresholdLatch stage1 = new(false), stage2 = new(false);
    bool hasBaseline;
    VoiceLineEvent? open;

    public bool HasBaseline => hasBaseline;
    public VoiceLineEvent? Open => open;

    public void Rebaseline(double ratio)
    {
        stage1.Rebaseline(ratio, Stage1Ratio);
        stage2.Rebaseline(ratio, Stage2Ratio);
        hasBaseline = true;
    }

    public void Invalidate() { hasBaseline = false; open = null; }

    public VoiceLineEvent? Step(double ratio, double rearmMargin)
    {
        if (!hasBaseline) { Rebaseline(ratio); return null; }
        bool s2 = stage2.Step(ratio, Stage2Ratio, rearmMargin), s1 = stage1.Step(ratio, Stage1Ratio, rearmMargin);
        if (s2) { open = VoiceLineEvent.CarryStage2; stage1.Consume(); }
        else if (s1 && open != VoiceLineEvent.CarryStage2) open = VoiceLineEvent.CarryStage1;
        if (open == VoiceLineEvent.CarryStage1 && ratio <= Stage1Ratio) open = null;
        else if (open == VoiceLineEvent.CarryStage2 && ratio <= Stage2Ratio) open = null;
        return open;
    }

    public void Acknowledge(VoiceLineEvent e) { if (open == e) open = null; }
}
