using UnityEngine;

[DefaultExecutionOrder(-50), DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement), typeof(Rigidbody2D))]
public sealed class PlayerLadder : MonoBehaviour
{
    public LadderMap ladders;
    public StatsManager stats;
    [Min(.1f)] public float climbSpeed = 2f;
    public bool IsClimbing { get; private set; }
    public bool BuildMode { get; private set; }
    Rigidbody2D body;
    Collider2D bodyCollider;
    Camera view;
    SpriteRenderer preview;
    CompactHud hotbar;
    float gravity, reattachAt;

    void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();
        view = Camera.main;
        hotbar = FindFirstObjectByType<CompactHud>();
    }

    void Update()
    {
        if (GameplayInputBlocker.IsBlocked) { HidePreview(); return; }
        if (!hotbar) hotbar = FindFirstObjectByType<CompactHud>();
        var selected = hotbar ? hotbar.SelectedItem : null;
        if (Input.GetKeyDown(KeyCode.L) && hotbar)
        {
            int ladderSlot = System.Array.FindIndex(hotbar.slots, item => item && item.item == Item.Ladder) + 1;
            if (ladderSlot > 0) hotbar.SelectSlot(hotbar.SelectedSlot == ladderSlot ? 0 : ladderSlot);
            selected = hotbar.SelectedItem;
        }
        if (Input.GetKeyDown(KeyCode.Escape) && hotbar && selected && selected.item == Item.Ladder)
        {
            hotbar.SelectSlot(0);
            selected = null;
        }
        if (!view || !stats || TileMiner.IsPointerOverUi(Input.mousePosition)) { HidePreview(); return; }
        Vector3 world = view.ScreenToWorldPoint(Input.mousePosition);
        world.z = 0;
        var inventory = InventoryManager.Instance;
        if (Input.GetMouseButtonDown(1) && selected)
        {
            if (selected.item == Item.Ladder && ladders)
            {
                var cell = ladders.Map.Terrain.WorldToCell(world);
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                    ladders.TryRemove(cell, inventory, transform.position, stats.Reach);
                else
                    ladders.TryPlace(cell, inventory, transform.position, stats.Reach);
            }
            else if (selected.item == Item.Torche)
            {
                var map = ladders ? ladders.Map : FindFirstObjectByType<MapGenerator>();
                PlacedTorch.TryPlace(map, selected, world, transform.position, stats.Reach);
            }
        }

        bool showLadderPreview = BuildMode && ladders && selected && selected.item == Item.Ladder;
        if (!showLadderPreview) { HidePreview(); return; }
        var previewCell = ladders.Map.Terrain.WorldToCell(world);
        bool reachable = ladders.InReach(previewCell, transform.position, stats.Reach);
        bool occupied = ladders.Has(previewCell);
        bool valid = reachable && ladders.CanPlace(previewCell) && inventory && inventory.GetCount(ladders.ladderItem) > 0;
        ShowPreview(previewCell, valid ? new Color(.55f, 1, .55f, .65f) :
            occupied && reachable ? new Color(1, .8f, .3f, .7f) : new Color(1, .25f, .25f, .5f));
    }

    public void SetBuildMode(bool value) { BuildMode = value; if (!value) HidePreview(); }

    // Called by PlayerMovement before applying its normal movement, so only one controller writes velocity.
    public bool Step(float horizontal, float vertical, bool jump, bool flying)
    {
        if (flying || !ladders || !bodyCollider) { Detach(); return false; }
        if (GameplayInputBlocker.IsBlocked) { horizontal = vertical = 0; jump = false; }
        if (!ladders.FindContact(bodyCollider.bounds, out var cell)) { Detach(); return false; }
        if (IsClimbing && (jump || Mathf.Abs(horizontal) > .1f))
        {
            Detach();
            body.linearVelocity = new Vector2(horizontal * stats.EffectiveMoveSpeed, 0);
            if (jump) GetComponent<PlayerMovement>().LaunchJump();
            return true;
        }
        if (!IsClimbing)
        {
            if (Time.time < reattachAt || Mathf.Abs(vertical) < .1f || Mathf.Abs(horizontal) > .1f || jump) return false;
            gravity = body.gravityScale;
            body.gravityScale = 0;
            IsClimbing = true;
        }
        var tiles = ladders.Tiles;
        float effectiveClimbSpeed = climbSpeed * GameplayTestSettings.MovementMultiplier;
        float vx = Mathf.Clamp((tiles.GetCellCenterWorld(cell).x - bodyCollider.bounds.center.x) / Time.fixedDeltaTime,
            -effectiveClimbSpeed, effectiveClimbSpeed);
        float vy = vertical * effectiveClimbSpeed;
        if (vy > 0 && !ladders.Has(cell + Vector3Int.up))
        {
            // Feet can reach the last rung's top edge for a level sideways exit, without climbing into empty air.
            float top = tiles.CellToWorld(cell + Vector3Int.up).y;
            vy = Mathf.Min(vy, Mathf.Max(0, (top - bodyCollider.bounds.min.y) / Time.fixedDeltaTime));
        }
        body.linearVelocity = new Vector2(vx, vy);
        return true;
    }

    public void Detach()
    {
        if (!IsClimbing) return;
        if (body) body.gravityScale = gravity;
        IsClimbing = false;
        reattachAt = Time.time + .25f;
    }

    void ShowPreview(Vector3Int cell, Color color)
    {
        if (!ladders.segment) return;
        if (!preview)
        {
            var go = new GameObject("Ladder placement preview");
            preview = go.AddComponent<SpriteRenderer>();
            preview.sortingLayerName = "Default";
            preview.sortingOrder = 20;
            preview.sharedMaterial = ladders.ladderMaterial;
        }
        preview.sprite = ladders.segment.sprite;
        preview.transform.localScale = ladders.Map.Terrain.orientationMatrix.lossyScale;
        preview.transform.position = ladders.Map.Terrain.GetCellCenterWorld(cell);
        preview.color = color;
        preview.enabled = true;
    }

    void HidePreview() { if (preview) preview.enabled = false; }
    void OnDisable() { Detach(); BuildMode = false; HidePreview(); }
    void OnDestroy() { if (preview) Destroy(preview.gameObject); }
}
