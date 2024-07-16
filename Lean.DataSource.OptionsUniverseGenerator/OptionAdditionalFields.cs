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

using System;
using System.Collections.Generic;
using System.Linq;

namespace QuantConnect.DataSource.OptionsUniverseGenerator
{
    /// <summary>
    /// Option additional fields from the daily option universe file
    /// </summary>
    public class OptionAdditionalFields : DerivativeUniverseGenerator.IAdditionalFields
    {
        /// <summary>
        /// Implied Volatility Rank
        /// </summary>
        /// <remarks>The relative volatility over the past year</remarks>
        public decimal IvRank { get; set; }

        /// <summary>
        /// Implied Volatility Percentile
        /// </summary>
        /// <remarks>The ratio of the current implied volatility baing higher than that over the past year</remarks>
        public decimal IvPercentile { get; set; }

        /// <summary>
        /// Volatility Index
        /// </summary>
        public decimal? Vix { get; set; } = null;

        /// <summary>
        /// Implied Volatility Rank with Volatility Index as IV input
        /// </summary>
        public decimal? VixIvRank { get; set; } = null;

        /// <summary>
        /// Implied Volatility Percentile with Volatility Index as IV input
        /// </summary>
        public decimal? VixIvPercentile { get; set; } = null;

        public string Header => "iv_rank,iv_percentile,vix,vix_iv_rank,vix_iv_percentile";

        public string Content => $"{IvRank},{IvPercentile},{WriteNullableField(Vix)},{WriteNullableField(VixIvRank)},{WriteNullableField(VixIvPercentile)}";

        /// <summary>
        /// Update the additional fields
        /// </summary>
        /// <param name="currentDate">Current datetime</param>
        /// <param name="underlyingPrice">Current price of the underlying</param>
        /// <param name="symbolPrices">Option contract symbols and their corresponding option prices</param>
        /// <param name="ivs">List of past year's ATM implied volatilities</param>
        /// <param name="vixes">List of past year's Volatility Index</param>
        public void Update(DateTime currentDate, decimal underlyingPrice, Dictionary<Symbol, decimal> symbolPrices, List<decimal> ivs, List<decimal> vixes = null)
        {
            IvRank = CalculateIvRank(ivs);
            IvPercentile = CalculateIvPercentile(ivs);

            Vix = CalculateVix(currentDate, underlyingPrice, symbolPrices);

            if (vixes != null)
            {
                VixIvRank = CalculateIvRank(vixes.Where(x => x > 0m).ToList());
                VixIvPercentile = CalculateIvPercentile(vixes.Where(x => x > 0m).ToList());
            }
        }

        // source: https://www.tastylive.com/concepts-strategies/implied-volatility-rank-percentile
        private decimal CalculateIvRank(List<decimal> ivs)
        {
            var oneYearLow = ivs.Min();
            return (ivs[^1] - oneYearLow) / (ivs.Max() - oneYearLow);
        }

        // source: https://www.tastylive.com/concepts-strategies/implied-volatility-rank-percentile
        private decimal CalculateIvPercentile(List<decimal> ivs)
        {
            var daysBelowCurrentIv = ivs.Count(x => x < ivs[^1]);
            return daysBelowCurrentIv / ivs.Count;
        }

        private decimal? CalculateVix(DateTime currentDate, decimal underlyingPrice, Dictionary<Symbol, decimal> symbolPrices)
        {
            var vixIndicator = new Vix(symbolPrices);
            var vix = vixIndicator.CalculateVix(currentDate, underlyingPrice);
            return vix <= 0m ? null : vix;
        }

        private string WriteNullableField(decimal? field)
        {
            return field.HasValue ? field.Value.ToString() : string.Empty;
        }
    }
}
