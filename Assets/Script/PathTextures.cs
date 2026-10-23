using System.Collections.Generic;
using UnityEngine;

namespace GardenXR
{
    /// <summary>
    /// Textures d'allées générées par code (aucune image à importer), répétables sans couture :
    /// - Gravier : milliers de cailloux de tailles et teintes variées, ombrés, avec une bordure acier ;
    /// - Dallage : dalles en pierre posées en quinconce, joints, chanfreins et variations de teinte ;
    /// - Terre battue : terre damée avec grain, traces de passage, cailloux épars et bords enherbés.
    /// Orientation : u (0 → 1) = largeur de l'allée, v = longueur (1 unité = une largeur).
    /// </summary>
    public static class PathTextures
    {
        const int Size = 512;
        static readonly Dictionary<PathPattern, Texture2D> _cache = new Dictionary<PathPattern, Texture2D>();

        public static PathPattern Resolve(PathStyle style)
        {
            if (style.pattern != PathPattern.Auto) return style.pattern;
            string n = (style.name ?? "").ToLowerInvariant();
            if (n.Contains("grav")) return PathPattern.Gravier;
            if (n.Contains("dall") || n.Contains("pav") || n.Contains("pierre")) return PathPattern.Dallage;
            if (n.Contains("terre") || n.Contains("dirt")) return PathPattern.TerreBattue;
            return PathPattern.Uni;
        }

        public static Texture2D Get(PathPattern pattern)
        {
            if (pattern == PathPattern.Uni || pattern == PathPattern.Auto) return null;
            if (_cache.TryGetValue(pattern, out var t) && t) return t;

            var px = new Color[Size * Size];
            switch (pattern)
            {
                case PathPattern.Gravier: Gravel(px); break;
                case PathPattern.Dallage: Paving(px); break;
                case PathPattern.TerreBattue: Dirt(px); break;
            }

            t = new Texture2D(Size, Size, TextureFormat.RGBA32, true)
            {
                name = "Allee_" + pattern,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8 // net même vu en biais, au sol, dans le casque
            };
            t.SetPixels(px);
            t.Apply(true);
            _cache[pattern] = t;
            return t;
        }

        // ---------- Herbe ----------

        static Texture2D _grass;

        /// <summary>
        /// Gazon vu de dessus, pour 1 m × 1 m : sous-couche de terre et d'herbe sombre, des dizaines de milliers
        /// de brins orientés au hasard (base sombre, pointe claire), touffes plus denses, quelques trèfles
        /// et rares pâquerettes. Répétable sans couture.
        /// </summary>
        public static Texture2D Grass()
        {
            if (_grass) return _grass;
            var px = new Color[Size * Size];
            var rng = new System.Random(53);

            // Sous-couche : vert sombre avec taches plus claires / plus sèches
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float n = Fbm(x, y, 120f, 4, 61);
                float dry = Fbm(x, y, 60f, 3, 67);
                Color c = Color.Lerp(new Color(0.10f, 0.20f, 0.06f), new Color(0.18f, 0.32f, 0.10f), n);
                c = Color.Lerp(c, new Color(0.26f, 0.30f, 0.12f), Mathf.Clamp01((dry - 0.62f) * 3f));
                px[y * Size + x] = c;
            }

            Color[] greens =
            {
                new Color(0.26f, 0.48f, 0.14f), new Color(0.32f, 0.56f, 0.17f), new Color(0.22f, 0.42f, 0.12f),
                new Color(0.38f, 0.60f, 0.20f), new Color(0.30f, 0.50f, 0.20f), new Color(0.44f, 0.58f, 0.22f),
            };

            // Brins : plus nombreux dans les touffes (bruit de densité)
            for (int i = 0; i < 38000; i++)
            {
                float x0 = (float)rng.NextDouble() * Size, y0 = (float)rng.NextDouble() * Size;
                float density = Fbm(x0, y0, 45f, 2, 71);
                if (rng.NextDouble() > 0.35f + density) continue;

                float len = Mathf.Lerp(4f, 11f, (float)rng.NextDouble());
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float bend = ((float)rng.NextDouble() - 0.5f) * 0.6f;
                Color col = greens[rng.Next(greens.Length)] * Mathf.Lerp(0.85f, 1.15f, density);
                DrawBlade(px, x0, y0, len, ang, bend, col);
            }

