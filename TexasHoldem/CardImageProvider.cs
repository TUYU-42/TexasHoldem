using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace TexasHoldem
{
    /// <summary>
    /// Loads and caches card PNG images from Resources/Cards/.
    /// Filenames: AS.png, KH.png, 10D.png, back.png, etc.
    /// </summary>
    public static class CardImageProvider
    {
        private static readonly Dictionary<string, Image> _cache = new Dictionary<string, Image>();
        private static Image _placeholder;

        private static string CardsDir
        {
            get
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string p = Path.Combine(baseDir, "Resources", "Cards");
                if (Directory.Exists(p)) return p;
                var dir = new DirectoryInfo(baseDir);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "Resources", "Cards");
                    if (Directory.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
                return p;
            }
        }

        public static Image GetCard(Card c) => Get(c.ImageKey);
        public static Image GetBack() => Get("back");

        public static Image Get(string key)
        {
            if (_cache.TryGetValue(key, out var img)) return img;
            string path = Path.Combine(CardsDir, key + ".png");
            if (File.Exists(path))
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    img = Image.FromStream(fs);
                }
                _cache[key] = img;
                return img;
            }
            return Placeholder();
        }

        private static Image Placeholder()
        {
            if (_placeholder != null) return _placeholder;
            var bmp = new Bitmap(140, 196);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.LightGray);
                using (var pen = new Pen(Color.Black, 2))
                    g.DrawRectangle(pen, 1, 1, 138, 194);
            }
            _placeholder = bmp;
            return bmp;
        }
    }
}
