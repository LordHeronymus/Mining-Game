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
    float inputY, previousGravity;
    bool flying;

    private bool moving;
    public bool IsMoving => moving;
    public float HorizontalInput => inputX;
    public bool IsFlying => flying;
    public bool IsOnGround => col && IsGrounded();


    float Accel => stats.MoveSpeed / Mathf.Max(0.0001f, accelTime);
    float Decel => stats.MoveSpeed / Mathf.Max(0.0001f, decelTime);

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
    }

    void Update()
    {
        SyncFlyMode();
        if (GameplayInputBlocker.IsBlocked)
        {
            inputX = 0;
            inputY = 0;
            jumpRequested = false;
            moving = false;
            return;
        }
        inputX = Input.GetAxisRaw("Horizontal");
        if (flying)
        {
            inputX = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            inputY = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            jumpRequested = false;
        }
        else if (Input.GetKeyDown(KeyCode.Space)) jumpRequested = true;

        moving = inputX != 0 || (flying && inputY != 0);
    }

    void FixedUpdate()
    {
        SyncFlyMode();
        if (GameplayInputBlocker.IsBlocked)
        {
            inputX = 0f;
            inputY = 0f;
            jumpRequested = false;
            moving = false;
        }
        float targetVx = inputX * stats.MoveSpeed;

        float rate = (Mathf.Abs(targetVx) > Mathf.Abs(rb.linearVelocity.x)) ? Accel : Decel;
        rate *= IsGrounded() ? 1 : groundedCoeff;

        float newVx = Mathf.MoveTowards(rb.linearVelocity.x, targetVx, rate * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(newVx, rb.linearVelocity.y);

        if (flying)
        {
            rb.linearVelocity = new Vector2(newVx, inputY * stats.MoveSpeed);
            jumpRequested = false;
            return;
        }

        // Jump
        if (jumpRequested && IsGrounded())
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f); // konsistenter Absprung
            rb.AddForce(Vector2.up * stats.JumpForce, ForceMode2D.Impulse);
        }
        jumpRequested = false;
    }

    void SyncFlyMode()
    {
        bool enabled = GameplayTestSettings.FlyMode;
        if (enabled == flying) return;
        if (enabled) { previousGravity = rb.gravityScale; rb.gravityScale = 0; }
        else rb.gravityScale = previousGravity;
        flying = enabled;
        inputY = 0; jumpRequested = false;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0);
    }

    void OnDisable()
    {
        if (flying && rb) rb.gravityScale = previousGravity;
        flying = false; inputY = 0; jumpRequested = false;
    }

    bool IsGrounded()
    {
        // Punkt direkt UNTER dem Collider, unabhängig von Rotation
        Vector2 p = (Vector2)col.bounds.center + Vector2.down * (col.bounds.extents.y + 0.02f);
        float r = 0.08f; // klein halten
        return Physics2D.OverlapCircle(p, r, groundLayer);
    }
}
