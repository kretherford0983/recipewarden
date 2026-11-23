using System.Text;

namespace RecipeWarden.Infrastructure.Nutrition;

/// <summary>
/// A small RFC 4180 reader for USDA FoodData Central CSV files: quoted fields, doubled quotes, commas and line breaks
/// inside quotes. Rows are read one at a time, so large files never sit in memory.
/// </summary>
internal sealed class FdcCsv(TextReader reader)
{
    private Dictionary<string, int>? columns;

    public const int MaxFieldLength = 64 * 1024;

    /// <summary>Reads the header; false for an empty file.</summary>
    public bool ReadHeader()
    {
        var header = ReadRow();
        if (header is null)
        {
            return false;
        }

        columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            columns.TryAdd(header[i].Trim().TrimStart('﻿'), i);
        }

        return true;
    }

    public bool Has(string column) => columns?.ContainsKey(column) == true;

    public static string? Get(IReadOnlyList<string> row, FdcCsv csv, string column) =>
        csv.columns is { } c && c.TryGetValue(column, out var i) && i < row.Count ? row[i] : null;

    public IReadOnlyList<string>? ReadRow()
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var any = false;
        while (true)
        {
            var next = reader.Read();
            if (next < 0)
            {
                if (!any)
                {
                    return null;
                }

                fields.Add(field.ToString());
                return fields;
            }

            any = true;
            var c = (char)next;
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                fields.Add(field.ToString());
                return fields;
            }
            else if (c != '\r')
            {
                field.Append(c);
            }

            if (field.Length > MaxFieldLength)
            {
                throw new InvalidDataException("A CSV field is too long.");
            }
        }
    }
}
