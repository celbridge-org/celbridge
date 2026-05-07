namespace Celbridge.Spreadsheet.Helpers;

/// <summary>
/// RFC 4180 CSV parser used by IImportCsvCommand. Comma delimiter,
/// double-quote quoting, embedded quotes doubled. Recognises both CRLF and LF
/// row terminators. A trailing newline does not produce a final empty row.
/// All fields are returned as strings; numeric and other typed coercion is
/// the caller's responsibility.
/// </summary>
internal static class SpreadsheetCsvParser
{
    public static IReadOnlyList<IReadOnlyList<string>> Parse(string csvText)
    {
        var rows = new List<IReadOnlyList<string>>();
        if (string.IsNullOrEmpty(csvText))
        {
            return rows;
        }

        // Strip a leading UTF-8 BOM (U+FEFF). Excel writes CSV files with a
        // BOM and consumes them transparently; without this strip, the BOM
        // ends up as part of the first cell's value.
        if (csvText[0] == '﻿')
        {
            csvText = csvText.Substring(1);
            if (csvText.Length == 0)
            {
                return rows;
            }
        }

        var currentRow = new List<string>();
        var fieldBuilder = new System.Text.StringBuilder();
        var inQuotes = false;
        var sawAnyContent = false;

        for (int characterIndex = 0; characterIndex < csvText.Length; characterIndex++)
        {
            var character = csvText[characterIndex];

            if (inQuotes)
            {
                if (character == '"')
                {
                    var nextIndex = characterIndex + 1;
                    if (nextIndex < csvText.Length && csvText[nextIndex] == '"')
                    {
                        fieldBuilder.Append('"');
                        characterIndex++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    fieldBuilder.Append(character);
                }
                continue;
            }

            switch (character)
            {
                case ',':
                    currentRow.Add(fieldBuilder.ToString());
                    fieldBuilder.Clear();
                    sawAnyContent = true;
                    break;

                case '"':
                    inQuotes = true;
                    sawAnyContent = true;
                    break;

                case '\r':
                    var peekIndex = characterIndex + 1;
                    if (peekIndex < csvText.Length && csvText[peekIndex] == '\n')
                    {
                        characterIndex++;
                    }
                    currentRow.Add(fieldBuilder.ToString());
                    fieldBuilder.Clear();
                    rows.Add(currentRow);
                    currentRow = new List<string>();
                    sawAnyContent = false;
                    break;

                case '\n':
                    currentRow.Add(fieldBuilder.ToString());
                    fieldBuilder.Clear();
                    rows.Add(currentRow);
                    currentRow = new List<string>();
                    sawAnyContent = false;
                    break;

                default:
                    fieldBuilder.Append(character);
                    sawAnyContent = true;
                    break;
            }
        }

        if (sawAnyContent || fieldBuilder.Length > 0)
        {
            currentRow.Add(fieldBuilder.ToString());
            rows.Add(currentRow);
        }

        return rows;
    }
}
