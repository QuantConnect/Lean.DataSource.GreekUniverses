/*
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lean.DataSource.DerivativeUniverseGenerator;
using NodaTime;
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine.DataFeeds;
using QuantConnect.Lean.Engine.DataFeeds.Enumerators;
using QuantConnect.Lean.Engine.HistoricalData;
using QuantConnect.Logging;
using QuantConnect.Securities;
using QuantConnect.Util;

namespace QuantConnect.DataSource.DerivativeUniverseGenerator
{
    /// <summary>
    /// Derivatives Universe Generator
    /// </summary>
    public abstract class DerivativeUniverseGenerator
    {
        protected readonly DateTime _processingDate;
        protected readonly SecurityType _securityType;
        protected readonly string _market;
        protected readonly string _dataFolderRoot;
        protected readonly string _outputFolderRoot;
        protected readonly string _universesOutputFolderRoot;

        private readonly IDataProvider _dataProvider;
        private readonly IHistoryProvider _historyProvider;
        private readonly ZipDataCacheProvider _dataCacheProvider;

        protected readonly MarketHoursDatabase _marketHoursDatabase;

        private long _forceEtaUpdate;

        /// <summary>
        /// Resolutions used to fetch price history
        /// </summary>
        protected virtual Resolution[] PriceHistoryResolutions { get; } = new[] { Resolution.Daily };

        private readonly int _historyBarCount = 1;

        /// <summary>
        /// Initializes a new instance of the <see cref="DerivativeUniverseGenerator" /> class.
        /// </summary>
        /// <param name="processingDate">The processing date</param>
        /// <param name="securityType">Derivative security type to process</param>
        /// <param name="market">Market of data to process</param>
        /// <param name="dataFolderRoot">Path to the data folder</param>
        /// <param name="outputFolderRoot">Path to the output folder</param>
        public DerivativeUniverseGenerator(DateTime processingDate, SecurityType securityType, string market, string dataFolderRoot,
            string outputFolderRoot)
        {
            _processingDate = processingDate;
            _securityType = securityType;
            _market = market;
            _dataFolderRoot = dataFolderRoot;
            _outputFolderRoot = outputFolderRoot;

            _universesOutputFolderRoot = Path.Combine(_outputFolderRoot, _securityType.SecurityTypeToLower(), _market, "universes");
            if (!Directory.Exists(_universesOutputFolderRoot))
            {
                Directory.CreateDirectory(_universesOutputFolderRoot);
            }

            _dataProvider = Composer.Instance.GetExportedValueByTypeName<IDataProvider>(Config.Get("data-provider", "DefaultDataProvider"));

            var mapFileProvider = Composer.Instance.GetExportedValueByTypeName<IMapFileProvider>(Config.Get("map-file-provider", "LocalZipMapFileProvider"));
            mapFileProvider.Initialize(_dataProvider);

            var factorFileProvider = Composer.Instance.GetExportedValueByTypeName<IFactorFileProvider>(Config.Get("factor-file-provider", "LocalZipFactorFileProvider"));
            factorFileProvider.Initialize(mapFileProvider, _dataProvider);

            var api = new Api.Api();
            api.Initialize(Globals.UserId, Globals.UserToken, Globals.DataFolder);

            _dataCacheProvider = new ZipDataCacheProvider(_dataProvider);
            _historyProvider = new HistoryProviderManager();
            var parameters = new HistoryProviderInitializeParameters(null, api, _dataProvider, _dataCacheProvider, mapFileProvider,
                factorFileProvider, (_) => { }, true, new DataPermissionManager(), null,
                new AlgorithmSettings() { DailyPreciseEndTime = securityType == SecurityType.IndexOption });
            _historyProvider.Initialize(parameters);

            _marketHoursDatabase = MarketHoursDatabase.FromDataFolder();
        }

        /// <summary>
        /// Runs this instance.
        /// </summary>
        public bool Run()
        {
            Log.Trace($"DerivativeUniverseGenerator.Run(): Processing {_securityType}-{_market} universes for date {_processingDate:yyyy-MM-dd}");

            try
            {
                var symbolChainProvider = new ChainSymbolProvider(_dataCacheProvider, _processingDate, _securityType, _market, _dataFolderRoot);
                var symbols = symbolChainProvider.GetSymbols();
                Log.Trace($"DerivativeUniverseGenerator.Run(): found {symbols.Count} underlying symbols with {symbols.Sum(x => x.Value.Count)} derivative symbols");
                return GenerateUniverses(symbols);
            }
            catch (Exception ex)
            {
                Log.Error(ex,
                    $"DerivativeUniverseGenerator.Run(): Error processing {_securityType}-{_market} universes for date {_processingDate:yyyy-MM-dd}");
                return false;
            }
        }

        /// <summary>
        /// Generates the universes for each given canonical symbol and its constituents (options, future contracts, etc).
        /// </summary>
        /// <param name="symbols">The symbols keyed by their canonical</param>
        private bool GenerateUniverses(Dictionary<Symbol, List<Symbol>> symbols)
        {
            var cancellationTokenSource = new CancellationTokenSource();
            var symbolCounter = 0;
            var totalContracts = symbols.Sum(x => x.Value.Count);
            var underlyingsWithMissingData = 0;
            var start = DateTime.UtcNow;
            Parallel.ForEach(symbols, new ParallelOptions { MaxDegreeOfParallelism = (int)(Environment.ProcessorCount * 1.5m), CancellationToken = cancellationTokenSource.Token }, kvp =>
            {
                var canonicalSymbol = kvp.Key;
                var contractsSymbols = kvp.Value;
                try
                {
                    var underlyingSymbol = canonicalSymbol.Underlying;

                    var universeFileName = GetUniverseFileName(canonicalSymbol);

                    var underlyingMarketHoursEntry = _marketHoursDatabase.GetEntry(underlyingSymbol.ID.Market, underlyingSymbol, underlyingSymbol.SecurityType);
                    var optionMarketHoursEntry = _marketHoursDatabase.GetEntry(canonicalSymbol.ID.Market, canonicalSymbol, canonicalSymbol.SecurityType);

                    if (!underlyingMarketHoursEntry.ExchangeHours.IsDateOpen(_processingDate) ||
                        !optionMarketHoursEntry.ExchangeHours.IsDateOpen(_processingDate))
                    {
                        Log.Trace($"Market is closed on {_processingDate:yyyy/MM/dd} for {underlyingSymbol}, universe file will not be generated.");
                        return;
                    }

                    Log.Trace($"DerivativeUniverseGenerator.GenerateUniverses(): " +
                        $"Generating universe for {canonicalSymbol} on {_processingDate:yyyy/MM/dd} with {contractsSymbols.Count} constituents.");

                    using var writer = new StreamWriter(universeFileName);

                    // Add the header
                    var tempEntry = CreateUniverseEntry(canonicalSymbol);
                    writer.WriteLine($"#{tempEntry.GetHeader()}");

                    var underlyingEntryGenerated = TryGenerateAndWriteUnderlyingLine(underlyingSymbol, underlyingMarketHoursEntry, writer,
                        out var underlyingEntry, out var underlyingHistory);
                    // Underlying not mapped or missing data, so just skip them
                    if (!underlyingEntryGenerated)
                    {
                        Interlocked.Increment(ref underlyingsWithMissingData);
                        Log.Error($"DerivativeUniverseGenerator.GenerateUniverses(): " +
                            $"Underlying data missing for {underlyingSymbol} on {_processingDate:yyyy/MM/dd}, universe file will not be generated.");
                        UpdateEta(ref symbolCounter, totalContracts, start, contractsSymbols.Count);
                        return;
                    }

                    var derivativeEntries = GenerateDerivativeEntries(canonicalSymbol, contractsSymbols, optionMarketHoursEntry, underlyingHistory,
                        underlyingEntry);
                    foreach (var entry in derivativeEntries)
                    {
                        writer.WriteLine(entry.ToCsv());
                    }

                    UpdateEta(ref symbolCounter, totalContracts, start, contractsSymbols.Count);
                }
                catch (Exception exception)
                {
                    Log.Error(exception, $"Exception processing {canonicalSymbol}");
                    cancellationTokenSource.Cancel();
                }
            });

            if (underlyingsWithMissingData > 0)
            {
                Log.Trace($"DerivativeUniverseGenerator.GenerateUniverses(): " +
                    $"Underlying data missing for {underlyingsWithMissingData} out of {symbols.Count} symbols on {_processingDate:yyyy/MM/dd}.");
            }

            return !cancellationTokenSource.IsCancellationRequested;
        }

        private void UpdateEta(ref int symbolCounter, int totalContracts, DateTime start, int processedContractsCount)
        {
            const int step = 100000;
            var prevMod = symbolCounter % step;
            var currentCounter = Interlocked.Add(ref symbolCounter, processedContractsCount);
            var currentMod = currentCounter % step;
            if (processedContractsCount >= step || currentMod <= prevMod || Interlocked.CompareExchange(ref _forceEtaUpdate, 0, 1) == 1)
            {
                var took = DateTime.UtcNow - start;
                try
                {
                    var eta = (totalContracts - currentCounter) / currentCounter * took;
                    Log.Trace($"DerivativeUniverseGenerator.GenerateUniverses(): finished processing {currentCounter} symbols. Took: {took}. ETA: {eta}");
                }
                catch
                {
                    // We couldn't get a proper ETA, let's force the update on next call
                    Interlocked.Exchange(ref _forceEtaUpdate, 1);
                }
            }
        }

        /// <summary>
        /// Generates the file name where the derivative's universe entry will be saved.
        /// </summary>
        protected abstract string GetUniverseFileName(Symbol canonicalSymbol);

        /// <summary>
        /// Generates the derivative's underlying universe entry and gets the historical data used for the entry generation.
        /// </summary>
        /// <returns>
        /// The historical data used for the underlying universe entry generation.
        /// </returns>
        /// <remarks>
        /// This method should be overridden to return an empty list (and skipping writing to the stream) if no underlying entry is needed.
        /// </remarks>
        protected virtual bool TryGenerateAndWriteUnderlyingLine(Symbol underlyingSymbol, MarketHoursDatabase.Entry marketHoursEntry,
            StreamWriter writer, out IDerivativeUniverseFileEntry entry, out List<Slice> history)
        {
            GetHistoryTimeRange(PriceHistoryResolutions[0], marketHoursEntry, out var historyEndUtc, out var historyStartUtc);
            var underlyingHistoryRequest = new HistoryRequest(
                historyStartUtc,
                historyEndUtc,
                typeof(TradeBar),
                underlyingSymbol,
                PriceHistoryResolutions[0],
                marketHoursEntry.ExchangeHours,
                marketHoursEntry.DataTimeZone,
                PriceHistoryResolutions[0],
                includeExtendedMarketHours: false,
                isCustomData: false,
                DataNormalizationMode.ScaledRaw,
                LeanData.GetCommonTickTypeForCommonDataTypes(typeof(TradeBar), _securityType));

            entry = CreateUniverseEntry(underlyingSymbol);
            history = GetHistory(new[] { underlyingHistoryRequest }, marketHoursEntry.ExchangeHours.TimeZone, marketHoursEntry);
            var success = true;

            if (history == null || history.Count == 0)
            {
                Log.Error($"DerivativeUniverseGenerator.GenerateUnderlyingLine(): " +
                    $"No historical data found for underlying {underlyingSymbol} on {_processingDate:yyyy/MM/dd}. Prices will be set to zero.");
                success = false;
            }
            else
            {
                entry.Update(history[^1]);
            }
            writer.WriteLine(entry.ToCsv());

            return success;
        }

        private List<Slice> GetHistory(HistoryRequest[] historyRequests,
            DateTimeZone sliceTimeZone, MarketHoursDatabase.Entry marketHoursEntry)
        {
            List<Slice> history = null;

            foreach (var resolution in PriceHistoryResolutions)
            {
                GetHistoryTimeRange(resolution, marketHoursEntry, out var historyEndUtc, out var historyStartUtc);

                var resolutionHistoryRequests = historyRequests.Select(x =>
                {
                    var request = new HistoryRequest(x, x.Symbol, historyStartUtc, historyEndUtc);
                    request.Resolution = resolution;
                    request.FillForwardResolution = resolution;
                    return request;
                }).ToArray();

                history = _historyProvider.GetHistory(resolutionHistoryRequests, sliceTimeZone).ToList();
                if (history != null && history.Count > 0)
                {
                    return history;
                }
            }

            return history;
        }

        private void GetHistoryTimeRange(Resolution resolution, MarketHoursDatabase.Entry marketHoursEntry,
            out DateTime historyEndUtc, out DateTime historyStartUtc)
        {
            historyEndUtc = resolution != Resolution.Daily ? _processingDate : _processingDate.AddDays(1);
            historyStartUtc = Time.GetStartTimeForTradeBars(marketHoursEntry.ExchangeHours, historyEndUtc, resolution.ToTimeSpan(),
                _historyBarCount, false, marketHoursEntry.DataTimeZone);

            historyEndUtc = historyEndUtc.ConvertToUtc(marketHoursEntry.ExchangeHours.TimeZone);
            historyStartUtc = historyStartUtc.ConvertToUtc(marketHoursEntry.ExchangeHours.TimeZone);
        }

        /// <summary>
        /// Generates the derivative universe entries for the specified canonical symbol.
        /// </summary>
        /// <remarks>The underlying history is a List to avoid multiple enumerations of the history</remarks>
        protected virtual IEnumerable<IDerivativeUniverseFileEntry> GenerateDerivativeEntries(Symbol canonicalSymbol, List<Symbol> symbols,
            MarketHoursDatabase.Entry marketHoursEntry, List<Slice> underlyingHistory, IDerivativeUniverseFileEntry underlyingEntry)
        {
            foreach (var symbol in symbols)
            {
                if (Log.DebuggingEnabled)
                {
                    Log.Debug($"Generating universe entry for {symbol.Value}");
                }

                GetHistoryTimeRange(PriceHistoryResolutions[0], marketHoursEntry, out var historyEndUtc, out var historyStartUtc);
                var historyRequests = GetDerivativeHistoryRequests(symbol, historyStartUtc, historyEndUtc, marketHoursEntry);

                IDerivativeUniverseFileEntry entry;
                if (historyRequests == null || historyRequests.Length == 0)
                {
                    entry = GenerateDerivativeEntry(symbol, Enumerable.Empty<Slice>().ToList(), Enumerable.Empty<Slice>().ToList());
                }
                else
                {
                    var history = GetHistory(historyRequests, marketHoursEntry.ExchangeHours.TimeZone, marketHoursEntry);
                    entry = GenerateDerivativeEntry(symbol, history, underlyingHistory);
                }

                yield return entry;
            }
        }

        /// <summary>
        /// Creates the requests to get the data to be used to generate the universe entry for the given derivative symbol
        /// </summary>
        protected virtual HistoryRequest[] GetDerivativeHistoryRequests(Symbol symbol, DateTime startUtc, DateTime endUtc,
            MarketHoursDatabase.Entry marketHoursEntry)
        {
            var dataTypes = new[] { typeof(TradeBar), typeof(QuoteBar), typeof(OpenInterest) };
            return dataTypes.Select(dataType => new HistoryRequest(
                startUtc,
                endUtc,
                dataType,
                symbol,
                PriceHistoryResolutions[0],
                marketHoursEntry.ExchangeHours,
                marketHoursEntry.DataTimeZone,
                PriceHistoryResolutions[0],
                includeExtendedMarketHours: false,
                isCustomData: false,
                DataNormalizationMode.ScaledRaw,
                LeanData.GetCommonTickTypeForCommonDataTypes(dataType, _securityType))).ToArray();
        }

        /// <summary>
        /// Generates the given symbol universe entry from the provided historical data
        /// </summary>
        protected virtual IDerivativeUniverseFileEntry GenerateDerivativeEntry(Symbol symbol, List<Slice> history, List<Slice> underlyingHistory)
        {
            var entry = CreateUniverseEntry(symbol);
            using var enumerator = new SynchronizingSliceEnumerator(history.GetEnumerator(), underlyingHistory.GetEnumerator());

            while (enumerator.MoveNext())
            {
                entry.Update(enumerator.Current);
            }

            return entry;
        }

        /// <summary>
        /// Factory method to create an instance of <see cref="IDerivativeUniverseFileEntry"/> for the given <paramref name="symbol"/>
        /// </summary>
        protected abstract IDerivativeUniverseFileEntry CreateUniverseEntry(Symbol symbol);
    }
}