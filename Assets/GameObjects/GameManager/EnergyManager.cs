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
    }

    void Update()
    {
        float consumption = idleConsumtion;
        if (playerMovement.IsMoving) consumption += moveConsumption;
        if (tileMiner.IsMingin) consumption += diggingConsumption;

        if (energy <= 0f) return;
        energy = Mathf.Max(energy - consumption * Time.deltaTime, 0f);

        slider.value = energy / stats.MaxEnergy;
        energyText.text = $"{Mathf.CeilToInt(energy).ToString()} / {stats.MaxEnergy}";
    }
}
