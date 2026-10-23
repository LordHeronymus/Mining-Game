using UnityEngine;
using UnityEngine.UI;

// Gold seal (ChallengeArt part 7) with the tick as its own layer, so F1 → UI → Challenge-Siegel can nudge it.
// The atlas already holds the centred tick; at offset 0 the layer stays off. The layer carries the seal ground around
// the tick and covers the painted one for offsets up to MaxOffset image pixels.
[RequireComponent(typeof(Image))]
public sealed class ChallengeSealCheck : MonoBehaviour
{
    public const float MaxOffset = 4f;
    const float SealWidth = 262, SealHeight = 253;
    Image seal, check;

    public static Image Attach(Image seal)
    {
        var rect = HomeUi.Rect("Seal Check", seal.transform, Vector2.zero, Vector2.zero);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = ChallengeArt.SealCheck(); image.preserveAspect = true; image.raycastTarget = false; image.enabled = false;
        var sync = seal.gameObject.AddComponent<ChallengeSealCheck>(); sync.seal = seal; sync.check = image;
        sync.LateUpdate(); return image;
    }

    // Offset in pixels of the seal image (+ = right / up), from the GPS preferences.
    public static Vector2 Offset
    {
        get
        {
            var preferences = GpsSettings.Preferences;
            return new Vector2(Mathf.Clamp(preferences.sealCheckOffsetX, -MaxOffset, MaxOffset), Mathf.Clamp(preferences.sealCheckOffsetY, -MaxOffset, MaxOffset));
        }
    }

    void LateUpdate()
    {
        if (!seal || !check) return;
        var offset = Offset;
        check.enabled = seal.enabled && check.sprite && offset != Vector2.zero;
        if (!check.enabled) return;
        check.color = seal.color;
        // Both images fit the seal into the same rect (preserveAspect); one image pixel is this many UI units.
        var size = seal.rectTransform.rect.size;
        check.rectTransform.anchoredPosition = offset * Mathf.Min(size.x / SealWidth, size.y / SealHeight);
    }
}
