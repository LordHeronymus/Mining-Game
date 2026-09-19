using UnityEngine;

[DisallowMultipleComponent]
public sealed class SurfaceRabbit : MonoBehaviour
{
    public MapGenerator map;
    public Material material;
    [Min(.2f), InspectorName("Grösse")] public float size = .85f;
    [Min(1), InspectorName("Bewegungsradius")] public float roamRadius = 6;
    [Min(.1f), InspectorName("Hüpfhöhe")] public float hopHeight = .45f;
    [Min(.2f), InspectorName("Hüpfdauer (s)")] public float hopDuration = .52f;
    [Min(0), InspectorName("Wartezeit vor dem Auftauchen (s)")] public float entryDelay = 3f;
    [InspectorName("Pausen beim Reinhoppeln (s)")] public Vector2 entryRestDuration = new Vector2(1.5f, 3f);
    [InspectorName("Ruhephase (s)")] public Vector2 restDuration = new Vector2(4, 8);
    [InspectorName("Abstand zwischen Ruhephasen (s)")] public Vector2 restInterval = new Vector2(8, 15);
    [InspectorName("Fellfarbe")] public Color furColor = new Color(.94f, .955f, .98f);

    const int Segments = 20, Parts = 26, Stride = Segments + 1;
    readonly Vector3[] vertices = new Vector3[Parts * Stride];
    readonly Color[] colors = new Color[Parts * Stride];
    readonly System.Random random = new System.Random();
    Mesh mesh;
    MeshRenderer meshRenderer;
    float homeX, surfaceY, fromX, toX, hopTime, wait, age, retry;
    float restTime, untilRest, lookTime, headTilt;
    int direction = 1, hopsRemaining, entryHopsUntilRest, part;
    bool ready, hopping, entering, returning, resting;

    void OnEnable()
    {
        // Generated geometry is rebuilt after a script reload.
        for (int i = transform.childCount-1; i >= 0; i--)
        {
            var old = transform.GetChild(i);
            if (old.name != "Rabbit shape (generated)") continue;
            old.gameObject.SetActive(false);
            var filter = old.GetComponent<MeshFilter>();
            if (filter && filter.sharedMesh) Release(filter.sharedMesh);
            Release(old.gameObject);
        }
        homeX = transform.position.x; ready = hopping = entering = returning = false; retry = 0; age = 0;
        resting = false; headTilt = 0; untilRest = RandomSeconds(restInterval);
        if (!map || !material) return;
        var child = new GameObject("Rabbit shape (generated)") { hideFlags = HideFlags.DontSave };
        child.layer = gameObject.layer; child.transform.SetParent(transform, false);
        mesh = new Mesh { name = "Surface rabbit", hideFlags = HideFlags.DontSave };
        mesh.MarkDynamic(); mesh.vertices = vertices; mesh.uv = new Vector2[vertices.Length];
        var indices = new int[Parts * Segments * 3]; int index = 0;
        for (int p = 0; p < Parts; p++)
            for (int s = 0; s < Segments; s++)
            {
                indices[index++] = p * Stride;
                indices[index++] = p * Stride + 1 + s;
                indices[index++] = p * Stride + 1 + (s+1)%Segments;
            }
        mesh.triangles = indices;
        child.AddComponent<MeshFilter>().sharedMesh = mesh;
        meshRenderer = child.AddComponent<MeshRenderer>(); meshRenderer.sharedMaterial = material;
        meshRenderer.sortingLayerName = "Default"; meshRenderer.sortingOrder = 20;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
    }

