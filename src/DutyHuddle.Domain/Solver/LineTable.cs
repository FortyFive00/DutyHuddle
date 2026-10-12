using System.Numerics;

namespace DutyHuddle.Domain.Solver;

/// <summary>The 10 lines of the 4x4 grid (4 rows, 4 columns, 2 diagonals). Cell i = row * 4 + column.</summary>
public static class LineTable
{
    public static readonly ushort[] Lines = BuildLines();

    private static readonly byte[] LineCount = BuildLineCounts();

    public static int CountLines(ushort mask) => LineCount[mask];

    public static int CellIndex(int row, int column) => row * 4 + column;

    public static bool HasCell(ushort mask, int cell) => (mask & (1 << cell)) != 0;

    public static int PopCount(ushort mask) => BitOperations.PopCount(mask);

    private static ushort[] BuildLines()
    {
        var lines = new List<ushort>(10);
        for (var r = 0; r < 4; r++)
            lines.Add((ushort)(0xF << (r * 4)));
        for (var c = 0; c < 4; c++)
            lines.Add((ushort)((1 << c) | (1 << (4 + c)) | (1 << (8 + c)) | (1 << (12 + c))));
        lines.Add((ushort)((1 << 0) | (1 << 5) | (1 << 10) | (1 << 15)));
        lines.Add((ushort)((1 << 3) | (1 << 6) | (1 << 9) | (1 << 12)));
        return [.. lines];
    }

    private static byte[] BuildLineCounts()
    {
        var counts = new byte[1 << 16];
        for (var mask = 0; mask < counts.Length; mask++)
        {
            byte n = 0;
            foreach (var line in Lines)
                if ((mask & line) == line)
                    n++;
            counts[mask] = n;
        }

        return counts;
    }
}
