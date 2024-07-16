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

using QuantConnect.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QuantConnect.DataSource.OptionsUniverseGenerator
{
    /// <summary>
    /// Volatility Index
    /// </summary>
    public class Vix
    {
        private readonly List<Symbol> _contracts;
        private readonly Dictionary<Symbol, decimal> _symbolPrices;
        private readonly InterestRateProvider _interestRateProvider = new();

        /// <summary>
        /// Instantiate a new instance of a <see cref="Vix"> object
        /// </summary>
        /// <param name="symbolPrices"></param>
        public Vix(Dictionary<Symbol, decimal> symbolPrices)
        {
            _symbolPrices = symbolPrices;
            _contracts = symbolPrices.Keys.ToList();
        }

        /// <summary>
        /// Calculate the Volatility Index
        /// </summary>
        /// <param name="currentDate">Current datetime</param>
        /// <param name="underlyingPrice">Current price of the underlying security</param>
        /// <returns>Volatility Index value</returns>
        /// <remarks>source: https://cdn.cboe.com/api/global/us_indices/governance/Cboe_Volatility_Index_Mathematics_Methodology.pdf</remarks>
        public decimal CalculateVix(DateTime currentDate, decimal underlyingPrice)
        {
            // Bracket method of expiry selection
            var nearExpiries = _contracts.Where(x => x.ID.Date >= currentDate.AddDays(23) &&
                x.ID.Date <= currentDate.AddDays(30))
                .OrderBy(x => x.ID.Date)
                .ToList();
            var farExpiries = _contracts.Where(x => x.ID.Date >= currentDate.AddDays(31) &&
                x.ID.Date <= currentDate.AddDays(37))
                .OrderBy(x => x.ID.Date)
                .ToList();
            if (nearExpiries.Count == 0 || farExpiries.Count == 0)
            {
                return -1m;
            }
            var nearExpiry = nearExpiries.Last().ID.Date;
            var farExpiry = farExpiries.First().ID.Date;

            var nearYearTillExpiry = YearTillExpiry(nearExpiry, currentDate);
            var farYearTillExpiry = YearTillExpiry(farExpiry, currentDate);

            // Get forward price of realized cumulative variance via linear interpolation:
            // sqrt [ { T_1 * σ^2_1 * (T_2 - T_0) / (T_2 - T_1) + T_2 * σ^2_2 * (T_0 - T_1) / (T_2 - T_1) } / T_0 ]
            var nearSuccess = GetVariance(underlyingPrice, nearExpiry, nearYearTillExpiry, out var nearVariance);
            var farSuccess = GetVariance(underlyingPrice, farExpiry, farYearTillExpiry, out var farVariance);
            if (!nearSuccess || !farSuccess)
            {
                return -1m;
            }

            var constantTermYearTillExpiry = 30m / 365m;     // 1 month
            var diffYearTillExpiry = farYearTillExpiry - nearYearTillExpiry;
            var cumulativeVariance = (nearYearTillExpiry * nearVariance * (farYearTillExpiry - constantTermYearTillExpiry) / diffYearTillExpiry +
                farYearTillExpiry * farVariance * (constantTermYearTillExpiry - nearYearTillExpiry) / diffYearTillExpiry)
                / constantTermYearTillExpiry;
            return Convert.ToDecimal(Math.Sqrt((double)cumulativeVariance)) * 100m;
        }

        private decimal YearTillExpiry(DateTime expiry, DateTime current)
        {
            return Convert.ToDecimal((expiry - current).TotalDays) / 365m;
        }

        private bool GetVariance(decimal underlyingPrice, DateTime expiry, decimal yearTillExpiry, out decimal variance)
        {
            // Determine forward and K0 and obtain only ATM and OTM contracts
            var forward = FilterContracts(underlyingPrice, expiry, yearTillExpiry, out var otmMidPrice, out var timeMultiple);
            if (otmMidPrice.Count < 3 || forward <= 0m)
            {
                variance = 0m;
                return false;
            }
            var k0 = otmMidPrice.Select(x => x.Key).Where(x => x <= forward).Max();

            // Get the first part of the vix equation: 2 / T * Σ_i { ΔK_i / K_i^2 * e^RT * Q(K_i) }
            var cumulativeVariance = 2 / yearTillExpiry * WeightedAveragePrice(otmMidPrice, timeMultiple);

            // Get the second part of the vix equation: 1 / T * (F / K0 - 1)^2
            var overflow = forward / k0 - 1;
            var adjustment = 1 / yearTillExpiry * overflow * overflow;

            variance = cumulativeVariance - adjustment;
            return true;
        }

        private decimal FilterContracts(decimal underlyingPrice, DateTime expiry, decimal yearTillExpiry, out Dictionary<decimal, decimal> filtered, 
            out decimal timeValueMultiple)
        {
            // Get all contracts on that expiry
            var symbolPrices = _symbolPrices.Where(x => x.Key.ID.Date == expiry).ToDictionary(x => x.Key, x => x.Value);

            // Get the least call-put difference as ATM strike
            var belowAtm = IterateOptionChain(
                symbolPrices.Where(x => x.Key.ID.StrikePrice <= underlyingPrice).ToDictionary(x => x.Key, x => x.Value), expiry, true);
            var aboveAtm = IterateOptionChain(
                symbolPrices.Where(x => x.Key.ID.StrikePrice > underlyingPrice).ToDictionary(x => x.Key, x => x.Value), expiry, false);
            var callPutDifference = belowAtm.Concat(aboveAtm).ToDictionary(x => x.Key, x => x.Value);

            if (callPutDifference.Count == 0)
            {
                timeValueMultiple = 0m;
                filtered = new();
                return -1m;
            }

            var atmPrice = callPutDifference.MinBy(x => Math.Abs(x.Value));

            // Adjust to obtain the implied underlying price (F)
            var interestRate = _interestRateProvider.GetInterestRate(expiry);
            timeValueMultiple = Convert.ToDecimal(Math.Exp((double)(interestRate * yearTillExpiry)));
            var forward = atmPrice.Key + timeValueMultiple * atmPrice.Value;

            // Filter OTM contracts based on k0, for F, pick both call and put
            var otms = symbolPrices.Keys.Where(x =>
                callPutDifference.ContainsKey(x.ID.StrikePrice) &&
                ((x.ID.StrikePrice >= forward && x.ID.OptionRight == OptionRight.Call) ||
                (x.ID.StrikePrice <= forward && x.ID.OptionRight == OptionRight.Put))
            ).ToList();
            // Get the bid-ask mid price of the OTM contracts
            filtered = new();
            foreach (var strike in otms.Select(x => x.ID.StrikePrice).Distinct())
            {
                var price = symbolPrices.Where(x => x.Key.ID.StrikePrice == strike).Average(x => x.Value);
                filtered.Add(strike, price);
            }

            return forward;
        }

        private Dictionary<decimal, decimal> IterateOptionChain(Dictionary<Symbol, decimal> symbolPrices, DateTime expiry, bool reverse)
        {
            var callPutDifference = new Dictionary<decimal, decimal>();
            var symbols = symbolPrices.Keys;
            var lastFail = false;

            var strikes = reverse ? 
                symbols.Select(x => x.ID.StrikePrice).Distinct().OrderByDescending(x => x).ToList() :
                symbols.Select(x => x.ID.StrikePrice).Distinct().OrderBy(x => x).ToList();

            foreach (var strike in strikes)
            {
                var call = symbols.SingleOrDefault(x => x.ID.Date == expiry && x.ID.OptionRight == OptionRight.Call && x.ID.StrikePrice == strike);
                var put = symbols.SingleOrDefault(x => x.ID.Date == expiry && x.ID.OptionRight == OptionRight.Put && x.ID.StrikePrice == strike);
                if (call != null && put != null)
                {
                    var callPrice = symbolPrices[call];
                    var putPrice = symbolPrices[put];
                    if (callPrice != 0m && putPrice != 0m)
                    {
                        callPutDifference[strike] = callPrice - putPrice;
                        lastFail = false;
                        continue;
                    }
                }
                // Only liquid strikes should be considered: if consecutive 2 strikes without bid, truncate that end of the chain
                if (lastFail)
                {
                    break;
                }
                lastFail = true;
            }

            return callPutDifference;
        }

        private decimal WeightedAveragePrice(Dictionary<decimal, decimal> midPrices, decimal timeMultple)
        {
            var sortedMidPrices = midPrices.OrderBy(x => x.Key).ToList();
            var sum = 0m;

            // Σ_i { ΔK_i / K_i^2 * e^RT * Q(K_i) }
            for (int i = 0; i < sortedMidPrices.Count; i++)
            {
                var strike = sortedMidPrices[i].Key;

                decimal deltaK;
                if (i == 0)
                {
                    deltaK = sortedMidPrices[i + 1].Key - strike;
                }
                else if (i == sortedMidPrices.Count - 1)
                {
                    deltaK = strike - sortedMidPrices[i - 1].Key;
                }
                else
                {
                    deltaK = (sortedMidPrices[i + 1].Key - sortedMidPrices[i - 1].Key) / 2m;
                }

                sum += deltaK / strike / strike * timeMultple * sortedMidPrices[i].Value;
            }

            return sum;
        }
    }
}