    float Random(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
    float RandomSeconds(Vector2 range) => Random(Mathf.Max(.5f, Mathf.Min(range.x, range.y)),
        Mathf.Max(.5f, Mathf.Max(range.x, range.y)));

    void BeginRest()
    {
        resting = true; restTime = RandomSeconds(entering ? entryRestDuration : restDuration);
        lookTime = Random(.8f, 1.4f); hopsRemaining = 0;
    }

    void Update() => Tick(Time.deltaTime);

    public void CopySettingsFrom(SurfaceRabbit source)
    {
        map = source.map; material = source.material; size = source.size;
        roamRadius = source.roamRadius; hopHeight = source.hopHeight; hopDuration = source.hopDuration;
        entryDelay = source.entryDelay; entryRestDuration = source.entryRestDuration;
        restDuration = source.restDuration; restInterval = source.restInterval; furColor = source.furColor;
    }

    public bool TrySpawn()
    {
        if (!map || !map.IsGenerated) return false;
        surfaceY = map.Terrain.CellToWorld(new Vector3Int(0,1,0)).y;
        if (!FindEntrance(out float x, out float center)) return false;
        homeX = center;
        transform.position = new Vector3(x, surfaceY, transform.position.z);
        direction = x < center ? 1 : -1;
        ready = entering = true; hopping = returning = resting = false;
        wait = Mathf.Max(0, entryDelay); untilRest = RandomSeconds(restInterval);
        entryHopsUntilRest = random.Next(2, 5);
        if (meshRenderer) { meshRenderer.enabled = true; Draw(0,0); }
        return true;
    }

    void Tick(float deltaTime)
    {
        if (!meshRenderer) return;
        if (!map.IsGenerated) { ready = false; meshRenderer.enabled = false; return; }
        age += deltaTime;
        headTilt = Mathf.MoveTowards(headTilt, resting ? Mathf.Sin(age*.9f)*8 : 0, deltaTime*25);
        surfaceY = map.Terrain.CellToWorld(new Vector3Int(0,1,0)).y;
        if (!ready)
        {
            meshRenderer.enabled = false;
            retry -= deltaTime; if (retry > 0) return;
            retry = 1;
            if (!TrySpawn()) return;
        }
        meshRenderer.enabled = true;
        if (!entering && !resting) untilRest -= deltaTime;
        float lift = 0, progress = 0;
        if (hopping)
        {
            // Mining a gap during a hop reverses the existing arc instead of crossing the gap.
            if (!returning && !HasContinuousGround(fromX, toX))
            {
                returning = true; direction = -direction;
                entering = false; homeX = fromX; hopsRemaining = 0;
            }
            hopTime = returning ? Mathf.Max(0, hopTime-deltaTime) : hopTime+deltaTime;
            progress = Mathf.Clamp01(hopTime / Mathf.Max(.2f, hopDuration));
            lift = Mathf.Sin(progress * Mathf.PI) * Mathf.Max(.1f, hopHeight);
            transform.position = new Vector3(Mathf.Lerp(fromX, toX, progress), surfaceY + lift, transform.position.z);
            if ((!returning && progress >= 1) || (returning && progress <= 0))
            {
                hopping = false;
                if (!HasGround(transform.position.x)) { ready = false; retry = 0; meshRenderer.enabled = false; }
                if (entering && Mathf.Abs(transform.position.x-homeX) < .01f)
                {
                    entering = false; hopsRemaining = 0;
                    BeginRest();
                }
                else if (entering)
                {
                    if (--entryHopsUntilRest <= 0) BeginRest();
                    else wait = .13f;
                }
                else wait = --hopsRemaining > 0 ? .13f : Random(.9f, 2.4f);
            }
        }
        else
        {
            if (!HasGround(transform.position.x)) { ready = false; retry = 0; meshRenderer.enabled = false; return; }
            if (resting || (!entering && untilRest <= 0))
            {
                if (!resting) BeginRest();
                restTime -= deltaTime; lookTime -= deltaTime;
                if (lookTime <= 0)
                {
                    direction = -direction; lookTime = Random(1.3f, 2.4f);
                }
                if (restTime <= 0)
                {
                    resting = false; untilRest = RandomSeconds(restInterval); wait = .25f;
                    if (entering)
                    {
                        direction = transform.position.x < homeX ? 1 : -1;
                        entryHopsUntilRest = random.Next(2, 5);
                    }
                }
                Draw(0, 0);
                return;
            }
            wait -= deltaTime;
            if (wait <= 0)
            {
                if (!entering && hopsRemaining <= 0)
                {
                    hopsRemaining = random.Next(2, 5);
                    if (random.NextDouble() < .3) direction = -direction;
                }
                bool found = false;
                for (int attempt = 0; attempt < 2 && !found; attempt++)
                {
                    float distance = Random(.65f, 1.25f);
                    if (entering) distance = Mathf.Min(distance, Mathf.Abs(homeX-transform.position.x));
                    float candidate = transform.position.x + direction * distance;
                    if ((entering || Mathf.Abs(candidate-homeX) <= Mathf.Max(1,roamRadius)) &&
                        HasContinuousGround(transform.position.x, candidate))
                    {
                        fromX = transform.position.x; toX = candidate;
                        hopping = true; returning = false; hopTime = 0; found = true;
                    }
                    else
                    {
                        direction = -direction;
                        if (entering) { entering = false; homeX = transform.position.x; hopsRemaining = 0; }
                    }
                }
                if (!found) wait = 1;
            }
        }
        Draw(lift, progress);
    }

    bool HasGround(float x)
    {
        return HasContinuousGround(x, x);
    }

    bool HasContinuousGround(float startX, float endX)
    {
        var tiles = map.Terrain;
        float margin = .2f*Mathf.Max(.2f,size)+.02f;
        var left = tiles.WorldToCell(new Vector3(Mathf.Min(startX,endX)-margin, surfaceY-.01f, tiles.transform.position.z));
        var right = tiles.WorldToCell(new Vector3(Mathf.Max(startX,endX)+margin, surfaceY-.01f, tiles.transform.position.z));
        if (left.y != 0 || right.y != 0) return false;
        for (int x = Mathf.Min(left.x,right.x); x <= Mathf.Max(left.x,right.x); x++)
        {
            if (!tiles.HasTile(new Vector3Int(x,0,0))) return false;
        }
        return true;
    }

    bool FindEntrance(out float spawnX, out float centerX)
    {
        spawnX = centerX = 0;
        var camera = Camera.main;
        if (!camera) return false;
        float distance = Vector3.Dot(transform.position-camera.transform.position, camera.transform.forward);
        if (distance <= 0) return false;
        var left = camera.ViewportToWorldPoint(new Vector3(0,.5f,distance));
        var right = camera.ViewportToWorldPoint(new Vector3(1,.5f,distance));
        if (!FindPerch((left.x+right.x)*.5f, out centerX)) return false;
        int preferredSide = random.Next(2) == 0 ? -1 : 1;
        for (int sideIndex = 0; sideIndex < 2; sideIndex++)
        {
            int side = sideIndex == 0 ? preferredSide : -preferredSide;
            float edge = side < 0 ? Mathf.Min(left.x,right.x) : Mathf.Max(left.x,right.x);
            for (int step = 0; step <= 16; step++)
            {
                float candidate = edge + side*(Mathf.Max(1,size)+step*.25f);
                if (!HasContinuousGround(candidate, centerX)) continue;
                spawnX = candidate; return true;
            }
        }
        return false;
    }

    bool FindPerch(float center, out float result)
    {
        for (int i = 0; i <= Mathf.CeilToInt(Mathf.Max(1,roamRadius)*4); i++)
            for (int sign = -1; sign <= 1; sign += 2)
            {
                float x = center + i * .25f * sign;
                if (HasGround(x)) { result = x; return true; }
            }
        result = center; return false;
    }

    void Draw(float lift, float progress)
    {
        part = 0;
        float stretch = hopping ? Mathf.Sin(progress*Mathf.PI)*.045f : Mathf.Sin(age*2)*.005f;
        float earSway = Mathf.Sin(age*2.2f)*5 + (hopping ? -12*Mathf.Sin(progress*Mathf.PI) : 0);
        Color shade = new Color(furColor.r*.7f, furColor.g*.69f, furColor.b*.67f);
        Color light = Color.Lerp(furColor, Color.white, .5f);
        Color pink = new Color(.78f,.49f,.49f);
        // Shadow stays on the ground while the rabbit rises.
        Ellipse(0, -lift/Mathf.Max(.2f,size)+.015f, .31f*(1-lift*.3f), .027f,
            new Color(.08f,.1f,.12f,.22f));
        Ellipse(-.30f,.23f,.10f,.095f,shade);
        Ellipse(-.315f,.245f,.085f,.077f,light);
        HeadEllipse(.105f,.54f,.047f,.205f,shade,20+earSway);
        HeadEllipse(.105f,.55f,.028f,.165f,pink,20+earSway);
        HeadEllipse(.23f,.56f,.047f,.22f,furColor,-8+earSway);
        HeadEllipse(.23f,.57f,.025f,.177f,pink,-8+earSway);
        Ellipse(-.08f,.21f,.277f+stretch,.18f-stretch*.5f,shade);
        Ellipse(-.07f,.231f,.257f+stretch,.158f-stretch*.5f,furColor);
        Ellipse(.09f,.20f,.135f,.135f,light);
        Ellipse(-.18f,.13f,.125f,.12f,shade);
        Ellipse(-.175f,.14f,.107f,.104f,furColor);
        float foot = hopping ? -.045f*Mathf.Sin(progress*Mathf.PI) : 0;
        Ellipse(-.135f+foot,.039f,.135f,.038f,shade);
        Ellipse(-.12f+foot,.052f,.12f,.035f,light);
        Ellipse(.17f-foot,.043f,.078f,.032f,shade);
        Ellipse(.184f-foot,.053f,.067f,.028f,light);
        HeadEllipse(.20f,.348f,.146f,.134f,shade);
        HeadEllipse(.209f,.36f,.132f,.12f,furColor);
        HeadEllipse(.30f,.314f,.085f,.057f,light);
        bool blink = Mathf.Repeat(age,4.8f)>4.64f;
        HeadEllipse(.273f,.385f,.018f,blink?.004f:.024f,new Color(.045f,.035f,.025f));
        if (!blink)HeadEllipse(.278f,.394f,.005f,.007f,Color.white);
        HeadEllipse(.375f,.331f,.019f,.013f,new Color(.54f,.32f,.32f));
        HeadEllipse(.354f,.292f,.019f,.003f,shade,-14);
        while(part < Parts)
        {
            for(int v=0;v<Stride;v++){vertices[part*Stride+v]=Vector3.zero;colors[part*Stride+v]=Color.clear;}
            part++;
        }
        mesh.vertices = vertices; mesh.colors = colors; mesh.RecalculateBounds();
    }

    void HeadEllipse(float x,float y,float rx,float ry,Color tint,float angle=0)
    {
        float radians=headTilt*Mathf.Deg2Rad,cos=Mathf.Cos(radians),sin=Mathf.Sin(radians);
        float dx=x-.18f,dy=y-.32f;
        Ellipse(.18f+dx*cos-dy*sin,.32f+dx*sin+dy*cos,rx,ry,tint,angle+headTilt);
    }

    void Ellipse(float x,float y,float rx,float ry,Color tint,float angle=0)
    {
        int start = part++*Stride;
        float radians=angle*Mathf.Deg2Rad,cos=Mathf.Cos(radians),sin=Mathf.Sin(radians);
        vertices[start]=new Vector3(x*direction,y,0)*size; colors[start]=tint;
        for(int i=0;i<Segments;i++)
        {
            float theta=i*Mathf.PI*2/Segments,px=Mathf.Cos(theta)*rx,py=Mathf.Sin(theta)*ry;
            vertices[start+1+i]=new Vector3((x+px*cos-py*sin)*direction,y+px*sin+py*cos,0)*size;
            colors[start+1+i]=tint;
        }
    }

    void OnDisable()
    {
        if(meshRenderer)Release(meshRenderer.gameObject);
        if(mesh)Release(mesh);
        meshRenderer=null;mesh=null;
    }
    static void Release(Object value)
    {
        if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);
    }
}