            // Trèfles (petites rosaces) et pâquerettes
            for (int i = 0; i < 70; i++)
            {
                float cx = (float)rng.NextDouble() * Size, cy = (float)rng.NextDouble() * Size;
                Color leaf = new Color(0.20f, 0.45f, 0.16f);
                for (int l = 0; l < 3; l++)
                {
                    float a = l * 2.094f + (float)rng.NextDouble();
                    Disc(px, cx + Mathf.Cos(a) * 3f, cy + Mathf.Sin(a) * 3f, 2.8f, leaf, true);
                }
            }
            for (int i = 0; i < 14; i++)
            {
                float cx = (float)rng.NextDouble() * Size, cy = (float)rng.NextDouble() * Size;
                for (int l = 0; l < 8; l++)
                {
                    float a = l * 0.785f;
                    Disc(px, cx + Mathf.Cos(a) * 2.6f, cy + Mathf.Sin(a) * 2.6f, 1.4f, new Color(0.96f, 0.96f, 0.92f), false);
                }
                Disc(px, cx, cy, 1.5f, new Color(0.95f, 0.78f, 0.15f), false);
            }

            _grass = new Texture2D(Size, Size, TextureFormat.RGBA32, true)
            {
                name = "Pelouse",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8
            };
            _grass.SetPixels(px);
            _grass.Apply(true);
            return _grass;
        }

        static void DrawBlade(Color[] px, float x0, float y0, float len, float ang, float bend, Color col)
        {
            int steps = Mathf.CeilToInt(len * 1.5f);
            for (int s = 0; s <= steps; s++)
            {
                float t = (float)s / steps;
                float a = ang + bend * t;
                float x = x0 + Mathf.Cos(a) * len * t, y = y0 + Mathf.Sin(a) * len * t;
                Color c = col * Mathf.Lerp(0.55f, 1.25f, t); // base dans l'ombre, pointe éclairée
                int k = Wrap(Mathf.RoundToInt(y)) * Size + Wrap(Mathf.RoundToInt(x));
                px[k] = c;
                if (t < 0.5f) // brin un peu plus épais à la base
                {
                    int k2 = Wrap(Mathf.RoundToInt(y + 0.7f)) * Size + Wrap(Mathf.RoundToInt(x + 0.7f));
                    px[k2] = Color.Lerp(px[k2], c * 0.9f, 0.7f);
                }
            }
        }

        static void Disc(Color[] px, float cx, float cy, float r, Color col, bool shade)
        {
            int ri = Mathf.CeilToInt(r);
            for (int dy = -ri; dy <= ri; dy++)
            for (int dx = -ri; dx <= ri; dx++)
            {
                float d = (dx * dx + dy * dy) / (r * r);
                if (d > 1f) continue;
                int k = Wrap(Mathf.RoundToInt(cy) + dy) * Size + Wrap(Mathf.RoundToInt(cx) + dx);
                px[k] = shade ? col * Mathf.Lerp(1.15f, 0.8f, d) : col;
            }
        }

        // ---------- Gravier ----------

        static void Gravel(Color[] px)
        {
            var rng = new System.Random(11);
            // Fond : terre fine sous les cailloux
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float n = Fbm(x, y, 40f, 4, 3);
                px[y * Size + x] = Color.Lerp(new Color(0.42f, 0.38f, 0.32f), new Color(0.55f, 0.5f, 0.43f), n);
            }

            Color[] palette =
            {
                new Color(0.82f, 0.79f, 0.72f), new Color(0.72f, 0.69f, 0.63f), new Color(0.88f, 0.85f, 0.78f),
                new Color(0.62f, 0.58f, 0.52f), new Color(0.78f, 0.70f, 0.58f), new Color(0.68f, 0.66f, 0.66f),
            };

            // Cailloux en couches : les plus gros d'abord, les petits comblent les trous
            DrawPebbles(px, rng, 1300, 5f, 10f, palette);
            DrawPebbles(px, rng, 2600, 3f, 6f, palette);
            DrawPebbles(px, rng, 2500, 1.5f, 3f, palette);

