using UnityEngine;

[DisallowMultipleComponent]
public sealed class SurfaceBirds : MonoBehaviour
{
    public Material material;
    [InspectorName("Blaugrau")] public Color color = new Color(.08f, .13f, .19f, .85f);
    [InspectorName("Graubraun")] public Color warmColor = new Color(.24f, .18f, .14f, .85f);
    [InspectorName("Schiefergrau")] public Color slateColor = new Color(.30f, .35f, .41f, .85f);
    [InspectorName("Ferne Farbe")] public Color distantColor = new Color(.48f, .60f, .73f, .76f);
    [Range(0, 1), InspectorName("Atmosphärische Aufhellung")]
    public float distanceColorStrength = .7f;
    [Range(0, .8f), InspectorName("Körperabdunklung")]
    public float bodyDarkening = .45f;
    [InspectorName("Schnabelfarbe")] public Color beakColor = new Color(1f, .73f, .08f, 1f);
    [Range(0, 1), InspectorName("Anteil mit hellen Flügelspitzen")]
    public float whiteWingtipChance = .4f;
    [Min(.1f)] public float size = .55f;
    [Range(0, .4f), InspectorName("Individuelle Grössenvariation")]
    public float sizeVariation = .18f;
    [Range(0, .35f), InspectorName("Tiefenbewegung des Schwarms")]
    public float flockDepthMotion = .16f;
    [Min(4f), InspectorName("Dauer der Tiefenbewegung (s)")]
    public float depthMotionPeriod = 16f;
    [Min(.1f)] public float flightSpeed = 1.5f;
    public Vector2 altitude = new Vector2(4.5f, 9f);
    public Vector2 flockInterval = new Vector2(9f, 16f);
    [Range(1, 8)] public int birdsPerFlock = 5;
    [Min(.1f)] public float wingbeatsPerSecond = 1.6f;
    [Min(0.1f), InspectorName("Nachts außerhalb des Bildes (s)")]
    public float nightOffscreenDespawnDelay = 3f;

    const int Capacity = 24, WingSegments = 12, BodySegments = 16;
    const int WingVertices = (WingSegments + 1) * 2;
    const int BeakOffset = WingVertices * 2 + BodySegments + 1;
    const int VerticesPerBird = BeakOffset + 3;
    struct Bird
    {
        public bool active, whiteWingtips;
        public float x, y, age, speed, scale, phase, direction, depth;
        public float depthPhase, depthPeriodFactor;
        public float offscreenTime;
        public int paletteIndex;
    }
    readonly Bird[] birds = new Bird[Capacity];
    readonly Vector3[] vertices = new Vector3[Capacity * VerticesPerBird];
    readonly Color[] colors = new Color[Capacity * VerticesPerBird];
    readonly System.Random random = new System.Random();
    SurfaceBackgroundController background;
    SkyController sky;
    readonly Plane[] viewPlanes = new Plane[6];
    Mesh mesh;
    MeshRenderer meshRenderer;
    float nextFlock;
    bool firstFlock;

