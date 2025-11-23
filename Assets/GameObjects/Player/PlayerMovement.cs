using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    [SerializeField] StatsManager stats;
    [SerializeField] LayerMask groundLayer;

    [Header("Movement")]
    public float accelTime = 1f;         // Zeit von 0 -> max (Sekunden)
    public float decelTime = 2f;      // Zeit zum Abbremsen
    public float groundedCoeff = 0.3f;

    Rigidbody2D rb;
    Collider2D col;
    float inputX;
    bool jumpRequested;

    private bool moving;
    public bool IsMoving => moving;


    float Accel => stats.MoveSpeed / Mathf.Max(0.0001f, accelTime);
    float Decel => stats.MoveSpeed / Mathf.Max(0.0001f, decelTime);

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
    }

    void Update()
    {
        inputX = Input.GetAxisRaw("Horizontal");
        if (Input.GetKeyDown(KeyCode.Space)) jumpRequested = true;

        moving = inputX != 0;
    }

    void FixedUpdate()
    {
        float targetVx = inputX * stats.MoveSpeed;

        float rate = (Mathf.Abs(targetVx) > Mathf.Abs(rb.linearVelocity.x)) ? Accel : Decel;
        rate *= IsGrounded() ? 1 : groundedCoeff;

        float newVx = Mathf.MoveTowards(rb.linearVelocity.x, targetVx, rate * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(newVx, rb.linearVelocity.y);

        // Jump
        if (jumpRequested && IsGrounded())
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f); // konsistenter Absprung
            rb.AddForce(Vector2.up * stats.JumpForce, ForceMode2D.Impulse);
        }
        jumpRequested = false;
    }

    bool IsGrounded()
    {
        // Punkt direkt UNTER dem Collider, unabhängig von Rotation
        Vector2 p = (Vector2)col.bounds.center + Vector2.down * (col.bounds.extents.y + 0.02f);
        float r = 0.08f; // klein halten
        return Physics2D.OverlapCircle(p, r, groundLayer);
    }
}
