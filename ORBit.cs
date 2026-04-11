// Christopher Plunkett April 2026#region Using declarations

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// 5-Min ORB Indicator — Works on MES, MNQ, ES, NQ (configure Point Value).
    /// Shaded ORB box, directional bias banner + arrow, auto TP/SL lines with dollar labels,
    /// dynamic contract count display. Based on TradingStats.net 6,142-day backtest.
    /// </summary>
    public class ORBit : Indicator
    {
        #region State
        private double orbHigh, orbLow, orbClose, orbMid, orbRange;
        private double tpLevel, slLevel;
        private int contracts, candleBias, breakDir;
        private bool orbSet, boTriggered, tpHit, slHit;
        private DateTime orbDate;
        private int orbStartBar, orbEndBar;
        #endregion

        #region Properties — Risk
        [NinjaScriptProperty]
        [Display(Name = "Risk Per Trade ($)", Description = "Max dollar risk per trade", Order = 1, GroupName = "1. Risk")]
        [Range(50, 100000)]
        public double RiskPerTrade { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Point Value", Description = "MES=5, MNQ=2, ES=50, NQ=20", Order = 2, GroupName = "1. Risk")]
        [Range(0.5, 500)]
        public double PointValue { get; set; }
        #endregion

        #region Properties — Timing
        [NinjaScriptProperty]
        [Display(Name = "ORB Start Hour (ET)", Order = 1, GroupName = "2. Timing")][Range(0,23)]
        public int StartH { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "ORB Start Minute", Order = 2, GroupName = "2. Timing")][Range(0,59)]
        public int StartM { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "ORB End Hour (ET)", Order = 3, GroupName = "2. Timing")][Range(0,23)]
        public int EndH { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "ORB End Minute", Order = 4, GroupName = "2. Timing")][Range(0,59)]
        public int EndM { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Deadline Hour (ET)", Order = 5, GroupName = "2. Timing")][Range(0,23)]
        public int DeadH { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Deadline Minute", Order = 6, GroupName = "2. Timing")][Range(0,59)]
        public int DeadM { get; set; }
        #endregion

        #region Properties — Display

        [XmlIgnore]
        [Display(Name = "ORB Box Color", Order = 1, GroupName = "3. Colors")]
        public Brush BoxColor { get; set; }
        [Browsable(false)]
        public string BoxColorSerializable
        {
            get { return Serialize.BrushToString(BoxColor); }
            set { BoxColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "ORB Box Opacity %", Order = 2, GroupName = "3. Colors")][Range(5,80)]
        public int BoxOpacity { get; set; }

        [XmlIgnore]
        [Display(Name = "TP Color", Order = 3, GroupName = "3. Colors")]
        public Brush TpColor { get; set; }
        [Browsable(false)]
        public string TpColorSerializable
        {
            get { return Serialize.BrushToString(TpColor); }
            set { TpColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "SL Color", Order = 4, GroupName = "3. Colors")]
        public Brush SlColor { get; set; }
        [Browsable(false)]
        public string SlColorSerializable
        {
            get { return Serialize.BrushToString(SlColor); }
            set { SlColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Long Bias Color", Order = 5, GroupName = "3. Colors")]
        public Brush LongColor { get; set; }
        [Browsable(false)]
        public string LongColorSerializable
        {
            get { return Serialize.BrushToString(LongColor); }
            set { LongColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Short Bias Color", Order = 6, GroupName = "3. Colors")]
        public Brush ShortColor { get; set; }
        [Browsable(false)]
        public string ShortColorSerializable
        {
            get { return Serialize.BrushToString(ShortColor); }
            set { ShortColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Show Alerts", Order = 1, GroupName = "4. Alerts")]
        public bool Alerts { get; set; }
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "5-Min ORB — MES/MNQ/ES/NQ configurable. Shaded box, bias arrow, auto TP/SL/contracts.";
                Name = "ORBit";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DrawOnPricePanel = true;
                IsSuspendedWhileInactive = true;
                PaintPriceMarkers = false;

                RiskPerTrade = 500;
                PointValue = 5; // MES default — change to 2 for MNQ, 50 for ES, 20 for NQ
                StartH = 9; StartM = 30;
                EndH = 9; EndM = 35;
                DeadH = 11; DeadM = 30;

                BoxColor = Brushes.DodgerBlue;
                BoxOpacity = 20;
                TpColor = Brushes.Lime;
                SlColor = Brushes.Crimson;
                LongColor = Brushes.Lime;
                ShortColor = Brushes.OrangeRed;
                Alerts = true;
            }
            else if (State == State.Configure) { DayReset(); }
        }

        private void DayReset()
        {
            orbHigh = double.MinValue; orbLow = double.MaxValue;
            orbClose = orbMid = orbRange = tpLevel = slLevel = 0;
            orbSet = boTriggered = tpHit = slHit = false;
            breakDir = candleBias = contracts = 0;
            orbStartBar = orbEndBar = -1;
        }

        private int BarMins() { return Time[0].Hour * 60 + Time[0].Minute; }
        private int ToMins(int h, int m) { return h * 60 + m; }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 5) return;

            int bm = BarMins();
            int orbStart = ToMins(StartH, StartM);
            int orbEnd = ToMins(EndH, EndM);
            int deadline = ToMins(DeadH, DeadM);
            DateTime bd = Time[0].Date;

            // === NEW DAY ===
            if (bd != orbDate) { DayReset(); orbDate = bd; }

            // === ORB WINDOW ===
            // NinjaTrader bar times are close times, so the bar labeled 9:30 closes at 9:30.
            // Use the window from the first bar after the start time through the bar at the end time.
            if (bm > orbStart && bm <= orbEnd)
            {
                if (orbStartBar < 0) orbStartBar = CurrentBar;
                if (High[0] > orbHigh) orbHigh = High[0];
                if (Low[0] < orbLow) orbLow = Low[0];
                orbClose = Close[0];
            }

            // === ORB ESTABLISHED ===
            if (bm >= orbEnd && !orbSet && orbHigh != double.MinValue)
            {
                orbSet = true;
                orbRange = orbHigh - orbLow;
                orbMid = (orbHigh + orbLow) / 2.0;
                orbEndBar = CurrentBar;
                if (orbStartBar < 0) orbStartBar = CurrentBar;

                if (orbRange <= 0) { orbSet = false; return; }

                // Candle direction bias
                candleBias = orbClose > orbMid ? 1 : orbClose < orbMid ? -1 : 0;

                // Position sizing: stop = 2.0x range
                double riskPerCt = orbRange * 2.0 * PointValue;
                contracts = riskPerCt > 0 ? (int)Math.Floor(RiskPerTrade / riskPerCt) : 0;
                if (contracts < 1) { contracts = 0; }

                // Levels
                if (candleBias == 1)      { tpLevel = orbHigh + orbRange; slLevel = orbLow - orbRange; }
                else if (candleBias == -1) { tpLevel = orbLow - orbRange;  slLevel = orbHigh + orbRange; }

                // --- DRAW SHADED ORB BOX ---
                // areaOpacity (last param) is 0-100 and controls fill transparency
                Draw.Rectangle(this, "ORBBox" + bd.ToString("yyyyMMdd"), false,
                    Time[0].AddMinutes(-(EndM - StartM)), orbHigh,
                    Time[0].AddHours(6), orbLow,
                    BoxColor, BoxColor, BoxOpacity);

                // ORB boundary lines (solid through the box, extend right)
                Draw.Line(this, "ORBHi" + bd.ToString("yyyyMMdd"), false,
                    Time[0].AddMinutes(-(EndM - StartM)), orbHigh,
                    Time[0].AddHours(6), orbHigh,
                    BoxColor, DashStyleHelper.Solid, 2);
                Draw.Line(this, "ORBLo" + bd.ToString("yyyyMMdd"), false,
                    Time[0].AddMinutes(-(EndM - StartM)), orbLow,
                    Time[0].AddHours(6), orbLow,
                    BoxColor, DashStyleHelper.Solid, 2);

                // Midpoint
                Draw.Line(this, "ORBMid" + bd.ToString("yyyyMMdd"), false,
                    Time[0], orbMid, Time[0].AddHours(6), orbMid,
                    Brushes.Gray, DashStyleHelper.Dash, 1);

                // --- BIAS ARROW + BANNER ---
                Brush biasColor;
                string biasText;
                if (candleBias == 1)
                {
                    biasColor = LongColor;
                    biasText = "LONG ONLY";
                    Draw.ArrowUp(this, "BiasArrow" + bd.ToString("yyyyMMdd"), false, 0,
                        orbLow - orbRange * 0.6, biasColor);
                    Draw.Text(this, "BiasTag" + bd.ToString("yyyyMMdd"),
                        "▲ " + biasText, 0, orbLow - orbRange * 0.3,
                        biasColor);
                }
                else if (candleBias == -1)
                {
                    biasColor = ShortColor;
                    biasText = "SHORT ONLY";
                    Draw.ArrowDown(this, "BiasArrow" + bd.ToString("yyyyMMdd"), false, 0,
                        orbHigh + orbRange * 0.6, biasColor);
                    Draw.Text(this, "BiasTag" + bd.ToString("yyyyMMdd"),
                        "▼ " + biasText, 0, orbHigh + orbRange * 0.3,
                        biasColor);
                }
                else
                {
                    biasColor = Brushes.Gray;
                    biasText = "NO TRADE — NEUTRAL";
                    Draw.Text(this, "BiasTag" + bd.ToString("yyyyMMdd"),
                        "— " + biasText, 0, orbHigh + orbRange * 0.3,
                        biasColor);
                }

                // --- TP / SL LINES (dashed until confirmed) ---
                if (candleBias != 0 && contracts > 0)
                {
                    double winDollars = contracts * orbRange * PointValue;
                    double lossDollars = contracts * orbRange * 2.0 * PointValue;

                    Draw.HorizontalLine(this, "TP" + bd.ToString("yyyyMMdd"),
                        tpLevel, TpColor, DashStyleHelper.Dash, 2);
                    Draw.HorizontalLine(this, "SL" + bd.ToString("yyyyMMdd"),
                        slLevel, SlColor, DashStyleHelper.Dash, 2);

                    double labelOffset = TickSize * 2.0;
                    double tpLabelY = tpLevel - labelOffset;
                    double slLabelY = slLevel - labelOffset;

                    // TP label
                    Draw.Text(this, "TPLbl" + bd.ToString("yyyyMMdd"),
                        string.Format("── TP {0:F2}  (+${1:F0})", tpLevel, winDollars),
                        -5, tpLabelY, TpColor);
                    // SL label
                    Draw.Text(this, "SLLbl" + bd.ToString("yyyyMMdd"),
                        string.Format("── SL {0:F2}  (-${1:F0})", slLevel, lossDollars),
                        -5, slLabelY, SlColor);
                }

                // --- TOP-LEFT INFO PANEL ---
                string sym = PointValue == 5 ? "MES" : PointValue == 2 ? "MNQ" : PointValue == 50 ? "ES" : PointValue == 20 ? "NQ" : "?";
                string info = string.Format(
                    "{0}  |  {1}  |  Range: {2:F2}  |  {3} contracts  |  Risk: ${4:F0}\nTP: {5:F2}  |  SL: {6:F2}  |  Win: +${7:F0}  |  Loss: -${8:F0}  |  R:R = 1:2",
                    sym, biasText, orbRange, contracts, contracts * orbRange * 2.0 * PointValue,
                    tpLevel, slLevel,
                    contracts * orbRange * PointValue,
                    contracts * orbRange * 2.0 * PointValue);

                Draw.TextFixed(this, "InfoPanel", info, TextPosition.TopLeft,
                    biasColor, new SimpleFont("Consolas", 12),
                    Brushes.Transparent, Brushes.Transparent, 0);

                // Alert
                if (Alerts)
                    Alert("ORBSet", Priority.Medium,
                        string.Format("ORB SET — {0} | Range: {1:F2} | {2} cts | {3}",
                            sym, orbRange, contracts, biasText),
                        NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert1.wav",
                        10, biasColor, Brushes.White);
            }

            // === BREAKOUT DETECTION ===
            if (orbSet && !boTriggered && bm >= ToMins(EndH, EndM) && bm < deadline && candleBias != 0 && contracts > 0)
            {
                bool longBO = candleBias == 1 && Close[0] > orbHigh;
                bool shortBO = candleBias == -1 && Close[0] < orbLow;

                if (longBO || shortBO)
                {
                    boTriggered = true;
                    breakDir = longBO ? 1 : -1;
                    Brush bColor = longBO ? LongColor : ShortColor;
                    string dir = longBO ? "LONG" : "SHORT";
                    double winD = contracts * orbRange * PointValue;
                    double lossD = contracts * orbRange * 2.0 * PointValue;

                    // Solid TP/SL
                    Draw.HorizontalLine(this, "TP" + orbDate.ToString("yyyyMMdd"),
                        tpLevel, TpColor, DashStyleHelper.Solid, 3);
                    Draw.HorizontalLine(this, "SL" + orbDate.ToString("yyyyMMdd"),
                        slLevel, SlColor, DashStyleHelper.Solid, 3);

                    // Entry line
                    Draw.HorizontalLine(this, "Entry" + orbDate.ToString("yyyyMMdd"),
                        Close[0], Brushes.White, DashStyleHelper.Dot, 1);

                    // Entry arrow
                    if (longBO)
                        Draw.ArrowUp(this, "EntryArrow" + orbDate.ToString("yyyyMMdd"),
                            false, 0, Low[0] - TickSize * 10, LongColor);
                    else
                        Draw.ArrowDown(this, "EntryArrow" + orbDate.ToString("yyyyMMdd"),
                            false, 0, High[0] + TickSize * 10, ShortColor);

                    // Bottom panel
                    string sym = PointValue == 5 ? "MES" : PointValue == 2 ? "MNQ" : PointValue == 50 ? "ES" : PointValue == 20 ? "NQ" : "?";
                    Draw.TextFixed(this, "EntryPanel",
                        string.Format("▶ {0} {1} {2} cts @ {3:F2}  |  TP: {4:F2} (+${5:F0})  |  SL: {6:F2} (-${7:F0})",
                            dir, sym, contracts, Close[0], tpLevel, winD, slLevel, lossD),
                        TextPosition.BottomLeft, bColor,
                        new SimpleFont("Consolas", 13), Brushes.Transparent, Brushes.Transparent, 0);

                    if (Alerts)
                        Alert("Breakout", Priority.High,
                            string.Format("{0} BREAKOUT — {1} cts @ {2:F2} | TP: {3:F2} | SL: {4:F2}",
                                dir, contracts, Close[0], tpLevel, slLevel),
                            NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert2.wav",
                            10, bColor, Brushes.Black);
                }
            }

            // === TRACK RESULT ===
            if (boTriggered && !tpHit && !slHit)
            {
                double winD = contracts * orbRange * PointValue;
                double lossD = contracts * orbRange * 2.0 * PointValue;
                bool tpNow = breakDir == 1 ? High[0] >= tpLevel : Low[0] <= tpLevel;
                bool slNow = breakDir == 1 ? Low[0] <= slLevel : High[0] >= slLevel;

                if (tpNow)
                {
                    tpHit = true;
                    Draw.Diamond(this, "TPHit" + orbDate.ToString("yyyyMMdd"), false, 0, tpLevel, TpColor);
                    Draw.TextFixed(this, "Result",
                        string.Format("✓ TP HIT — +${0:F0}", winD),
                        TextPosition.BottomRight, TpColor,
                        new SimpleFont("Consolas", 14), Brushes.Transparent, Brushes.Transparent, 0);
                }
                else if (slNow)
                {
                    slHit = true;
                    Draw.Diamond(this, "SLHit" + orbDate.ToString("yyyyMMdd"), false, 0, slLevel, SlColor);
                    Draw.TextFixed(this, "Result",
                        string.Format("✗ SL HIT — -${0:F0}", lossD),
                        TextPosition.BottomRight, SlColor,
                        new SimpleFont("Consolas", 14), Brushes.Transparent, Brushes.Transparent, 0);
                }
            }

            // === PAST DEADLINE ===
            if (orbSet && !boTriggered && bm >= deadline)
            {
                Draw.TextFixed(this, "EntryPanel",
                    "NO TRADE — Past deadline",
                    TextPosition.BottomLeft, Brushes.Gray,
                    new SimpleFont("Consolas", 12), Brushes.Transparent, Brushes.Transparent, 0);
            }
        }
    }
}
