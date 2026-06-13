// ============================================================================
//  NAS100 / US100  Opening Range Breakout cBot
//  Timeframe : M5    |   Instrument : NAS100/US100   |   Direction : Both
//  Platform  : cTrader / cAlgo (.NET)
// ============================================================================
//
//  Strategy Logic
//  ─────────────────────────────────────────────────────────────────────────
//  1. OPENING RANGE (default: first 15 min after NY open = 14:30–14:45 UTC):
//       Record the High and Low of M5 bars opening within that window.
//
//  2. HTF TREND FILTER:
//       H1 EMA50  — H1 close > EMA50 = bullish, < EMA50 = bearish
//       D1 EMA200 — D1 close > EMA200 = bullish, < EMA200 = bearish
//       Both must agree; long only when both bullish, short only when both bearish.
//
//  3. BREAKOUT ENTRY (after ORB is locked):
//       Long:  M5 bar closes above ORB High + MinBreakoutPts AND trend bullish
//       Short: M5 bar closes below ORB Low  − MinBreakoutPts AND trend bearish
//       ATR Ratio filter: 0.5×avg ≤ ATR ≤ 2.5×avg (filters low-vol & news spikes)
//
//  4. STOP MANAGEMENT:
//       SL Long  = entry  − (entry − ORB Low)  − ATR × SlAtrBuffer
//       SL Short = entry  + (ORB High − entry) + ATR × SlAtrBuffer
//       TP = entry ± SL_dist × RrRatio   (default 2:1)
//       BE = when profit ≥ BeTriggerXSL × SL_dist → move SL to entry ± 1 pip
//       Trail (after BE) = trail at ATR × TrailAtrMult from current price
//
//  5. POSITION SIZING:
//       Risk RiskPct% of equity per trade; capped at MaxPositionUnits
//
//  6. FTMO / PROP FIRM COMPLIANCE:
//       Max MaxTradesPerDay trades per day
//       No entry if daily loss ≥ DailyLossLimitPct%
//       No entry if drawdown from peak ≥ MaxDrawdownPct%
//       No new entries after LastEntryHour UTC; weekdays only
// ============================================================================

