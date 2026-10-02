using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed class LoadingCrystalVisual : MonoBehaviour
{
    public const float CycleSeconds = 1.5f;
    public const float ContactPhase = .62f;
    static readonly Vector2 Contact = new Vector2(39, 124);
    readonly List<Object> owned = new List<Object>();
    readonly Image[] glows = new Image[7];
    readonly Image[] sparks = new Image[24];
    readonly Image[] shards = new Image[12];
    readonly Image[] twinkles = new Image[8];
    readonly Image[] twinkleHalos = new Image[8];
    RectTransform pickaxe;
    Image flash, lantern;
    Image burst, afterglow, nucleus;
    Material liquid;
    AudioClip hitClip;
    AudioSource hitSource;
    int lastAudibleStroke = -1;
    float previousElapsed;

    public static float SwingAngle(float phase)
    {
        if (phase < .48f) return Mathf.Lerp(0, -32, Mathf.SmoothStep(0, 1, phase / .48f));
        if (phase < ContactPhase) { float t = (phase - .48f) / .14f; return Mathf.Lerp(-32, 0, t * t); }
        if (phase < .71f) return Mathf.Lerp(0, -4, Mathf.Sin((phase - ContactPhase) / .09f * Mathf.PI * .5f));
        return Mathf.Lerp(-4, 0, Mathf.SmoothStep(0, 1, (phase - .71f) / .12f));
    }

    public Image Build()
    {
        var glow = CreateGlow();
        var halo = Image("Crystal halo", Vector2.zero, new Vector2(1100, 950), glow);
        halo.color = new Color(0, .35f, .5f, .16f);
        Image("Crystal illustration", new Vector2(0, 170), new Vector2(1050, 700), Resources.Load<Sprite>("Loading/CrystalIllustration"));
        Vector2[] points = { new Vector2(-145, 443), new Vector2(-216, 325), new Vector2(-181, 168),
            new Vector2(-135, -7), new Vector2(186, -14), new Vector2(236, 189), new Vector2(274, 82) };
        for (int i = 0; i < glows.Length; i++) glows[i] = Image("Crystal light " + i, points[i], new Vector2(110, 150), glow);
        lantern = Image("Lantern light", new Vector2(-241, 115), new Vector2(110, 120), glow);
        var tool = Image("Animated pickaxe", new Vector2(454, 66), new Vector2(580, 580 * 2f / 3f), Resources.Load<Sprite>("Loading/Pickaxe"));
        pickaxe = tool.rectTransform;
        // Pivot is the grip end; the lower blade lands on Contact at zero rotation.
        pickaxe.pivot = new Vector2(1310f / 1536f, 70f / 1024f);
        flash = Image("Impact flash", Contact, new Vector2(170, 170), glow);
        var effects = Resources.Load<Texture2D>("Loading/ImpactAtlas");
        var lightShader = Shader.Find("UI/LoadingImpactLight");
        var lightMaterial = lightShader ? new Material(lightShader) : null;
        if (lightMaterial) owned.Add(lightMaterial);
        burst = Image("Impact rays", Contact, new Vector2(166, 185), EffectSprite(effects, 60, 825, 350, 390));
        burst.rectTransform.pivot = new Vector2(170f / 350, 145f / 390);
        afterglow = Image("Impact afterglow", Contact, new Vector2(152, 162), EffectSprite(effects, 505, 900, 255, 270));
        afterglow.rectTransform.pivot = new Vector2(130f / 255, 104f / 270);
        nucleus = Image("White hot impact", Contact, new Vector2(47, 57), EffectSprite(effects, 950, 920, 195, 235));
        nucleus.rectTransform.pivot = new Vector2(90f / 195, 95f / 235);
        burst.material = afterglow.material = nucleus.material = lightMaterial;
        var streaks = new[] { EffectSprite(effects, 160, 425, 125, 390), EffectSprite(effects, 580, 480, 110, 310), EffectSprite(effects, 995, 545, 105, 235) };
        for (int i = 0; i < sparks.Length; i++)
        {
            var size = i % 3 == 0 ? new Vector2(7, 35) : i % 3 == 1 ? new Vector2(7, 23) : new Vector2(6, 13);
            sparks[i] = Image("Impact spark " + i, Contact, size, streaks[i % 3]);
            sparks[i].material = lightMaterial;
        }
        var stones = new[] { EffectSprite(effects, 115, 140, 230, 210), EffectSprite(effects, 540, 105, 205, 255), EffectSprite(effects, 925, 155, 220, 195) };
        for (int i = 0; i < shards.Length; i++)
        {
            float size = 15 + i % 4 * 4;
            shards[i] = Image("Stone chip " + i, Contact, new Vector2(size, size), stones[i % 3]);
            shards[i].preserveAspect = true;
        }
        var atlas = Resources.Load<Texture2D>("Loading/ProgressAtlas");
        var channel = Slice(atlas, new Rect(0, 310, 2098, 320));
        var fillSprite = Slice(atlas, new Rect(240, 120, 1615, 115));
        Image("Crystal progress frame", new Vector2(0, -340), new Vector2(800, 800f * 320 / 2098), channel);
        var fill = Image("Progress", new Vector2(0, -340), new Vector2(581, 42), fillSprite);
        fill.type = UnityEngine.UI.Image.Type.Filled; fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal; fill.fillOrigin = 0;
        var shader = Shader.Find("UI/LoadingCrystalLiquid");
        if (shader) { liquid = new Material(shader); owned.Add(liquid); fill.material = liquid; }
        BuildTwinkles(glow);
        Animate(0);
        return fill;
    }

    public void Animate(float elapsed)
    {
        if (elapsed < previousElapsed) lastAudibleStroke = -1;
        previousElapsed = elapsed;
        int audibleStroke = Mathf.FloorToInt(elapsed / CycleSeconds - ContactPhase);
        if (audibleStroke >= 0 && audibleStroke > lastAudibleStroke)
        {
            lastAudibleStroke = audibleStroke;
            PlayImpactSound();
        }
        float phase = Mathf.Repeat(elapsed / CycleSeconds, 1);
        pickaxe.localRotation = Quaternion.Euler(0, 0, SwingAngle(phase));
        // Particle age is analytical: a slow loading frame cannot skip the hit trigger.
        float age = phase >= ContactPhase ? (phase - ContactPhase) * CycleSeconds : 10;
        flash.color = new Color(1, .63f, .17f, .95f * Mathf.Exp(-age * 18));
        flash.rectTransform.localScale = Vector3.one * (1 + Mathf.Min(age, .3f) * 2);
        burst.color = new Color(1, 1, 1, Mathf.Exp(-age * 10));
        burst.rectTransform.localScale = Vector3.one * (.85f + Mathf.Min(age, .2f) * 1.8f);
        afterglow.color = new Color(1, 1, 1, .7f * Mathf.Exp(-age * 8));
        afterglow.rectTransform.localScale = Vector3.one * (.9f + Mathf.Min(age, .25f) * 2);
        nucleus.color = new Color(1, 1, 1, Mathf.Exp(-age * 24));
        int stroke = Mathf.FloorToInt(elapsed / CycleSeconds);
        for (int i = 0; i < sparks.Length; i++)
        {
            float angle = (12 + (i * 137.508f + stroke * 23) % 156) * Mathf.Deg2Rad;
            float speed = 300 + i % 7 * 36;
            Vector2 velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
            float flight = Mathf.Min(age, .7f);
            sparks[i].rectTransform.anchoredPosition = Contact + velocity * flight + Vector2.down * flight * flight * 110;
            Vector2 tangent = velocity + Vector2.down * flight * 220;
            sparks[i].rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg - 90);
            sparks[i].color = new Color(1, 1, 1, 1 - Mathf.SmoothStep(0, 1, age / (.3f + i % 5 * .05f)));
            sparks[i].rectTransform.localScale = Vector3.one * Mathf.Lerp(1, .35f, Mathf.Clamp01(age / .5f));
        }
        for (int i = 0; i < shards.Length; i++)
        {
            float angle = (16 + (i * 97.1f + stroke * 17) % 150) * Mathf.Deg2Rad;
            float speed = 240 + i % 5 * 36;
            float flight = Mathf.Min(age, .7f);
            var velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
            shards[i].rectTransform.anchoredPosition = Contact + velocity * flight + Vector2.down * flight * flight * 240;
            shards[i].rectTransform.localRotation = Quaternion.Euler(0, 0, i * 37 + (i % 2 == 0 ? -1 : 1) * flight * (135 + i * 23));
            shards[i].color = new Color(1, 1, 1, 1 - Mathf.SmoothStep(0, 1, (age - .25f) / .29f));
        }
        for (int i = 0; i < glows.Length; i++)
        {
            float pulse = .5f + .5f * Mathf.Sin(elapsed * (1.1f + i * .07f) + i * 1.9f);
            glows[i].color = new Color(.08f, .8f, 1, .09f + pulse * .18f);
            glows[i].rectTransform.localScale = Vector3.one * (.9f + pulse * .22f);
        }
        float lanternPulse = .5f + .5f * Mathf.Sin(elapsed * 2.6f);
        float lanternFlicker = .025f * Mathf.Sin(elapsed * 8.1f) + .012f * Mathf.Sin(elapsed * 17.3f);
        lantern.color = new Color(1, .61f, .12f, .12f + lanternPulse * .27f + lanternFlicker);
        lantern.rectTransform.localScale = Vector3.one * (.9f + lanternPulse * .28f);
        if (liquid) liquid.SetFloat("_AnimationTime", elapsed);
        AnimateTwinkles(elapsed);
    }

    void BuildTwinkles(Sprite glow)
    {
        // Fixed anchors on actual crystal tips; each light has its own slower clock.
        Vector2[] tips = { new Vector2(-107, 501), new Vector2(-113, 284),
            new Vector2(-232, 377), new Vector2(-200, 28), new Vector2(-156, -17),
            new Vector2(251, 154), new Vector2(316, 63), new Vector2(350, -330) };
        var star = CreateTwinkle();
        for (int i = 0; i < twinkles.Length; i++)
        {
            float size = (i == 0 ? 52 : i == 7 ? 29 : 32 + i % 3 * 7) * 1.25f;
            twinkleHalos[i] = Image("Crystal twinkle halo " + i, tips[i], Vector2.one * size * 2.4f, glow);
            twinkles[i] = Image("Crystal twinkle " + i, tips[i], new Vector2(size * .84f, size), star);
            twinkles[i].rectTransform.localRotation = Quaternion.Euler(0, 0, i % 3 * 6 - 6);
        }
    }

    void AnimateTwinkles(float elapsed)
    {
        for (int i = 0; i < twinkles.Length; i++)
        {
            float period = 3.2f + i * .29f;
            float clock = Mathf.Max(0, elapsed) + i * 2.713f;
            int cycle = Mathf.FloorToInt(clock / period);
            float variation = .5f + .5f * Mathf.Sin(cycle * 7.13f + i * 2.31f);
            float duration = .8f + variation * .4f;
            float age = Mathf.Repeat(clock, period);
            float envelope = age < duration ? Mathf.Pow(Mathf.Sin(age / duration * Mathf.PI), 2) : 0;
            float brightness = envelope * (.9f + variation * .1f);
            twinkles[i].color = new Color(1, 1, 1, brightness);
            twinkles[i].rectTransform.localScale = Vector3.one * (.55f + envelope * (.65f + variation * .2f));
            twinkleHalos[i].color = new Color(.04f, .73f, 1, brightness * .7f);
            twinkleHalos[i].rectTransform.localScale = Vector3.one * (.7f + envelope * .5f);
        }
    }

    Sprite CreateTwinkle()
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "Crystal four-point light"; texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float u = Mathf.Abs((x + .5f) / size * 2 - 1);
            float v = Mathf.Abs((y + .5f) / size * 2 - 1);
            float radiusSquared = u * u + v * v;
            float vertical = Mathf.Exp(-u / (.012f + .045f * (1 - v))) * Mathf.Pow(1 - v, 1.5f);
            float horizontal = Mathf.Exp(-v / (.012f + .045f * (1 - u))) * Mathf.Pow(1 - u, 1.5f);
            float core = Mathf.Exp(-radiusSquared * 420);
            float alpha = Mathf.Max(core, Mathf.Max(vertical, horizontal));
            var color = Color.Lerp(new Color(.30f, .88f, 1), Color.white, Mathf.Exp(-radiusSquared * 9));
            color.a = alpha; pixels[y * size + x] = color;
        }
        texture.SetPixels(pixels); texture.Apply(false, true); owned.Add(texture);
        return Slice(texture, new Rect(0, 0, size, size));
    }

    void PlayImpactSound()
    {
        if (!Application.isPlaying) return;
        if (!hitClip) hitClip = Resources.Load<AudioClip>("Audio/LoadingPickaxeHit");
        if (!hitClip) return;
        if (!hitSource)
        {
            hitSource = gameObject.AddComponent<AudioSource>();
            hitSource.playOnAwake = false;
            hitSource.spatialBlend = 0f;
            hitSource.ignoreListenerPause = true;
        }
        hitSource.pitch = AudioManager.TunedPitch(hitClip, 1f);
        hitSource.PlayOneShot(hitClip, AudioManager.TunedVolume(hitClip,
            LoadingAudio.Settings ? LoadingAudio.Settings.pickaxeVolume : 1f));
    }

    void OnDisable()
    {
        if (hitSource) hitSource.Stop();
        lastAudibleStroke = -1;
        previousElapsed = 0f;
    }

    Image Image(string name, Vector2 position, Vector2 size, Sprite sprite)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(transform, false); image.rectTransform.anchoredPosition = position; image.rectTransform.sizeDelta = size;
        image.sprite = sprite; image.raycastTarget = false; return image;
    }
    Sprite Slice(Texture2D texture, Rect rect)
    {
        var sprite = Sprite.Create(texture, rect, Vector2.one * .5f, 100, 0, SpriteMeshType.FullRect);
        owned.Add(sprite); return sprite;
    }
    Sprite EffectSprite(Texture2D atlas, float x, float top, float width, float height)
    {
        // Atlas coordinates are authored from the top-left of the 1280px source.
        float scale = atlas.width / 1280f;
        return Slice(atlas, new Rect(x * scale, atlas.height - (top + height) * scale, width * scale, height * scale));
    }
    Sprite CreateGlow()
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false); texture.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float radius = new Vector2((x + .5f) / size * 2 - 1, (y + .5f) / size * 2 - 1).magnitude;
            pixels[y * size + x] = new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1 - radius), 2.5f));
        }
        texture.SetPixels(pixels); texture.Apply(false, true); owned.Add(texture);
        return Slice(texture, new Rect(0, 0, size, size));
    }
    void OnDestroy() { foreach (var asset in owned) if (asset) Destroy(asset); }
}
