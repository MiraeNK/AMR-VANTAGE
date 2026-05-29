using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FMR.AisinAMR.Helpers
{
    public static class PgmLoader
    {
        public static BitmapSource? LoadPgm(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                
                string magic = ReadWord(fs);
                if (magic != "P5" && magic != "P2")
                {
                    return null; // Not a valid PGM file
                }

                int width = int.Parse(ReadWord(fs));
                int height = int.Parse(ReadWord(fs));
                int maxval = int.Parse(ReadWord(fs));

                byte[] pixels = new byte[width * height];

                if (magic == "P5")
                {
                    // Binary PGM
                    int bytesRead = fs.Read(pixels, 0, pixels.Length);
                    if (bytesRead < pixels.Length)
                    {
                        // Incomplete file, but we can still try to render what we have
                    }
                }
                else if (magic == "P2")
                {
                    // ASCII PGM
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        string valStr = ReadWord(fs);
                        if (string.IsNullOrEmpty(valStr)) break;
                        pixels[i] = (byte)(int.Parse(valStr) * 255 / maxval); // Scale if maxval != 255
                    }
                }

                int stride = width; // 1 byte per pixel for Gray8
                
                var bitmap = BitmapSource.Create(
                    width,
                    height,
                    96,
                    96,
                    PixelFormats.Gray8,
                    null,
                    pixels,
                    stride);
                
                bitmap.Freeze(); // Make cross-thread accessible
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load PGM: {ex.Message}");
                return null;
            }
        }

        private static string ReadWord(Stream fs)
        {
            var sb = new StringBuilder();
            bool inComment = false;
            
            while (true)
            {
                int b = fs.ReadByte();
                if (b == -1) break; // EOF
                
                char c = (char)b;

                if (c == '#')
                {
                    inComment = true;
                    continue;
                }

                if (inComment)
                {
                    if (c == '\n' || c == '\r')
                    {
                        inComment = false;
                    }
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0)
                    {
                        break; // Word boundary reached
                    }
                    continue; // Skip leading whitespace
                }

                sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
