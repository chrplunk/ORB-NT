# ORB-NT

A comprehensive Opening Range Breakout (ORB) trading system for NinjaTrader 8, optimized for Micro E-mini futures (MES/MNQ) and E-mini futures (ES/NQ).

## Overview

This NinjaTrader addon consists of two components:
- **ORBit**: Visual indicator showing the ORB range, bias signals, and automated profit targets/stop losses
- **ORB_Strategy**: Automated trading strategy implementing the ORB breakout methodology

Based on extensive backtesting (6,142 trading days) from TradingStats.net, this system captures opening range breakouts with sophisticated risk management and timing filters.

## Features

### Core Functionality
- **5-minute timeframe ORB calculation**
- **Configurable ORB time window** (start/end times in ET)
- **Dynamic contract sizing** based on risk per trade
- **Automated profit targets and stop losses**
- **Directional bias detection** using candle patterns
- **Time-based trade deadline** to avoid holding positions overnight

### Visual Indicators
- **Shaded ORB box** showing the opening range
- **Directional bias banner and arrow** signals
- **Auto TP/SL lines** with dollar amount labels
- **Dynamic contract count display**
- **Color-coded signals** (customizable)

### Risk Management
- **Fixed dollar risk per trade** ($50-$100,000 range)
- **Point value configuration** for different instruments:
  - MES (Micro E-mini S&P 500): 5 points
  - MNQ (Micro E-mini Nasdaq): 2 points
  - ES (E-mini S&P 500): 50 points
  - NQ (E-mini Nasdaq): 20 points

### Strategy Filters
- **Candle direction confirmation**
- **Day-of-week trading filters**
- **Time-based position exit**
- **1:1 risk-reward ratio** with 2:1 total stop loss

## Installation

1. Copy `ORBit.cs` and `ORB_Strategy.cs` to your NinjaTrader scripts directory
2. Compile the scripts in NinjaTrader
3. Add the indicator to your chart or enable the strategy

## Configuration

### Risk Settings
- **Risk Per Trade**: Maximum dollar amount to risk per trade
- **Point Value**: Contract point value for P&L calculations
- **Max Contracts**: Maximum position size limit

### Timing Settings
- **ORB Start Time**: When the opening range begins (ET)
- **ORB End Time**: When the opening range closes (ET)
- **Trade Deadline**: Latest time to exit positions (ET)

### Display Settings
- **ORB Box Color & Opacity**: Visual styling for the range box
- **TP/SL Colors**: Profit target and stop loss line colors
- **Bias Colors**: Long/short signal colors
- **Alert Settings**: Enable/disable audio/visual alerts

## Supported Instruments

- **MES** (Micro E-mini S&P 500 Futures)
- **MNQ** (Micro E-mini Nasdaq-100 Futures)
- **ES** (E-mini S&P 500 Futures)
- **NQ** (E-mini Nasdaq-100 Futures)

## Backtest Results

Based on TradingStats.net analysis of 6,142 trading days with the following methodology:
- 5-minute ORB on MES/MNQ/ES/NQ
- 1:1 risk-reward with 2:1 stop loss extension
- Dynamic position sizing
- Candle bias and day filters
- Time-based exit rules

## Usage

### Manual Trading
1. Add ORBit to your 5-minute chart
2. Configure instrument and risk settings
3. Wait for ORB range to establish
4. Enter trades on breakout in bias direction
5. Use auto TP/SL levels for exit targets

### Automated Trading
1. Enable ORB_Strategy on your chart
2. Configure all settings as desired
3. Start automated trading during market hours
4. Monitor performance and adjust filters as needed

## Risk Disclaimer

This software is for educational and research purposes. Futures trading involves substantial risk and is not suitable for all investors. Past performance does not guarantee future results. Always test strategies thoroughly and use proper risk management.

## License

MIT License - Copyright (c) 2026 chrplunk

See LICENSE file for full license text.

## Support

For questions or issues, please refer to the NinjaTrader community forums or create an issue in the repository.
