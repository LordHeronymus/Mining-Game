using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float maxSpeed = 6f;          // Endgeschwindigkeit
    public float accelTime = 1f;         // Zeit von 0 -> max (Sekunden)
    public float decelTime = 2f;      // Zeit zum Abbremsen
    public float groundedCoeff = 0.3f;

    [Header("Jump")]
    public float jumpImpulse = 10f;
    public LayerMask groundLayer;

    Rigidbody2D rb;
    Collider2D col;
    float inputX;
    bool jumpRequested;

    float Accel => maxSpeed / Mathf.Max(0.0001f, accelTime);
    float Decel => maxSpeed / Mathf.Max(0.0001f, decelTime);

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
    }

    void Update()
    {
        inputX = Input.GetAxisRaw("Horizontal");       // -1,0,1
        if (Input.GetKeyDown(KeyCode.Space)) jumpRequested = true;
    }

    void FixedUpdate()
    {
        float targetVx = inputX * maxSpeed;

        float rate = (Mathf.Abs(targetVx) > Mathf.Abs(rb.linearVelocity.x)) ? Accel : Decel;
        rate *= IsGrounded() ? 1 : groundedCoeff;

        float newVx = Mathf.MoveTowards(rb.linearVelocity.x, targetVx, rate * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(newVx, rb.linearVelocity.y);



        // Jump
        if (jumpRequested && IsGrounded())
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f); // konsistenter Absprung
            rb.AddForce(Vector2.up * jumpImpulse, ForceMode2D.Impulse);
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
