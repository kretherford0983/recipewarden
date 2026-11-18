using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace RecipeWarden.Infrastructure.Data;

/// <summary>Stores timestamps as round-trip ISO-8601 strings normalized to UTC (section 8.1).</summary>
public sealed class DateTimeOffsetToIsoStringConverter() : ValueConverter<DateTimeOffset, string>(
    v => v.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
    v => DateTimeOffset.Parse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