            Border(px, 0.045f, new Color(0.24f, 0.24f, 0.26f), new Color(0.45f, 0.45f, 0.48f)); // bordure acier
        }

        static void DrawPebbles(Color[] px, System.Random rng, int count, float rMin, float rMax, Color[] palette)
        {
            for (int i = 0; i < count; i++)
            {
                float cx = (float)rng.NextDouble() * Size, cy = (float)rng.NextDouble() * Size;
                float rx = Mathf.Lerp(rMin, rMax, (float)rng.NextDouble());
                float ry = rx * Mathf.Lerp(0.6f, 1f, (float)rng.NextDouble());
                float ang = (float)rng.NextDouble() * Mathf.PI;
                Color baseCol = palette[rng.Next(palette.Length)] * Mathf.Lerp(0.88f, 1.08f, (float)rng.NextDouble());
                float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                int r = Mathf.CeilToInt(rx + 2f);

                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    float lx = (dx * ca + dy * sa) / rx, ly = (-dx * sa + dy * ca) / ry;
                    float d = lx * lx + ly * ly;
                    int x = Wrap(Mathf.RoundToInt(cx) + dx), y = Wrap(Mathf.RoundToInt(cy) + dy);
                    int k = y * Size + x;

                    if (d <= 1f)
                    {
                        // Relief : éclairé en haut à gauche, sombre sur le bord opposé
                        float light = 1f + 0.28f * (-lx * 0.6f + ly * 0.8f) * (1f - d);
                        float rim = Mathf.SmoothStep(1f, 0.75f, d);
                        px[k] = baseCol * light * Mathf.Lerp(0.7f, 1f, rim);
                    }
                    else if (d <= 1.6f && dx > 0 && dy < 0)
                    {
                        px[k] *= 0.82f; // petite ombre portée
                    }
                }
            }
        }

        // ---------- Dallage ----------

        static void Paving(Color[] px)
        {
            var rng = new System.Random(23);
            const int rows = 2, cols = 2;          // 2 rangées × 2 dalles par motif
            int rowH = Size / rows, colW = Size / cols;
            const int joint = 7, bevel = 10;

            // Teinte de chaque dalle (y compris les dalles décalées)
            var tint = new Color[rows, cols + 1];
            for (int r = 0; r < rows; r++)
            for (int c = 0; c <= cols; c++)
            {
                float v = (float)rng.NextDouble();
                tint[r, c] = Color.Lerp(new Color(0.60f, 0.57f, 0.52f), new Color(0.74f, 0.70f, 0.63f), v)
                             * Mathf.Lerp(0.92f, 1.05f, (float)rng.NextDouble());
            }

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int row = y / rowH;
                int offset = (row % 2 == 1) ? colW / 2 : 0;    // pose en quinconce
                int xs = x + offset;
                int col = (xs / colW) % (cols + 1);
                int lx = xs % colW, ly = y % rowH;

                int edge = Mathf.Min(Mathf.Min(lx, colW - 1 - lx), Mathf.Min(ly, rowH - 1 - ly));
                Color c;
                if (edge < joint / 2)
                {
                    // Joint : sable/mortier légèrement granuleux
                    c = Color.Lerp(new Color(0.30f, 0.29f, 0.27f), new Color(0.42f, 0.40f, 0.36f), Fbm(x, y, 6f, 2, 9));
                }
                else
                {
                    float grain = Fbm(x, y, 22f, 4, 5);
                    float speck = Fbm(x, y, 4f, 2, 17);
                    c = tint[row, col] * Mathf.Lerp(0.88f, 1.08f, grain);
                    if (speck > 0.72f) c *= 0.9f;
                    // Chanfrein : bord haut/gauche clair, bas/droit sombre
                    if (edge < bevel)
                    {
                        float k = 1f - (float)edge / bevel;
                        bool lit = ly > rowH - bevel || lx < bevel;
                        c *= lit ? 1f + 0.12f * k : 1f - 0.22f * k;
                    }
                }
                px[y * Size + x] = c;
            }

            Border(px, 0.04f, new Color(0.45f, 0.43f, 0.40f), new Color(0.62f, 0.60f, 0.56f)); // bordure pierre
        }

        // ---------- Terre battue ----------

        static void Dirt(Color[] px)
        {
            var rng = new System.Random(37);
            Color dark = new Color(0.40f, 0.29f, 0.19f), light = new Color(0.62f, 0.48f, 0.33f);

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float u = (float)x / Size;
                float n = Fbm(x, y, 90f, 5, 1);
                float fine = Fbm(x, y, 8f, 2, 2);
                Color c = Color.Lerp(dark, light, n * 0.8f + fine * 0.25f);

                // Traces de passage, un peu plus tassées et sombres
                float rut = Mathf.Exp(-Mathf.Pow((u - 0.32f) / 0.07f, 2f)) + Mathf.Exp(-Mathf.Pow((u - 0.68f) / 0.07f, 2f));
                c *= 1f - 0.12f * rut * Fbm(x, y, 30f, 2, 4);

                // Bords enherbés, irréguliers
                float edge = Mathf.Min(u, 1f - u);
                float grassLimit = 0.035f + 0.035f * Fbm(x, y, 18f, 3, 6);
                if (edge < grassLimit)
                {
                    float g = Fbm(x, y, 5f, 2, 8);
                    c = Color.Lerp(new Color(0.24f, 0.40f, 0.16f), new Color(0.38f, 0.55f, 0.22f), g);
                }
                else if (edge < grassLimit + 0.02f) c *= 0.85f; // limite herbe / terre

                px[y * Size + x] = c;
            }

            // Petits cailloux épars
            Color[] stones = { new Color(0.70f, 0.64f, 0.56f), new Color(0.55f, 0.50f, 0.45f) };
            for (int i = 0; i < 260; i++)
            {
                float cx = (float)rng.NextDouble() * Size, cy = (float)rng.NextDouble() * Size;
                float u = cx / Size;
                if (u < 0.08f || u > 0.92f) continue;
                float r = Mathf.Lerp(1.5f, 4f, (float)rng.NextDouble());
                Color sc = stones[rng.Next(stones.Length)];
                int ri = Mathf.CeilToInt(r);
                for (int dy = -ri; dy <= ri; dy++)
                for (int dx = -ri; dx <= ri; dx++)
                {
                    float d = (dx * dx + dy * dy) / (r * r);
                    if (d > 1f) continue;
                    int k = Wrap(Mathf.RoundToInt(cy) + dy) * Size + Wrap(Mathf.RoundToInt(cx) + dx);
                    px[k] = sc * (1f + 0.2f * (-dx + dy) / r * (1f - d));
                }
            }
        }

        // ---------- Utilitaires ----------

        /// <summary>Bande de bordure de chaque côté (u proche de 0 et de 1), avec une arête claire.</summary>
        static void Border(Color[] px, float widthU, Color dark, Color lightEdge)
        {
            int w = Mathf.Max(2, Mathf.RoundToInt(widthU * Size));
            for (int y = 0; y < Size; y++)
            for (int i = 0; i < w; i++)
            {
                float k = (float)i / w;
                Color c = Color.Lerp(dark, lightEdge, k > 0.75f ? 1f : 0f) * Mathf.Lerp(0.95f, 1.05f, Fbm(i, y, 12f, 2, 13));
                px[y * Size + i] = c;
                px[y * Size + (Size - 1 - i)] = c;
            }
        }

        static int Wrap(int v) => ((v % Size) + Size) % Size;

        /// <summary>Bruit fractal répétable sans couture (mélange de 4 échantillons décalés).</summary>
        static float Fbm(float x, float y, float scale, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f, f = 1f / scale;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * TileNoise(x, y, f, seed + o * 31);
                norm += amp;
                amp *= 0.5f;
                f *= 2f;
            }
            return Mathf.Clamp01(sum / norm);
        }

        static float TileNoise(float x, float y, float f, int seed)
        {
            float ox = seed * 17.3f, oy = seed * 9.7f;
            float a = Mathf.PerlinNoise(ox + x * f, oy + y * f);
            float b = Mathf.PerlinNoise(ox + (x - Size) * f, oy + y * f);
            float c = Mathf.PerlinNoise(ox + x * f, oy + (y - Size) * f);
            float d = Mathf.PerlinNoise(ox + (x - Size) * f, oy + (y - Size) * f);
            float tx = x / Size, ty = y / Size;
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }
    }
}
