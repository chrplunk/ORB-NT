#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// 5-Min ORB Auto Strategy — MES/MNQ/ES/NQ configurable.
    /// 1.0x TP, 2.0x total SL (1.0x extension), dynamic sizing, candle direction filter,
    /// day-of-week filters, time stop. Based on TradingStats.net 6,142-day backtest.
    /// </summary>
    public class ORB_Strategy : Strategy
    {
        #region State
        private double orbHigh, orbLow, orbClose, orbMid, orbRange;
        private double tpLevel, slLevel;
        private int calcContracts, candleBias;
        private bool orbSet, enteredToday, doneToday;
        private DateTime orbDate;
        #endregion

        #region Properties
        [NinjaScriptProperty][Display(Name="Risk Per Trade ($)",Order=1,GroupName="1. Risk")][Range(50,100000)]
        public double RiskPerTrade { get; set; }
        [NinjaScriptProperty][Display(Name="Point Value",Description="MES=5, MNQ=2, ES=50, NQ=20",Order=2,GroupName="1. Risk")][Range(0.5,500)]
        public double PointValue { get; set; }
        [NinjaScriptProperty][Display(Name="Max Contracts",Order=3,GroupName="1. Risk")][Range(1,200)]
        public int MaxContracts { get; set; }

        [NinjaScriptProperty][Display(Name="ORB Start Hour (ET)",Order=1,GroupName="2. Timing")][Range(0,23)]
        public int StartH { get; set; }
        [NinjaScriptProperty][Display(Name="ORB Start Minute",Order=2,GroupName="2. Timing")][Range(0,59)]
        public int StartM { get; set; }
        [NinjaScriptProperty][Display(Name="ORB End Hour (ET)",Order=3,GroupName="2. Timing")][Range(0,23)]
        public int EndH { get; set; }
        [NinjaScriptProperty][Display(Name="ORB End Minute",Order=4,GroupName="2. Timing")][Range(0,59)]
        public int EndM { get; set; }
        [NinjaScriptProperty][Display(Name="Deadline Hour (ET)",Order=5,GroupName="2. Timing")][Range(0,23)]
        public int DeadH { get; set; }
        [NinjaScriptProperty][Display(Name="Deadline Minute",Order=6,GroupName="2. Timing")][Range(0,59)]
        public int DeadM { get; set; }
        [NinjaScriptProperty][Display(Name="Time Stop Hour (ET)",Description="Flatten position at this time",Order=7,GroupName="2. Timing")][Range(0,23)]
        public int StopH { get; set; }
        [NinjaScriptProperty][Display(Name="Time Stop Minute",Order=8,GroupName="2. Timing")][Range(0,59)]
        public int StopM { get; set; }

        [NinjaScriptProperty][Display(Name="Skip Wednesday",Order=1,GroupName="3. Day Filters")]
        public bool SkipWed { get; set; }
        [NinjaScriptProperty][Display(Name="Half Size Wednesday",Order=2,GroupName="3. Day Filters")]
        public bool HalfWed { get; set; }
        [NinjaScriptProperty][Display(Name="Trade Longs",Order=3,GroupName="3. Day Filters")]
        public bool TradeLongs { get; set; }
        [NinjaScriptProperty][Display(Name="Trade Shorts",Order=4,GroupName="3. Day Filters")]
        public bool TradeShorts { get; set; }
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "5-Min ORB Strategy — MES/MNQ/ES/NQ. 1.0x TP, 2.0x SL, dynamic sizing.";
                Name = "ORB_Strategy";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                Slippage = 2;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 5;
                IsInstantiatedOnEachOptimizationIteration = true;

                RiskPerTrade = 500; PointValue = 5; MaxContracts = 25;
                StartH = 9; StartM = 30; EndH = 9; EndM = 35;
                DeadH = 11; DeadM = 30; StopH = 15; StopM = 0;
                SkipWed = false; HalfWed = true;
                TradeLongs = true; TradeShorts = true;
            }
            else if (State == State.Configure) { DayReset(); }
        }

        private void DayReset()
        {
            orbHigh = double.MinValue; orbLow = double.MaxValue;
            orbClose = orbMid = orbRange = tpLevel = slLevel = 0;
            orbSet = enteredToday = doneToday = false;
            candleBias = calcContracts = 0;
        }

        private int BarMins() { return Time[0].Hour * 60 + Time[0].Minute; }
        private int ToMins(int h, int m) { return h * 60 + m; }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade) return;

            int bm = BarMins();
            int orbStart = ToMins(StartH, StartM);
            int orbEnd = ToMins(EndH, EndM);
            int deadline = ToMins(DeadH, DeadM);
            int timeStop = ToMins(StopH, StopM);
            DateTime bd = Time[0].Date;
            DayOfWeek dow = bd.DayOfWeek;

            // New day
            if (bd != orbDate) { DayReset(); orbDate = bd; }

            // Skip Wednesday
            if (SkipWed && dow == DayOfWeek.Wednesday) return;

            // ORB window
            if (bm >= orbStart && bm < orbEnd)
            {
                if (High[0] > orbHigh) orbHigh = High[0];
                if (Low[0] < orbLow) orbLow = Low[0];
                orbClose = Close[0];
            }

            // ORB established
            if (bm >= orbEnd && !orbSet && orbHigh != double.MinValue)
            {
                orbSet = true;
                orbRange = orbHigh - orbLow;
                orbMid = (orbHigh + orbLow) / 2.0;
                if (orbRange <= 0) { orbSet = false; return; }

                candleBias = orbClose > orbMid ? 1 : orbClose < orbMid ? -1 : 0;

                double riskPerCt = orbRange * 2.0 * PointValue;
                double effectiveRisk = (HalfWed && dow == DayOfWeek.Wednesday) ? RiskPerTrade / 2.0 : RiskPerTrade;
                calcContracts = riskPerCt > 0 ? (int)Math.Floor(effectiveRisk / riskPerCt) : 0;
                calcContracts = Math.Min(calcContracts, MaxContracts);
                if (calcContracts < 1) { orbSet = false; return; }

                if (candleBias == 1)      { tpLevel = orbHigh + orbRange; slLevel = orbLow - orbRange; }
                else if (candleBias == -1) { tpLevel = orbLow - orbRange;  slLevel = orbHigh + orbRange; }

                // Draw ORB box
                Brush fill = Brushes.DodgerBlue.Clone(); fill.Opacity = 0.15; fill.Freeze();
                Draw.Rectangle(this, "Box", false,
                    Time[0].AddMinutes(-(EndM - StartM)), orbHigh, Time[0].AddHours(6), orbLow,
                    Brushes.DodgerBlue, fill, 0);
                if (candleBias != 0)
                {
                    Draw.HorizontalLine(this, "TP", tpLevel, Brushes.Lime, DashStyleHelper.Dash, 2);
                    Draw.HorizontalLine(this, "SL", slLevel, Brushes.Crimson, DashStyleHelper.Dash, 2);
                }

                string sym = PointValue == 5 ? "MES" : PointValue == 2 ? "MNQ" : PointValue == 50 ? "ES" : "NQ";
                string bias = candleBias == 1 ? "LONG ONLY" : candleBias == -1 ? "SHORT ONLY" : "NEUTRAL";
                Print(string.Format("[ORB] {0} {1} | Range:{2:F2} | {3} | {4} cts | TP:{5:F2} | SL:{6:F2}",
                    bd.ToShortDateString(), sym, orbRange, bias, calcContracts, tpLevel, slLevel));
            }

            // === ENTRY ===
            if (orbSet && !enteredToday && !doneToday && bm >= orbEnd && bm < deadline
                && Position.MarketPosition == MarketPosition.Flat)
            {
                if (candleBias == 1 && TradeLongs && Close[0] > orbHigh)
                {
                    EnterLong(calcContracts, "ORB_Long");
                    SetProfitTarget("ORB_Long", CalculationMode.Price, tpLevel);
                    SetStopLoss("ORB_Long", CalculationMode.Price, slLevel, false);
                    enteredToday = true;
                    Draw.HorizontalLine(this, "TP", tpLevel, Brushes.Lime, DashStyleHelper.Solid, 3);
                    Draw.HorizontalLine(this, "SL", slLevel, Brushes.Crimson, DashStyleHelper.Solid, 3);
                    Draw.ArrowUp(this, "Entry", false, 0, Low[0] - TickSize * 10, Brushes.Lime);
                    Print(string.Format("[ENTRY] LONG {0} @ {1:F2}", calcContracts, Close[0]));
                }
                else if (candleBias == -1 && TradeShorts && Close[0] < orbLow)
                {
                    EnterShort(calcContracts, "ORB_Short");
                    SetProfitTarget("ORB_Short", CalculationMode.Price, tpLevel);
                    SetStopLoss("ORB_Short", CalculationMode.Price, slLevel, false);
                    enteredToday = true;
                    Draw.HorizontalLine(this, "TP", tpLevel, Brushes.Lime, DashStyleHelper.Solid, 3);
                    Draw.HorizontalLine(this, "SL", slLevel, Brushes.Crimson, DashStyleHelper.Solid, 3);
                    Draw.ArrowDown(this, "Entry", false, 0, High[0] + TickSize * 10, Brushes.OrangeRed);
                    Print(string.Format("[ENTRY] SHORT {0} @ {1:F2}", calcContracts, Close[0]));
                }
            }

            // === TIME STOP ===
            if (bm >= timeStop && Position.MarketPosition != MarketPosition.Flat)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong("TimeStop", "ORB_Long");
                else
                    ExitShort("TimeStop", "ORB_Short");
                doneToday = true;
                Print("[TIME STOP] Position flattened");
            }

            // Track flat after entry
            if (enteredToday && Position.MarketPosition == MarketPosition.Flat)
                doneToday = true;
        }
    }
}