    void OnEnable()
    {
        // Remove transient geometry left behind by a script reload during Play mode.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var previousChild = transform.GetChild(i);
            if (previousChild.name != "Bird silhouettes (generated)") continue;
            previousChild.gameObject.SetActive(false);
            var filter = previousChild.GetComponent<MeshFilter>();
            if (filter && filter.sharedMesh) Release(filter.sharedMesh);
            Release(previousChild.gameObject);
        }
        background = GetComponentInParent<SurfaceBackgroundController>();
        sky = background ? background.GetComponentInChildren<SkyController>(true) : null;
        nextFlock = 0; firstFlock = true;
        System.Array.Clear(birds, 0, birds.Length);
        if (!material) return;
        var child = new GameObject("Bird silhouettes (generated)") { hideFlags = HideFlags.DontSave };
        child.transform.SetParent(transform, false);
        child.layer = gameObject.layer;
        mesh = new Mesh { name = "Surface bird flock", hideFlags = HideFlags.DontSave };
        mesh.MarkDynamic();
        mesh.vertices = vertices;
        mesh.uv = new Vector2[vertices.Length];
        var indices = new int[Capacity * (WingSegments * 12 + BodySegments * 3 + 3)];
        int index = 0;
        for (int bird = 0; bird < Capacity; bird++)
        {
            int start = bird * VerticesPerBird;
            for (int side = 0; side < 2; side++)
                for (int segment = 0; segment < WingSegments; segment++)
                {
                    int v = start + side * WingVertices + segment * 2;
                    indices[index++] = v; indices[index++] = v + 1; indices[index++] = v + 2;
                    indices[index++] = v + 1; indices[index++] = v + 3; indices[index++] = v + 2;
                }
            int body = start + WingVertices * 2;
            for (int segment = 0; segment < BodySegments; segment++)
            {
                indices[index++] = body;
                indices[index++] = body + 1 + segment;
                indices[index++] = body + 1 + (segment + 1) % BodySegments;
            }
            indices[index++] = start + BeakOffset;
            indices[index++] = start + BeakOffset + 1;
            indices[index++] = start + BeakOffset + 2;
        }
        mesh.triangles = indices;
        child.AddComponent<MeshFilter>().sharedMesh = mesh;
        meshRenderer = child.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.sortingLayerName = "Background";
        meshRenderer.sortingOrder = 35;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
    }

    float Random(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());

    void LateUpdate()
    {
        var camera = background ? background.RenderCamera : Camera.main;
        if (camera) Tick(camera, Time.deltaTime);
    }

    void Tick(Camera camera, float deltaTime)
    {
        if (!meshRenderer) return;
        bool night = sky && sky.isActiveAndEnabled && sky.IsNight;
        if (night) firstFlock = false;
        float visibility = background ? background.GetOpacity(camera) : 1;
        meshRenderer.enabled = visibility > .01f && camera.transform.position.y + camera.orthographicSize > altitude.x;
        if (!meshRenderer.enabled && !night)
        {
            System.Array.Clear(birds, 0, birds.Length);
            nextFlock = 0; firstFlock = true;
            return;
        }
        float halfWidth = camera.orthographicSize * camera.aspect;
        float center = camera.transform.position.x;
        GeometryUtility.CalculateFrustumPlanes(camera, viewPlanes);
        if (!night) nextFlock -= deltaTime;
        if (!night && nextFlock <= 0)
        {
            float direction = random.Next(2) == 0 ? -1 : 1;
            float x = firstFlock ? center - direction * halfWidth * .45f : center - direction * (halfWidth + 2);
            float y = Random(Mathf.Min(altitude.x, altitude.y), Mathf.Max(altitude.x, altitude.y));
            float flockDepth = Random(0, 1);
            float depthPhase = Random(0, Mathf.PI * 2);
            float depthPeriodFactor = Random(.85f, 1.2f);
            int spawned = 0;
            for (int i = 0; i < Capacity && spawned < Mathf.Clamp(birdsPerFlock, 1, 8); i++)
            {
                if (birds[i].active) continue;
                float depth = Mathf.Clamp01(flockDepth + Random(-.12f, .12f));
                float paletteRoll = Random(0, 1);
                birds[i] = new Bird { active = true, x = x - direction * spawned * .75f,
                    y = y + Random(-.45f, .45f), speed = Mathf.Max(.1f, flightSpeed) * Random(.92f, 1.08f) * Mathf.Lerp(1, .72f, flockDepth),
                    scale = Random(1-Mathf.Clamp(sizeVariation, 0, .4f), 1+Mathf.Clamp(sizeVariation, 0, .4f)) * Mathf.Lerp(1, .65f, depth),
                    phase = Random(0, Mathf.PI * 2), direction = direction, depth = depth,
                    depthPhase = depthPhase, depthPeriodFactor = depthPeriodFactor,
                    whiteWingtips = Random(0, 1) < Mathf.Clamp01(whiteWingtipChance),
                    paletteIndex = paletteRoll < .45f ? 0 : paletteRoll < .85f ? 1 : 2 };
                spawned++;
            }
            firstFlock = false;
            nextFlock = Random(Mathf.Max(1, flockInterval.x), Mathf.Max(1, flockInterval.y));
        }
        for (int i = 0; i < Capacity; i++)
        {
            var bird = birds[i];
            int start = i * VerticesPerBird;
            if (bird.active)
            {
                bird.age += deltaTime;
                bird.x += bird.direction * bird.speed * deltaTime;
                if (night)
                {
                    Vector3 worldOrigin = new Vector3(bird.x,
                        bird.y + Mathf.Sin(bird.age * 1.15f + bird.phase) * .18f, transform.position.z);
                    float worldScale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
                    float radius = Mathf.Max(.1f, size) * bird.scale *
                        GetDepthScale(bird.age, bird.depthPhase, bird.depthPeriodFactor) * worldScale * .8f;
                    bool onScreen = GeometryUtility.TestPlanesAABB(viewPlanes, new Bounds(worldOrigin, Vector3.one * radius * 2f));
                    bird.offscreenTime = onScreen ? 0f : bird.offscreenTime + deltaTime;
                    if (bird.offscreenTime >= Mathf.Max(.1f, nightOffscreenDespawnDelay)) bird.active = false;
                }
                else
                {
                    bird.offscreenTime = 0f;
                    if (Mathf.Abs(bird.x - center) > halfWidth + 10) bird.active = false;
                }
            }
            birds[i] = bird;
            if (!bird.active)
            {
                for (int v = 0; v < VerticesPerBird; v++) { vertices[start + v] = Vector3.zero; colors[start + v] = Color.clear; }
                continue;
            }
            float phase = bird.age * Mathf.Max(.1f, wingbeatsPerSecond) * Mathf.PI * 2 + bird.phase;
            // Each bird alternates a few wingbeats with a short glide.
            float glide = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.7f, .92f,
                .5f + .5f * Mathf.Sin(bird.age * .75f + bird.phase)));
            float wing = Mathf.Lerp(Mathf.Sin(phase), .25f, glide);
            Vector3 origin = transform.InverseTransformPoint(new Vector3(bird.x,
                bird.y + Mathf.Sin(bird.age * 1.15f + bird.phase) * .18f, transform.position.z));
            float depthScale = GetDepthScale(bird.age, bird.depthPhase, bird.depthPeriodFactor);
            float scale = Mathf.Max(.1f, size) * bird.scale * depthScale;
            DrawWing(start, origin, scale, wing, -1);
            DrawWing(start + WingVertices, origin, scale, wing, 1);
            int body = start + WingVertices * 2;
            Put(body, origin, scale, 0, -.027f);
            for (int segment = 0; segment < BodySegments; segment++)
            {
                float angle = segment * Mathf.PI * 2 / BodySegments;
                Put(body + 1 + segment, origin, scale, Mathf.Cos(angle) * .073f,
                    -.027f + Mathf.Sin(angle) * .102f);
            }
            // The beak attaches to the upper body and projects in the direction of flight.
            int beak = start + BeakOffset;
            Put(beak, origin, scale, .025f * bird.direction, .076f);
            Put(beak+1, origin, scale, .025f * bird.direction, .018f);
            Put(beak+2, origin, scale, .14f * bird.direction, .045f);
            float visibleDepth = bird.depth - (depthScale-1)*1.5f;
            Color tint = GetBirdColor(bird.paletteIndex, visibleDepth); tint.a *= visibility;
            for (int v = 0; v < VerticesPerBird; v++) colors[start + v] = tint;
            if (bird.whiteWingtips)
            {
                Color tipTint = Color.Lerp(new Color(.9f, .92f, .88f), distantColor,
                    Mathf.Clamp01(visibleDepth)*Mathf.Clamp01(distanceColorStrength)*.4f);
                tipTint.a = tint.a;
                for (int segment = 0; segment <= WingSegments; segment++)
                {
                    float blend = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.65f, .72f, WingPosition(segment)));
                    Color wingTint = Color.Lerp(tint, tipTint, blend);
                    for (int side = 0; side < 2; side++)
                    {
                        int v = start + side * WingVertices + segment * 2;
                        colors[v] = colors[v+1] = wingTint;
                    }
                }
            }
            Color bodyTint = tint * (1-Mathf.Clamp(bodyDarkening, 0, .8f)); bodyTint.a = tint.a;
            for (int v = body; v < beak; v++) colors[v] = bodyTint;
            Color yellow = Color.Lerp(beakColor, distantColor, Mathf.Clamp01(visibleDepth)*distanceColorStrength*.4f);
            yellow.a = beakColor.a * tint.a;
            colors[beak] = yellow;
            colors[beak+1] = new Color(yellow.r*.8f, yellow.g*.7f, yellow.b*.65f, yellow.a);
            colors[beak+2] = yellow;
        }
        mesh.vertices = vertices; mesh.colors = colors; mesh.RecalculateBounds();
    }

    void Put(int index, Vector3 origin, float scale, float x, float y) =>
        vertices[index] = origin + new Vector3(x, y, 0) * scale;

    Color GetBirdColor(int paletteIndex, float depth)
    {
        Color baseColor = paletteIndex == 1 ? warmColor : paletteIndex == 2 ? slateColor : color;
        return Color.Lerp(baseColor, distantColor, Mathf.Clamp01(depth) * Mathf.Clamp01(distanceColorStrength));
    }

    float GetDepthScale(float age, float phase, float periodFactor)
    {
        // Phase, age and period are shared by a flock; individual size ratios stay constant.
        float cycle = age * Mathf.PI * 2 / (Mathf.Max(4, depthMotionPeriod) * Mathf.Max(.1f, periodFactor));
        return 1 + Mathf.Clamp(flockDepthMotion, 0, .35f) * Mathf.Sin(cycle + phase);
    }

    void DrawWing(int start, Vector3 origin, float scale, float wing, float side)
    {
        var root = new Vector2(.025f, 0);
        var bend = new Vector2(.28f, wing * .28f + .12f);
        var tip = new Vector2(.62f, wing * .45f);
        for (int segment = 0; segment <= WingSegments; segment++)
        {
            float t = WingPosition(segment);
            // A curved ribbon with a rounded taper keeps every wing pose smooth.
            Vector2 center = (1-t)*(1-t)*root + 2*(1-t)*t*bend + t*t*tip;
            Vector2 tangent = ((1-t)*(bend-root) + t*(tip-bend)).normalized;
            Vector2 normal = new Vector2(-tangent.y, tangent.x);
            float width = .055f * (1-.55f*t) * Mathf.Sqrt(Mathf.Max(0, 1-Mathf.Pow(t, 8)));
            Vector2 top = center + normal * width, bottom = center - normal * width;
            Put(start + segment*2, origin, scale, top.x * side, top.y);
            Put(start + segment*2+1, origin, scale, bottom.x * side, bottom.y);
        }
    }

    // Place a mesh edge exactly at the beginning of the outer 35%.
    static float WingPosition(int segment) => segment <= 8
        ? segment / 8f * .65f : .65f + (segment-8) / (float)(WingSegments-8) * .35f;

    void OnDisable()
    {
        if (meshRenderer) Release(meshRenderer.gameObject);
        if (mesh) Release(mesh);
        meshRenderer = null; mesh = null;
    }

    static void Release(Object value)
    {
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
}
