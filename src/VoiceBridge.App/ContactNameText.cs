using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace VoiceBridge.App;

/// <summary>
/// Draws contact names that contain rare symbols. Windows keeps some of those
/// glyphs only in the Segoe symbol fonts, so the Chinese interface font cannot
/// draw them and the name shows up as the wrong shape. This splits the name
/// into runs and gives every run the first font that really owns its glyph.
/// </summary>
internal static class ContactNameText
{
    private readonly record struct Face(string Family, FontWeight Weight, string Probe);

    private static readonly Face[] Faces =
    {
        new("Microsoft YaHei UI", FontWeights.Normal, "msyh.ttc"),
        new("Segoe UI Symbol", FontWeights.Bold, "seguisb.ttf"),
        new("Segoe UI Symbol", FontWeights.Normal, "seguisym.ttf"),
        new("Segoe UI Emoji", FontWeights.Normal, "seguiemj.ttf")
    };

    private static readonly Dictionary<string, GlyphTypeface?> Typefaces = new();
    private static readonly Dictionary<int, Face?> Coverage = new();

    /// <summary>
    /// Writes the name into the block, splitting it only when needed. The block
    /// must not use a binding on Text, because the text is replaced by runs.
    /// </summary>
    public static void Apply(TextBlock block, string? text)
    {
        if (block == null || string.IsNullOrEmpty(text))
            return;
        var faces = new List<Face?>();
        foreach (var chunk in Chunks(text))
            faces.Add(Resolve(chunk.Codepoint));

        block.ClearValue(TextBlock.TextProperty);
        block.Inlines.Clear();

        var first = faces[0];
        if (first == null)
        {
            // Nothing is known about these glyphs, keep the inherited font.
            block.Text = text;
            return;
        }
        if (faces.TrueForAll(face => face == first))
        {
            block.Inlines.Add(Build(text, first.Value));
            return;
        }
        var builder = new StringBuilder();
        Face? current = null;
        var index = 0;
        foreach (var chunk in Chunks(text))
        {
            var face = faces[index++];
            if (current == null || face != current)
            {
                Flush(block, builder, current);
                current = face;
            }
            builder.Append(chunk.Text);
        }
        Flush(block, builder, current);
    }

    private static Run Build(string text, Face face)
    {
        var run = new Run(text) { FontFamily = new FontFamily(face.Family) };
        if (!face.Family.Contains("YaHei", StringComparison.OrdinalIgnoreCase))
            run.FontWeight = face.Weight;
        return run;
    }

    private static string FontsDirectory() =>
        Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    private readonly record struct Chunk(int Codepoint, string Text);

    private static IEnumerable<Chunk> Chunks(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var value = char.ConvertToUtf32(text, index);
            var length = char.IsHighSurrogate(text[index]) ? 2 : 1;
            yield return new Chunk(value, text.Substring(index, length));
            index += length - 1;
        }
    }

    private static void Flush(TextBlock block, StringBuilder builder, Face? face)
    {
        if (builder.Length == 0)
            return;
        if (face == null)
            block.Inlines.Add(new Run(builder.ToString()));
        else
            block.Inlines.Add(Build(builder.ToString(), face.Value));
        builder.Clear();
    }

    /// <summary>Returns the first font that owns the glyph, or null when none does.</summary>
    private static Face? Resolve(int codepoint)
    {
        if (Coverage.TryGetValue(codepoint, out var cached))
            return cached;
        Face? found = null;
        foreach (var face in Faces)
        {
            var typeface = Typeface(face);
            if (typeface != null && typeface.CharacterToGlyphMap.ContainsKey(codepoint))
            {
                found = face;
                break;
            }
        }
        Coverage[codepoint] = found;
        return found;
    }

    private static GlyphTypeface? Typeface(Face face)
    {
        var key = face.Probe;
        if (Typefaces.TryGetValue(key, out var cached))
            return cached;
        GlyphTypeface? result = null;
        try
        {
            var path = Path.Combine(FontsDirectory(), face.Probe);
            if (File.Exists(path))
                result = new GlyphTypeface(new Uri(path));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException
            or UriFormatException)
        {
            result = null;
        }
        Typefaces[key] = result;
        return result;
    }
}
