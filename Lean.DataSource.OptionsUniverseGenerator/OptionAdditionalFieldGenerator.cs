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
        private const string _vixHeader = "vix";
        private const string _sidHeader = "#symbol_id";
        private const string _tickerHeader = "symbol_value";

        private Dictionary<string, CircularBuffer<decimal>> _vix = new();

        public OptionAdditionalFieldGenerator(DateTime processingDate, string rootPath)
            : base(processingDate, rootPath)
        {
        }

        public override bool Run()
        {
            // per symbol
            try
            {
                foreach (var subFolder in Directory.GetDirectories(_rootPath))
                {
                    // warm up vixes
                    if (!_vix.TryGetValue(subFolder, out var vixes))
                    {
                        WarmUpVixes(_processingDate, subFolder);
                        vixes = _vix[subFolder];
                    }

                    var ivs = GetIvs(_processingDate, subFolder, out var latestFile);
                    var additionalFields = new OptionAdditionalFields();

                    if (ivs.Count > 0)
                    {
                        var prices = GetColumn(latestFile, _priceHeader);
                        var symbols = GetSymbols(latestFile, _sidHeader, _tickerHeader);
                        var symbolPrices = CreateContractDictionary(symbols, prices);
                        var underlyingPrice = prices[0];

                        var vixList = vixes.IsFull ? vixes.ToList() : null;
                        additionalFields.Update(_processingDate, underlyingPrice, symbolPrices, ivs, vixList);
                        var vix = additionalFields.Vix.HasValue ? additionalFields.Vix.Value : -1m;
                        vixes.Add(vix);
                    }

                    WriteToCsv(latestFile, additionalFields);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex,
                    $"OptionAdditionalFieldGenerator.Run(): Error processing addtional fields for date {_processingDate:yyyy-MM-dd}");
                return false;
            }

            return true;
        }

        private void WarmUpVixes(DateTime processingDate, string path)
        {
            _vix[path] = new(252);

            var vixes = Directory.EnumerateFiles(path, "*.csv")
                .Where(file => DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyyMMdd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var fileDate)
                    && fileDate > processingDate.AddYears(-1)
                    && fileDate <= processingDate)
                .OrderBy(file => file)
                .Select(file => GetVix(file))
                .Where(vix => vix > 0m)
                .ToList();

            foreach (var vix in vixes)
            {
                _vix[path].Add(vix);
            }
        }

        private decimal GetVix(string path)
        {
            var lines = File.ReadAllLines(path)
                   .Where(s => !string.IsNullOrWhiteSpace(s))
                   .ToList();
            var headers = lines[0].Split(',');
            var vixIndex = Array.IndexOf(headers, _vixHeader);
            if (vixIndex == -1 || lines.Count < 3)
            {
                return -1m;
            }

            var vix = lines[2].Split(',')[vixIndex];            // Skip header and underlying row

            if (string.IsNullOrWhiteSpace(vix))
            {
                return -1m;
            }
            return decimal.Parse(vix);
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
            
            if (lastYearFiles.Count < 0)
            {
                file = string.Empty;
                return new List<decimal>();
            }

            file = lastYearFiles[^1];
            if (lastYearFiles.Count < 252)
            {
                return new List<decimal>();
            }

            return lastYearFiles.Select(csvFile => GetAtmIv(csvFile))
                .ToList();
        }

        private decimal GetAtmIv(string csvPath)
        {
            var lines = File.ReadAllLines(csvPath)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
            var headers = lines[0].Split(',');
            var deltaIndex = Array.IndexOf(headers, _deltaHeader);
            var ivIndex = Array.IndexOf(headers, _impliedVolHeader);

            if (deltaIndex == -1 || ivIndex == -1)
            {
                return 0m;
            }

            // Skip underlying row
            return lines.Skip(2)
                .Select(line =>
                {
                    var values = line.Split(',');
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
