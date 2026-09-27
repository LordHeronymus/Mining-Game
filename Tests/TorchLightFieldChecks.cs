using System;
using System.Collections.Generic;

public static class TorchLightFieldChecks
{
    public static object Main()
    {
        const int width = 9, height = 9;
        var solid = new bool[width * height];
        for (int y = 0; y < height; y++) solid[y * width + 4] = true;
        var field = new TorchLightField(width, height, solid, 1.1f, 1.1f,
            PlacedTorch.PropagationDistance);
        var left = new List<TorchLightField.Source> { new TorchLightField.Source(2, 4, .85f) };
        field.Rebuild(left);
        Check(field.Get(3, 4) > .1f, "Light does not reach the open cell beside the wall.");
        Check(field.Get(4, 4) > .05f, "The near wall face is unlit.");
        Check(field.Get(5, 4) == 0f, "Light crossed a sealed wall.");

        Check(field.SetSolid(4, 7, false), "Opening a wall cell was ignored.");
        field.Rebuild(left);
        float aroundCorner = field.Get(5, 6);
        Check(aroundCorner > 0f && aroundCorner < field.Get(3, 4),
            "Light did not weaken while travelling around a corner.");

        var two = new List<TorchLightField.Source>(left)
        { new TorchLightField.Source(6, 4, .85f) };
        field.Rebuild(two);
        float twoTorchBrightness = field.Get(5, 4);
        Check(twoTorchBrightness > aroundCorner, "A second torch did not illuminate its side.");
        field.Rebuild(left);
        Check(field.Get(5, 4) < twoTorchBrightness,
            "Removed torch light remained in the field.");

        Check(field.SetSolid(4, 4, false), "Opening the direct path was ignored.");
        field.Rebuild(left);
        Check(field.Get(5, 4) > aroundCorner,
            "Removing the blocking tile did not brighten the direct path.");
        return new { sealedWall = true, aroundCorner, twoTorchBrightness,
            openedPath = field.Get(5, 4) };
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
