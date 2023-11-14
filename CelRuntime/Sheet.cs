#nullable enable

using CommunityToolkit.Diagnostics;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using System;

namespace CelRuntime
{
    public class Sheet
    {
        private readonly string[] _scopes = { SheetsService.Scope.Spreadsheets };
        private readonly string _applicationName = "Celbridge Demo";
        private readonly string _spreadsheetId = "1TL6JRiiS0nMc8q4S1Mg3gplScXhAkmZvRuzR9iqk1tw";
        private SheetsService? _service;

        private System.Collections.Generic.IList<System.Collections.Generic.IList<object>> Values;

        public bool Init(string sheetAPIKey)
        {
            try
            {
                GoogleCredential credential = GoogleCredential.FromJson(sheetAPIKey)
                        .CreateScoped(_scopes);

                // Creating Google Sheets API service...
                _service = new SheetsService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = _applicationName,
                });
            }
            catch (Exception ex)
            {
                Environment.PrintError($"Failed to initialize Google Sheets API. {ex.Message}");
                return false;
            }

            ReadSheet("CharacterData");

            return true;
        }

        // Todo: make this async
        public bool ReadSheet(string sheetName)
        {
            Guard.IsNotNull(_service);

            try
            {
                // You can define a range in sheet name, e.g. "CharacterData!A:L"
                var range = $"{sheetName}"; 
                SpreadsheetsResource.ValuesResource.GetRequest request =
                        _service.Spreadsheets.Values.Get(_spreadsheetId, range);

                var response = request.Execute();

                Values = response.Values;
            }
            catch (Exception ex)
            {
                Environment.PrintError($"Failed to read sheet. {ex.Message}");
                return false;
            }

            return true;
        }

        public void Clear()
        {
            Values.Clear();
        }

        public double GetNumRows()
        {
            if (Values == null)
            {
                return default;
            }

            return Values.Count;
        }

        public double GetNumColumns()
        {
            if (Values == null ||
                Values.Count == 0)
            {
                return default;
            }

            // Assumes the sheet columns are defined in the first row
            return Values[0].Count;
        }

        public double GetNumber(double rowIndex, double colIndex)
        {
            if (rowIndex < 0 || rowIndex >= Values.Count)
            {
                return 0;
            }

            var row = Values[(int)rowIndex];
            if (colIndex < 0 || colIndex >= row.Count)
            {
                return 0;
            }

            var v = (string)row[(int)colIndex];

            if (double.TryParse(v, out var value))
            {
                return value;
            }

            return default;
        }

        public string GetString(double rowIndex, double colIndex)
        {
            if (rowIndex < 0 || rowIndex >= Values.Count)
            {
                return string.Empty;
            }

            var row = Values[(int)rowIndex];
            if (colIndex < 0 || colIndex >= row.Count)
            {
                return string.Empty;
            }

            return (string)row[(int)colIndex];
        }
    }
}
