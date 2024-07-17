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

using QuantConnect.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuantConnect.DataSource.DerivativeUniverseGenerator
{
    /// <summary>
    /// Generate additional fields that needed to calculate from the whoel derivative chain
    /// </summary>
    public class AdditionalFieldGenerator
    {
        protected readonly DateTime _processingDate;
        protected readonly string _rootPath;

        /// <summary>
        /// Instantiate a new instance of <see cref="AdditionalFieldGenerator"/>
        /// </summary>
        /// <param name="processingDate"></param>
        /// <param name="rootPath"></param>
        public AdditionalFieldGenerator(DateTime processingDate, string rootPath)
        {
            _processingDate = processingDate;
            _rootPath = rootPath;
        }

        /// <summary>
        /// Run the additional fields generation
        /// </summary>
        /// <returns>If the generator run successfully</returns>
        public virtual bool Run()
        {
            throw new NotImplementedException("AdditionalFieldGenerator.Run(): Run method must be implemented.");
        }

        /// <summary>
        /// Write the additional fields to the Csv file being generated
        /// </summary>
        /// <param name="csvPath">Target csv file path</param>
        /// <param name="additionalFields">The addtional field content</param>
        protected virtual void WriteToCsv(string csvPath, IAdditionalFields additionalFields)
        {
            if (string.IsNullOrWhiteSpace(csvPath))
            {
                Log.Error("AdditionalFieldGenerator.WriteToCsv(): invalid file path provided");
                return;
            }

            var csv = File.ReadAllLines(csvPath)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
            for (int i = 0; i < csv.Count; i++)
            {
                if (i == 0)
                {
                    csv[i] += $",{additionalFields.Header}";
                }
                else
                {
                    csv[i] += $",{additionalFields.Content}";
                }
            }

            File.WriteAllLines(csvPath, csv);
        }

        /// <summary>
        /// Get a column's values as list
        /// </summary>
        /// <param name="csvPath">Path of the csv file</param>
        /// <param name="header">Header of the column required</param>
        /// <returns>List of value of the selected column</returns>
        /// <exception cref="Exception">Header not found</exception>
        protected List<decimal> GetColumn(string csvPath, string header)
        {
            var lines = File.ReadAllLines(csvPath)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
            var headers = lines[0].Split(',');
            var headerIndex = Array.IndexOf(headers, header);

            if (headerIndex == -1)
            {
                throw new Exception($"AdditionalFieldGenerator.GetColumn(): {header} not found in header row");
            }

            return lines.Skip(1)        // header row
                .Select(line => decimal.Parse(line.Split(',')[headerIndex]))
                .ToList();
        }

        /// <summary>
        /// Get a list of option contract symbols
        /// </summary>
        /// <param name="csvPath">Path of the csv file</param>
        /// <param name="sidHeader">Header of the SID column</param>
        /// <param name="tickerHeader">Header of the ticker column</param>
        /// <returns>List of Symbol object of the whole option chain</returns>
        /// <exception cref="Exception">Header(s) not found</exception>
        protected List<Symbol> GetSymbols(string csvPath, string sidHeader, string tickerHeader)
        {
            var lines = File.ReadAllLines(csvPath)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
            var headers = lines[0].Split(',');
            var sidHeaderIndex = Array.IndexOf(headers, sidHeader);
            var tickerHeaderIndex = Array.IndexOf(headers, tickerHeader);

            if (sidHeaderIndex == -1 || tickerHeaderIndex == -1)
            {
                throw new Exception($"AdditionalFieldGenerator.GetColumn(): [{sidHeaderIndex}, {tickerHeaderIndex}] not found in header row");
            }

            return lines.Skip(1)        // header row
                .Select(line =>
                {
                    var items = line.Split(',');
                    var sid = SecurityIdentifier.Parse(items[sidHeaderIndex]);
                    return new Symbol(sid, items[tickerHeaderIndex]);
                })
                .ToList();
        }
    }
}
