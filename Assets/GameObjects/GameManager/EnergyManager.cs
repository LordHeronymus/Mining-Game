using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EnergyManager : MonoBehaviour
{
    public StatsManager stats;
    public PlayerMovement playerMovement;
    public TileMiner tileMiner;
    public Slider slider;
    public TextMeshProUGUI energyText;

    public float idleConsumtion = 1f;
    [Min(0f)] public float consumptionMultiplier = 1f;
    public float moveConsumption = 2f;
    public float diggingConsumption = 3f;

    public float energy;
    AudioClip lowEnergyBeep;
    AudioSource lowEnergySource;
    float nextLowEnergyBeepTime;
    bool lowEnergyWarningActive;

    const float LowEnergyWarningThreshold = .15f;
    const float LowEnergyFastThreshold = .01f;
    public float delta
    {
        get { return stats.MaxEnergy - energy; }
    }

    void Start()
    {
        ResetForNewRun();
        lowEnergyBeep = Resources.Load<AudioClip>("Audio/LowEnergyBip");
        if (lowEnergyBeep)
        {
            lowEnergySource = gameObject.AddComponent<AudioSource>();
            lowEnergySource.playOnAwake = false;
            lowEnergySource.spatialBlend = 0f;
            lowEnergySource.clip = lowEnergyBeep;
        }
        if (slider && slider.fillRect)
        {
            var fillGraphic = slider.fillRect.GetComponent<Graphic>();
            if (fillGraphic) fillGraphic.enabled = false;
        }
    }

    void Update()
    {
        if (LoadingProgress.Active)
        {
            if (lowEnergySource && lowEnergySource.isPlaying) lowEnergySource.Stop();
            return;
        }
        // The stats object can survive a scene transition; its scene references cannot.
        if (!stats) stats = StatsManager.Instance;
        if (!playerMovement) playerMovement = FindFirstObjectByType<PlayerMovement>();
        if (!tileMiner) tileMiner = FindFirstObjectByType<TileMiner>();
        if (!stats || !playerMovement || !tileMiner) return;
        stats.RefreshEnergyCapacity();
        energy = Mathf.Min(energy, stats.MaxEnergy);
        float consumption = idleConsumtion;
        if (playerMovement.IsMoving) consumption += moveConsumption;
        if (tileMiner.IsMingin) consumption += diggingConsumption;
        consumption *= consumptionMultiplier;

        if (!GameplayTestSettings.NoEnergyConsume)
            energy = Mathf.Max(energy - consumption * Time.deltaTime, 0f);

        if (energy <= 0f && !GameOverPanel.IsOpen && !GameVictoryPanel.IsOpen)
            FindFirstObjectByType<GameOverPanel>(FindObjectsInactive.Include)?.Show();

        UpdateLowEnergyWarning();

        // CompactHud owns the visible energy display. Updating the legacy Slider during
        // the UI layout pass can enqueue a recursive rebuild of its Fill image.
        if (slider && slider.gameObject.activeInHierarchy)
            slider.SetValueWithoutNotify(energy / stats.MaxEnergy);
        if (energyText && energyText.gameObject.activeInHierarchy)
            energyText.text = $"{Mathf.CeilToInt(energy).ToString()} / {stats.MaxEnergy}";
    }

    public void DrainEnergy(float amount)
    {
        energy = Mathf.Max(0f, energy - Mathf.Max(0f, amount));
    }

    public void ResetForNewRun()
    {
        if (!stats) stats = StatsManager.Instance;
        if (!stats) return;
        stats.RefreshEnergyCapacity();
        energy = stats.MaxEnergy;
        lowEnergyWarningActive = false;
    }

    void UpdateLowEnergyWarning()
    {
        float fraction = stats.MaxEnergy > 0f ? energy / stats.MaxEnergy : 0f;
        if (!lowEnergySource || fraction <= 0f || fraction > LowEnergyWarningThreshold ||
            GameOverPanel.IsOpen || Time.timeScale == 0f)
        {
            lowEnergyWarningActive = false;
            if (lowEnergySource && lowEnergySource.isPlaying) lowEnergySource.Stop();
            return;
        }

        float urgency = Mathf.InverseLerp(LowEnergyWarningThreshold, LowEnergyFastThreshold, fraction);
        float interval = 1f / Mathf.Lerp(2f, 5f, urgency);
        if (!lowEnergyWarningActive)
        {
            nextLowEnergyBeepTime = Time.time;
            lowEnergyWarningActive = true;
        }

        if (Time.time < nextLowEnergyBeepTime) return;
        lowEnergySource.pitch = AudioManager.TunedPitch(lowEnergyBeep,
            Mathf.Lerp(1f, 1.25f, urgency));
        lowEnergySource.volume = AudioManager.TunedVolume(lowEnergyBeep, 1f);
        lowEnergySource.Play();
        nextLowEnergyBeepTime = Time.time + interval;
    }
}
