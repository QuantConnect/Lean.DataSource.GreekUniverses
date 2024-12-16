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

namespace QuantConnect.DataSource.OptionsUniverseGenerator
{
    /// <summary>
    /// Entry point for derivatives universe generator.
    /// </summary>
    /// <param name="args">
    /// All CLI argument are optional, if defined they will override the ones defined in config.json
    /// Possible arguments are:
    ///     "--security-type="          : Option security type to process.
    ///     "--market="                 : Market of data to process.
    /// </param>
    public class Program : DerivativeUniverseGenerator.Program
    {
        public static void Main(string[] args)
        {
            Program program = new();
            program.MainImpl(args);
        }

        protected override DerivativeUniverseGenerator.DerivativeUniverseGenerator GetUniverseGenerator(SecurityType securityType, string market,
            string dataFolderRoot, string outputFolderRoot, DateTime processingDate)
        {
            return new OptionsUniverseGenerator(processingDate, securityType, market, dataFolderRoot, outputFolderRoot);
        }

        protected override DerivativeUniverseGenerator.AdditionalFieldGenerator GetAdditionalFieldGenerator(DateTime processingDate,
            string outputFolderRoot)
        {
            return new OptionAdditionalFieldGenerator(processingDate, outputFolderRoot);
        }
    }
}
