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
        private double firstCandleHigh, firstCandleLow;
        private double tpLevel, slLevel;
        private int calcContracts, candleBias;
        private bool orbSet, enteredToday, doneToday, firstCandleCaptured;
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
        [Display(Name = "TP %", Description = "Take profit as % of price (e.g. 0.28 = 0.28%)", Order = 5, GroupName = "1. Risk")]
        [Range(0.01, 5.0)]
        public double TpPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL Multiplier (x ORB Range)", Description = "Stop loss distance as multiple of ORB range. 2.0 = default.", Order = 6, GroupName = "1. Risk")]
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
        [Display(Name = "Use Bias Filter", Description = "Enable bias filtering. Direction set by Bias Mode below.", Order = 1, GroupName = "3. Entry Filters")]
        public bool UseBiasFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Bias Mode",
                 Description = "FirstCandle: bias from 9:30 5-min candle close vs its own high/low midpoint. ORBClose: bias from first bar after ORB ends vs ORB midpoint.",
                 Order = 2, GroupName = "3. Entry Filters")]
        public BiasModeType BiasMode { get; set; }

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
                Description         = "15-Min ORB Strategy — MNQ/MES. Bias Mode: FirstCandle (9:30 5-min) or ORBClose (post-9:45).";
                Name                = "ORBit_Strategy";
                Calculate           = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling       = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;

                RiskPerTrade  = 200;
                PointValue    = 2;        // MNQ default
                MaxContracts  = 25;
                UsePctTP      = true;
                TpPct         = 0.28;
                SlMultiplier  = 2.0;

                StartH = 9;  StartM = 30;
                EndH   = 9;  EndM   = 45;
                DeadH  = 11; DeadM  = 0;
                StopH  = 15; StopM  = 0;

                UseBiasFilter = true;
                BiasMode      = BiasModeType.FirstCandle;
                TradeLongs    = true;
                TradeShorts   = true;
            }
            else if (State == State.Configure)
            {
                etZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 5) return;

            DateTime etTime = TimeZoneInfo.ConvertTimeFromUtc(Time[0].ToUniversalTime(), etZone);
            DateTime etDate = etTime.Date;

            // ── Reset daily state ──────────────────────────────────
            if (etDate != orbDate)
            {
                orbSet              = false;
                enteredToday        = false;
                doneToday           = false;
                firstCandleCaptured = false;
                orbHigh             = 0;
                orbLow              = double.MaxValue;
                firstCandleHigh     = 0;
                firstCandleLow      = double.MaxValue;
                candleBias          = 0;
                orbDate             = etDate;
            }

            if (doneToday) return;

            TimeSpan t        = etTime.TimeOfDay;
            TimeSpan orbStart = new TimeSpan(StartH, StartM, 0);
            TimeSpan firstEnd = orbStart.Add(TimeSpan.FromMinutes(5)); // 9:35 ET
            TimeSpan orbEnd   = new TimeSpan(EndH, EndM, 0);           // 9:45 ET
            TimeSpan deadline = new TimeSpan(DeadH, DeadM, 0);
            TimeSpan timeStop = new TimeSpan(StopH, StopM, 0);

            // ── Track first 5-min candle high/low (9:30–9:35) ─────
            if (t >= orbStart && t < firstEnd)
            {
                firstCandleHigh = Math.Max(firstCandleHigh, High[0]);
                firstCandleLow  = Math.Min(firstCandleLow,  Low[0]);
            }

            // ── Lock FirstCandle bias at 9:35 close ───────────────
            // The 9:35 bar close is the close of the first 5-min candle.
            // Compare it to the midpoint of that candle's own high/low range.
            if (!firstCandleCaptured && t >= firstEnd
                && firstCandleHigh > 0 && firstCandleLow < double.MaxValue)
            {
                if (UseBiasFilter && BiasMode == BiasModeType.FirstCandle)
                {
                    double firstMid = (firstCandleHigh + firstCandleLow) / 2.0;
                    candleBias = Close[0] >= firstMid ? 1 : -1;
                }
                firstCandleCaptured = true;
            }

            // ── Build full 15-min ORB (9:30–9:45) ─────────────────
            if (t >= orbStart && t < orbEnd)
            {
                orbHigh = Math.Max(orbHigh, High[0]);
                orbLow  = Math.Min(orbLow,  Low[0]);
                return;
            }

            // ── Lock ORB at first bar after 9:45 ──────────────────
            if (!orbSet && t >= orbEnd && orbHigh > 0 && orbLow < double.MaxValue)
            {
                orbRange = orbHigh - orbLow;

                // ORBClose mode: bias from first post-ORB bar vs ORB midpoint
                if (UseBiasFilter && BiasMode == BiasModeType.ORBClose)
                {
                    double orbMid  = (orbHigh + orbLow) / 2.0;
                    candleBias     = Close[0] >= orbMid ? 1 : -1;
                }

                orbSet = true;
            }

            if (!orbSet) return;

            // ── Time stop: flatten and quit for the day ────────────
            if (t >= timeStop)
            {
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    ExitLong();
                    ExitShort();
                }
                doneToday = true;
                return;
            }

            // ── No entries past deadline or if already traded ──────
            if (t >= deadline || enteredToday) return;

            // ── Position sizing ────────────────────────────────────
            double slDistance      = orbRange * SlMultiplier;
            double riskPerContract = slDistance * PointValue;
            if (riskPerContract <= 0) return;

            calcContracts = (int)Math.Floor(RiskPerTrade / riskPerContract);
            calcContracts = Math.Max(1, Math.Min(calcContracts, MaxContracts));

            // ── TP distance ────────────────────────────────────────
            double tpDistance = UsePctTP
                ? Close[0] * (TpPct / 100.0)
                : orbRange;

            // ── Bias gates ─────────────────────────────────────────
            bool biasOkLong  = !UseBiasFilter || candleBias == 1;
            bool biasOkShort = !UseBiasFilter || candleBias == -1;

            // ── Long entry ─────────────────────────────────────────
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

    public enum BiasModeType
    {
        [Description("First 5-min candle (9:30 close) vs its own high/low midpoint")]
        FirstCandle,

        [Description("First bar after ORB ends vs ORB high/low midpoint (original)")]
        ORBClose
    }
}