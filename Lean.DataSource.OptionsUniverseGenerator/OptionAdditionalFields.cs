﻿/*
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

        public string Header => "ivrank,ivpercentile";

        public string Content => $"{IvRank},{IvPercentile}";

        /// <summary>
        /// Update the additional fields
        /// </summary>
        /// <param name="ivs">List of past year's ATM implied volatilities</param>
        public void Update(List<decimal> ivs)
        {
            CalculateIvRank(ivs);
            CalculateIvPercentile(ivs);
        }

        // source: https://www.tastylive.com/concepts-strategies/implied-volatility-rank-percentile
        private void CalculateIvRank(List<decimal> ivs)
        {
            var oneYearLow = ivs.Min();
            IvRank = (ivs[^1] - oneYearLow) / (ivs.Max() - oneYearLow);
        }

        // source: https://www.tastylive.com/concepts-strategies/implied-volatility-rank-percentile
        private void CalculateIvPercentile(List<decimal> ivs)
        {
            var daysBelowCurrentIv = ivs.Count(x => x < ivs[^1]);
            IvPercentile = daysBelowCurrentIv / ivs.Count;
        }
    }
}
