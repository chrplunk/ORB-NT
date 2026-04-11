# ORB-NT

An Opening Range Breakout (ORB) trading system for NinjaTrader 8, built for Micro E-mini and E-mini futures. Runs on 5-minute bars with a 15-minute ORB window, FirstCandle directional bias, percentage-based take profit, and automatic position sizing.

## Components

| File | Type | Purpose |
|------|------|---------|
| `ORBit.cs` | Indicator | Chart overlay — shaded ORB box, bias arrow, TP/SL lines with dollar labels, contract count panel |
| `ORBit_Strategy.cs` | Strategy | Automated execution — entries, exits, position sizing, time stop, bias filtering |
| `orb-cheatsheet.html` | Reference | Visual cheat sheet with backtest stats, step-by-step logic, and settings |

## How It Works

### Timeline (all times Eastern)

| Time | Event |
|------|-------|
| **9:30 - 9:35** | First 5-min candle tracked. Its close vs its own midpoint sets the **directional bias** for the day. |
| **9:35** | Bias locked. Above midpoint = bullish (longs only). Below = bearish (shorts only). |
| **9:30 - 9:45** | Full 15-minute ORB range builds. `orbHigh` and `orbLow` are tracked across all bars. |
| **9:45** | ORB locks. Range, TP, SL, and contract size are calculated. Watching for breakout. |
| **After 9:45** | First 5-min bar that **closes** beyond ORB high (long) or ORB low (short) matching bias direction triggers entry at market on bar close. |
| **11:00** | Entry deadline. No signal by now = flat for the day. |
| **3:00 PM** | Time stop. All positions flattened at market. |

### Bias Filter — FirstCandle Mode

The first 5-minute candle (9:30-9:35) determines the day's directional bias:

- **Bullish**: Close >= midpoint of the first candle's range. Only long breakouts are valid.
- **Bearish**: Close < midpoint of the first candle's range. Only short breakouts are valid.
- **Neutral** (indicator only): Close equals midpoint exactly. No trade.

An alternative **ORBClose** mode is available in the strategy, which sets bias from the first bar after the ORB ends vs the ORB midpoint.

### Position Sizing

```
stopDistance = orbRange x SlMultiplier (default 2.0)
riskPerContract = stopDistance x PointValue
contracts = floor(RiskPerTrade / riskPerContract)
contracts = clamp(1, MaxContracts)
```

### Take Profit / Stop Loss

- **TP**: 0.28% of entry price (percentage-based, configurable). At MES ~5,000 this is roughly 14 points.
- **SL**: ORB range x 2.0 from entry (configurable via `SlMultiplier`).
- The strategy can alternatively use 1x ORB range as TP when `UsePctTP` is set to false.

### One Trade Per Day

The `enteredToday` flag ensures only one entry per session. Once a trade is placed, all subsequent signals are ignored.

## Backtest Results (MES, 5-min bars, $500 risk)

| Metric | Value |
|--------|-------|
| Net Profit | $49,850 |
| Profit Factor | 1.72 |
| Win Rate | 69.08% (249 trades) |
| Profit / Month | $3,059 |
| Max Consecutive Losers | 3 |
| Max Drawdown | $4,402 |

> **Tradeify eval warning**: $500 risk produced $4,402 max drawdown, exceeding the $2,000 trailing drawdown limit. Use $200 risk for eval accounts (projected ~$1,760 max DD). Run a fresh backtest at $200 to confirm.

## Default Settings

| Setting | Default |
|---------|---------|
| Risk Per Trade | $500 (use $200 for eval) |
| Point Value | MES = 5, MNQ = 2, ES = 50, NQ = 20 |
| Max Contracts | 25 |
| TP % | 0.28 |
| SL Multiplier | 2.0x |
| ORB Window | 9:30 - 9:45 ET |
| Entry Deadline | 11:00 ET |
| Time Stop | 15:00 ET |
| Bias Filter | On (FirstCandle mode) |
| Trade Longs + Shorts | Both enabled |
| Chart Type | Minute, Value = 5 |

## Installation

1. Copy `ORBit.cs` and `ORBit_Strategy.cs` to your NinjaTrader 8 custom scripts directory:
   - `Documents/NinjaTrader 8/bin/Custom/Indicators/` (indicator)
   - `Documents/NinjaTrader 8/bin/Custom/Strategies/` (strategy)
2. Open NinjaTrader's NinjaScript Editor and compile.
3. Add the **ORBit** indicator to a 5-minute chart, or enable **ORBit_Strategy** for automated trading.

## Indicator Features (ORBit.cs)

- Shaded ORB box with high/low boundary lines and midpoint
- Bias arrow and "LONG ONLY" / "SHORT ONLY" banner
- Dashed TP/SL lines on ORB lock, solid lines on breakout confirmation
- Entry arrow and dotted entry line on breakout
- Top-left info panel: instrument, bias, range, contracts, risk, TP/SL, R:R
- Bottom-left entry panel on breakout with full trade details
- Bottom-right result panel (TP HIT / SL HIT) with dollar P&L
- Configurable colors, opacity, and audio alerts

## Supported Instruments

- **MES** — Micro E-mini S&P 500 (Point Value = 5)
- **MNQ** — Micro E-mini Nasdaq-100 (Point Value = 2)
- **ES** — E-mini S&P 500 (Point Value = 50)
- **NQ** — E-mini Nasdaq-100 (Point Value = 20)

## Risk Disclaimer

This software is for educational and research purposes. Futures trading involves substantial risk and is not suitable for all investors. Past performance does not guarantee future results. Always test strategies thoroughly and use proper risk management.

## License

MIT License - Copyright (c) 2026 chrplunk

See LICENSE file for full license text.
