using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerMovement : MonoBehaviour
{
    const float JumpBufferDuration = .12f;
    const float JumpGraceDuration = .08f;

    [Header("References")]
    [SerializeField] StatsManager stats;
    [SerializeField] LayerMask groundLayer;

    [Header("Movement")]
    public float accelTime = 1f;         // Zeit von 0 -> max (Sekunden)
    public float decelTime = 2f;      // Zeit zum Abbremsen
    public float directionChangeDecelTime = .25f;
    public float groundedCoeff = 0.3f;

    Rigidbody2D rb;
    Collider2D col;
    PhysicsMaterial2D originalColliderMaterial;
    PhysicsMaterial2D noFrictionMaterial;
    PlayerLadder ladder;
    MapGenerator jumpMap;
    readonly ContactPoint2D[] groundContacts = new ContactPoint2D[8];
    float inputX;
    bool jumpRequested;
    float jumpBufferRemaining;
    float jumpGraceRemaining;
    float inputY, previousGravity;
    bool flying;
    bool noClip;
    bool colliderWasEnabled;
    float previousRunVelocity;
    bool hasGroundedForFall;
    bool trackingFall;
    float fallPeakY;

    private bool moving;
    public bool IsMoving => moving;
    public float HorizontalInput => inputX;
    public bool IsFlying => flying;
    public bool IsClimbing => ladder && ladder.IsClimbing;
    public bool IsOnGround => col && IsGrounded();


    float Accel => stats.EffectiveMoveSpeed / Mathf.Max(0.0001f, accelTime);
    float Decel => stats.EffectiveMoveSpeed / Mathf.Max(0.0001f, decelTime);
    float DirectionChangeDecel => stats.EffectiveMoveSpeed / Mathf.Max(0.0001f, directionChangeDecelTime);

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        colliderWasEnabled = col && col.enabled;
        ladder = GetComponent<PlayerLadder>();
        jumpMap = Object.FindFirstObjectByType<MapGenerator>();
        if (col)
        {
            originalColliderMaterial = col.sharedMaterial;
            noFrictionMaterial = new PhysicsMaterial2D("Player No Friction")
            {
                friction = 0f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine2D.Minimum,
                bounceCombine = PhysicsMaterialCombine2D.Minimum,
                hideFlags = HideFlags.HideAndDontSave
            };
            col.sharedMaterial = noFrictionMaterial;
        }
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
        else if (Input.GetKeyDown(KeyCode.Space))
        {
            jumpRequested = true;
            jumpBufferRemaining = JumpBufferDuration;
        }

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
            jumpBufferRemaining = 0f;
            moving = false;
        }
        if (ladder && ladder.enabled && ladder.Step(inputX, inputY, jumpRequested, flying))
        {
            previousRunVelocity=0;
            jumpRequested = false;
            jumpBufferRemaining = 0f;
            jumpGraceRemaining = 0f;
            PauseFallTrackingForLadder();
            return;
        }
        float targetVx = inputX * stats.EffectiveMoveSpeed;

        bool grounded=IsGrounded();
        TrackLanding(grounded);
        bool supported=grounded;
        if(supported&&rb.linearVelocity.y<=.1f)jumpGraceRemaining=JumpGraceDuration;
        else jumpGraceRemaining=Mathf.Max(0f,jumpGraceRemaining-Time.fixedDeltaTime);
        bool jumpBuffered=jumpBufferRemaining>0f;
        jumpBufferRemaining=Mathf.Max(0f,jumpBufferRemaining-Time.fixedDeltaTime);
        float runVelocity=rb.linearVelocity.x;
        // Preserve motor acceleration while supported: contact friction must not
        // reset the acceleration ramp every physics tick and pin the player in place.
        if(supported&&inputX*previousRunVelocity>0&&Mathf.Abs(previousRunVelocity)>Mathf.Abs(runVelocity))
            runVelocity=previousRunVelocity;
        bool reversing = inputX * runVelocity < -0.0001f;
        float rate = reversing ? DirectionChangeDecel : (Mathf.Abs(targetVx) > Mathf.Abs(runVelocity) ? Accel : Decel);
        rate *= grounded ? 1 : groundedCoeff;

        float newVx = Mathf.MoveTowards(runVelocity, targetVx, rate * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(newVx, rb.linearVelocity.y);

        if (flying)
        {
            previousRunVelocity=0;
            rb.linearVelocity = new Vector2(newVx, inputY * stats.EffectiveMoveSpeed);
            jumpRequested = false;
            return;
        }

        previousRunVelocity=rb.linearVelocity.x;

        // Jump
        if (jumpBuffered && (supported || jumpGraceRemaining > 0f))
        {
            previousRunVelocity=0;
            jumpBufferRemaining=0f;
            jumpGraceRemaining=0f;
            LaunchJump();
        }
        jumpRequested = false;
    }

    public void LaunchJump()
    {
        if (!jumpMap) jumpMap = Object.FindFirstObjectByType<MapGenerator>();
        float blockHeight = jumpMap && jumpMap.Terrain
            ? Mathf.Abs((jumpMap.Terrain.CellToWorld(Vector3Int.up) - jumpMap.Terrain.CellToWorld(Vector3Int.zero)).y) : 1f;
        float height = Mathf.Max(0f, Mathf.Clamp(stats.JumpHeightBlocks, 0f, 100f) * blockHeight - TakeoffClearance());
        float speed = CalculateJumpSpeed(height, -Physics2D.gravity.y * rb.gravityScale, rb.linearDamping, Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, speed);
    }

    float TakeoffClearance()
    {
        // Physics keeps a small contact gap. Measure from the actual supporting
        // surface, not from that hovering rest position, when launching on ground.
        var feet = col.bounds;
        var physics = gameObject.scene.GetPhysicsScene2D();
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < 3; i++)
        {
            float x = Mathf.Lerp(feet.min.x + .01f, feet.max.x - .01f, i * .5f);
            var hit = physics.Raycast(new Vector2(x, feet.min.y + .02f), Vector2.down, .07f, groundLayer);
            if (hit.collider && hit.normal.y > .5f)
                nearest = Mathf.Min(nearest, Mathf.Max(0f, feet.min.y - hit.point.y));
        }
        return float.IsPositiveInfinity(nearest) ? 0f : nearest;
    }

    // Include fixed-step gravity and damping to match the actual physics apex.
    public static float CalculateJumpSpeed(float height, float gravity, float damping, float step)
    {
        if (!(height > 0f) || !(gravity > 0f) || !(step > 0f) || float.IsInfinity(height)) return 0f;
        float low = 0f;
        float high = Mathf.Sqrt(2f * gravity * height) + gravity * step + Mathf.Max(0f, damping) * height;
        for (int i = 0; i < 16 && JumpRise(high, gravity, damping, step) < height; i++) high *= 2f;
        for (int i = 0; i < 28; i++)
        {
            float speed = (low + high) * .5f;
            if (JumpRise(speed, gravity, damping, step) < height) low = speed;
            else high = speed;
        }
        return (low + high) * .5f;
    }

    static float JumpRise(float speed, float gravity, float damping, float step)
    {
        float rise = 0f;
        float decay = 1f / (1f + Mathf.Max(0f, damping) * step);
        for (int i = 0; i < 10000; i++)
        {
            speed = (speed - gravity * step) * decay;
            if (speed <= 0f) break;
            rise += speed * step;
        }
        return rise;
    }

    void TrackLanding(bool grounded)
    {
        if (flying || noClip || !col || !col.enabled)
        {
            ResetFallTracking();
            return;
        }

        if (grounded)
        {
            if (trackingFall && stats)
            {
                float fallHeightWorld = Mathf.Max(0f, fallPeakY - col.bounds.min.y);
                float blockHeight = GetBlockHeightWorld();
                float fallHeightBlocks = fallHeightWorld / blockHeight;
                float damage = StatsManager.CalculateFallDamage(fallHeightBlocks,
                    stats.fallDamageHeightBlocks, stats.fallDamageBase, stats.fallDamageExponent);
                if (damage > 0f)
                {
                    float healthBeforeDamage = stats.Health;
                    if (stats.ApplyDamage(damage) && healthBeforeDamage - stats.Health > 30f)
                        AudioManager.Instance?.Play(SoundType.BoneBreaking1);
                }
            }

            trackingFall = false;
            hasGroundedForFall = true;
            return;
        }

        if (!hasGroundedForFall) return;
        float feetY = col.bounds.min.y;
        if (!trackingFall)
        {
            trackingFall = true;
            fallPeakY = feetY;
        }
        else fallPeakY = Mathf.Max(fallPeakY, feetY);
    }

    float GetBlockHeightWorld()
    {
        if (!jumpMap) jumpMap = Object.FindFirstObjectByType<MapGenerator>();
        if (!jumpMap || !jumpMap.Terrain) return 1f;
        return Mathf.Max(0.0001f, Mathf.Abs((jumpMap.Terrain.CellToWorld(Vector3Int.up) -
            jumpMap.Terrain.CellToWorld(Vector3Int.zero)).y));
    }

    void PauseFallTrackingForLadder()
    {
        trackingFall = false;
        hasGroundedForFall = true;
    }
    void ResetFallTracking()
    {
        trackingFall = false;
        hasGroundedForFall = false;
    }
    void SyncFlyMode()
    {
        bool requestedNoClip = GameplayTestSettings.NoClipMode;
        if (requestedNoClip != noClip)
        {
            if (requestedNoClip)
            {
                colliderWasEnabled = col && col.enabled;
                if (col) col.enabled = false;
                if (ladder) ladder.Detach();
            }
            else if (col) col.enabled = colliderWasEnabled;
            noClip = requestedNoClip;
        }
        bool enabled = GameplayTestSettings.FlyMode || requestedNoClip;
        if (enabled == flying) return;
        if (enabled) { if (ladder) ladder.Detach(); previousGravity = rb.gravityScale; rb.gravityScale = 0; }
        else rb.gravityScale = previousGravity;
        flying = enabled;
        inputY = 0; jumpRequested = false;
        jumpBufferRemaining=0f;
        jumpGraceRemaining=0f;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0);
    }

    void OnDisable()
    {
        if (ladder) ladder.Detach();
        if (flying && rb) rb.gravityScale = previousGravity;
        if (noClip && col) col.enabled = colliderWasEnabled;
        noClip = false;
        flying = false; inputY = 0; jumpRequested = false;
        jumpBufferRemaining=0f;
        jumpGraceRemaining=0f;
        previousRunVelocity=0;
        ResetFallTracking();
    }

    void OnDestroy()
    {
        if (col && col.sharedMaterial == noFrictionMaterial)
            col.sharedMaterial = originalColliderMaterial;
        if (noFrictionMaterial) Destroy(noFrictionMaterial);
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
