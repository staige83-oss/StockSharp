namespace StockSharp.Samples.Testing.RsiConsole;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Logging;

using StockSharp.Algo;
using StockSharp.Algo.Commissions;
using StockSharp.Algo.Storages;
using StockSharp.Algo.Testing;
using StockSharp.BusinessEntities;
using StockSharp.Configuration;
using StockSharp.Messages;
using StockSharp.Reporting;

/// <summary>
/// Headless, cross-platform backtest of <see cref="RsiReversionStrategy"/> on the sample history data.
/// Runs without any prompt and writes <c>rsi_backtest_report.json</c> next to the executable.
/// </summary>
class Program
{
	static async Task<int> Main()
	{
		if (Paths.HistoryDataPath == null)
		{
			Console.WriteLine("History data path not found. Install StockSharp.Samples.HistoryData package.");
			return 1;
		}

		Console.WriteLine($"History data: {Paths.HistoryDataPath}");
		Console.WriteLine($"Security:     {Paths.HistoryDefaultSecurity}");
		Console.WriteLine($"Period:       {Paths.HistoryBeginDate:d} - {Paths.HistoryEndDate:d}");
		Console.WriteLine();

		using var logManager = new LogManager();
		logManager.Listeners.Add(new FileLogListener("rsi_backtest.log"));

		var exchangeInfoProvider = new InMemoryExchangeInfoProvider();
		var id = Paths.HistoryDefaultSecurity.ToSecurityId();

		var security = new Security
		{
			Id = Paths.HistoryDefaultSecurity,
			Code = id.SecurityCode,
			Board = exchangeInfoProvider.GetOrCreateBoard(id.BoardCode),
		};

		var storageRegistry = new StorageRegistry
		{
			DefaultDrive = new LocalMarketDataDrive(Paths.FileSystem, Paths.HistoryDataPath)
		};

		var startTime = Paths.HistoryBeginDate.UtcKind();
		var stopTime = Paths.HistoryEndDate.UtcKind();

		var level1Info = new Level1ChangeMessage
		{
			SecurityId = security.ToSecurityId(),
			ServerTime = startTime,
		}
		.TryAdd(Level1Fields.MinPrice, 0.01m)
		.TryAdd(Level1Fields.MaxPrice, 1000000m)
		.TryAdd(Level1Fields.MarginBuy, 10000m)
		.TryAdd(Level1Fields.MarginSell, 10000m);

		var portfolio = Portfolio.CreateSimulator();
		portfolio.CurrentValue = 100000;

		var connector = new HistoryEmulationConnector(new CollectionSecurityProvider([security]), [portfolio])
		{
			EmulationAdapter =
			{
				Settings =
				{
					MatchOnTouch = false,
					CommissionRules = [new CommissionTradeRule { Value = 0.01m }],
				}
			},

			HistoryMessageAdapter =
			{
				StorageRegistry = storageRegistry,
				StartDate = startTime,
				StopDate = stopTime,
			},
		};

		logManager.Sources.Add(connector);

		var strategy = new RsiReversionStrategy
		{
			RsiLength = 14,
			TrendLength = 200,
			Oversold = 30,
			Overbought = 70,
			Volume = 1,
			CandleType = TimeSpan.FromMinutes(5).TimeFrame(),
			Portfolio = portfolio,
			Security = security,
			Connector = connector,
		};

		logManager.Sources.Add(strategy);

		var finished = new TaskCompletionSource();

		connector.SecurityReceived += (subscription, s) =>
		{
			if (s == security)
				_ = connector.EmulationAdapter.SendInMessageAsync(level1Info, default);
		};

		var lastProgress = 0;
		connector.ProgressChanged += steps =>
		{
			var progress = (int)steps;

			if (progress > lastProgress && progress % 10 == 0)
			{
				Console.Write($"\rProgress: {progress}%   ");
				lastProgress = progress;
			}
		};

		connector.StateChanged2 += async state =>
		{
			if (state != ChannelStates.Stopped)
				return;

			await strategy.StopAsync();
			finished.TrySetResult();
		};

		Console.WriteLine($"Running RSI({strategy.RsiLength}) {strategy.Oversold}/{strategy.Overbought} with SMA({strategy.TrendLength}) trend filter on {strategy.CandleType.Arg} candles...");

		await strategy.StartAsync();
		connector.Connect();
		await connector.StartAsync();

		await finished.Task;

		Console.WriteLine();
		Console.WriteLine();
		Console.WriteLine("=== Backtest completed ===");

		foreach (var param in strategy.StatisticManager.Parameters.OrderBy(p => p.Name))
			Console.WriteLine($"  {param.Name,-32} {param.Value}");

		var reportPath = Path.GetFullPath("rsi_backtest_report.json");

		await using (var stream = File.Create(reportPath))
			await new JsonReportGenerator().Generate(strategy, stream, CancellationToken.None);

		Console.WriteLine();
		Console.WriteLine($"Report saved: {reportPath}");

		return 0;
	}
}
