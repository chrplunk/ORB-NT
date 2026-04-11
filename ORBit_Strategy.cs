// Christopher Plunkett April 2026

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class ORBit_Strategy : Strategy
    {
        #region Private Variables
        private double orbHigh, orbLow, orbRange;
        private double tpLevel, slLevel;
        private int calcContracts, candleBias;
        private bool orbSet, enteredToday, doneToday;
        private DateTime orbDate;
        private TimeZoneInfo etZone;
        #endregion

        #region Properties

        // ── 1. Risk ──────────────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "Risk Per Trade ($)", Order = 1, GroupName = "1. Risk")]
        [Range(50, 100000)]
        public double RiskPerTrade { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Point Value", Description = "MES=5, ES=50, MNQ=2, NQ=20", Order = 2, GroupName = "1. Risk")]
        [Range(0.5, 500)]
        public double PointValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Max Contracts", Order = 3, GroupName = "1. Risk")]
        [Range(1, 200)]
        public int MaxContracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use % TP (vs 1x Range)", Order = 4, GroupName = "1. Risk")]
        public bool UsePctTP { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TP %", Description = "Take profit as % of price (e.g. 0.27 = 0.27%)", Order = 5, GroupName = "1. Risk")]
        [Range(0.01, 5.0)]
        public double TpPct { get; set; }

        // ── NEW: SL Multiplier ────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "SL Multiplier (x ORB Range)", Description = "Stop loss distance as multiple of ORB range. 1.0 = full range, 0.5 = half range, 1.5 = 1.5x range", Order = 6, GroupName = "1. Risk")]
        [Range(0.1, 5.0)]
        public double SlMultiplier { get; set; }

        // ── 2. Timing ─────────────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "ORB Start Hour (ET)", Order = 1, GroupName = "2. Timing")]
        [Range(0, 23)]
        public int StartH { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ORB Start Minute", Order = 2, GroupName = "2. Timing")]
        [Range(0, 59)]
        public int StartM { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ORB End Hour (ET)", Order = 3, GroupName = "2. Timing")]
        [Range(0, 23)]
        public int EndH { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ORB End Minute", Order = 4, GroupName = "2. Timing")]
        [Range(0, 59)]
        public int EndM { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Deadline Hour (ET)", Order = 5, GroupName = "2. Timing")]
        [Range(0, 23)]
        public int DeadH { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Deadline Minute", Order = 6, GroupName = "2. Timing")]
        [Range(0, 59)]
        public int DeadM { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Time Stop Hour (ET)", Order = 7, GroupName = "2. Timing")]
        [Range(0, 23)]
        public int StopH { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Time Stop Minute", Order = 8, GroupName = "2. Timing")]
        [Range(0, 59)]
        public int StopM { get; set; }

        // ── 3. Entry Filters ──────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "Use Bias Filter", Description = "If true: only take longs when ORB candle closes above midpoint, only take shorts when it closes below. Reduces whipsaw trades.", Order = 1, GroupName = "3. Entry Filters")]
        public bool UseBiasFilter { get; set; }

        // ── 4. Day Filters ────────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "Trade Longs", Order = 1, GroupName = "4. Day Filters")]
        public bool TradeLongs { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade Shorts", Order = 2, GroupName = "4. Day Filters")]
        public bool TradeShorts { get; set; }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description     = "15-Min ORB Strategy — MNQ/MES. Optimizable TP%, SL Multiplier, and Bias Filter.";
                Name            = "ORBit_Strategy";
                Calculate       = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling   = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;

                // Defaults — 15-min ORB, $200 risk for Tradeify $50K eval
                RiskPerTrade    = 200;
                PointValue      = 2;      // MNQ default
                MaxContracts    = 25;
                UsePctTP        = true;
                TpPct           = 0.28;
                SlMultiplier    = 2.0;

                StartH = 9;  StartM = 30;
                EndH   = 9;  EndM   = 45;
                DeadH  = 11; DeadM  = 0;
                StopH  = 15; StopM  = 0;

                UseBiasFilter = true;
                TradeLongs  = true;
                TradeShorts = true;
            }
            else if (State == State.Configure)
            {
                etZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 5) return;

            // Convert bar time to ET
            DateTime etTime = TimeZoneInfo.ConvertTimeFromUtc(Time[0].ToUniversalTime(), etZone);
            DateTime etDate = etTime.Date;

            // ── Reset daily state ──────────────────────────────────
            if (etDate != orbDate)
            {
                orbSet       = false;
                enteredToday = false;
                doneToday    = false;
                orbHigh      = 0;
                orbLow       = double.MaxValue;
                orbDate      = etDate;
            }

            if (doneToday) return;

            // ── Build ORB ──────────────────────────────────────────
            TimeSpan t        = etTime.TimeOfDay;
            TimeSpan orbStart = new TimeSpan(StartH, StartM, 0);
            TimeSpan orbEnd   = new TimeSpan(EndH, EndM, 0);
            TimeSpan deadline = new TimeSpan(DeadH, DeadM, 0);
            TimeSpan timeStop = new TimeSpan(StopH, StopM, 0);

            if (t >= orbStart && t < orbEnd)
            {
                orbHigh = Math.Max(orbHigh, High[0]);
                orbLow  = Math.Min(orbLow,  Low[0]);
                return;
            }

            // ── Mark ORB complete on the first bar after orbEnd ────
            if (!orbSet && t >= orbEnd && orbHigh > 0 && orbLow < double.MaxValue)
            {
                orbRange   = orbHigh - orbLow;
                // Bias: 1 = bullish (close above midpoint), -1 = bearish
                candleBias = Close[0] >= (orbHigh + orbLow) / 2.0 ? 1 : -1;
                orbSet     = true;
            }

            if (!orbSet) return;

            // ── Time stop: flatten and stop trading ────────────────
            if (t >= timeStop)
            {
                if (Position.MarketPosition != MarketPosition.Flat)
                    ExitLong(); ExitShort();
                doneToday = true;
                return;
            }

            // ── Entry deadline ─────────────────────────────────────
            if (t >= deadline || enteredToday) return;

            // ── Calculate contracts ────────────────────────────────
            double slDistance = orbRange * SlMultiplier;
            double riskPerContract = slDistance * PointValue;
            if (riskPerContract <= 0) return;

            double riskAmount = RiskPerTrade;

            calcContracts = (int)Math.Floor(riskAmount / riskPerContract);
            calcContracts = Math.Max(1, Math.Min(calcContracts, MaxContracts));

            // ── Calculate TP ───────────────────────────────────────
            double tpDistance = UsePctTP
                ? Close[0] * (TpPct / 100.0)
                : orbRange;

            // ── Long entry ─────────────────────────────────────────
            bool biasOkLong  = !UseBiasFilter || candleBias == 1;
            bool biasOkShort = !UseBiasFilter || candleBias == -1;

            if (TradeLongs && biasOkLong && Close[0] > orbHigh)
            {
                tpLevel = Close[0] + tpDistance;
                slLevel = Close[0] - slDistance;

                EnterLong(calcContracts, "ORB_Long");
                SetProfitTarget("ORB_Long", CalculationMode.Price, tpLevel);
                SetStopLoss("ORB_Long",    CalculationMode.Price, slLevel, false);

                enteredToday = true;
            }

            // ── Short entry ────────────────────────────────────────
            else if (TradeShorts && biasOkShort && Close[0] < orbLow)
            {
                tpLevel = Close[0] - tpDistance;
                slLevel = Close[0] + slDistance;

                EnterShort(calcContracts, "ORB_Short");
                SetProfitTarget("ORB_Short", CalculationMode.Price, tpLevel);
                SetStopLoss("ORB_Short",    CalculationMode.Price, slLevel, false);

                enteredToday = true;
            }
        }
    }
}