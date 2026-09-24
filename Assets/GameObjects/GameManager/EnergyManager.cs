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
    public float delta
    {
        get { return stats.MaxEnergy - energy; }
    }

    void Start()
    {
        energy = stats.MaxEnergy;
        if (slider && slider.fillRect)
        {
            var fillGraphic = slider.fillRect.GetComponent<Graphic>();
            if (fillGraphic) fillGraphic.enabled = false;
        }
    }

    void Update()
    {
        float consumption = idleConsumtion;
        if (playerMovement.IsMoving) consumption += moveConsumption;
        if (tileMiner.IsMingin) consumption += diggingConsumption;
        consumption *= consumptionMultiplier;

        if (!GameplayTestSettings.NoEnergyConsume)
            energy = Mathf.Max(energy - consumption * Time.deltaTime, 0f);

        // CompactHud owns the visible energy display. Updating the legacy Slider during
        // the UI layout pass can enqueue a recursive rebuild of its Fill image.
        if (slider && slider.gameObject.activeInHierarchy)
            slider.SetValueWithoutNotify(energy / stats.MaxEnergy);
        if (energyText && energyText.gameObject.activeInHierarchy)
            energyText.text = $"{Mathf.CeilToInt(energy).ToString()} / {stats.MaxEnergy}";
    }
}
