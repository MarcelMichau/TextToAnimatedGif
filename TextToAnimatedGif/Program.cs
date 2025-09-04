using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;

// Defaults
string text = "Hello!";
string outputPath = "output.gif";
int frameCount = 48;
int depth = 16;

// Parse args
if (args.Length > 0)
{
    text = args[0]; // first arg = text
}

for (int i = 1; i < args.Length; i++)
{
    switch (args[i].ToLower())
    {
        case "--output":
        case "-o":
            if (i + 1 < args.Length) outputPath = args[++i];
            break;
        case "--frames":
        case "-f":
            if (i + 1 < args.Length && int.TryParse(args[++i], out int f))
                frameCount = f;
            break;
        case "--depth":
        case "-d":
            if (i + 1 < args.Length && int.TryParse(args[++i], out int d))
                depth = d;
            break;
    }
}

Console.WriteLine($"Generating GIF...");
Console.WriteLine($" Text:     {text}");
Console.WriteLine($" Output:   {outputPath}");
Console.WriteLine($" Frames:   {frameCount}");
Console.WriteLine($" Depth:    {depth}");

TextToAnimatedGif.GenerateGif(
    text: text,
    outputPath: outputPath,
    frameCount: frameCount,
    depth: depth
);

Console.WriteLine("Done!");

internal class TextToAnimatedGif
{
    public static void GenerateGif(
        string text,
        string outputPath,
        int frameCount,
        int depth)
    {
        // Calculate dynamic size based on text
        int fontSize = 72;
        int paddingX = (int)(fontSize * 1.2f + depth * 2.0f); // Increased for 3D/animation
        int paddingY = (int)(fontSize * 2.0f + depth * 3.0f); // Increased for 3D/animation
        int textWidth, textHeight;
        using (var typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold))
        using (var font = new SKFont(typeface, fontSize))
        {
            var bounds = new SKRect();
            font.MeasureText(text, out bounds);
            textWidth = (int)Math.Ceiling(bounds.Width);
            textHeight = (int)Math.Ceiling(bounds.Height);
        }
        int width = textWidth + paddingX;
        int height = textHeight + paddingY;

        using var gif = new Image<Rgba32>(width, height);

        for (int i = 0; i < frameCount; i++)
        {
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(248, 248, 255));

            // Use SKFont instead of obsolete SKPaint.TextSize/Typeface
            using var typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
            using var font = new SKFont(typeface, 72);

            using var paint = new SKPaint
            {
                IsAntialias = true,
                IsStroke = false
            };

            // Measure for centering using SKFont
            var bounds = new SKRect();
            font.MeasureText(text, out bounds);
            float x = -bounds.MidX;
            float y = -bounds.MidY;

            // Normalized time [0..1]
            float t = i / (float)(frameCount - 1);

            // Loop time smoothly (0→1→0)
            float cycle = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * t)));

            // Apply easing
            float zoomEase = EaseInOutSine(cycle);
            float rotateEase = EaseInOutQuad(cycle);

            // Animate
            float scale = 0.9f + 0.2f * zoomEase;
            float rotation = -10f + 20f * rotateEase;

            // Color cycle
            float hue = (i / (float)frameCount) * 360f;
            var faceColor = HsvToSkColor(hue, 1f, 1f);
            var sideColor = HsvToSkColor((hue + 300) % 360, 1f, 0.6f);

            canvas.Translate(width / 2f, height / 2f + 10);
            canvas.Scale(scale);
            canvas.RotateDegrees(rotation);

            DrawExtrudedText(canvas, text, font, paint, x, y, depth, faceColor, sideColor);

            // Export frame
            using var snapshot = surface.Snapshot();
            using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
            using var ms = new MemoryStream();
            data.SaveTo(ms);
            ms.Position = 0;

            var frame = Image.Load<Rgba32>(ms);
            gif.Frames.AddFrame(frame.Frames[0]);

            var meta = gif.Frames[^1].Metadata.GetFormatMetadata(GifFormat.Instance);
            meta.FrameDelay = 6;
        }

        gif.Frames.RemoveFrame(0);
        gif.Metadata.GetFormatMetadata(GifFormat.Instance).RepeatCount = 0;
        gif.Save(outputPath, new GifEncoder());
    }

    private static void DrawExtrudedText(SKCanvas canvas, string text, SKFont font, SKPaint paint,
        float x, float y, int depth,
        SKColor faceColor, SKColor sideColor)
    {
        var originalColor = paint.Color;

        for (int d = depth; d >= 1; d--)
        {
            float tt = d / (float)depth;
            var c = LerpColor(sideColor, SKColors.Black, 0.25f * tt);
            paint.Color = c;
            canvas.DrawText(text, x + d, y + d, SKTextAlign.Left, font, paint);
        }

        paint.Color = faceColor;
        canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
        paint.Color = originalColor;
    }

    private static SKColor LerpColor(SKColor a, SKColor b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        byte r = (byte)(a.Red + (b.Red - a.Red) * t);
        byte g = (byte)(a.Green + (b.Green - a.Green) * t);
        byte bl = (byte)(a.Blue + (b.Blue - a.Blue) * t);
        byte al = (byte)(a.Alpha + (b.Alpha - a.Alpha) * t);
        return new SKColor(r, g, bl, al);
    }

    private static SKColor HsvToSkColor(float h, float s, float v)
    {
        h = h % 360f;
        int hi = (int)(h / 60f) % 6;
        float f = h / 60f - (int)(h / 60f);

        v = v * 255f;
        byte V = (byte)v;
        byte p = (byte)(v * (1 - s));
        byte q = (byte)(v * (1 - f * s));
        byte t = (byte)(v * (1 - (1 - f) * s));

        return hi switch
        {
            0 => new SKColor(V, t, p),
            1 => new SKColor(q, V, p),
            2 => new SKColor(p, V, t),
            3 => new SKColor(p, q, V),
            4 => new SKColor(t, p, V),
            _ => new SKColor(V, p, q),
        };
    }

    private static float EaseInOutSine(float t) =>
        -(float)(Math.Cos(Math.PI * t) - 1) / 2;

    private static float EaseInOutQuad(float t) =>
        t < 0.5f ? 2 * t * t : 1 - (float)Math.Pow(-2 * t + 2, 2) / 2;
}