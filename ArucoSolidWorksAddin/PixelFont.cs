using System;
using System.Collections.Generic;

namespace ArucoSolidWorksAddin
{
    internal static class PixelFont
    {
        private static readonly Dictionary<char, string[]> Glyphs =
            new Dictionary<char, string[]>
            {
                ['0'] = new[] { "111", "101", "101", "101", "111" },
                ['1'] = new[] { "010", "110", "010", "010", "111" },
                ['2'] = new[] { "111", "001", "111", "100", "111" },
                ['3'] = new[] { "111", "001", "111", "001", "111" },
                ['4'] = new[] { "101", "101", "111", "001", "001" },
                ['5'] = new[] { "111", "100", "111", "001", "111" },
                ['6'] = new[] { "111", "100", "111", "101", "111" },
                ['7'] = new[] { "111", "001", "010", "010", "010" },
                ['8'] = new[] { "111", "101", "111", "101", "111" },
                ['9'] = new[] { "111", "101", "111", "001", "111" },
                ['X'] = new[] { "101", "101", "010", "101", "101" },
                ['Y'] = new[] { "101", "101", "010", "010", "010" },
            };

        public static string[] Get(char character)
        {
            if (!Glyphs.TryGetValue(character, out string[] glyph))
                throw new ArgumentException("Unsupported pixel-font character: " + character);
            return glyph;
        }
    }
}
