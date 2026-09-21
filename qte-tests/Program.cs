using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using WarDogs.Qte;

Console.OutputEncoding = Encoding.UTF8;
string samples = Path.GetFullPath(args.Length > 0 ? args[0] : "test");
string output = Path.GetFullPath(args.Length > 1 ? args[1] : "artifacts/qte-tests");
Directory.CreateDirectory(output);
if (args.Contains("--benchmark"))
{
    var sample = Directory.GetFiles(samples, "*_QTE_*.jpg").Order().First();
    using var full = new Bitmap(sample);
    var roi = QteRecognizer.DefaultRegion(full.Size);
    using var crop = full.Clone(roi, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
    for (int i = 0; i < 30; i++) QteRecognizer.RecognizeRegion(crop, full.Height);
    var samplesMs = new double[200];
    var watch = new Stopwatch();
    for (int i = 0; i < samplesMs.Length; i++)
    {
        watch.Restart(); QteRecognizer.RecognizeRegion(crop, full.Height); watch.Stop();
        samplesMs[i] = watch.Elapsed.TotalMilliseconds;
    }
    Array.Sort(samplesMs);
    Console.WriteLine($"Region recognition: {samplesMs.Length} runs after 30 warmups; {crop.Width}x{crop.Height}; client height {full.Height}; average={samplesMs.Average():F2} ms, p95={samplesMs[189]:F2} ms, max={samplesMs[^1]:F2} ms. Excludes screenshot and disk decoding.");
    return 0;
}
if (args.Contains("--benchmark-capture"))
{
    var desktop = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("No screen");
    int w = Math.Min(230, desktop.Width), h = Math.Min(60, desktop.Height);
    using var frame = new Bitmap(w, h);
    using var graphics = Graphics.FromImage(frame);
    double Measure()
    {
        var sw = Stopwatch.StartNew();
        graphics.CopyFromScreen(desktop.Left + (desktop.Width - w) / 2, desktop.Top + (desktop.Height - h) / 2, 0, 0, new Size(w, h));
        QteRecognizer.RecognizeRegion(frame, desktop.Height);
        return sw.Elapsed.TotalMilliseconds;
    }
    try { for (int i = 0; i < 30; i++) Measure(); }
    catch (System.ComponentModel.Win32Exception ex)
    {
        Console.WriteLine($"Desktop capture unavailable in this session: {ex.Message}. Requires an interactive desktop.");
        return 2;
    }
    var times = Enumerable.Range(0, 200).Select(_ => Measure()).Order().ToArray();
    Console.WriteLine($"Desktop capture + recognition: {times.Length} runs after 30 warmups; {w}x{h}; reference height {desktop.Height}; average={times.Average():F2} ms, p95={times[189]:F2} ms, max={times[^1]:F2} ms. Desktop only; excludes game capture and disk decoding.");
    return 0;
}
var results = new List<object>();
int passed = 0, failed = 0;
void Check(string name, Recognition result, string expected)
{
    bool ok = result.Sequence == expected;
    if (ok) passed++; else failed++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}: expected={expected}, actual={result.Sequence}, scores={string.Join(",", result.Arrows.Select(a => a.Score.ToString("F3")))}");
    results.Add(new { name, expected, actual = result.Sequence, passed = ok, result });
}
var files = Directory.GetFiles(samples, "*_QTE_*.jpg").Order().ToArray();
if (files.Length != 3) throw new InvalidOperationException($"Expected three labeled samples, found {files.Length}");
foreach (var file in files)
{
    string name = Path.GetFileNameWithoutExtension(file);
    string expected = name.Split("_QTE_")[1];
    using var original = new Bitmap(file);
    foreach (double scale in new[] { 1.0, .8, .75, .5, 1.5 })
    {
        using var scaled = new Bitmap((int)(original.Width * scale), (int)(original.Height * scale));
        using (var graphics = Graphics.FromImage(scaled))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(original, new Rectangle(Point.Empty, scaled.Size));
        }
        var result = QteRecognizer.Recognize(scaled);
        Check($"{name} {scaled.Width}x{scaled.Height}", result, expected);
        var region = QteRecognizer.DefaultRegion(scaled.Size);
        using (var cropped = scaled.Clone(region, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
            Check($"{name} region {scaled.Width}x{scaled.Height}", QteRecognizer.RecognizeRegion(cropped, scaled.Height), expected);
        if (scale == 1)
        {
            using var crop = scaled.Clone(result.Region, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            crop.Save(Path.Combine(output, name + "_crop.png"));
        }
    }
    // Real background negatives at different vertical positions, same region dimensions.
    foreach (double y in new[] { .2, .4, .7 })
    {
        var roi = QteRecognizer.DefaultRegion(original.Size);
        roi.Y = (int)(original.Height * y);
        Check($"{name} background y={y}", QteRecognizer.Recognize(original, roi), "");
    }
    // Three arrows must not produce a complete sequence.
    using var partial = new Bitmap(original);
    var area = QteRecognizer.DefaultRegion(original.Size);
    using (var g = Graphics.FromImage(partial))
        g.FillRectangle(Brushes.Black, area.X, area.Y, area.Width / 4, area.Height);
    Check($"{name} incomplete", QteRecognizer.Recognize(partial), "");

    // A pressed arrow changes away from white (green on success, red on failure),
    // so the old group must no longer be accepted as a fresh four-white-arrow QTE.
    var white = QteRecognizer.Recognize(original);
    using var pressed = new Bitmap(original);
    var first = white.Arrows[0].Bounds;
    for (int y = first.Top; y < first.Bottom; y++)
    for (int x = first.Left; x < first.Right; x++)
    {
        var color = pressed.GetPixel(x, y);
        int min = Math.Min(color.R, Math.Min(color.G, color.B)), max = Math.Max(color.R, Math.Max(color.G, color.B));
        if (min >= 160 && max - min <= 55) pressed.SetPixel(x, y, Color.LimeGreen);
    }
    Check($"{name} pressed arrow is not white", QteRecognizer.Recognize(pressed), "");
}
using (var blank = new Bitmap(2560, 1440))
{
    Check("black frame", QteRecognizer.Recognize(blank), "");
    using (var g = Graphics.FromImage(blank)) g.Clear(Color.White);
    Check("white frame", QteRecognizer.Recognize(blank), "");
}
File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { passed, failed, results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{passed} passed; {failed} failed. Output: {output}");
return failed == 0 ? 0 : 1;
