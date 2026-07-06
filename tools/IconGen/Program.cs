using SkiaSharp;
using Svg.Skia;

// IconGen — rasterizes assets/logo/ednexus-icon.svg into a multi-size Windows .ico and PNGs.
//   dotnet run --project tools/IconGen -- <input.svg> <outDir>
// Defaults resolve relative to the current directory (run from the repo root).

string root = Directory.GetCurrentDirectory();
string input = args.Length > 0 ? args[0] : Path.Combine(root, "assets", "logo", "ednexus-icon.svg");
string outDir = args.Length > 1 ? args[1] : Path.Combine(root, "assets", "icons");
Directory.CreateDirectory(outDir);

using var svg = new SKSvg();
var picture = svg.Load(input);
if (picture is null)
{
    Console.Error.WriteLine($"Failed to load SVG: {input}");
    return 1;
}

float source = picture.CullRect.Width; // 512 from the viewBox
int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
var pngBySize = new Dictionary<int, byte[]>();

foreach (var size in sizes)
{
    using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
    using (var canvas = new SKCanvas(bitmap))
    {
        canvas.Clear(SKColors.Transparent);
        var scale = size / source;
        canvas.Scale(scale);
        canvas.DrawPicture(picture);
        canvas.Flush();
    }

    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    var bytes = data.ToArray();
    pngBySize[size] = bytes;
    File.WriteAllBytes(Path.Combine(outDir, $"ednexus-{size}.png"), bytes);
    Console.WriteLine($"  png {size}x{size}");
}

var icoPath = Path.Combine(outDir, "ednexus.ico");
WriteIco(icoPath, sizes, pngBySize);
Console.WriteLine($"  ico -> {icoPath}");
return 0;

// Writes a Windows .ico whose entries are PNG-encoded (supported on Vista+).
static void WriteIco(string path, int[] sizes, Dictionary<int, byte[]> pngBySize)
{
    using var fs = File.Create(path);
    using var w = new BinaryWriter(fs);

    w.Write((ushort)0);              // reserved
    w.Write((ushort)1);              // type = icon
    w.Write((ushort)sizes.Length);   // image count

    var offset = 6 + 16 * sizes.Length;
    foreach (var size in sizes)
    {
        var bytes = pngBySize[size];
        w.Write((byte)(size >= 256 ? 0 : size)); // width  (0 => 256)
        w.Write((byte)(size >= 256 ? 0 : size)); // height (0 => 256)
        w.Write((byte)0);            // palette size
        w.Write((byte)0);            // reserved
        w.Write((ushort)1);          // colour planes
        w.Write((ushort)32);         // bits per pixel
        w.Write(bytes.Length);       // image data size
        w.Write(offset);             // image data offset
        offset += bytes.Length;
    }

    foreach (var size in sizes)
        w.Write(pngBySize[size]);
}
