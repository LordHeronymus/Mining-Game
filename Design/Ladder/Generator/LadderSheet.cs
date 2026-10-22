// Contact sheets for the ladder variants (Design/Ladder).
// Build: csc /r:System.Drawing.dll /out:<scratch>\LadderSheet.exe LadderSheet.cs
// tiles <out.png> <tilePx> <stack> <label=sprite.png>...   sprites stacked vertically on earth and stone backdrops
// row   <out.png> <height> <label=image.png>...             images scaled to one height, side by side with captions
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

static class LadderSheet
{
    static readonly Color Back = Color.FromArgb(28, 22, 18);
    static readonly Color Text = Color.FromArgb(240, 222, 190);

    static void Main(string[] args)
    {
        if (args[0] == "tiles") Tiles(args);
        else Row(args);
        Console.WriteLine("ok");
    }

    static List<KeyValuePair<string, string>> Pairs(string[] args, int start)
    {
        var list = new List<KeyValuePair<string, string>>();
        for (int i = start; i < args.Length; i++)
        {
            int eq = args[i].IndexOf('=');
            list.Add(new KeyValuePair<string, string>(args[i].Substring(0, eq), args[i].Substring(eq + 1)));
        }
        return list;
    }

    static void Tiles(string[] args)
    {
        int tile = int.Parse(args[2]), stack = int.Parse(args[3]);
        var items = Pairs(args, 4);
        int pad = 24, caption = 44;
        // Each sprite twice: on earth brown and on grey stone.
        int w = pad + items.Count * (tile * 2 + pad * 2), h = caption + stack * tile + pad * 2;
        using (var bmp = new Bitmap(w, h))
        using (var g = Graphics.FromImage(bmp))
        {
            Setup(g);
            g.Clear(Back);
            int x = pad;
            foreach (var item in items)
            {
                using (var img = Image.FromFile(item.Value))
                {
                    var earth = Color.FromArgb(92, 60, 36);
                    var stone = Color.FromArgb(96, 96, 100);
                    for (int k = 0; k < 2; k++)
                    {
                        int ox = x + k * (tile + pad);
                        using (var b = new SolidBrush(k == 0 ? earth : stone)) g.FillRectangle(b, ox, caption, tile, stack * tile);
                        for (int s = 0; s < stack; s++) g.DrawImage(img, new Rectangle(ox, caption + s * tile, tile, tile));
                    }
                }
                Caption(g, item.Key, x, 6, tile * 2 + pad, caption - 10);
                x += tile * 2 + pad * 2;
            }
            bmp.Save(args[1], ImageFormat.Png);
        }
    }

    static void Row(string[] args)
    {
        int height = int.Parse(args[2]);
        var items = Pairs(args, 3);
        var images = new List<Image>();
        int pad = 20, caption = 52, w = pad;
        foreach (var item in items)
        {
            var img = Image.FromFile(item.Value);
            images.Add(img);
            w += img.Width * height / img.Height + pad;
        }
        using (var bmp = new Bitmap(w, height + caption + pad))
        using (var g = Graphics.FromImage(bmp))
        {
            Setup(g);
            g.Clear(Back);
            int x = pad;
            for (int i = 0; i < images.Count; i++)
            {
                int iw = images[i].Width * height / images[i].Height;
                g.DrawImage(images[i], new Rectangle(x, caption, iw, height));
                Caption(g, items[i].Key, x, 8, iw, caption - 14);
                x += iw + pad;
                images[i].Dispose();
            }
            bmp.Save(args[1], ImageFormat.Png);
        }
    }

    static void Setup(Graphics g)
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }

    static void Caption(Graphics g, string text, int x, int y, int w, int h)
    {
        using (var font = new Font("Segoe UI Semibold", h * 0.52f, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(Text))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            g.DrawString(text, font, brush, new RectangleF(x, y, w, h), format);
    }
}
