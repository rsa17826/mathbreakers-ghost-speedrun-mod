using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders text to a Texture2D using a hardcoded 5x7 pixel bitmap font -
/// no dependency on Unity's Font system, the game's Cocos2d-derived
/// CCText/CCFont system, or System.Drawing (which compiles fine here but
/// isn't actually present in Unity's embedded Mono runtime at play time,
/// causing a FileNotFoundException the first time it's used).
///
/// Every pixel is drawn by hand from a bit table, so there is nothing
/// external left that can be "broken" by this game's build.
///
/// Usage: GUI.DrawTexture(rect, TextRasterizer.GetTexture("Host", 3, Color.White));
/// ("fontSize" here is an integer pixel-scale multiplier, e.g. 2 or 3 -
/// each glyph is drawn at 5x7 pixels times this scale.)
/// </summary>
public static class TextRasterizer
{
  private const int GlyphWidth = 5;
  private const int GlyphHeight = 7;
  private const int GlyphSpacing = 1;

  // Cache is bounded: callers that render highly volatile strings (e.g.
  // per-frame coordinate text) would otherwise mint a brand-new Texture2D
  // every frame forever, since Mono's GC never reclaims native texture
  // memory and nothing here was ever calling Destroy(). Once the cache
  // hits _maxCacheEntries, the oldest entries are evicted and destroyed.
  private const int _maxCacheEntries = 256;
  private static readonly Dictionary<string, Texture2D> _cache =
    new Dictionary<string, Texture2D>();
  private static readonly LinkedList<string> _lru = new LinkedList<string>();
  private static readonly Dictionary<string, LinkedListNode<string>> _lruNodes =
    new Dictionary<string, LinkedListNode<string>>();
  private static readonly Dictionary<char, byte[]> _font = BuildFont();

  public static Texture2D GetTexture(string text, int scale, Color color)
  {
    if (string.IsNullOrEmpty(text))
      text = " ";
    if (scale < 1)
      scale = 1;

    var key = text + "|" + scale + "|" + color.r + "," + color.g + "," + color.b + "," + color.a;

    Texture2D cached;
    if (_cache.TryGetValue(key, out cached) && cached != null)
    {
      Touch(key);
      return cached;
    }

    var tex = Render(text, scale, color);
    _cache[key] = tex;
    Touch(key);
    EvictIfNeeded();
    return tex;
  }

  private static void Touch(string key)
  {
    LinkedListNode<string> node;
    if (_lruNodes.TryGetValue(key, out node))
    {
      _lru.Remove(node);
    }
    _lruNodes[key] = _lru.AddLast(key);
  }

  private static void EvictIfNeeded()
  {
    while (_cache.Count > _maxCacheEntries)
    {
      var oldestNode = _lru.First;
      var oldestKey = oldestNode.Value;
      _lru.RemoveFirst();
      _lruNodes.Remove(oldestKey);

      Texture2D oldTex;
      if (_cache.TryGetValue(oldestKey, out oldTex))
      {
        _cache.Remove(oldestKey);
        UnityEngine.Object.Destroy(oldTex);
      }
    }
  }

  private static Texture2D Render(string text, int scale, Color color)
  {
    int charsWide = text.Length;
    int width = Math.Max(1, charsWide * (GlyphWidth + GlyphSpacing) * scale);
    int height = GlyphHeight * scale;

    var tex = new Texture2D(width, height, TextureFormat.ARGB32, false);
    tex.filterMode = FilterMode.Point;

    var pixels = new Color32[width * height];
    var clear = new Color32(0, 0, 0, 0);
    for (int i = 0; i < pixels.Length; i++)
      pixels[i] = clear;

    var solid = (Color32)color;

    for (int ci = 0; ci < text.Length; ci++)
    {
      char c = text[ci];
      byte[] glyph;
      if (!_font.TryGetValue(c, out glyph))
        _font.TryGetValue('?', out glyph);
      if (glyph == null)
        continue;

      int glyphOriginX = ci * (GlyphWidth + GlyphSpacing) * scale;

      // Each byte in the glyph is one column, bit 0 = top row.
      for (int col = 0; col < GlyphWidth; col++)
      {
        byte columnBits = glyph[col];
        for (int row = 0; row < GlyphHeight; row++)
        {
          bool on = (columnBits & (1 << row)) != 0;
          if (!on)
            continue;

          // Texture2D pixel rows are bottom-up; glyph row 0 is the
          // top of the character, so flip vertically here.
          int flippedRow = (GlyphHeight - 1 - row);

          for (int sy = 0; sy < scale; sy++)
          {
            int py = flippedRow * scale + sy;
            for (int sx = 0; sx < scale; sx++)
            {
              int px = glyphOriginX + col * scale + sx;
              if (px < 0 || px >= width || py < 0 || py >= height)
                continue;
              pixels[py * width + px] = solid;
            }
          }
        }
      }
    }

    tex.SetPixels32(pixels);
    tex.Apply();
    return tex;
  }

