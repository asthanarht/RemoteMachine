using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

if (args.Length != 1) throw new ArgumentException("Pass the RemoteHub Assets directory.");
string directory = Path.GetFullPath(args[0]);
var source = BitmapFrame.Create(new Uri(Path.Combine(directory, "RemoteHub-original.png")), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
if (source.PixelWidth != 1536 || source.PixelHeight != 1024)
    throw new InvalidDataException("Expected the supplied 1536x1024 RemoteHub R-arrow artwork sheet.");
const int size = 264;
var mark = new CroppedBitmap(source, new Int32Rect(735, 45, size, size));
byte[] Png(BitmapSource bitmap)
{
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var stream = new MemoryStream();
    encoder.Save(stream);
    return stream.ToArray();
}
BitmapSource Resize(int dimension)
{
    var visual = new DrawingVisual();
    RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
    using (var drawing = visual.RenderOpen()) drawing.DrawImage(mark, new Rect(0, 0, dimension, dimension));
    var bitmap = new RenderTargetBitmap(dimension, dimension, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(visual);
    return bitmap;
}
File.WriteAllBytes(Path.Combine(directory, "RemoteHub-mark.png"), Png(mark));
var lockup = new CroppedBitmap(source, new Int32Rect(73, 34, 586, 284));
File.WriteAllBytes(Path.Combine(directory, "RemoteHub-lockup.png"), Png(lockup));
int[] sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
byte[][] frames = sizes.Select(value => Png(Resize(value))).ToArray();
using (var file = File.Create(Path.Combine(directory, "RemoteHub.ico")))
using (var writer = new BinaryWriter(file))
{
    writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
    uint offset = (uint)(6 + 16 * sizes.Length);
    for (int i = 0; i < sizes.Length; i++)
    {
        writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
        writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
        writer.Write((uint)frames[i].Length); writer.Write(offset);
        offset += (uint)frames[i].Length;
    }
    foreach (byte[] frame in frames) writer.Write(frame);
}
Console.WriteLine($"Preserved {source.PixelWidth}x{source.PixelHeight} original. Exported transparent {size}x{size} mark, original-color lockup and {sizes.Length} Windows icon sizes.");
