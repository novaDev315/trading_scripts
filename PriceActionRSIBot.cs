using System;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace PriceActionRSIBot
{
    public enum BotTradeType
    {
        Buy,
        Sell
    }

    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class PriceActionRSIBot : Robot
    {
        private bool isPositionOpen = false;

        [Parameter("RSI Overbought Threshold", DefaultValue = 70)]
        public int RSIOverboughtThreshold { get; set; }

        [Parameter("RSI Oversold Threshold", DefaultValue = 30)]
        public int RSIOversoldThreshold { get; set; }

        [Parameter("RSI Periods", DefaultValue = 14)]
        public int RSIPeriods { get; set; }

        [Parameter("Take Profit in Pips", DefaultValue = 50)]
        public int TakeProfitPips { get; set; }

        [Parameter("Stop Loss in Pips", DefaultValue = 30)]
        public int StopLossPips { get; set; }

        [Parameter("Trailing Stop in Pips", DefaultValue = 20)]
        public int TrailingStopPips { get; set; }

        [Parameter("Max Risk Percentage", DefaultValue = 1.0)]
        public double MaxRiskPercentage { get; set; }

        private MovingAverage shortMA;
        private MovingAverage longMA;
        private BollingerBands bollingerBands;
        private AverageTrueRange atr;
        private IndicatorDataSeries volume;

        protected override void OnStart()
        {
            ChartObjects.DrawText("botLabel", "Price Action RSI Bot", StaticPosition.Center, Colors.White);

            shortMA = Indicators.MovingAverage(MarketSeries.ClosePrices, 20, MovingAverageType.Exponential);
            longMA = Indicators.MovingAverage(MarketSeries.ClosePrices, 50, MovingAverageType.Exponential);
            bollingerBands = Indicators.BollingerBands(MarketSeries.ClosePrices, 20, 2, MovingAverageType.Simple);
            atr = Indicators.AverageTrueRange(14, MovingAverageType.Simple);
            volume = MarketSeries.TickVolume;
        }

        protected override void OnBar()
        {
            foreach (var position in Positions)
            {
                ApplyTrailingStop(position);
            }

            if (!isPositionOpen)
            {
                double rsiValue = RSI();
                double shortMAValue = shortMA.Result.LastValue;
                double longMAValue = longMA.Result.LastValue;
                double upperBollingerBand = bollingerBands.Top.LastValue;
                double lowerBollingerBand = bollingerBands.Bottom.LastValue;
                double volumeValue = volume.LastValue;
                double atrValue = atr.Result.LastValue;

                if (CanOpenPosition(rsiValue, shortMAValue, longMAValue, upperBollingerBand, lowerBollingerBand, volumeValue))
                {
                    BotTradeType tradeType = rsiValue < RSIOversoldThreshold ? BotTradeType.Buy : BotTradeType.Sell;
                    double takeProfit = GetTakeProfitPrice(tradeType, atrValue);
                    double stopLoss = GetStopLossPrice(tradeType, atrValue);
                    ExecuteTrade(tradeType, takeProfit, stopLoss);
                }
            }
        }

        private bool CanOpenPosition(double rsiValue, double shortMAValue, double longMAValue, double upperBollingerBand, double lowerBollingerBand, double volumeValue)
        {
            bool isBullishCondition = rsiValue < RSIOversoldThreshold && shortMAValue > longMAValue && MarketSeries.ClosePrices.LastValue > lowerBollingerBand && volumeValue > 0;
            bool isBearishCondition = rsiValue > RSIOverboughtThreshold && shortMAValue < longMAValue && MarketSeries.ClosePrices.LastValue < upperBollingerBand && volumeValue > 0;

            return isBullishCondition || isBearishCondition;
        }

        private void ApplyTrailingStop(Position position)
        {
            double newStopLoss = position.TradeType == TradeType.Buy
                ? position.EntryPrice + Symbol.PipSize * TrailingStopPips
                : position.EntryPrice - Symbol.PipSize * TrailingStopPips;

            if (position.StopLoss != newStopLoss)
            {
                ModifyPosition(position, newStopLoss, position.TakeProfit);
            }
        }

        private void ExecuteTrade(BotTradeType tradeType, double takeProfit, double stopLoss)
        {
            double accountBalance = Account.Balance;
            double riskAmount = accountBalance * (MaxRiskPercentage / 100);

            double positionSize = riskAmount / StopLossPips;
            double tradeAmount = Symbol.NormalizeVolume(positionSize, RoundingMode.ToNearest);

            ExecuteMarketOrder(tradeType.ToTradeType(), Symbol, tradeAmount, tradeType + " order", null, null, takeProfit, stopLoss, "Entry reason: RSI");

            isPositionOpen = true; CalculateStopLossTakeProfit
        }

        private double GetTakeProfitPrice(BotTradeType tradeType, double atrValue)
        {
            return tradeType == BotTradeType.Buy ? Symbol.Bid + atrValue * TakeProfitMultiplier : Symbol.Ask - atrValue * TakeProfitMultiplier;
        }

        private double GetStopLossPrice(BotTradeType tradeType, double atrValue)
        {
            return tradeType == BotTradeType.Buy ? Symbol.Bid - atrValue * StopLossMultiplier : Symbol.Ask + atrValue * StopLossMultiplier;
        }

        private double RSI()
        {
            var rsi = Indicators.RelativeStrengthIndex(MarketSeries.ClosePrices, RSIPeriods);

            return rsi.Result.LastValue;
        }
    }

    public static class Extensions
    {
        public static TradeType ToTradeType(this BotTradeType botTradeType)
        {
            return botTradeType == BotTradeType.Buy ? TradeType.Buy : TradeType.Sell;
        }
    }
}
