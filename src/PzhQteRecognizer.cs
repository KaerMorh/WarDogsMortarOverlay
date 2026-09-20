using System.Drawing;

namespace WarDogs.Qte;

public record Arrow(string Direction, Rectangle Bounds, double Score, double Margin);
public record Recognition(Rectangle Region, IReadOnlyList<Arrow> Arrows)
{
    public bool Accepted => Arrows.Count == 4;
    public string Sequence => string.Concat(Arrows.Select(a => a.Direction));
}

// Receives pixels only, never filenames or expected answers.
public static class QteRecognizer
{
    public static Rectangle DefaultRegion(Size size) => new(
        (int)Math.Round(size.Width * .455), (int)Math.Round(size.Height * .557),
        (int)Math.Round(size.Width * .09), (int)Math.Round(size.Height * .042));

    public static Recognition Recognize(Bitmap image, Rectangle? region = null)
    {
        var roi = Rectangle.Intersect(new Rectangle(Point.Empty, image.Size), region ?? DefaultRegion(image.Size));
        return RecognizeCore(image, roi, image.Height);
    }

    public static Recognition RecognizeRegion(Bitmap regionImage, int referenceClientHeight)
    {
        return RecognizeCore(regionImage, new Rectangle(Point.Empty, regionImage.Size), referenceClientHeight);
    }

    static Recognition RecognizeCore(Bitmap image, Rectangle roi, int referenceClientHeight)
    {
        if (roi.Width <= 0 || roi.Height <= 0) return new(roi, []);
        int w = roi.Width, h = roi.Height;
        var mask = new bool[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var c = image.GetPixel(roi.X + x, roi.Y + y);
            int min = Math.Min(c.R, Math.Min(c.G, c.B)), max = Math.Max(c.R, Math.Max(c.G, c.B));
            mask[y * w + x] = min >= 160 && max - min <= 55;
        }

        var visited = new bool[mask.Length];
        var arrows = new List<Arrow>();
        for (int seed = 0; seed < mask.Length; seed++)
        {
            if (!mask[seed] || visited[seed]) continue;
            var pixels = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(seed); visited[seed] = true;
            int left = w, top = h, right = 0, bottom = 0;
            while (queue.TryDequeue(out int p))
            {
                pixels.Add(p);
                int x = p % w, y = p / w;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                    int n = ny * w + nx;
                    if (mask[n] && !visited[n]) { visited[n] = true; queue.Enqueue(n); }
                }
            }
            int bw = right - left + 1, bh = bottom - top + 1;
            // Relative to capture height; reject border lines, text and tiny noise.
            double unit = referenceClientHeight / 1440.0;
            if (bw < 20 * unit || bh < 20 * unit || bw > 45 * unit || bh > 45 * unit) continue;
            if ((double)bw / bh is < .65 or > 1.55) continue;
            double density = (double)pixels.Count / (bw * bh);
            if (density is < .35 or > .85) continue;
            var component = pixels.ToHashSet();
            var scores = Enumerable.Range(0, 4).Select(direction =>
            {
                int intersection = 0, union = 0;
                const int grid = 32;
                for (int y = 0; y < grid; y++)
                for (int x = 0; x < grid; x++)
                {
                    bool actual = component.Contains((top + y * bh / grid) * w + left + x * bw / grid);
                    bool template = Template((x + .5) / grid, (y + .5) / grid, direction);
                    if (actual && template) intersection++;
                    if (actual || template) union++;
                }
                return (Direction: direction, Score: (double)intersection / union);
            }).OrderByDescending(s => s.Score).ToArray();
            double margin = scores[0].Score - scores[1].Score;
            if (scores[0].Score < .70 || margin < .12) continue;
            arrows.Add(new(new[] { "左", "上", "右", "下" }[scores[0].Direction],
                new(roi.X + left, roi.Y + top, bw, bh), scores[0].Score, margin));
        }
        arrows.Sort((a, b) => a.Bounds.X.CompareTo(b.Bounds.X));
        if (arrows.Count != 4) return new(roi, []);
        double averageHeight = arrows.Average(a => a.Bounds.Height);
        var centersY = arrows.Select(a => a.Bounds.Top + a.Bounds.Height / 2.0).ToArray();
        var gaps = Enumerable.Range(1, 3).Select(i => (double)arrows[i].Bounds.X - arrows[i - 1].Bounds.X).ToArray();
        if (centersY.Max() - centersY.Min() > averageHeight * .2 ||
            gaps.Max() / gaps.Min() > 1.25 || gaps.Min() < averageHeight * 1.1 ||
            gaps.Max() > averageHeight * 2.5) return new(roi, []);
        return new(roi, arrows);
    }

    static bool Template(double x, double y, int direction)
    {
        (x, y) = direction switch { 1 => (y, 1 - x), 2 => (1 - x, 1 - y), 3 => (1 - y, x), _ => (x, y) };
        return x <= .5 ? Math.Abs(y - .5) <= x : Math.Abs(y - .5) <= .23;
    }
}
