# NAS100 / US100 Opening Range Breakout (ORB) cBot

## Áttekintés

| Paraméter | Érték |
|-----------|-------|
| Instrument | NAS100 / US100 (NASDAQ 100 index) |
| Timeframe | M5 |
| Irány | Long és Short |
| Havi célprofit | ~5–8% |
| Max kockázat / trade | 1% account balance |
| Risk:Reward | 1:2 (alapértelmezett) |
| NY Session | 14:30–20:00 UTC |

---

## Stratégia logika

### 1. Opening Range (ORB) meghatározása
Az EA a **NY piac nyitása utáni első 15 percet** (14:30–14:45 UTC) figyeli:
- Rögzíti az ebben az időszakban kialakult **legmagasabb** és **legalacsonyabb** árat
- Ez lesz az aznapi kulcs referenciaszint (breakout zóna)

> Miért az első 15 perc? Az intézményi szereplők a nyitáskor pozícionálnak.
> Az ORB tartomány megmutatja a „megállapodott" szinteket, amelyek áttörése trendmozgást jelzhet.

### 2. HTF Trend filter (Higher Timeframe)

| Szűrő | Bullish feltétel | Bearish feltétel |
|-------|-----------------|-----------------|
| H1 EMA50 | H1 close > EMA50 | H1 close < EMA50 |
| D1 EMA200 | D1 close > EMA200 | D1 close < EMA200 |

Csak akkor lép be, ha **mindkét** timeframe ugyanazt az irányt mutatja.

### 3. Breakout belépési jel

```
Long:  M5 gyertya zárul > ORB High + MinBreakoutPts  ÉS  trend bullish
Short: M5 gyertya zárul < ORB Low  − MinBreakoutPts  ÉS  trend bearish
```

### 4. ATR Volatilitás szűrő

```
ATR arány = aktuális ATR(14) / ATR(14) átlaga (50 bar SMA)
Belépés csak akkor, ha: 0.5 ≤ ATR arány ≤ 2.5
```

- **Túl alacsony ATR** → konszolidáció, nincs momentum → kihagyás
- **Túl magas ATR** → extrém volatilitás (hír esemény) → kihagyás

### 5. Stop Loss & Take Profit számítás

```
Long:
  SL   = entry − (entry − ORB Low) − ATR × SlAtrBuffer
  TP   = entry + SL_távolság × RR_arány

Short:
  SL   = entry + (ORB High − entry) + ATR × SlAtrBuffer
  TP   = entry − SL_távolság × RR_arány
```

Az SL az ORB range ellentétes oldalán van + egy ATR buffer → logikai invalidáció.

### 6. Break-even & Trailing Stop

| Fázis | Logika |
|-------|--------|
| **BE** | Amikor profit ≥ 1×SL_távolság → SL az entry ± 1 pip szintre kerül |
| **Trail** | BE után: SL = aktuális ár − (ATR × 1.5) (long) / + (ATR × 1.5) (short) |

---

## Position Sizing

```
Kockázati összeg = Account Equity × RiskPct%
SL távolság      = |Entry − SL price|

Volume (units) = Kockázati összeg / (SL_távolság × TickValue / TickSize)
Maximum        = MaxPositionUnits (alapért. 100 unit)
```

---

## FTMO / Prop Firm Compliance

| Szabály | Alapértelmezett |
|---------|----------------|
| Max trades/nap | 3 |
| Napi veszteség limit | 4.5% |
| Max drawdown (peak-tól) | 9.0% |
| Utolsó belépési idő | 18:00 UTC |
| Kereskedési napok | Hétfő–Péntek |

---

## Miért működik ez NASDAQ-on?

1. **ORB** a leghatékonyabb NASDAQ stratégiák egyike — az intézményi játékosok a nyitáskor pozícionálnak, az ORB kitörése általában trendszerű mozgást indít el
2. **Dual EMA filter** (H1 + D1) kizárja a counter-trend belépéseket
3. **ATR szűrő** automatikusan elkerüli a hír eseményeket és az alacsony volatilitású periódusokat
4. **NY session** (14:30–20:00 UTC) adja a NASDAQ mozgásainak döntő részét
5. **ATR trailing stop** hagyja futni a nyerő pozíciókat, nem vágja le korán

---

## Telepítés (cTrader / cAlgo)

1. Másold a `NAS100_ORB_cBot.cs` fájlt a cAlgo Projects mappába  
   `%AppData%\cAlgo\Sources\Robots\`
2. **cTrader → Automate → Build** → fordítsd le a robotot
3. Nyiss **NAS100 / US100 M5** chartot
4. Húzd az EA-t a chartra → konfiguráld a paramétereket

> **Fontos:** Ellenőrizd a szimbólum nevét a brokernél — lehet `NAS100`, `US100`, `NAS100.cash`, `USTEC`, `NASDAQ` stb.

### Ajánlott paraméterek éles kereskedéshez

```
Opening Range:
  NY Open Hour    = 14         (UTC)
  NY Open Minute  = 30
  ORB Duration    = 15 min
  Min Breakout    = 10.0 pts

Risk/Reward:
  Risk Per Trade  = 1.0%
  RR Ratio        = 2.0
  SL ATR Buffer   = 0.5 × ATR
  Max Position    = 100 units

ATR Filter:
  ATR Period      = 14
  ATR Avg Period  = 50
  ATR Min Ratio   = 0.5
  ATR Max Ratio   = 2.5

HTF Filter:
  H1 EMA         = 50
  D1 EMA         = 200

Breakeven/Trail:
  BE Trigger      = 1.0 × SL
  Trail ATR Mult  = 1.5

FTMO:
  Max Trades/Day  = 3
  Daily Loss Lim  = 4.5%
  Max Drawdown    = 9.0%
  Last Entry      = 18:00 UTC
```

---

## Fontos figyelmeztetések

- **Demo számlán tesztelj** legalább 2–3 hónapig éles kereskedés előtt
- NASDAQ rendkívül érzékeny a **Fed bejelentésekre** (FOMC) és **tech cégek earnings**-eire
- **NFP, CPI, FOMC napokon** állítsd le az EA-t — a szűrők nem elegendők extrém hír-volatilitáshoz
- Az **ORB stratégia összeomlott piacon** (gap-down/up nyitás) megbízhatatlan — ellenőrizd a napi híreket
- A historikus teljesítmény nem garantálja a jövőbeli eredményeket
