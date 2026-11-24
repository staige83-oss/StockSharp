namespace StockSharp.Samples.Testing.RsiConsole;

using System;

using Ecng.Common;

using StockSharp.Algo.Indicators;
using StockSharp.Algo.Strategies;
using StockSharp.Messages;

/// <summary>
/// RSI mean-reversion strategy with a trend filter.
/// </summary>
/// <remarks>
/// Goes long when RSI drops below <see cref="Oversold"/> while the close is above the trend SMA,
/// goes short when RSI rises above <see cref="Overbought"/> while the close is below it,
/// and exits once RSI returns to the neutral 50 level. A percent stop-loss protects every position.
/// </remarks>
class RsiReversionStrategy : Strategy
{
	private readonly StrategyParam<int> _rsiLength;
	private readonly StrategyParam<int> _trendLength;
	private readonly StrategyParam<decimal> _oversold;
	private readonly StrategyParam<decimal> _overbought;
	private readonly StrategyParam<Unit> _stopValue;
	private readonly StrategyParam<DataType> _candleType;

	public RsiReversionStrategy()
	{
		_rsiLength = Param(nameof(RsiLength), 14)
			.SetCanOptimize(true)
			.SetOptimize(7, 21, 7);

		_trendLength = Param(nameof(TrendLength), 200)
			.SetCanOptimize(true)
			.SetOptimize(100, 300, 50);

		_oversold = Param(nameof(Oversold), 30m);
		_overbought = Param(nameof(Overbought), 70m);
		_stopValue = Param(nameof(StopValue), new Unit(1, UnitTypes.Percent));
		_candleType = Param(nameof(CandleType), TimeSpan.FromMinutes(5).TimeFrame()).SetRequired();
	}

	public int RsiLength
	{
		get => _rsiLength.Value;
		set => _rsiLength.Value = value;
	}

	public int TrendLength
	{
		get => _trendLength.Value;
		set => _trendLength.Value = value;
	}

	public decimal Oversold
	{
		get => _oversold.Value;
		set => _oversold.Value = value;
	}

	public decimal Overbought
	{
		get => _overbought.Value;
		set => _overbought.Value = value;
	}

	public Unit StopValue
	{
		get => _stopValue.Value;
		set => _stopValue.Value = value;
	}

	public DataType CandleType
	{
		get => _candleType.Value;
		set => _candleType.Value = value;
	}

	protected override void OnStarted2(DateTime time)
	{
		base.OnStarted2(time);

		var rsi = new RelativeStrengthIndex { Length = RsiLength };
		var trend = new SMA { Length = TrendLength };

		SubscribeCandles(CandleType)
			.Bind(rsi, trend, OnProcess)
			.Start();

		StartProtection(new Unit(), StopValue);
	}

	private void OnProcess(ICandleMessage candle, decimal rsi, decimal trend)
	{
		if (!IsFormedAndOnlineAndAllowTrading())
			return;

		var close = candle.ClosePrice;

		if (Position == 0)
		{
			if (rsi < Oversold && close > trend)
				BuyMarket(Volume);
			else if (rsi > Overbought && close < trend)
				SellMarket(Volume);
		}
		else if (Position > 0 && rsi >= 50)
		{
			SellMarket(Position);
		}
		else if (Position < 0 && rsi <= 50)
		{
			BuyMarket(Position.Abs());
		}
	}
}