  /// <summary>Clears the texture cache (e.g. if you want to force a re-render).</summary>
  public static void ClearCache()
  {
    foreach (var tex in _cache.Values)
      UnityEngine.Object.Destroy(tex);
    _cache.Clear();
    _lru.Clear();
    _lruNodes.Clear();
  }

  // Standard 5x7 glyph table (classic "glcdfont" layout used across many
  // embedded/graphics projects): 5 bytes per glyph, one byte per column,
  // bit 0 of each byte is the top pixel of that column.
  private static Dictionary<char, byte[]> BuildFont()
  {
    var f = new Dictionary<char, byte[]>();

    f[' '] = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00 };
    f['!'] = new byte[] { 0x00, 0x00, 0x5F, 0x00, 0x00 };
    f['"'] = new byte[] { 0x00, 0x07, 0x00, 0x07, 0x00 };
    f['#'] = new byte[] { 0x14, 0x7F, 0x14, 0x7F, 0x14 };
    f['$'] = new byte[] { 0x24, 0x2A, 0x7F, 0x2A, 0x12 };
    f['%'] = new byte[] { 0x23, 0x13, 0x08, 0x64, 0x62 };
    f['&'] = new byte[] { 0x36, 0x49, 0x55, 0x22, 0x50 };
    f['\''] = new byte[] { 0x00, 0x05, 0x03, 0x00, 0x00 };
    f['('] = new byte[] { 0x00, 0x1C, 0x22, 0x41, 0x00 };
    f[')'] = new byte[] { 0x00, 0x41, 0x22, 0x1C, 0x00 };
    f['*'] = new byte[] { 0x14, 0x08, 0x3E, 0x08, 0x14 };
    f['+'] = new byte[] { 0x08, 0x08, 0x3E, 0x08, 0x08 };
    f[','] = new byte[] { 0x00, 0x50, 0x30, 0x00, 0x00 };
    f['-'] = new byte[] { 0x08, 0x08, 0x08, 0x08, 0x08 };
    f['.'] = new byte[] { 0x00, 0x60, 0x60, 0x00, 0x00 };
    f['/'] = new byte[] { 0x20, 0x10, 0x08, 0x04, 0x02 };
    f['0'] = new byte[] { 0x3E, 0x51, 0x49, 0x45, 0x3E };
    f['1'] = new byte[] { 0x00, 0x42, 0x7F, 0x40, 0x00 };
    f['2'] = new byte[] { 0x42, 0x61, 0x51, 0x49, 0x46 };
    f['3'] = new byte[] { 0x21, 0x41, 0x45, 0x4B, 0x31 };
    f['4'] = new byte[] { 0x18, 0x14, 0x12, 0x7F, 0x10 };
    f['5'] = new byte[] { 0x27, 0x45, 0x45, 0x45, 0x39 };
    f['6'] = new byte[] { 0x3C, 0x4A, 0x49, 0x49, 0x30 };
    f['7'] = new byte[] { 0x01, 0x71, 0x09, 0x05, 0x03 };
    f['8'] = new byte[] { 0x36, 0x49, 0x49, 0x49, 0x36 };
    f['9'] = new byte[] { 0x06, 0x49, 0x49, 0x29, 0x1E };
    f[':'] = new byte[] { 0x00, 0x36, 0x36, 0x00, 0x00 };
    f[';'] = new byte[] { 0x00, 0x56, 0x36, 0x00, 0x00 };
    f['<'] = new byte[] { 0x08, 0x14, 0x22, 0x41, 0x00 };
    f['='] = new byte[] { 0x14, 0x14, 0x14, 0x14, 0x14 };
    f['>'] = new byte[] { 0x00, 0x41, 0x22, 0x14, 0x08 };
    f['?'] = new byte[] { 0x02, 0x01, 0x51, 0x09, 0x06 };
    f['@'] = new byte[] { 0x32, 0x49, 0x79, 0x41, 0x3E };
    f['A'] = new byte[] { 0x7E, 0x11, 0x11, 0x11, 0x7E };
    f['B'] = new byte[] { 0x7F, 0x49, 0x49, 0x49, 0x36 };
    f['C'] = new byte[] { 0x3E, 0x41, 0x41, 0x41, 0x22 };
    f['D'] = new byte[] { 0x7F, 0x41, 0x41, 0x22, 0x1C };
    f['E'] = new byte[] { 0x7F, 0x49, 0x49, 0x49, 0x41 };
    f['F'] = new byte[] { 0x7F, 0x09, 0x09, 0x09, 0x01 };
    f['G'] = new byte[] { 0x3E, 0x41, 0x49, 0x49, 0x7A };
    f['H'] = new byte[] { 0x7F, 0x08, 0x08, 0x08, 0x7F };
    f['I'] = new byte[] { 0x00, 0x41, 0x7F, 0x41, 0x00 };
    f['J'] = new byte[] { 0x20, 0x40, 0x41, 0x3F, 0x01 };
    f['K'] = new byte[] { 0x7F, 0x08, 0x14, 0x22, 0x41 };
    f['L'] = new byte[] { 0x7F, 0x40, 0x40, 0x40, 0x40 };
    f['M'] = new byte[] { 0x7F, 0x02, 0x0C, 0x02, 0x7F };
    f['N'] = new byte[] { 0x7F, 0x04, 0x08, 0x10, 0x7F };
    f['O'] = new byte[] { 0x3E, 0x41, 0x41, 0x41, 0x3E };
    f['P'] = new byte[] { 0x7F, 0x09, 0x09, 0x09, 0x06 };
    f['Q'] = new byte[] { 0x3E, 0x41, 0x51, 0x21, 0x5E };
    f['R'] = new byte[] { 0x7F, 0x09, 0x19, 0x29, 0x46 };
    f['S'] = new byte[] { 0x46, 0x49, 0x49, 0x49, 0x31 };
    f['T'] = new byte[] { 0x01, 0x01, 0x7F, 0x01, 0x01 };
    f['U'] = new byte[] { 0x3F, 0x40, 0x40, 0x40, 0x3F };
    f['V'] = new byte[] { 0x1F, 0x20, 0x40, 0x20, 0x1F };
    f['W'] = new byte[] { 0x3F, 0x40, 0x38, 0x40, 0x3F };
    f['X'] = new byte[] { 0x63, 0x14, 0x08, 0x14, 0x63 };
    f['Y'] = new byte[] { 0x07, 0x08, 0x70, 0x08, 0x07 };
    f['Z'] = new byte[] { 0x61, 0x51, 0x49, 0x45, 0x43 };
    f['['] = new byte[] { 0x00, 0x7F, 0x41, 0x41, 0x00 };
    f['\\'] = new byte[] { 0x02, 0x04, 0x08, 0x10, 0x20 };
    f[']'] = new byte[] { 0x00, 0x41, 0x41, 0x7F, 0x00 };
    f['^'] = new byte[] { 0x04, 0x02, 0x01, 0x02, 0x04 };
    f['_'] = new byte[] { 0x40, 0x40, 0x40, 0x40, 0x40 };
    f['`'] = new byte[] { 0x00, 0x01, 0x02, 0x04, 0x00 };
    f['a'] = new byte[] { 0x20, 0x54, 0x54, 0x54, 0x78 };
    f['b'] = new byte[] { 0x7F, 0x48, 0x44, 0x44, 0x38 };
    f['c'] = new byte[] { 0x38, 0x44, 0x44, 0x44, 0x20 };
    f['d'] = new byte[] { 0x38, 0x44, 0x44, 0x48, 0x7F };
    f['e'] = new byte[] { 0x38, 0x54, 0x54, 0x54, 0x18 };
    f['f'] = new byte[] { 0x08, 0x7E, 0x09, 0x01, 0x02 };
    f['g'] = new byte[] { 0x0C, 0x52, 0x52, 0x52, 0x3E };
    f['h'] = new byte[] { 0x7F, 0x08, 0x04, 0x04, 0x78 };
    f['i'] = new byte[] { 0x00, 0x44, 0x7D, 0x40, 0x00 };
    f['j'] = new byte[] { 0x20, 0x40, 0x44, 0x3D, 0x00 };
    f['k'] = new byte[] { 0x7F, 0x10, 0x28, 0x44, 0x00 };
    f['l'] = new byte[] { 0x00, 0x41, 0x7F, 0x40, 0x00 };
    f['m'] = new byte[] { 0x7C, 0x04, 0x18, 0x04, 0x78 };
    f['n'] = new byte[] { 0x7C, 0x08, 0x04, 0x04, 0x78 };
    f['o'] = new byte[] { 0x38, 0x44, 0x44, 0x44, 0x38 };
    f['p'] = new byte[] { 0x7C, 0x14, 0x14, 0x14, 0x08 };
    f['q'] = new byte[] { 0x08, 0x14, 0x14, 0x18, 0x7C };
    f['r'] = new byte[] { 0x7C, 0x08, 0x04, 0x04, 0x08 };
    f['s'] = new byte[] { 0x48, 0x54, 0x54, 0x54, 0x20 };
    f['t'] = new byte[] { 0x04, 0x3F, 0x44, 0x40, 0x20 };
    f['u'] = new byte[] { 0x3C, 0x40, 0x40, 0x20, 0x7C };
    f['v'] = new byte[] { 0x1C, 0x20, 0x40, 0x20, 0x1C };
    f['w'] = new byte[] { 0x3C, 0x40, 0x30, 0x40, 0x3C };
    f['x'] = new byte[] { 0x44, 0x28, 0x10, 0x28, 0x44 };
    f['y'] = new byte[] { 0x0C, 0x50, 0x50, 0x50, 0x3C };
    f['z'] = new byte[] { 0x44, 0x64, 0x54, 0x4C, 0x44 };
    f['{'] = new byte[] { 0x00, 0x08, 0x36, 0x41, 0x00 };
    f['|'] = new byte[] { 0x00, 0x00, 0x7F, 0x00, 0x00 };
    f['}'] = new byte[] { 0x00, 0x41, 0x36, 0x08, 0x00 };
    f['~'] = new byte[] { 0x08, 0x04, 0x08, 0x10, 0x08 };

    return f;
  }
}
