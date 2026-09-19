using UnityEngine;
using UnityEngine.Rendering.Universal;

[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Rigidbody2D))]
[DefaultExecutionOrder(1300)]
public sealed class MinerPlayerVisual : MonoBehaviour
{
    public Material material;
    public SpriteRenderer legacySprite;
    public SkyController sky;
    public Light2D headlamp;
    [Min(.3f), InspectorName("Figurenhöhe")] public float height = 1.06f;
    [Min(.1f), InspectorName("Laufanimation")] public float walkAnimationSpeed = 1;
    [Min(.1f), InspectorName("Abbauanimation (Schläge/s)")] public float miningSwingsPerSecond = 2;
    [InspectorName("Helmfarbe")] public Color helmetColor = new Color(1, .56f, .025f);
    [InspectorName("Arbeitsgewand")] public Color clothingColor = new Color(.035f, .24f, .55f);
    [InspectorName("Hautfarbe")] public Color skinColor = new Color(.78f, .43f, .24f);
    [InspectorName("Stiefel und Handschuhe")] public Color leatherColor = new Color(.22f, .105f, .045f);

    const string GeneratedName = "Miner figure (generated)";
    CritterMesh geometry;
    Rigidbody2D body;
    Collider2D bodyCollider;
    PlayerMovement movement;
    TileMiner miner;
    float age, walkPhase, swingPhase, walking, airborne, miningWeight;
    float gaitStride = .34f, gaitDuty = .62f, running;
    int facing = 1;
    Vector2 footPosition, lampPosition;

    void OnEnable()
    {
        CritterMesh.RemoveGenerated(transform, GeneratedName);
        body = GetComponent<Rigidbody2D>(); bodyCollider = GetComponent<Collider2D>();
        movement = GetComponent<PlayerMovement>(); miner = GetComponent<TileMiner>();
        age = walkPhase = swingPhase = walking = airborne = miningWeight = running = 0; gaitStride = .34f; gaitDuty = .62f;
        Refresh();
    }

    public void Refresh()
    {
        if (geometry == null && material) geometry = new CritterMesh(transform, GeneratedName, material, 30);
        if (geometry == null) return;
        if (legacySprite) legacySprite.enabled = false;
        geometry.renderer.sharedMaterial = material;
        DrawPose(false, transform.position + Vector3.right);
    }

    void LateUpdate()
    {
        if (geometry == null) { Refresh(); if (geometry == null) return; }
        if (!Application.isPlaying) { DrawPose(false, transform.position + Vector3.right); UpdateLamp(); return; }
        if (movement && Mathf.Abs(movement.HorizontalInput) > .01f) facing = movement.HorizontalInput < 0 ? -1 : 1;
        Vector2 velocity = body ? body.linearVelocity : Vector2.zero;
        bool grounded = movement ? movement.IsOnGround : Mathf.Abs(velocity.y) < .05f;
        Animate(Time.deltaTime, velocity, grounded, movement && movement.IsFlying,
            miner && miner.IsMining, miner ? miner.MiningTarget : (Vector2)transform.position + Vector2.right);
    }

    void Animate(float dt, Vector2 velocity, bool grounded, bool flying, bool mining, Vector2 target)
    {
        age += dt;
        if (mining && Mathf.Abs(target.x - transform.position.x) > .05f) facing = target.x < transform.position.x ? -1 : 1;
        else if (Mathf.Abs(velocity.x) > .12f && (!movement || Mathf.Abs(movement.HorizontalInput) < .01f)) facing = velocity.x < 0 ? -1 : 1;
        float speed = Mathf.Abs(velocity.x);
        walking = Mathf.MoveTowards(walking, grounded && !flying ? Mathf.Clamp01(speed / .8f) : 0, dt * 9);
        airborne = Mathf.MoveTowards(airborne, !grounded || flying ? 1 : 0, dt * 10);
        miningWeight = Mathf.MoveTowards(miningWeight, mining ? 1 : 0, dt * 12);
        running = Mathf.MoveTowards(running, Mathf.InverseLerp(1.8f, 6, speed), dt * 4);
        gaitStride = Mathf.Lerp(.34f, .44f, running);
        gaitDuty = Mathf.Lerp(.62f, .30f, running);
        // The backwards stance sweep cancels body travel; fast running has shorter contacts.
        float worldStride = gaitStride * Mathf.Max(.3f, height) / 1.29f;
        if (grounded && !flying && speed > .03f)
            walkPhase = Mathf.Repeat(walkPhase + dt * speed * gaitDuty / worldStride * Mathf.PI * 2 * Mathf.Max(.1f, walkAnimationSpeed), Mathf.PI * 2);
        if (mining) swingPhase += dt * Mathf.Max(.1f, miningSwingsPerSecond) * Mathf.PI * 2;
        else swingPhase = 0;
        DrawPose(mining, target); UpdateLamp();
    }

