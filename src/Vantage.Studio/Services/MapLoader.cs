using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Vantage.Studio.Services;

/// <summary>Occupancy grid in the shared map frame (ROS map_server format).</summary>
public sealed record MapLayer(string Name, BitmapSource Image, double Resolution, double OriginX, double OriginY)
{
    public double WidthM => Image.PixelWidth * Resolution;
    public double HeightM => Image.PixelHeight * Resolution;
}

public static class MapLoader
{
    /// <summary>Loads a ROS map .yaml (image, resolution, origin) or a bare image with 0.05 m/px at the origin.</summary>
    public static MapLayer Load(string path)
    {
        if (path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
        {
            var kv = File.ReadAllLines(path)
                .Select(l => l.Split('#')[0])
                .Where(l => l.Contains(':'))
                .Select(l => (Key: l[..l.IndexOf(':')].Trim(), Value: l[(l.IndexOf(':') + 1)..].Trim()))
                .ToDictionary(x => x.Key, x => x.Value);

            var image = Path.Combine(Path.GetDirectoryName(path)!, kv["image"].Trim('"', '\''));
            double res = double.Parse(kv["resolution"], CultureInfo.InvariantCulture);
            var origin = kv["origin"].Trim('[', ']').Split(',').Select(s => double.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
            return new MapLayer(Path.GetFileName(path), LoadImage(image), res, origin[0], origin[1]);
        }
        return new MapLayer(Path.GetFileName(path), LoadImage(path), 0.05, 0, 0);
    }

    private static BitmapSource LoadImage(string file)
    {
        BitmapSource bmp = file.EndsWith(".pgm", StringComparison.OrdinalIgnoreCase)
            ? LoadPgm(file)
            : new BitmapImage(new Uri(Path.GetFullPath(file)));
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Binary (P5) or ASCII (P2) PGM, 8-bit.</summary>
    private static BitmapSource LoadPgm(string file)
    {
        var data = File.ReadAllBytes(file);
        int pos = 0;
        string Token()
        {
            while (pos < data.Length)
            {
                if (data[pos] == '#') { while (pos < data.Length && data[pos] != '\n') pos++; }
                else if (char.IsWhiteSpace((char)data[pos])) pos++;
                else break;
            }
            int start = pos;
            while (pos < data.Length && !char.IsWhiteSpace((char)data[pos])) pos++;
            return System.Text.Encoding.ASCII.GetString(data, start, pos - start);
        }

        string magic = Token();
        int w = int.Parse(Token()), h = int.Parse(Token()), max = int.Parse(Token());
        var pixels = new byte[w * h];
        if (magic == "P5")
        {
            pos++;
            Array.Copy(data, pos, pixels, 0, Math.Min(pixels.Length, data.Length - pos));
        }
        else if (magic == "P2")
        {
            for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(int.Parse(Token()) * 255 / Math.Max(1, max));
        }
        else throw new InvalidDataException($"Unsupported PGM type {magic}.");

        return BitmapSource.Create(w, h, 96, 96, PixelFormats.Gray8, null, pixels, w);
    }
}
