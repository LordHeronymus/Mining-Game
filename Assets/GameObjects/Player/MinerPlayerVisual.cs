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
    [InspectorName("Helmfarbe")] public Color helmetColor = new Color(.84f, .33f, .011f);
    [InspectorName("Arbeitsgewand")] public Color clothingColor = new Color(.009f, .085f, .29f);
    [InspectorName("Hautfarbe")] public Color skinColor = new Color(.90f, .34f, .10f);
    [InspectorName("Stiefel und Handschuhe")] public Color leatherColor = new Color(.14f, .055f, .018f);
    [Min(0f), InspectorName("Stirnlampenhelligkeit")] public float headlampIntensity = 1.5f;

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
        if (geometry == null && material) geometry = new CritterMesh(transform, GeneratedName, material, 30, true);
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
        Color darkCloth = Color.Lerp(clothingColor, new Color(.006f, .025f, .09f), .42f);
        Color lightCloth = Color.Lerp(clothingColor, new Color(.045f, .22f, .53f), .25f);
        Color leatherShade = Color.Lerp(leatherColor, Color.black, .42f);
        Color skinShade = Color.Lerp(skinColor, new Color(.40f, .15f, .04f), .33f);
        Color helmetShade = Color.Lerp(helmetColor, new Color(.43f, .15f, .006f), .45f);

        // Far leg and arm sit behind the body; the near limbs stay readable during a swing.
        Leg(-.035f, backFoot, bob, darkCloth, leatherShade);
        Vector2 farShoulder = new Vector2(-.15f, .76f + bob);
        Vector2 farHand = new Vector2(-.16f + armSwing * .11f, .51f + bob + Mathf.Abs(armSwing) * .018f + airborne * .08f);
        Arm(farShoulder, new Vector2(-.24f + armSwing * .035f, .625f + bob), farHand, darkCloth, skinShade, leatherShade);
        geometry.ShadedEllipse(-.16f, .65f + bob, .10f, .18f, leatherShade, leatherColor, Color.Lerp(leatherColor, new Color(.58f, .30f, .13f), .22f), 8);
        geometry.ShadedEllipse(-.174f, .66f + bob, .067f, .14f, leatherShade, leatherColor, Color.Lerp(leatherColor, new Color(.70f, .36f, .16f), .24f), 8);
        Leg(.035f, frontFoot, bob, clothingColor, leatherColor);

        geometry.ShadedEllipse(0, .635f + bob, .205f, .25f, new Color(.006f, .045f, .13f), darkCloth, clothingColor);
        geometry.ShadedEllipse(.015f, .65f + bob, .182f, .218f, darkCloth, clothingColor, lightCloth);
        geometry.ShadedEllipse(-.028f, .675f + bob, .078f, .157f, clothingColor, lightCloth, Color.Lerp(lightCloth, Color.white, .08f));
        geometry.Stroke(-.068f, .85f + bob, -.032f, .735f + bob, .012f, Color.Lerp(lightCloth, Color.white, .16f));
        geometry.Stroke(.085f, .80f + bob, .097f, .68f + bob, .007f, Color.Lerp(lightCloth, Color.white, .12f));
        geometry.Stroke(-.12f, .81f + bob, -.10f, .56f + bob, .025f, darkCloth);
        geometry.Stroke(.13f, .81f + bob, .11f, .56f + bob, .025f, darkCloth);
        geometry.Ellipse(-.10f, .62f + bob, .015f, .017f, helmetColor);
        geometry.Ellipse(.11f, .62f + bob, .015f, .017f, helmetColor);
        geometry.Ellipse(.017f, .56f + bob, .065f, .05f, darkCloth);
        geometry.Stroke(-.17f, .45f + bob, .17f, .45f + bob, .039f, leatherShade);
        geometry.ShadedEllipse(.067f, .45f + bob, .039f, .034f, new Color(.35f, .21f, .035f), new Color(.78f, .56f, .16f), new Color(1, .82f, .34f));
        geometry.Ellipse(.067f, .45f + bob, .019f, .016f, leatherShade);

        geometry.Ellipse(.035f, .84f + bob, .07f, .069f, skinShade);
        geometry.ShadedEllipse(.074f, .99f + bob, .187f, .176f, new Color(.36f, .15f, .09f), skinShade, skinColor);
        geometry.ShadedEllipse(.10f, 1.005f + bob, .168f, .155f, skinShade, skinColor, Color.Lerp(skinColor, Color.white, .11f));
        geometry.Ellipse(-.035f, 1.005f + bob, .046f, .06f, skinShade);
        geometry.Ellipse(-.025f, 1.011f + bob, .027f, .038f, skinColor);
        geometry.ShadedEllipse(.245f, .976f + bob, .060f, .043f, skinShade, skinColor, Color.Lerp(skinColor, Color.white, .25f));
        geometry.Ellipse(.205f, .992f + bob, .020f, .009f, Color.Lerp(skinColor, Color.white, .28f));
        geometry.Ellipse(.13f, .896f + bob, .118f, .062f, leatherShade);
        geometry.Ellipse(.176f, .922f + bob, .072f, .028f, Color.Lerp(skinColor, new Color(.46f, .20f, .12f), .17f));
        bool blink = Mathf.Repeat(age, 5.2f) > 5.04f;
        geometry.Ellipse(.192f, 1.037f + bob, .023f, blink ? .005f : .032f, new Color(.018f, .025f, .032f));
        if (!blink) geometry.Ellipse(.200f, 1.049f + bob, .007f, .009f, Color.white);
        geometry.Stroke(.155f, 1.088f + bob, .211f, 1.081f + bob, .011f, leatherShade);
        geometry.Stroke(.185f, .917f + bob, .233f, .925f + bob, .0045f, leatherShade);

        HelmetDome(.067f, 1.111f + bob, .239f, .177f, helmetShade);
        HelmetDome(.075f, 1.124f + bob, .216f, .160f, helmetColor);
        geometry.Stroke(.095f, 1.19f + bob, .095f, 1.263f + bob, .018f, helmetShade);
        geometry.ShadedEllipse(.117f, 1.113f + bob, .283f, .038f, helmetShade, helmetColor, Color.Lerp(helmetColor, Color.white, .19f));
        geometry.Ellipse(.127f, 1.126f + bob, .28f, .023f, Color.Lerp(helmetColor, Color.white, .06f));
        geometry.Ellipse(.31f, 1.155f + bob, .078f, .072f, new Color(.07f, .09f, .11f));
        geometry.ShadedEllipse(.326f, 1.161f + bob, .059f, .056f, new Color(.30f, .33f, .30f), new Color(.70f, .73f, .64f), new Color(1, .98f, .79f));
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
        Color pantLight = Color.Lerp(pants, new Color(.08f, .30f, .62f), .23f);
        geometry.Stroke(hip.x - .017f, hip.y + .009f, knee.x - .017f, knee.y + .009f, .013f, pantLight);
        geometry.Stroke(knee.x - .015f, knee.y + .008f, ankle.x - .015f, ankle.y + .008f, .010f, pantLight);
        geometry.ShadedEllipse(knee.x, knee.y, .073f, .070f, Color.Lerp(pants, Color.black, .35f), pants, pantLight);
        Color bootShadow = Color.Lerp(boot, Color.black, .50f);
        Color bootLight = Color.Lerp(boot, new Color(.70f, .35f, .14f), .12f);
        geometry.ShadedEllipse(ankle.x, ankle.y, .078f, .079f, bootShadow, boot, bootLight);
        Vector2 bootCenter = FootPoint(ankle, new Vector2(.04f, -.048f), foot.angle);
        geometry.ShadedEllipse(bootCenter.x, bootCenter.y, .112f, .050f, bootShadow, boot, bootLight, foot.angle);
        Vector2 toeShine = FootPoint(ankle, new Vector2(.086f, -.026f), foot.angle);
        geometry.Ellipse(toeShine.x, toeShine.y, .043f, .009f, Color.Lerp(boot, new Color(.85f, .47f, .22f), .19f), foot.angle);
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
        for (int i = 0; i < 32; i++)
        {
            float a = i * Mathf.PI / 32, b = (i + 1) * Mathf.PI / 32;
            geometry.Triangle(center, center + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry),
                center + new Vector2(Mathf.Cos(b) * rx, Mathf.Sin(b) * ry), color);
        }
    }

    void Arm(Vector2 shoulder, Vector2 elbow, Vector2 hand, Color sleeve, Color skin, Color glove)
    {
        geometry.Stroke(shoulder.x, shoulder.y, elbow.x, elbow.y, .064f, sleeve);
        geometry.Stroke(shoulder.x - .016f, shoulder.y + .011f, elbow.x - .016f, elbow.y + .011f, .014f,
            Color.Lerp(sleeve, new Color(.12f, .35f, .70f), .22f));
        geometry.Stroke(elbow.x, elbow.y, hand.x, hand.y, .043f, skin);
        geometry.ShadedEllipse(hand.x, hand.y, .054f, .049f, Color.Lerp(glove, Color.black, .45f), glove,
            Color.Lerp(glove, new Color(.85f, .47f, .23f), .38f));
    }

    void DrawPickaxe(Vector2 hand, float degrees, Color dark)
    {
        float angle = degrees * Mathf.Deg2Rad;
        Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        Vector2 side = new Vector2(-direction.y, direction.x);
        Vector2 butt = hand - direction * .12f, head = hand + direction * .37f;
        geometry.Stroke(butt.x, butt.y, head.x, head.y, .029f, dark);
        geometry.Stroke(butt.x - .009f, butt.y + .006f, head.x - .009f, head.y + .006f, .013f, new Color(.71f, .39f, .16f));
        geometry.Stroke(butt.x + .012f, butt.y - .006f, head.x + .012f, head.y - .006f, .007f, new Color(.24f, .105f, .035f));
        Color steelShadow = new Color(.20f, .19f, .26f), steel = new Color(.41f, .40f, .46f);
        Color edge = new Color(.88f, .94f, 1);
        Vector2 left = head + side * .13f - direction * .015f, right = head - side * .13f - direction * .015f;
        Vector2 leftTip = head + side * .25f - direction * .11f, rightTip = head - side * .25f - direction * .11f;
        geometry.Stroke(left.x, left.y, right.x, right.y, .039f, steelShadow);
        geometry.Stroke(left.x + direction.x * .018f, left.y + direction.y * .018f,
            right.x + direction.x * .018f, right.y + direction.y * .018f, .020f, steel);
        geometry.Triangle(left + direction * .022f, left - direction * .038f, leftTip, steel);
        geometry.Triangle(right + direction * .022f, right - direction * .038f, rightTip, steelShadow);
        geometry.Stroke(left.x + direction.x * .037f, left.y + direction.y * .037f,
            right.x + direction.x * .037f, right.y + direction.y * .037f, .008f, edge);
        float glint = Mathf.Pow(Mathf.Max(0, Mathf.Sin(age * 1.7f + swingPhase)), 12);
        Vector2 flash = Vector2.Lerp(left, head, .30f);
        geometry.Stroke(flash.x, flash.y, flash.x + side.x * .055f, flash.y + side.y * .055f,
            .006f + glint * .006f, Color.Lerp(edge, Color.white, .35f + glint * .65f));
        geometry.ShadedEllipse(head.x, head.y, .038f, .039f, new Color(.08f, .11f, .15f), steelShadow, steel);
    }

    void UpdateLamp()
    {
        if (!headlamp) return;
        headlamp.transform.position = new Vector3(lampPosition.x, lampPosition.y, transform.position.z);
        float direction = Mathf.Atan2(-.22f, facing) * Mathf.Rad2Deg - 90f;
        headlamp.transform.rotation = Quaternion.Euler(0, 0, direction);
        headlamp.color = new Color(1, .83f, .48f);
        headlamp.intensity = Mathf.Max(0, headlampIntensity);
    }

    void OnDisable()
    {
        geometry?.Dispose(); geometry = null;
        if (headlamp) headlamp.intensity = 0;
    }
}