    void DrawPose(bool mining, Vector2 target)
    {
        if (geometry == null) return;
        geometry.Clear();
        float scale = Mathf.Max(.3f, height) / 1.29f;
        footPosition = new Vector2(transform.position.x, bodyCollider && bodyCollider.enabled ? bodyCollider.bounds.min.y : transform.position.y - height * .5f);
        float night = sky && sky.isActiveAndEnabled ? sky.NightBlend : 0;
        float brightness = Mathf.Lerp(1, .78f, night);
        Color tint = new Color(brightness, brightness, brightness, 1);
        geometry.Begin(footPosition, scale, facing, tint);
        if (airborne < .5f) geometry.Ellipse(0, .01f, .26f, .022f, new Color(.015f, .025f, .04f, .25f));
        float phase = walkPhase / (Mathf.PI * 2);
        float bobPhase = phase - Mathf.Lerp(.06f, .18f, running);
        float bob = (.5f - .5f * Mathf.Cos(bobPhase * Mathf.PI * 4)) * Mathf.Lerp(.012f, .027f, running) * walking
            + Mathf.Sin(age * 2.5f) * .006f * (1 - walking);
        var backFoot = MinerGait.Sample(phase + .5f, gaitStride, gaitDuty, Mathf.Lerp(.10f, .19f, running));
        var frontFoot = MinerGait.Sample(phase, gaitStride, gaitDuty, Mathf.Lerp(.10f, .19f, running));
        backFoot.ankle = Vector2.Lerp(new Vector2(-.07f, .10f), backFoot.ankle, walking);
        frontFoot.ankle = Vector2.Lerp(new Vector2(.07f, .10f), frontFoot.ankle, walking);
        backFoot.ankle += new Vector2(-.05f, .13f) * airborne;
        frontFoot.ankle += new Vector2(.025f, .065f) * airborne;
        backFoot.angle *= walking; frontFoot.angle *= walking;
        float armSwing = Mathf.Cos(walkPhase - gaitDuty * Mathf.PI) * walking;
        Color darkCloth = Color.Lerp(clothingColor, new Color(.01f, .035f, .08f), .48f);
        Color lightCloth = Color.Lerp(clothingColor, new Color(.12f, .48f, .78f), .38f);
        Color leatherShade = Color.Lerp(leatherColor, Color.black, .42f);
        Color skinShade = Color.Lerp(skinColor, new Color(.30f, .13f, .06f), .35f);
        Color helmetShade = Color.Lerp(helmetColor, new Color(.53f, .22f, .005f), .48f);

        // Far leg and arm sit behind the body; the near limbs stay readable during a swing.
        Leg(-.035f, backFoot, bob, darkCloth, leatherShade);
        Vector2 farShoulder = new Vector2(-.15f, .76f + bob);
        Vector2 farHand = new Vector2(-.16f + armSwing * .11f, .51f + bob + Mathf.Abs(armSwing) * .018f + airborne * .08f);
        Arm(farShoulder, new Vector2(-.24f + armSwing * .035f, .625f + bob), farHand, darkCloth, skinShade, leatherShade);
        geometry.Ellipse(-.16f, .65f + bob, .10f, .18f, leatherShade, 8);
        geometry.Ellipse(-.174f, .66f + bob, .067f, .14f, leatherColor, 8);
        Leg(.035f, frontFoot, bob, clothingColor, leatherColor);

        geometry.Ellipse(0, .635f + bob, .205f, .25f, darkCloth);
        geometry.Ellipse(.015f, .65f + bob, .182f, .218f, clothingColor);
        geometry.Ellipse(.044f, .65f + bob, .115f, .175f, lightCloth);
        geometry.Stroke(-.12f, .81f + bob, -.10f, .56f + bob, .025f, darkCloth);
        geometry.Stroke(.13f, .81f + bob, .11f, .56f + bob, .025f, darkCloth);
        geometry.Ellipse(-.10f, .62f + bob, .015f, .017f, helmetColor);
        geometry.Ellipse(.11f, .62f + bob, .015f, .017f, helmetColor);
        geometry.Ellipse(.017f, .56f + bob, .065f, .05f, darkCloth);
        geometry.Stroke(-.17f, .45f + bob, .17f, .45f + bob, .039f, leatherShade);
        geometry.Ellipse(.067f, .45f + bob, .039f, .034f, new Color(.65f, .49f, .13f));
        geometry.Ellipse(.067f, .45f + bob, .019f, .016f, leatherShade);

        geometry.Ellipse(.035f, .84f + bob, .07f, .069f, skinShade);
        geometry.Ellipse(.074f, .99f + bob, .187f, .176f, skinShade);
        geometry.Ellipse(.10f, 1.005f + bob, .168f, .155f, skinColor);
        geometry.Ellipse(-.035f, 1.005f + bob, .046f, .06f, skinShade);
        geometry.Ellipse(-.025f, 1.011f + bob, .027f, .038f, skinColor);
        geometry.Ellipse(.245f, .976f + bob, .060f, .043f, skinColor);
        geometry.Ellipse(.13f, .896f + bob, .118f, .062f, leatherShade);
        geometry.Ellipse(.176f, .922f + bob, .072f, .028f, skinColor);
        bool blink = Mathf.Repeat(age, 5.2f) > 5.04f;
        geometry.Ellipse(.192f, 1.037f + bob, .023f, blink ? .005f : .032f, new Color(.018f, .025f, .032f));
        if (!blink) geometry.Ellipse(.200f, 1.049f + bob, .007f, .009f, Color.white);
        geometry.Stroke(.155f, 1.088f + bob, .211f, 1.081f + bob, .011f, leatherShade);
        geometry.Stroke(.185f, .917f + bob, .233f, .925f + bob, .0045f, leatherShade);

        HelmetDome(.067f, 1.111f + bob, .239f, .177f, helmetShade);
        HelmetDome(.075f, 1.124f + bob, .216f, .160f, helmetColor);
        geometry.Ellipse(.016f, 1.205f + bob, .111f, .049f, Color.Lerp(helmetColor, Color.white, .32f), 15);
        geometry.Stroke(.095f, 1.19f + bob, .095f, 1.263f + bob, .018f, helmetShade);
        geometry.Ellipse(.117f, 1.113f + bob, .283f, .038f, helmetShade);
        geometry.Ellipse(.127f, 1.126f + bob, .28f, .023f, helmetColor);
        geometry.Ellipse(.31f, 1.155f + bob, .078f, .072f, new Color(.07f, .09f, .11f));
        geometry.Ellipse(.326f, 1.161f + bob, .059f, .056f, new Color(.70f, .73f, .64f));
        geometry.Ellipse(.337f, 1.165f + bob, .043f, .041f, new Color(1.3f, 1.18f, .75f));
        geometry.Ellipse(.348f, 1.177f + bob, .018f, .019f, Color.white);
        lampPosition = footPosition + new Vector2(.36f * facing, 1.16f + bob) * scale;

        Vector2 shoulder = new Vector2(.16f, .77f + bob);
        Vector2 restHand = new Vector2(.27f - armSwing * .045f, .53f + bob + Mathf.Abs(armSwing) * .012f + airborne * .08f);
        float strike = .5f - .5f * Mathf.Cos(swingPhase);
        Vector2 workHand = new Vector2(Mathf.Lerp(.11f, .39f, strike), Mathf.Lerp(.98f, .65f, strike) + bob);
        Vector2 hand = Vector2.Lerp(restHand, workHand, miningWeight);
        Vector2 elbow = Vector2.Lerp(new Vector2(.245f, .645f + bob), new Vector2(.26f, .83f + bob), miningWeight);
        Arm(shoulder, elbow, hand, lightCloth, skinColor, leatherColor);
        Vector2 aim = target - (footPosition + new Vector2(0, .65f * scale)); aim.x *= facing;
        float aimAngle = Mathf.Clamp(Mathf.Atan2(aim.y, Mathf.Max(.02f, aim.x)) * Mathf.Rad2Deg, -75, 65);
        float angle = Mathf.Lerp(65 - armSwing * 3, Mathf.Lerp(140, aimAngle - 8, strike), miningWeight);
        DrawPickaxe(hand, angle, leatherShade);
        geometry.Ellipse(hand.x, hand.y, .054f, .049f, leatherColor);
        geometry.Stroke(hand.x - .018f, hand.y + .025f, hand.x + .025f, hand.y + .016f, .009f, Color.Lerp(leatherColor, skinColor, .45f));
        geometry.Upload();
    }

