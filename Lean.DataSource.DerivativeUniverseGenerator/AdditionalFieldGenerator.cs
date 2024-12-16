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
                    csv[i] += $",{additionalFields.GetHeader()}";
                }
                else
                {
                    csv[i] += $",{additionalFields.ToCsv()}";
                }
            }

            File.WriteAllLines(csvPath, csv);
        }
    }
}
