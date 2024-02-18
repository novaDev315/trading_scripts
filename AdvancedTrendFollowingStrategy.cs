using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using cAlgo.API;
using cAlgo.API.Collections;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class AdvancedTrendFollowingStrategy : Robot
    {
        private enum TrendDirection
        {
            Up,
            Down,
            Undefined
        }
        private MovingAverage shortMA;
        private MovingAverage longMA;
        private MovingAverage macdSignal;
        private MacdHistogram macd;
        private IchimokuKinkoHyo ichimoku;
        private ExponentialMovingAverage tenkanSen;
        private ExponentialMovingAverage kijunSen;
        private AverageTrueRange atr;
        private double supportLevel;
        private double resistanceLevel;

        [Parameter("Short MA Periods", DefaultValue = 20)]
        public int ShortMAPeriods { get; set; }

        [Parameter("Long MA Periods", DefaultValue = 50)]
        public int LongMAPeriods { get; set; }

        [Parameter("MACD Fast EMA Periods", DefaultValue = 12)]
        public int MacdFastEMA { get; set; }

        [Parameter("MACD Slow EMA Periods", DefaultValue = 26)]
        public int MacdSlowEMA { get; set; }

        [Parameter("MACD Signal SMA Periods", DefaultValue = 9)]
        public int MacdSignalSMA { get; set; }

        [Parameter("Risk Percentage", DefaultValue = 1)]
        public double RiskPercentage { get; set; }

        protected override void OnStart()
        {
            shortMA = Indicators.MovingAverage(MarketSeries.Close, ShortMAPeriods, MovingAverageType.Exponential);
            longMA = Indicators.MovingAverage(MarketSeries.Close, LongMAPeriods, MovingAverageType.Exponential);
            macd = Indicators.MacdHistogram(MarketSeries.Close, MacdFastEMA, MacdSlowEMA, MacdSignalSMA);
            atr = Indicators.AverageTrueRange(14, MovingAverageType.Exponential);
            macdSignal = Indicators.SimpleMovingAverage(macd.Signal, MacdSignalSMA);
            ichimoku = Indicators.IchimokuKinkoHyo(9, 26, 52);
            tenkanSen = Indicators.ExponentialMovingAverage(MarketSeries.Close, 9);
            kijunSen = Indicators.ExponentialMovingAverage(MarketSeries.Close, 26);

        }

        protected override void OnTick()
        {
            // Check if there's already an open position
            if (Positions.Count > 0)
                return;

            // Identify the current trend direction
            TrendDirection trendDirection = IdentifyTrendDirection();

            // Identify support and resistance levels
            IdentifySupportResistanceLevels();

            // Identify candlestick patterns
            bool bullishCandlePattern = RecognizeBullishCandlePattern();
            bool bearishCandlePattern = RecognizeBearishCandlePattern();

            // Check for MACD crossovers
            bool bullishMacdCrossover = IsBullishMacdCrossover();
            bool bearishMacdCrossover = IsBearishMacdCrossover();

            // Check for Ichimoku Cloud signals
            bool bullishIchimokuSignal = IsBullishIchimokuSignal();
            bool bearishIchimokuSignal = IsBearishIchimokuSignal();

            // Implement trading logic based on trend, levels, and patterns
            if ((trendDirection == TrendDirection.Up && (bullishMacdCrossover || bullishIchimokuSignal || bullishCandlePattern)) ||
               (trendDirection == TrendDirection.Down && (bearishMacdCrossover || bearishIchimokuSignal || bearishCandlePattern)))
            {
                ExecuteTrade(trendDirection);
            }

        }

        private void ExecuteTrade(TrendDirection trendDir)
        {

            TradeType tradeType = trendDir == TrendDirection.Up ? TradeType.Buy : TradeType.Sell;
            double entryPrice = tradeType == TradeType.Buy ? Symbol.Ask : Symbol.Bid;

            // Calculate ATR value for volatility estimation
            double atrValue = atr.Result.LastValue;

            // Calculate stop loss and take profit based on ATR and risk percentage
            CalculateStopLossTakeProfit(entryPrice, atrValue, RiskPercentage, tradeType, out double stopLoss, out double takeProfit);

            // Ensure that stop loss is not too close to the entry price
            double minStopLossDistance = atrValue * 1.5;
            if (tradeType == TradeType.Buy && entryPrice - stopLoss < minStopLossDistance)
            {
                stopLoss = entryPrice - minStopLossDistance;
            }
            else if (tradeType == TradeType.Sell && stopLoss - entryPrice < minStopLossDistance)
            {
                stopLoss = entryPrice + minStopLossDistance;
            }

            // Calculate position size based on risk and stop loss distance
            double riskAmount = Account.Balance * (RiskPercentage / 100);
            double positionSize = riskAmount / (stopLoss - entryPrice);

            // Normalize and execute the trade order
            double normalizedVolume = Symbol.NormalizeVolume(positionSize, RoundingMode.ToNearest);

            ExecuteMarketOrder(tradeType, Symbol, normalizedVolume, "Trend Following Order", stopLoss, takeProfit);

            // Implement pyramiding for additional positions
            /*
            double pyramidFactor = 0.5; // Fraction of position size to add for pyramiding
            double pyramidTakeProfitMultiplier = 2.0; // Multiplier for adjusting take profit for pyramid positions

            for (int pyramidLevel = 1; pyramidLevel <= 1; pyramidLevel++) // Adding up to 5 pyramid levels
            {
                double pyramidTakeProfit = takeProfit * (Math.Pow(pyramidTakeProfitMultiplier, pyramidLevel - 1));
                double pyramidTradeAmount = tradeAmount * pyramidFactor;

                // Normalize and execute the pyramid trade order
                double normalizedPyramidVolume = Symbol.NormalizeVolume(pyramidTradeAmount, RoundingMode.ToNearest);
                ExecuteMarketOrder(tradeType, Symbol, normalizedPyramidVolume, $"Pyramid Level {pyramidLevel}", stopLoss, pyramidTakeProfit);
            }
            */

        }
        private void CalculateStopLossTakeProfit(double entryPrice, double atrValue, double riskPercentage, TradeType tradeType, out double stopLoss, out double takeProfit)
        {
            // Calculate stop loss and take profit based on ATR and risk percentage
            double riskAmount = Account.Balance * (riskPercentage / 100);
            double pipValue = Symbol.PipValue;

            double atrInPips = atrValue / pipValue;

            // Define ATR-based multipliers for stop loss and take profit
            double stopLossATRMultiplier = 2.0;
            double takeProfitATRMultiplier = 10.0;

            // Define minimum distances in pips
            double minStopLossPips = 15; // Minimum 10 pips
            double minTakeProfitPips = 45; // Minimum 20 pips

            double stopLossPips = Math.Max(atrInPips * stopLossATRMultiplier, minStopLossPips);
            double takeProfitPips = Math.Max(atrInPips * takeProfitATRMultiplier, minTakeProfitPips);

            if (tradeType == TradeType.Buy)
            {
                stopLoss = Symbol.Bid - (stopLossPips * pipValue);
                takeProfit = Symbol.Bid + (takeProfitPips * pipValue);
            }
            else
            {
                stopLoss = Symbol.Ask + (stopLossPips * pipValue);
                takeProfit = Symbol.Ask - (takeProfitPips * pipValue);
            }
        }

        private TrendDirection IdentifyTrendDirection()
        {
            if (longMA.Result.HasCrossedAbove(shortMA.Result, 1))
            {
                return TrendDirection.Up;
            }
            else if (longMA.Result.HasCrossedBelow(shortMA.Result, 1))
            {
                return TrendDirection.Down;
            }
            else
            {
                return TrendDirection.Undefined;
            }
        }

        private void IdentifySupportResistanceLevels()
        {
            // Calculate pivot points for the current day
            double pivot = (MarketSeries.High.LastValue + MarketSeries.Low.LastValue + MarketSeries.Close.LastValue) / 3;
            double support1 = (2 * pivot) - MarketSeries.High.LastValue;
            double support2 = pivot - (MarketSeries.High.LastValue - MarketSeries.Low.LastValue);
            double resistance1 = (2 * pivot) - MarketSeries.Low.LastValue;
            double resistance2 = pivot + (MarketSeries.High.LastValue - MarketSeries.Low.LastValue);

            // Calculate Fibonacci retracement levels
            double priceRange = MarketSeries.High.Maximum(14) - MarketSeries.Low.Minimum(14);
            double fib236 = MarketSeries.Close.LastValue - 0.236 * priceRange;
            double fib382 = MarketSeries.Close.LastValue - 0.382 * priceRange;
            double fib500 = MarketSeries.Close.LastValue - 0.500 * priceRange;
            double fib618 = MarketSeries.Close.LastValue - 0.618 * priceRange;

            // Set support and resistance levels based on pivot points and Fibonacci levels
            supportLevel = Math.Min(Math.Min(support1, support2), Math.Min(fib236, fib382));
            resistanceLevel = Math.Max(Math.Max(resistance1, resistance2), Math.Max(fib500, fib618));
        }

        private bool IsBullishMacdCrossover()
        {
            if (macd.Histogram.Last(1) < 0 && macd.Histogram.LastValue > 0 &&
                macd.Signal.Last(1) < macd.Histogram.Last(1) && macd.Signal.LastValue > macd.Histogram.LastValue &&
                macd.Signal.Last(1) > 0 && macdSignal.Result.Last(1) > 0 && macdSignal.Result.LastValue > 0)
            {
                return true;
            }
            return false;
        }

        private bool IsBearishMacdCrossover()
        {
            if (macd.Histogram.Last(1) > 0 && macd.Histogram.LastValue < 0 &&
                macd.Signal.Last(1) > macd.Histogram.Last(1) && macd.Signal.LastValue < macd.Histogram.LastValue &&
                macd.Signal.Last(1) < 0 && macdSignal.Result.Last(1) < 0 && macdSignal.Result.LastValue < 0)
            {
                return true;
            }
            return false;
        }

        private bool IsBullishIchimokuSignal()
        {
            bool isBullishSignal = tenkanSen.Result.Last(1) < kijunSen.Result.Last(1) &&
                                   tenkanSen.Result.LastValue > kijunSen.Result.LastValue &&
                                   MarketSeries.Close.Last(1) > ichimoku.SenkouSpanB.Last(1) &&
                                   MarketSeries.Close.LastValue > ichimoku.SenkouSpanA.LastValue;

            return isBullishSignal;
        }

        private bool IsBearishIchimokuSignal()
        {
            bool isBearishSignal = tenkanSen.Result.Last(1) > kijunSen.Result.Last(1) &&
                                   tenkanSen.Result.LastValue < kijunSen.Result.LastValue &&
                                   MarketSeries.Close.Last(1) < ichimoku.SenkouSpanA.Last(1) &&
                                   MarketSeries.Close.LastValue < ichimoku.SenkouSpanB.LastValue;

            return isBearishSignal;
        }

        private bool RecognizeBullishCandlePattern()
        {
            // Identify complex candlestick patterns that are bullish
            bool bullishEngulfing = false;
            bool doubleBottom = false;
            bool headShouldersBottom = false;

            // Check for specific bullish candlestick patterns
            if (MarketSeries.Open.LastValue < MarketSeries.Close.LastValue)
            {
                // Bullish Engulfing Pattern
                bullishEngulfing = IsBullishEngulfing();

                // Double Bottom Pattern
                doubleBottom = IsDoubleBottom();

                // Head and Shoulders Bottom Pattern
                headShouldersBottom = IsHeadAndShouldersBottom();
            }

            return bullishEngulfing || doubleBottom || headShouldersBottom;
        }

        private bool RecognizeBearishCandlePattern()
        {
            // Identify complex candlestick patterns that are bearish
            bool bearishEngulfing = false;
            bool doubleTop = false;
            bool headShouldersTop = false;

            // Check for specific bearish candlestick patterns
            if (MarketSeries.Open.LastValue > MarketSeries.Close.LastValue)
            {
                // Bearish Engulfing Pattern
                bearishEngulfing = IsBearishEngulfing();

                // Double Top Pattern
                doubleTop = IsDoubleTop();

                // Head and Shoulders Top Pattern 
                headShouldersTop = IsHeadAndShouldersTop();
            }

            return bearishEngulfing || doubleTop || headShouldersTop;
        }


        // Identify double top pattern for bearish candlestick patterns
        private bool IsDoubleTop()
        {
            int lookbackPeriod = 20;

            for (int i = 2; i < lookbackPeriod; i++)
            {
                double high1 = MarketSeries.High.Last(i + 1);
                double high2 = MarketSeries.High.Last(i);
                double high3 = MarketSeries.High.Last(i - 1);
                double low1 = MarketSeries.Low.Last(i + 1);
                double low2 = MarketSeries.Low.Last(i);
                double low3 = MarketSeries.Low.Last(i - 1);

                if (high1 < high2 && high2 > high3 &&
                    low1 > low2 && low2 < low3)
                {
                    return true;
                }
            }

            return false;
        }

        // Identify double bottom pattern for bullish candlestick patterns
        private bool IsDoubleBottom()
        {
            int lookbackPeriod = 20;

            for (int i = 2; i < lookbackPeriod; i++)
            {
                double high1 = MarketSeries.High.Last(i + 1);
                double high2 = MarketSeries.High.Last(i);
                double high3 = MarketSeries.High.Last(i - 1);
                double low1 = MarketSeries.Low.Last(i + 1);
                double low2 = MarketSeries.Low.Last(i);
                double low3 = MarketSeries.Low.Last(i - 1);

                if (high1 > high2 && high2 < high3 &&
                    low1 < low2 && low2 > low3)
                {
                    return true;
                }
            }

            return false;
        }

        // Identify head and shoulders top pattern for bearish candlestick patterns
        private bool IsHeadAndShouldersTop()
        {
            int lookbackPeriod = 30;

            for (int i = 3; i < lookbackPeriod; i++)
            {
                double high1 = MarketSeries.High.Last(i + 2);
                double high2 = MarketSeries.High.Last(i + 1);
                double high3 = MarketSeries.High.Last(i);
                double high4 = MarketSeries.High.Last(i - 1);
                double low1 = MarketSeries.Low.Last(i + 2);
                double low2 = MarketSeries.Low.Last(i + 1);
                double low3 = MarketSeries.Low.Last(i);
                double low4 = MarketSeries.Low.Last(i - 1);

                if (high2 > high1 && high2 > high3 &&
                    low2 < low1 && low2 < low3 &&
                    high4 < high1 && high4 < high3 &&
                    low4 < low2)
                {
                    return true;
                }
            }

            return false;
        }

        // Identify head and shoulders bottom pattern for bullish candlestick patterns
        private bool IsHeadAndShouldersBottom()
        {
            int lookbackPeriod = 30;

            for (int i = 3; i < lookbackPeriod; i++)
            {
                double high1 = MarketSeries.High.Last(i + 2);
                double high2 = MarketSeries.High.Last(i + 1);
                double high3 = MarketSeries.High.Last(i);
                double high4 = MarketSeries.High.Last(i - 1);
                double low1 = MarketSeries.Low.Last(i + 2);
                double low2 = MarketSeries.Low.Last(i + 1);
                double low3 = MarketSeries.Low.Last(i);
                double low4 = MarketSeries.Low.Last(i - 1);

                if (high2 < high1 && high2 < high3 &&
                    low2 > low1 && low2 > low3 &&
                    high4 > high1 && high4 > high3 &&
                    low4 > low2)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsBullishEngulfing()
        {
            // Check if the current candle is a Bullish Engulfing pattern
            double currentOpen = MarketSeries.Open.LastValue;
            double currentClose = MarketSeries.Close.LastValue;
            double previousOpen = MarketSeries.Open[1];
            double previousClose = MarketSeries.Close[1];

            bool isBullishEngulfing = currentOpen < previousClose && currentClose > previousOpen;

            return isBullishEngulfing;
        }

        private bool IsBearishEngulfing()
        {
            // Check if the current candle is a Bearish Engulfing pattern
            double currentOpen = MarketSeries.Open.LastValue;
            double currentClose = MarketSeries.Close.LastValue;
            double previousOpen = MarketSeries.Open[1];
            double previousClose = MarketSeries.Close[1];

            bool isBearishEngulfing = currentOpen > previousClose && currentClose < previousOpen;

            return isBearishEngulfing;
        }
    }
}