    void Leg(float hipX, MinerGait.Foot foot, float bob, Color pants, Color boot)
    {
        var hip = new Vector2(hipX, Mathf.Lerp(.50f, .43f, running) + bob);
        var ankle = foot.ankle;
        var knee = MinerGait.Knee(hip, ankle, .24f, .23f);
        knee = Vector2.Lerp(new Vector2(Mathf.Lerp(hipX, ankle.x, .5f), .245f + airborne * .05f), knee, Mathf.Max(walking, airborne));
        geometry.Stroke(hip.x, hip.y, knee.x, knee.y, .074f, pants);
        geometry.Stroke(knee.x, knee.y, ankle.x, ankle.y, .068f, pants);
        geometry.Ellipse(knee.x, knee.y, .073f, .070f, pants);
        geometry.Ellipse(ankle.x, ankle.y, .078f, .079f, boot);
        Vector2 bootCenter = FootPoint(ankle, new Vector2(.04f, -.048f), foot.angle);
        geometry.Ellipse(bootCenter.x, bootCenter.y, .112f, .050f, boot, foot.angle);
        Vector2 heel = FootPoint(ankle, new Vector2(-.056f, -.081f), foot.angle);
        Vector2 toe = FootPoint(ankle, new Vector2(.123f, -.081f), foot.angle);
        geometry.Stroke(heel.x, heel.y, toe.x, toe.y, .015f, new Color(.018f, .021f, .025f));
        Vector2 laceA = FootPoint(ankle, new Vector2(0, -.018f), foot.angle);
        Vector2 laceB = FootPoint(ankle, new Vector2(.068f, -.024f), foot.angle);
        geometry.Stroke(laceA.x, laceA.y, laceB.x, laceB.y, .009f, Color.Lerp(boot, new Color(.57f, .34f, .12f), .4f));
    }