using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class NAS100_ORB_TrendFollow : Robot
    {
        // ────────────────────────────────────────────────────────────────────
        //  Parameters
        // ────────────────────────────────────────────────────────────────────

        #region Opening Range Parameters

        [Parameter("NY Open Hour (UTC)", Group = "Opening Range", DefaultValue = 14, MinValue = 0, MaxValue = 23)]
        public int NyOpenHour { get; set; }

        [Parameter("NY Open Minute (UTC)", Group = "Opening Range", DefaultValue = 30, MinValue = 0, MaxValue = 59)]
        public int NyOpenMinute { get; set; }

        [Parameter("ORB Duration (min)", Group = "Opening Range", DefaultValue = 15, MinValue = 5, MaxValue = 120)]
        public int OrbDurationMinutes { get; set; }

        [Parameter("Min Breakout Points", Group = "Opening Range", DefaultValue = 10.0, MinValue = 0.0)]
        public double MinBreakoutPts { get; set; }

        #endregion

        #region Risk / Reward Parameters

        [Parameter("Risk Per Trade %", Group = "Risk/Reward", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 5.0)]
        public double RiskPct { get; set; }

        [Parameter("Risk:Reward Ratio", Group = "Risk/Reward", DefaultValue = 2.0, MinValue = 0.5, MaxValue = 10.0)]
        public double RrRatio { get; set; }

        [Parameter("SL ATR Buffer (×ATR)", Group = "Risk/Reward", DefaultValue = 0.5, MinValue = 0.0, MaxValue = 5.0)]
        public double SlAtrBuffer { get; set; }

        [Parameter("Max Position Size (units)", Group = "Risk/Reward", DefaultValue = 100.0, MinValue = 1.0)]
        public double MaxPositionUnits { get; set; }

        #endregion

        #region ATR Filter Parameters

        [Parameter("ATR Period", Group = "ATR Filter", DefaultValue = 14, MinValue = 5, MaxValue = 50)]
        public int AtrPeriod { get; set; }

        [Parameter("ATR Average Period", Group = "ATR Filter", DefaultValue = 50, MinValue = 10, MaxValue = 200)]
        public int AtrAvgPeriod { get; set; }

        [Parameter("ATR Min Ratio", Group = "ATR Filter", DefaultValue = 0.5, MinValue = 0.1, MaxValue = 2.0)]
        public double AtrMinRatio { get; set; }

        [Parameter("ATR Max Ratio", Group = "ATR Filter", DefaultValue = 2.5, MinValue = 1.0, MaxValue = 10.0)]
        public double AtrMaxRatio { get; set; }

        #endregion

        #region HTF Filter Parameters

        [Parameter("H1 EMA Period", Group = "HTF Filter", DefaultValue = 50, MinValue = 5, MaxValue = 200)]
        public int H1EmaPeriod { get; set; }

        [Parameter("D1 EMA Period", Group = "HTF Filter", DefaultValue = 200, MinValue = 10, MaxValue = 500)]
        public int D1EmaPeriod { get; set; }

        #endregion

        #region Breakeven / Trail Parameters

        [Parameter("BE Trigger (×SL dist)", Group = "Breakeven/Trail", DefaultValue = 1.0, MinValue = 0.3, MaxValue = 5.0)]
        public double BeTriggerXSL { get; set; }

        [Parameter("Trail ATR Multiplier", Group = "Breakeven/Trail", DefaultValue = 1.5, MinValue = 0.5, MaxValue = 5.0)]
        public double TrailAtrMult { get; set; }

        #endregion

        #region FTMO Compliance Parameters

        [Parameter("Max Trades Per Day", Group = "FTMO", DefaultValue = 3, MinValue = 1, MaxValue = 10)]
        public int MaxTradesPerDay { get; set; }

        [Parameter("Daily Loss Limit %", Group = "FTMO", DefaultValue = 4.5, MinValue = 0.5, MaxValue = 20.0)]
        public double DailyLossLimitPct { get; set; }

        [Parameter("Max Drawdown %", Group = "FTMO", DefaultValue = 9.0, MinValue = 1.0, MaxValue = 50.0)]
        public double MaxDrawdownPct { get; set; }

        [Parameter("Last Entry UTC Hour", Group = "FTMO", DefaultValue = 18, MinValue = 14, MaxValue = 22)]
        public int LastEntryHour { get; set; }

        #endregion

        // ────────────────────────────────────────────────────────────────────
        //  Private state
        // ────────────────────────────────────────────────────────────────────

        private Bars _h1, _d1;
        private ExponentialMovingAverage _h1Ema, _d1Ema;
        private AverageTrueRange _atr;
        private SimpleMovingAverage _atrSma;

        // ORB state (reset daily)
        private double   _orbHigh;
        private double   _orbLow;
        private bool     _orbFormed;
        private DateTime _orbDate;

        // Breakeven tracking: positionId → has BE been triggered?
        private readonly Dictionary<long, bool> _beTriggered = new Dictionary<long, bool>();

        // FTMO daily tracking
        private double   _peakBalance;
        private double   _dayStartBalance;
        private int      _tradesToday;
        private DateTime _currentDay;

        private const string LABEL = "NAS_ORB";

        // ────────────────────────────────────────────────────────────────────
        //  Lifecycle
        // ────────────────────────────────────────────────────────────────────

        protected override void OnStart()
        {
            _h1 = MarketData.GetBars(TimeFrame.Hour);
            _d1 = MarketData.GetBars(TimeFrame.Daily);

            _h1Ema  = Indicators.ExponentialMovingAverage(_h1.ClosePrices, H1EmaPeriod);
            _d1Ema  = Indicators.ExponentialMovingAverage(_d1.ClosePrices, D1EmaPeriod);
            _atr    = Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.True);
            _atrSma = Indicators.SimpleMovingAverage(_atr.Result, AtrAvgPeriod);

            _peakBalance     = Account.Balance;
            _dayStartBalance = Account.Balance;
            _tradesToday     = 0;
            _currentDay      = Server.Time.Date;
            _orbFormed       = false;
            _orbDate         = DateTime.MinValue.Date;

            Print($"[START] NAS100 ORB cBot | Risk={RiskPct}% RR={RrRatio} BE@{BeTriggerXSL}×SL Trail={TrailAtrMult}×ATR");
        }

        // ────────────────────────────────────────────────────────────────────
        //  OnBar — called when a new M5 bar opens (Last(1) = just-closed bar)
        // ────────────────────────────────────────────────────────────────────

        protected override void OnBar()
        {
            var now   = Server.Time;
            var today = now.Date;

            // ── Daily reset ───────────────────────────────────────────────
            if (today > _currentDay)
            {
                _currentDay      = today;
                _tradesToday     = 0;
                _dayStartBalance = Account.Balance;
                Print($"[DAY] {today:yyyy-MM-dd}  Balance={_dayStartBalance:F2}");
            }

            if (Account.Balance > _peakBalance)
                _peakBalance = Account.Balance;

            // ── Skip weekends ─────────────────────────────────────────────
            if (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday)
                return;

            // ── Build ORB for today if not yet formed ─────────────────────
            TryBuildOrb(now);

            // ── No entries after last entry hour ──────────────────────────
            if (now.Hour >= LastEntryHour) return;

            // ── Skip if a position is already open ────────────────────────
            if (Positions.FindAll(LABEL, SymbolName).Length > 0) return;

            // ── FTMO guards ───────────────────────────────────────────────
            if (!PassesFtmoGuards()) return;

            // ── Need formed ORB ───────────────────────────────────────────
            if (!_orbFormed) return;

            // ── Try breakout entry ────────────────────────────────────────
            TryEntry();
        }

        // ────────────────────────────────────────────────────────────────────
        //  OnTick — manage BE and trailing for open positions
        // ────────────────────────────────────────────────────────────────────

        protected override void OnTick()
        {
            if (Account.Balance > _peakBalance)
                _peakBalance = Account.Balance;

            foreach (var pos in Positions.FindAll(LABEL, SymbolName))
                ManagePosition(pos);
        }

        // ────────────────────────────────────────────────────────────────────
        //  OnPositionClosed — cleanup dictionary entry
        // ────────────────────────────────────────────────────────────────────

        protected override void OnPositionClosed(PositionClosedEventArgs args)
        {
            var pos = args.Position;
            if (pos.Label != LABEL) return;
            _beTriggered.Remove(pos.Id);
            Print($"[CLOSE] {args.Reason,10} | Entry={pos.EntryPrice:F2} | PnL={pos.NetProfit:F2}");
        }

        // ════════════════════════════════════════════════════════════════════
        //  OPENING RANGE BUILDER
        // ════════════════════════════════════════════════════════════════════

        private void TryBuildOrb(DateTime now)
        {
            if (_orbDate != now.Date)
            {
                _orbDate   = now.Date;
                _orbFormed = false;
                _orbHigh   = double.MinValue;
                _orbLow    = double.MaxValue;
            }

            if (_orbFormed) return;

            var nyOpen = new DateTime(now.Year, now.Month, now.Day, NyOpenHour, NyOpenMinute, 0);
            var orbEnd = nyOpen.AddMinutes(OrbDurationMinutes);

            // Only lock the ORB after the window has fully passed
            if (now < orbEnd) return;

            double high  = double.MinValue;
            double low   = double.MaxValue;
            bool   found = false;

            // Scan completed bars (Last(i), i ≥ 1); bars are newest-first
            for (int i = 1; i < Math.Min(Bars.Count, 300); i++)
            {
                DateTime barOpen = Bars.OpenTimes.Last(i);

                if (barOpen < nyOpen) break;   // we've gone past the ORB window start
                if (barOpen >= orbEnd) continue; // bar starts after window — skip

                high  = Math.Max(high, Bars.HighPrices.Last(i));
                low   = Math.Min(low,  Bars.LowPrices.Last(i));
                found = true;
            }

            if (!found) return;

            _orbHigh   = high;
            _orbLow    = low;
            _orbFormed = true;
            Print($"[ORB] {now:yyyy-MM-dd} High={_orbHigh:F2} Low={_orbLow:F2} Range={_orbHigh - _orbLow:F2}pts");
        }

        // ════════════════════════════════════════════════════════════════════
        //  ENTRY LOGIC
        // ════════════════════════════════════════════════════════════════════

        private void TryEntry()
        {
            if (_h1.Count < H1EmaPeriod + 5) return;
            if (_d1.Count < D1EmaPeriod + 5) return;

            double h1Close = _h1.ClosePrices.Last(1);
            double h1Ema   = _h1Ema.Result.Last(1);
            double d1Close = _d1.ClosePrices.Last(1);
            double d1Ema   = _d1Ema.Result.Last(1);

            bool trendBull = h1Close > h1Ema && d1Close > d1Ema;
            bool trendBear = h1Close < h1Ema && d1Close < d1Ema;

            // ATR volatility gate
            double atrNow = _atr.Result.Last(1);
            double atrAvg = _atrSma.Result.Last(1);
            if (atrAvg <= 0 || atrNow <= 0) return;
            double atrRatio = atrNow / atrAvg;
            if (atrRatio < AtrMinRatio || atrRatio > AtrMaxRatio) return;

            double barClose = Bars.ClosePrices.Last(1);

            if (trendBull && barClose > _orbHigh + MinBreakoutPts)
            {
                EnterTrade(TradeType.Buy, atrNow);
                return;
            }

            if (trendBear && barClose < _orbLow - MinBreakoutPts)
            {
                EnterTrade(TradeType.Sell, atrNow);
            }
        }

        private void EnterTrade(TradeType direction, double atrValue)
        {
            double entryPrice, slDist, slPx, tpPx;

            if (direction == TradeType.Buy)
            {
                entryPrice = Symbol.Ask;
                slDist     = (entryPrice - _orbLow) + atrValue * SlAtrBuffer;
                slPx       = NormalizePrice(entryPrice - slDist);
                tpPx       = NormalizePrice(entryPrice + slDist * RrRatio);
            }
            else
            {
                entryPrice = Symbol.Bid;
                slDist     = (_orbHigh - entryPrice) + atrValue * SlAtrBuffer;
                slPx       = NormalizePrice(entryPrice + slDist);
                tpPx       = NormalizePrice(entryPrice - slDist * RrRatio);
            }

            if (slDist <= 0)
            {
                Print("[SKIP] SL distance is zero or negative — entry aborted.");
                return;
            }

            double volume = CalcVolumeUnits(slDist);
            if (volume < Symbol.VolumeInUnitsMin)
            {
                Print("[SKIP] Computed volume below broker minimum.");
                return;
            }

            var result = ExecuteMarketOrder(direction, SymbolName, volume, LABEL, slPx, tpPx);

            if (result.IsSuccessful)
            {
                _tradesToday++;
                _beTriggered[result.Position.Id] = false;
                Print($"[ENTRY] {direction} @ {entryPrice:F2}  SL={slPx:F2} ({slDist:F2}pts)  " +
                      $"TP={tpPx:F2}  Vol={volume:F1}  #{_tradesToday}/day");
            }
            else
            {
                Print($"[ENTRY FAIL] {result.Error}");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  POSITION MANAGEMENT — BREAKEVEN + ATR TRAILING STOP
        // ════════════════════════════════════════════════════════════════════

        private void ManagePosition(Position pos)
        {
            if (pos.StopLoss == null) return;

            double currentPrice = pos.TradeType == TradeType.Buy ? Symbol.Bid : Symbol.Ask;
            double entry        = pos.EntryPrice;
            double slDist       = Math.Abs(entry - pos.StopLoss.Value);
            if (slDist <= 0) return;

            if (!_beTriggered.ContainsKey(pos.Id))
                _beTriggered[pos.Id] = false;

            if (!_beTriggered[pos.Id])
            {
                // ── Phase 1: wait for BE trigger ──────────────────────────
                double beTrigger = pos.TradeType == TradeType.Buy
                    ? entry + slDist * BeTriggerXSL
                    : entry - slDist * BeTriggerXSL;

                bool triggered = pos.TradeType == TradeType.Buy
                    ? currentPrice >= beTrigger
                    : currentPrice <= beTrigger;

                if (!triggered) return;

                double newSL = pos.TradeType == TradeType.Buy
                    ? NormalizePrice(entry + Symbol.PipSize)
                    : NormalizePrice(entry - Symbol.PipSize);

                bool shouldMove = pos.TradeType == TradeType.Buy
                    ? newSL > pos.StopLoss.Value + Symbol.PipSize
                    : newSL < pos.StopLoss.Value - Symbol.PipSize;

                if (shouldMove)
                {
                    var r = ModifyPosition(pos, newSL, pos.TakeProfit);
                    if (r.IsSuccessful)
                    {
                        _beTriggered[pos.Id] = true;
                        Print($"[BE] {pos.TradeType} price={currentPrice:F2} → SL={newSL:F2}");
                    }
                }
            }
            else
            {
                // ── Phase 2: ATR trailing stop ────────────────────────────
                double trailDist = _atr.Result.Last(1) * TrailAtrMult;
                double newSL;
                bool   shouldMove;

                if (pos.TradeType == TradeType.Buy)
                {
                    newSL     = NormalizePrice(currentPrice - trailDist);
                    shouldMove = newSL > pos.StopLoss.Value + Symbol.PipSize;
                }
                else
                {
                    newSL     = NormalizePrice(currentPrice + trailDist);
                    shouldMove = newSL < pos.StopLoss.Value - Symbol.PipSize;
                }

                if (shouldMove)
                    ModifyPosition(pos, newSL, pos.TakeProfit);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  FTMO COMPLIANCE GUARDS
        // ════════════════════════════════════════════════════════════════════

        private bool PassesFtmoGuards()
        {
            if (_tradesToday >= MaxTradesPerDay) return false;

            double dayLossPct = -(Account.Balance - _dayStartBalance) / _dayStartBalance * 100.0;
            if (dayLossPct >= DailyLossLimitPct)
            {
                Print($"[FTMO] Daily loss {dayLossPct:F2}% ≥ {DailyLossLimitPct}% — no new entries.");
                return false;
            }

            double ddPct = (_peakBalance - Account.Balance) / _peakBalance * 100.0;
            if (ddPct >= MaxDrawdownPct)
            {
                Print($"[FTMO] Max DD {ddPct:F2}% ≥ {MaxDrawdownPct}% — trading halted!");
                return false;
            }

            return true;
        }

        // ════════════════════════════════════════════════════════════════════
        //  UTILITY HELPERS
        // ════════════════════════════════════════════════════════════════════

        private double CalcVolumeUnits(double slDist)
        {
            double riskMoney         = Account.Equity * (RiskPct / 100.0);
            double unitValuePerPoint = Symbol.TickValue / Symbol.TickSize;
            if (unitValuePerPoint <= 0) return 0;

            double units = riskMoney / (slDist * unitValuePerPoint);
            units = Math.Min(units, MaxPositionUnits);
            units = Symbol.NormalizeVolumeInUnits(units, RoundingMode.Down);
            return units;
        }

        private double NormalizePrice(double price)
        {
            return Math.Round(price / Symbol.TickSize) * Symbol.TickSize;
        }
    }
}
