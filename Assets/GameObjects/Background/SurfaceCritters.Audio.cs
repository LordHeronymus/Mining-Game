using UnityEngine;

public sealed partial class SurfaceCritters
{
    [Range(0f, 1f)] public float croakVolume = .35f;
    [Range(0f, 120f)] public float croaksPerMinute = 12f;
    [Range(0f, 1f)] public float croakIrregularity = .7f;

    readonly System.Random croakRandom = new System.Random();
    AudioSource croakSource;
    Critter callingFrog;
    float nextCroak = .5f;

    bool IsFrogVisible(Critter animal, Camera camera)
    {
        if (!camera || !camera.isActiveAndEnabled || geometry == null || !geometry.renderer.enabled ||
            (camera.cullingMask & (1 << geometry.renderer.gameObject.layer)) == 0 || animal.age < .1f) return false;
        float progress = animal.jumping ? Mathf.Clamp01(animal.travel / Mathf.Max(.1f, hopDuration)) : 0f;
        float lift = Mathf.Sin(progress * Mathf.PI) * Mathf.Max(.1f, hopHeight);
        var bounds = new Bounds(new Vector3(animal.x, surfaceY + lift + .25f * animal.scale, 0),
            new Vector3(1.05f * animal.scale, .55f * animal.scale, .02f));
        return GeometryUtility.TestPlanesAABB(viewPlanes, bounds);
    }

    void UpdateCroaking(float dt)
    {
        if (!Application.isPlaying || species != Species.Frog || croaksPerMinute <= 0f || croakVolume <= 0f)
        { StopCroaking(); return; }
        var camera = Camera.main;
        if (!camera) { StopCroaking(); return; }
        GeometryUtility.CalculateFrustumPlanes(camera, viewPlanes);
        Critter chosen = null;
        int visible = 0;
        foreach (var animal in animals)
            if (IsFrogVisible(animal, camera) && croakRandom.Next(++visible) == 0) chosen = animal;
        if (chosen == null) { StopCroaking(); return; }

        if (croakSource && croakSource.isPlaying)
        {
            if (callingFrog == null || !animals.Contains(callingFrog) || !IsFrogVisible(callingFrog, camera))
                croakSource.Stop();
            else ApplyCroakVolume(camera, callingFrog);
        }
        nextCroak -= Mathf.Max(0f, dt);
        if (nextCroak > 0f || (croakSource && croakSource.isPlaying)) return;
        if (!AudioManager.Instance) return;
        var clip = AudioManager.Instance.GetRandomFrogCroak();
        if (!clip) return;
        if (!croakSource)
        {
            croakSource = gameObject.AddComponent<AudioSource>();
            croakSource.playOnAwake = false;
            croakSource.loop = false;
            croakSource.spatialBlend = 0f;
        }
        callingFrog = chosen;
        croakSource.clip = clip;
        croakSource.pitch = 1f;
        ApplyCroakVolume(camera, chosen);
        croakSource.Play();
        float variation = Mathf.Clamp(-Mathf.Log(1f - (float)croakRandom.NextDouble()), .1f, 4f);
        nextCroak = 60f / Mathf.Clamp(croaksPerMinute, .01f, 120f) *
            Mathf.Lerp(1f, variation, Mathf.Clamp01(croakIrregularity));
    }

    void ApplyCroakVolume(Camera camera, Critter animal)
    {
        croakSource.volume = Mathf.Clamp01(croakVolume) * AudioManager.GetAmbienceVolume(AmbienceType.Frogs);
        croakSource.panStereo = Mathf.Clamp((camera.WorldToViewportPoint(
            new Vector3(animal.x, surfaceY, 0)).x - .5f) * 1.4f, -.7f, .7f);
    }

    void StopCroaking()
    {
        if (croakSource) croakSource.Stop();
        callingFrog = null;
        nextCroak = .5f;
    }
}
