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
        /// Expected 30 Days Implied Volatility
        /// </summary>
        /// <remarks>Linearly interpolated by bracket method</remarks>
        public decimal? Iv30 { get; set; } = null;

        /// <summary>
        /// Implied Volatility Rank
        /// </summary>
        /// <remarks>The relative volatility over the past year</remarks>
        public decimal? IvRank { get; set; } = null;

        /// <summary>
        /// Implied Volatility Percentile
        /// </summary>
        /// <remarks>The ratio of the current implied volatility baing higher than that over the past year</remarks>
        public decimal? IvPercentile { get; set; } = null;

        /// <summary>
        /// Update the additional fields
        /// </summary>
        /// <param name="ivs">List of past year's ATM implied volatilities</param>
        public void Update(List<decimal> ivs)
        {
            Iv30 = ivs[^1];
            IvRank = CalculateIvRank(ivs);
            IvPercentile = CalculateIvPercentile(ivs);
        }

        /// <summary>
        /// Convert the entry to a CSV string.
        /// </summary>
        public string GetHeader()
        {
            return "iv_30,iv_rank,iv_percentile";
        }

        /// <summary>
        /// Gets the header of the CSV file
        /// </summary>
        public string ToCsv()
        {
            return $"{WriteNullableField(Iv30)},{WriteNullableField(IvRank)},{WriteNullableField(IvPercentile)}";
        }

        // source: https://www.tastylive.com/concepts-strategies/implied-volatility-rank-percentile
        private decimal? CalculateIvRank(List<decimal> ivs)
        {
            if (ivs.Count < 2)
            {
                return null;
            }
            var oneYearLow = ivs.Min();
            return (ivs[^1] - oneYearLow) / (ivs.Max() - oneYearLow);
        }

        // source: https://www.tastylive.com/concepts-strategies/implied-volatility-rank-percentile
        private decimal? CalculateIvPercentile(List<decimal> ivs)
        {
            if (ivs.Count < 2)
            {
                return null;
            }
            var daysBelowCurrentIv = ivs.Count(x => x < ivs[^1]);
            return Convert.ToDecimal(daysBelowCurrentIv) / Convert.ToDecimal(ivs.Count);
        }

        private string WriteNullableField(decimal? field)
        {
            return field.HasValue ? field.Value.ToString() : string.Empty;
        }
    }
}
