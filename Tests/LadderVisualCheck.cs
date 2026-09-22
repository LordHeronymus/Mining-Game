using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class LadderVisualCheck
{
    struct Cell
    {
        public Tilemap map; public Vector3Int position; public TileBase tile;
        public Matrix4x4 matrix; public Color color; public TileFlags flags;
        public Cell(Tilemap map, Vector3Int position)
        {
            this.map = map; this.position = position; tile = map.GetTile(position);
            matrix = map.GetTransformMatrix(position); color = map.GetColor(position); flags = map.GetTileFlags(position);
        }
        public void Restore()
        {
            map.SetTile(position, tile);
            map.SetTileFlags(position, TileFlags.None);
            map.SetTransformMatrix(position, matrix); map.SetColor(position, color); map.SetTileFlags(position, flags);
        }
    }

    public static async Task<string> Run()
    {
        var player = Object.FindFirstObjectByType<PlayerLadder>();
        var map = player.ladders.Map;
        var movement = player.GetComponent<PlayerMovement>();
        var miner = player.GetComponent<TileMiner>();
        var body = player.GetComponent<Rigidbody2D>();
        var camera = Camera.main;
        var follow = camera.GetComponent<CameraFollow>();
        var savedPosition = body.position; var velocity = body.linearVelocity; float gravity = body.gravityScale;
        var cameraPosition = camera.transform.position; float size = camera.orthographicSize;
        bool moveEnabled = movement.enabled; bool followEnabled = follow.enabled;
        bool mineEnabled = miner.enabled;
        var cells = new List<Cell>();
        try
        {
            movement.enabled = false; follow.enabled = false; miner.enabled = false;
            for (int y = 0; y >= -12; y--)
                for (int x = 36; x <= 38; x++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    cells.Add(new Cell(map.Terrain, cell)); cells.Add(new Cell(map.OreOverlay, cell));
                    cells.Add(new Cell(map.GrassOverlay, cell)); cells.Add(new Cell(player.ladders.Tiles, cell));
                    map.Terrain.SetTile(cell, null); map.OreOverlay.SetTile(cell, null); map.GrassOverlay.SetTile(cell, null);
                    if (x == 37) player.ladders.Tiles.SetTile(cell, player.ladders.segment);
                }
            body.position = new Vector2(18.75f, -1.4f); body.linearVelocity = Vector2.zero; body.gravityScale = 0;
            camera.transform.position = new Vector3(16, -.8f, -10); camera.orthographicSize = 5;
            Physics2D.SyncTransforms();
            await Task.Delay(700);
            body.gravityScale = gravity;
            player.Step(0, 1, false, false);
            player.Step(0, 0, false, false);
            await Task.Delay(700);
            string path = Path.GetFullPath("Design/LadderImplemented.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            ScreenCapture.CaptureScreenshot(path);
            await Task.Delay(700);
            return path;
        }
        finally
        {
            player.Detach();
            foreach (var cell in cells) cell.Restore();
            player.ladders.Tiles.CompressBounds();
            body.position = savedPosition; body.linearVelocity = velocity; body.gravityScale = gravity;
            camera.transform.position = cameraPosition; camera.orthographicSize = size;
            follow.enabled = followEnabled; movement.enabled = moveEnabled; miner.enabled = mineEnabled;
            Physics2D.SyncTransforms();
        }
    }
}
