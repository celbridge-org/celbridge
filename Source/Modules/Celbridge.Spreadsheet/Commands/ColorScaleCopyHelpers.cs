using System.Globalization;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

// Workaround for a ClosedXML bug where IXLWorksheet.CopyTo throws a
// NullReferenceException when the source sheet contains a 2-stop or 3-stop
// color-scale conditional formatting rule. Snapshot the color-scale rules
// before CopyTo, strip them from the source, run CopyTo, then replay the
// snapshots onto both source and duplicate.
internal static class ColorScaleCopyHelpers
{
    internal record ColorScaleStopSnapshot(XLCFContentType ContentType, string FormulaValue, XLColor Color);

    internal record ColorScaleSnapshot(IReadOnlyList<string> RangeAddresses, IReadOnlyList<ColorScaleStopSnapshot> Stops);

    // Captures every color-scale CF on the sheet and removes them in place.
    // Other CF rule types are left untouched.
    public static List<ColorScaleSnapshot> ExtractAndRemove(IXLWorksheet sheet)
    {
        var snapshots = new List<ColorScaleSnapshot>();
        var formatsToRemove = new HashSet<IXLConditionalFormat>();

        foreach (var conditionalFormat in sheet.ConditionalFormats)
        {
            if (conditionalFormat.ConditionalFormatType != XLConditionalFormatType.ColorScale)
            {
                continue;
            }

            var rangeAddresses = new List<string>();
            foreach (var range in conditionalFormat.Ranges)
            {
                rangeAddresses.Add(range.RangeAddress.ToStringRelative());
            }

            var stops = new List<ColorScaleStopSnapshot>();
            for (int stopIndex = 1; stopIndex <= 3; stopIndex++)
            {
                if (!conditionalFormat.Colors.ContainsKey(stopIndex))
                {
                    break;
                }

                var contentType = conditionalFormat.ContentTypes.ContainsKey(stopIndex)
                    ? conditionalFormat.ContentTypes[stopIndex]
                    : XLCFContentType.Number;
                // ClosedXML can return a null XLFormula for "lowest"/"highest"
                // stops, so the formula reference must be null-checked before
                // dereferencing its Value.
                var formulaEntry = conditionalFormat.Values.ContainsKey(stopIndex)
                    ? conditionalFormat.Values[stopIndex]
                    : null;
                var formulaValue = formulaEntry?.Value ?? string.Empty;
                var color = conditionalFormat.Colors[stopIndex];

                stops.Add(new ColorScaleStopSnapshot(contentType, formulaValue, color));
            }

            if (stops.Count >= 2)
            {
                snapshots.Add(new ColorScaleSnapshot(rangeAddresses, stops));
                formatsToRemove.Add(conditionalFormat);
            }
        }

        if (formatsToRemove.Count > 0)
        {
            sheet.ConditionalFormats.Remove(format => formatsToRemove.Contains(format));
        }

        return snapshots;
    }

    public static void Reapply(IXLWorksheet sheet, IReadOnlyList<ColorScaleSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            foreach (var rangeAddress in snapshot.RangeAddresses)
            {
                IXLRange targetRange;
                try
                {
                    targetRange = sheet.Range(rangeAddress);
                }
                catch
                {
                    continue;
                }

                var conditionalFormat = targetRange.AddConditionalFormat();
                var builder = conditionalFormat.ColorScale();

                if (snapshot.Stops.Count == 2)
                {
                    var afterLow = ApplyLowStop(builder, snapshot.Stops[0]);
                    ApplyHighStopTwoStop(afterLow, snapshot.Stops[1]);
                }
                else
                {
                    var afterLow = ApplyLowStop(builder, snapshot.Stops[0]);
                    var afterMid = ApplyMidStop(afterLow, snapshot.Stops[1]);
                    ApplyHighStopThreeStop(afterMid, snapshot.Stops[2]);
                }
            }
        }
    }

    private static IXLCFColorScaleMid ApplyLowStop(IXLCFColorScaleMin builder, ColorScaleStopSnapshot stop)
    {
        if (IsMinMaxContentType(stop.ContentType))
        {
            return builder.LowestValue(stop.Color);
        }

        if (stop.ContentType == XLCFContentType.Formula)
        {
            return builder.Minimum(stop.ContentType, stop.FormulaValue, stop.Color);
        }

        if (TryParseStopValue(stop.FormulaValue, out var numericValue))
        {
            return builder.Minimum(stop.ContentType, numericValue, stop.Color);
        }

        return builder.LowestValue(stop.Color);
    }

    private static IXLCFColorScaleMax ApplyMidStop(IXLCFColorScaleMid builder, ColorScaleStopSnapshot stop)
    {
        if (stop.ContentType == XLCFContentType.Formula)
        {
            return builder.Midpoint(stop.ContentType, stop.FormulaValue, stop.Color);
        }

        if (TryParseStopValue(stop.FormulaValue, out var numericValue))
        {
            return builder.Midpoint(stop.ContentType, numericValue, stop.Color);
        }

        return builder.Midpoint(XLCFContentType.Percent, 50d, stop.Color);
    }

    private static void ApplyHighStopTwoStop(IXLCFColorScaleMid builder, ColorScaleStopSnapshot stop)
    {
        if (IsMinMaxContentType(stop.ContentType))
        {
            builder.HighestValue(stop.Color);
            return;
        }

        if (stop.ContentType == XLCFContentType.Formula)
        {
            builder.Maximum(stop.ContentType, stop.FormulaValue, stop.Color);
            return;
        }

        if (TryParseStopValue(stop.FormulaValue, out var numericValue))
        {
            builder.Maximum(stop.ContentType, numericValue, stop.Color);
            return;
        }

        builder.HighestValue(stop.Color);
    }

    private static void ApplyHighStopThreeStop(IXLCFColorScaleMax builder, ColorScaleStopSnapshot stop)
    {
        if (IsMinMaxContentType(stop.ContentType))
        {
            builder.HighestValue(stop.Color);
            return;
        }

        if (stop.ContentType == XLCFContentType.Formula)
        {
            builder.Maximum(stop.ContentType, stop.FormulaValue, stop.Color);
            return;
        }

        if (TryParseStopValue(stop.FormulaValue, out var numericValue))
        {
            builder.Maximum(stop.ContentType, numericValue, stop.Color);
            return;
        }

        builder.HighestValue(stop.Color);
    }

    // ClosedXML's "use the data minimum/maximum" stops surface as content
    // type values that are not Number, Percent, Percentile, or Formula.
    // Detect them by enum name so the helper does not need to hard-code
    // values that might be renamed.
    private static bool IsMinMaxContentType(XLCFContentType contentType)
    {
        var name = contentType.ToString();
        return name.Equals("Minimum", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Maximum", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseStopValue(string text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
