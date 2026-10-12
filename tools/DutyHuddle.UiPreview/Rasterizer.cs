using System.IO.Compression;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace DutyHuddle.UiPreview;

/// <summary>RGBA texture held in memory, indexed by the ImTextureID handle given to ImGui.</summary>
internal sealed record CpuTexture(int Width, int Height, byte[] Rgba);

/// <summary>
/// Software renderer for ImGui draw data: textured, vertex-coloured triangles with scissor rectangles and
/// alpha blending, which is all ImGui needs. Good enough to judge layout, colours and typography.
/// </summary>
internal sealed unsafe class Rasterizer
{
    private readonly int width;
    private readonly int height;
    private readonly float[] canvas;
    private readonly Dictionary<ulong, CpuTexture> textures;

    public Rasterizer(int width, int height, Vector4 background, Dictionary<ulong, CpuTexture> textures)
    {
        this.width = width;
        this.height = height;
        this.textures = textures;
        canvas = new float[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            canvas[i * 4] = background.X;
            canvas[i * 4 + 1] = background.Y;
            canvas[i * 4 + 2] = background.Z;
            canvas[i * 4 + 3] = 1f;
        }
    }

    public void Render(ImDrawDataPtr data)
    {
        for (var l = 0; l < data.CmdListsCount; l++)
        {
            ImDrawListPtr list = data.CmdLists[l];
            var vertices = list.VtxBuffer.Data;
            var indices = list.IdxBuffer.Data;
            for (var c = 0; c < list.CmdBuffer.Size; c++)
            {
                var cmd = list.CmdBuffer.Data[c];
                if (cmd.UserCallback != null)
                    continue;

                textures.TryGetValue(cmd.TextureId.Handle, out var texture);
                var clip = cmd.ClipRect;
                for (var i = 0; i < cmd.ElemCount; i += 3)
                {
                    var a = vertices[cmd.VtxOffset + indices[cmd.IdxOffset + i]];
                    var b = vertices[cmd.VtxOffset + indices[cmd.IdxOffset + i + 1]];
                    var d = vertices[cmd.VtxOffset + indices[cmd.IdxOffset + i + 2]];
                    Triangle(a, b, d, clip, texture);
                }
            }
        }
    }

    private void Triangle(ImDrawVert a, ImDrawVert b, ImDrawVert c, Vector4 clip, CpuTexture? texture)
    {
        var minX = (int)MathF.Max(MathF.Floor(MathF.Min(a.Pos.X, MathF.Min(b.Pos.X, c.Pos.X))), MathF.Max(clip.X, 0));
        var maxX = (int)MathF.Min(MathF.Ceiling(MathF.Max(a.Pos.X, MathF.Max(b.Pos.X, c.Pos.X))), MathF.Min(clip.Z, width));
        var minY = (int)MathF.Max(MathF.Floor(MathF.Min(a.Pos.Y, MathF.Min(b.Pos.Y, c.Pos.Y))), MathF.Max(clip.Y, 0));
        var maxY = (int)MathF.Min(MathF.Ceiling(MathF.Max(a.Pos.Y, MathF.Max(b.Pos.Y, c.Pos.Y))), MathF.Min(clip.W, height));
        if (minX >= maxX || minY >= maxY)
            return;

        var area = Edge(a.Pos, b.Pos, c.Pos);
        if (MathF.Abs(area) < 1e-6f)
            return;

        var ca = Unpack(a.Col);
        var cb = Unpack(b.Col);
        var cc = Unpack(c.Col);
        for (var y = minY; y < maxY; y++)
        {
            for (var x = minX; x < maxX; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var w0 = Edge(b.Pos, c.Pos, p) / area;
                var w1 = Edge(c.Pos, a.Pos, p) / area;
                var w2 = Edge(a.Pos, b.Pos, p) / area;
                if (w0 < -1e-5f || w1 < -1e-5f || w2 < -1e-5f)
                    continue;

                var color = ca * w0 + cb * w1 + cc * w2;
                if (texture is not null)
                {
                    var uv = a.Uv * w0 + b.Uv * w1 + c.Uv * w2;
                    color *= Sample(texture, uv);
                }

                Blend(x, y, color);
            }
        }
    }

    private static float Edge(Vector2 a, Vector2 b, Vector2 p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

    private static Vector4 Unpack(uint col)
        => new((col & 0xFF) / 255f, ((col >> 8) & 0xFF) / 255f, ((col >> 16) & 0xFF) / 255f, ((col >> 24) & 0xFF) / 255f);

    private static Vector4 Sample(CpuTexture texture, Vector2 uv)
    {
        var x = uv.X * texture.Width - 0.5f;
        var y = uv.Y * texture.Height - 0.5f;
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        return Vector4.Lerp(
            Vector4.Lerp(Texel(texture, x0, y0), Texel(texture, x0 + 1, y0), fx),
            Vector4.Lerp(Texel(texture, x0, y0 + 1), Texel(texture, x0 + 1, y0 + 1), fx),
            fy);
    }

    private static Vector4 Texel(CpuTexture texture, int x, int y)
    {
        x = Math.Clamp(x, 0, texture.Width - 1);
        y = Math.Clamp(y, 0, texture.Height - 1);
        var i = (y * texture.Width + x) * 4;
        var d = texture.Rgba;
        return new Vector4(d[i] / 255f, d[i + 1] / 255f, d[i + 2] / 255f, d[i + 3] / 255f);
    }

    private void Blend(int x, int y, Vector4 src)
    {
        var i = (y * width + x) * 4;
        var alpha = Math.Clamp(src.W, 0, 1);
        canvas[i] = src.X * alpha + canvas[i] * (1 - alpha);
        canvas[i + 1] = src.Y * alpha + canvas[i + 1] * (1 - alpha);
        canvas[i + 2] = src.Z * alpha + canvas[i + 2] * (1 - alpha);
    }

    public void SavePng(string path)
    {
        var raw = new byte[height * (width * 4 + 1)];
        for (var y = 0; y < height; y++)
        {
            var row = y * (width * 4 + 1);
            raw[row] = 0;
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                for (var k = 0; k < 4; k++)
                    raw[row + 1 + x * 4 + k] = (byte)Math.Clamp(MathF.Round((k == 3 ? 1f : canvas[i + k]) * 255f), 0, 255);
            }
        }

        using var file = File.Create(path);
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8;
        header[9] = 6;
        Chunk(file, "IHDR", header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw);
        Chunk(file, "IDAT", compressed.ToArray());
        Chunk(file, "IEND", []);
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        stream.Write(length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crc = new byte[4];
        WriteBigEndian(crc, 0, Crc32([.. typeBytes, .. data]));
        stream.Write(crc);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return ~crc;
    }
}
