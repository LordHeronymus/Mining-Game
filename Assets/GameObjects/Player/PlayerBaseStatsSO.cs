using UnityEngine;

[CreateAssetMenu(fileName = "PlayerBaseStats", menuName = "PlayerBaseStats")]
public class PlayerBaseStats : ScriptableObject
{
    public float moveSpeed = 7f;
    public float jumpForce = 10f;
    public float miningSpeed = 1.5f;
    public float reach = 2.0f;
    public float maxEnergy = 100f;
}
