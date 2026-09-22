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
    [Range(0f,.5f)] public float automaticStepHeight=.30f;

    Rigidbody2D rb;
    Collider2D col;
    PlayerLadder ladder;
    readonly ContactPoint2D[] groundContacts = new ContactPoint2D[8];
    float inputX;
    bool jumpRequested;
    float inputY, previousGravity;
    bool flying;
    float previousRunVelocity;

    private bool moving;
    public bool IsMoving => moving;
    public float HorizontalInput => inputX;
    public bool IsFlying => flying;
    public bool IsClimbing => ladder && ladder.IsClimbing;
    public bool IsOnGround => col && IsGrounded();


    float Accel => stats.MoveSpeed / Mathf.Max(0.0001f, accelTime);
    float Decel => stats.MoveSpeed / Mathf.Max(0.0001f, decelTime);

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        ladder = GetComponent<PlayerLadder>();
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
        inputY = Input.GetAxisRaw("Vertical");
        if (flying)
        {
            inputX = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            inputY = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            jumpRequested = false;
        }
        else if (Input.GetKeyDown(KeyCode.Space)) jumpRequested = true;

        moving = inputX != 0 || ((flying || IsClimbing) && inputY != 0);
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
        if (ladder && ladder.enabled && ladder.Step(inputX, inputY, jumpRequested, flying))
        {
            previousRunVelocity=0;
            jumpRequested = false;
            return;
        }
        float targetVx = inputX * stats.MoveSpeed;

        bool grounded=IsGrounded();
        bool supported=grounded||(!flying&&rb.linearVelocity.y<=.1f&&PlayerStepUp.HasSupport(rb,col,groundLayer));
        float runVelocity=rb.linearVelocity.x;
        // Preserve motor acceleration while supported: contact friction must not
        // reset the acceleration ramp every physics tick and pin the player in place.
        if(supported&&inputX*previousRunVelocity>0&&Mathf.Abs(previousRunVelocity)>Mathf.Abs(runVelocity))
            runVelocity=previousRunVelocity;
        float rate = (Mathf.Abs(targetVx) > Mathf.Abs(runVelocity)) ? Accel : Decel;
        rate *= grounded ? 1 : groundedCoeff;

        float newVx = Mathf.MoveTowards(runVelocity, targetVx, rate * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(newVx, rb.linearVelocity.y);

        if (flying)
        {
            previousRunVelocity=0;
            rb.linearVelocity = new Vector2(newVx, inputY * stats.MoveSpeed);
            jumpRequested = false;
            return;
        }

        if(supported&&!jumpRequested&&!GameplayInputBlocker.IsBlocked&&Mathf.Abs(inputX)>.01f&&
            newVx*inputX>0&&rb.linearVelocity.y<=.1f)
        {
            PlayerStepUp.TryStep(rb,col,inputX,newVx,automaticStepHeight,groundLayer);
        }
        previousRunVelocity=rb.linearVelocity.x;

        // Jump
        if (jumpRequested && grounded)
        {
            previousRunVelocity=0;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f); // konsistenter Absprung
            rb.AddForce(Vector2.up * stats.JumpForce, ForceMode2D.Impulse);
        }
        jumpRequested = false;
    }

    void SyncFlyMode()
    {
        bool enabled = GameplayTestSettings.FlyMode;
        if (enabled == flying) return;
        if (enabled) { if (ladder) ladder.Detach(); previousGravity = rb.gravityScale; rb.gravityScale = 0; }
        else rb.gravityScale = previousGravity;
        flying = enabled;
        inputY = 0; jumpRequested = false;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0);
    }

    void OnDisable()
    {
        if (ladder) ladder.Detach();
        if (flying && rb) rb.gravityScale = previousGravity;
        flying = false; inputY = 0; jumpRequested = false;
        previousRunVelocity=0;
    }

    bool IsGrounded()
    {
        int count = col.GetContacts(groundContacts);
        int mask = groundLayer.value;
        for (int i = 0; i < count; i++)
        {
            var contact = groundContacts[i];
            var surface = contact.collider;
            if (surface && (mask & (1 << surface.gameObject.layer)) != 0 && contact.normal.y > 0.5f)
                return true;
        }
        return false;
    }
}
