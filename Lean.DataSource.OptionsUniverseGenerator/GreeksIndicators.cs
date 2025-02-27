﻿/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
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

using QuantConnect.Data.Market;
using QuantConnect.Data;
using QuantConnect.Indicators;

namespace QuantConnect.DataSource.OptionsUniverseGenerator
{
    /// <summary>
    /// Helper class that holds and updates the greeks indicators
    /// </summary>
    public class GreeksIndicators
    {
        private readonly static IRiskFreeInterestRateModel _interestRateProvider = new InterestRateProvider();

        private readonly Symbol _optionSymbol;
        private readonly Symbol _mirrorOptionSymbol;

        private readonly ImpliedVolatility _iv;

        private readonly Delta _delta;
        private readonly Gamma _gamma;
        private readonly Vega _vega;
        private readonly Theta _theta;
        private readonly Rho _rho;

        public decimal ImpliedVolatility => _iv;

        public decimal InterestRate => _delta.RiskFreeRate;

        public decimal DividendYield => _delta.DividendYield;

        public GreeksIndicators(Symbol optionSymbol, Symbol mirrorOptionSymbol, OptionPricingModelType? optionModel = null,
            OptionPricingModelType? ivModel = null)
        {
            _optionSymbol = optionSymbol;
            _mirrorOptionSymbol = mirrorOptionSymbol;

            IDividendYieldModel dividendYieldModel = optionSymbol.SecurityType != SecurityType.IndexOption
                ? DividendYieldProvider.CreateForOption(_optionSymbol)
                : new ConstantDividendYieldModel(0);

            _iv = new ImpliedVolatility(_optionSymbol, _interestRateProvider, dividendYieldModel, _mirrorOptionSymbol, ivModel);
            _delta = new Delta(_optionSymbol, _interestRateProvider, dividendYieldModel, _mirrorOptionSymbol, optionModel, ivModel);
            _gamma = new Gamma(_optionSymbol, _interestRateProvider, dividendYieldModel, _mirrorOptionSymbol, optionModel, ivModel);
            _vega = new Vega(_optionSymbol, _interestRateProvider, dividendYieldModel, _mirrorOptionSymbol, optionModel, ivModel);
            _theta = new Theta(_optionSymbol, _interestRateProvider, dividendYieldModel, _mirrorOptionSymbol, optionModel, ivModel);
            _rho = new Rho(_optionSymbol, _interestRateProvider, dividendYieldModel, _mirrorOptionSymbol, optionModel, ivModel);

            _delta.ImpliedVolatility = _iv;
            _gamma.ImpliedVolatility = _iv;
            _vega.ImpliedVolatility = _iv;
            _theta.ImpliedVolatility = _iv;
            _rho.ImpliedVolatility = _iv;
        }

        public void Update(IBaseDataBar data)
        {
            _iv.Update(data);
            _delta.Update(data);
            _gamma.Update(data);
            _vega.Update(data);
            _theta.Update(data);
            _rho.Update(data);
        }

        public Greeks GetGreeks()
        {
            return new GreeksHolder(_delta, _gamma, _vega, _theta, _rho);
        }

        private class GreeksHolder : Greeks
        {
            public override decimal Delta { get; }

            public override decimal Gamma { get; }

            public override decimal Vega { get; }

            public override decimal Theta { get; }

            public override decimal Rho { get; }

            public override decimal Lambda { get; }

            public GreeksHolder(decimal delta, decimal gamma, decimal vega, decimal theta, decimal rho)
            {
                Delta = delta;
                Gamma = gamma;
                Vega = vega;
                Theta = theta;
                Rho = rho;
            }
        }
    }
}