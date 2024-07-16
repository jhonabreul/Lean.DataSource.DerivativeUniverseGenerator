/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2024 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using Microsoft.VisualStudio.Utilities;
using QuantConnect.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace QuantConnect.DataSource.OptionsUniverseGenerator
{
    public class OptionAdditionalFieldGenerator : DerivativeUniverseGenerator.AdditionalFieldGenerator
    {
        private const string _impliedVolHeader = "implied_volatility";
        private const string _deltaHeader = "delta";
        private const string _priceHeader = "close";
        private const string _sidHeader = "#symbol_id";
        private const string _tickerHeader = "symbol_value";

        private CircularBuffer<decimal> _vix = new(252);

        public OptionAdditionalFieldGenerator(DateTime processingDate, string rootPath)
            : base(processingDate, rootPath)
        {
        }

        public bool Run()
        {
            // per symbol
            try
            {
                foreach (var subFolder in Directory.GetDirectories(_rootPath))
                {
                    var ivs = GetIvs(_processingDate, subFolder, out var latestFile);
                    var prices = GetColumn(latestFile, _priceHeader);
                    var symbols = GetSymbols(latestFile, _sidHeader, _tickerHeader);
                    var symbolPrices = CreateContractDictionary(symbols, prices);
                    var underlyingPrice = prices[0];

                    var additionalFields = new OptionAdditionalFields();
                    var vixes = _vix.IsFull ? _vix.ToList() : null;
                    additionalFields.Update(_processingDate, underlyingPrice, symbolPrices, ivs, vixes);
                    var vix = additionalFields.Vix.HasValue ? additionalFields.Vix.Value : -1m;
                    _vix.Add(vix);

                    WriteToCsv(latestFile, additionalFields);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex,
                    $"AdditionalFieldGenerator.Run(): Error processing addtional fields for date {_processingDate:yyyy-MM-dd}");
                return false;
            }

            return true;
        }

        private List<decimal> GetIvs(DateTime currentDateTime, string path, out string file)
        {
            // get i-year ATM IVs to calculate IV rank and percentile
            var lastYearFiles = Directory.EnumerateFiles(path, "*.csv")
                .AsParallel()
                .Where(file => DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyyMMdd", 
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var fileDate)
                    && fileDate > currentDateTime.AddYears(-1)
                    && fileDate <= currentDateTime)
                .OrderBy(file => file)
                .ToList();
            file = lastYearFiles[^1];

            return lastYearFiles.Select(csvFile => GetAtmIv(csvFile))
                .ToList();
        }

        private decimal GetAtmIv(string csvPath)
        {
            var lines = File.ReadAllLines(csvPath)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
            var headers = lines[0].Split(',');
            int deltaIndex = Array.IndexOf(headers, _deltaHeader);
            int ivIndex = Array.IndexOf(headers, _impliedVolHeader);

            if (deltaIndex == -1 || ivIndex == -1)
            {
                return 0m;
            }

            return Enumerable.Range(1, lines.Count - 1)
                .AsParallel()
                .Select(i =>
                {
                    var values = lines[i].Split(',');
                    var delta = decimal.Parse(values[deltaIndex]);
                    var iv = decimal.Parse(values[ivIndex]);
                    return (Delta: delta, ImpliedVolatility: iv);
                })
                .OrderBy(x => Math.Abs(x.Delta - 0.5m))
                .Select(x => x.ImpliedVolatility)
                .First();
        }

        private Dictionary<TKey, TValue> CreateContractDictionary<TKey, TValue>(List<TKey> keys, List<TValue> values)
        {
            if (keys.Count != values.Count)
            {
                throw new ArgumentException("OptionAdditionalFieldGenerator.CreateContractDictionary(): The two lists must have the same number of elements.");
            }

            Dictionary<TKey, TValue> dictionary = new Dictionary<TKey, TValue>();
            // Skip underlying row
            for (int i = 1; i < keys.Count; i++)
            {
                dictionary[keys[i]] = values[i];
            }

            return dictionary;
        }
    }
}