    static Vector2 FootPoint(Vector2 ankle, Vector2 offset, float angle)
    {
        float c = Mathf.Cos(angle * Mathf.Deg2Rad), s = Mathf.Sin(angle * Mathf.Deg2Rad);
        return ankle + new Vector2(offset.x * c - offset.y * s, offset.x * s + offset.y * c);
    }

    void HelmetDome(float x, float y, float rx, float ry, Color color)
    {
        var center = new Vector2(x, y);
        for (int i = 0; i < 20; i++)
        {
            float a = i * Mathf.PI / 20, b = (i + 1) * Mathf.PI / 20;
            geometry.Triangle(center, center + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry),
                center + new Vector2(Mathf.Cos(b) * rx, Mathf.Sin(b) * ry), color);
        }
    }

    void Arm(Vector2 shoulder, Vector2 elbow, Vector2 hand, Color sleeve, Color skin, Color glove)
    {
        geometry.Stroke(shoulder.x, shoulder.y, elbow.x, elbow.y, .064f, sleeve);
        geometry.Stroke(elbow.x, elbow.y, hand.x, hand.y, .043f, skin);
        geometry.Ellipse(hand.x, hand.y, .054f, .049f, glove);
    }

    void DrawPickaxe(Vector2 hand, float degrees, Color dark)
    {
        float angle = degrees * Mathf.Deg2Rad;
        Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        Vector2 side = new Vector2(-direction.y, direction.x);
        Vector2 butt = hand - direction * .12f, head = hand + direction * .37f;
        geometry.Stroke(butt.x, butt.y, head.x, head.y, .027f, dark);
        geometry.Stroke(butt.x + .006f, butt.y, head.x + .006f, head.y, .016f, new Color(.52f, .28f, .10f));
        Color steel = new Color(.36f, .49f, .59f), edge = new Color(.72f, .85f, .91f);
        Vector2 left = head + side * .13f - direction * .015f, right = head - side * .13f - direction * .015f;
        Vector2 leftTip = head + side * .25f - direction * .11f, rightTip = head - side * .25f - direction * .11f;
        geometry.Stroke(left.x, left.y, right.x, right.y, .032f, steel);
        geometry.Triangle(left + direction * .025f, left - direction * .033f, leftTip, steel);
        geometry.Triangle(right + direction * .025f, right - direction * .033f, rightTip, steel);
        geometry.Stroke(left.x, left.y + .015f, right.x, right.y + .015f, .010f, edge);
        geometry.Ellipse(head.x, head.y, .038f, .039f, new Color(.10f, .14f, .18f));
    }

    void UpdateLamp()
    {
        if (!headlamp) return;
        headlamp.transform.position = new Vector3(lampPosition.x, lampPosition.y, transform.position.z);
        headlamp.color = new Color(1, .83f, .48f);
        headlamp.intensity = sky && sky.isActiveAndEnabled ? sky.NightBlend * 1.5f : 0;
    }

    void OnDisable()
    {
        geometry?.Dispose(); geometry = null;
        if (headlamp) headlamp.intensity = 0;
    }
}
