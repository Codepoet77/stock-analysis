import { useState } from 'react';
import type { BacktestParameters, BacktestTimeframe, EnabledSignalType, ExitStrategy, SessionFilter } from '../../types/backtest';

interface Props {
  onStart: (params: BacktestParameters) => void;
  onCancel: () => void;
  isRunning: boolean;
}

const TF_OPTIONS: { value: BacktestTimeframe; label: string }[] = [
  { value: 'OneMinute',     label: '1M' },
  { value: 'FiveMinute',    label: '5M' },
  { value: 'FifteenMinute', label: '15M' },
  { value: 'OneHour',       label: '1H' },
  { value: 'FourHour',      label: '4H' },
];

const SIGNAL_OPTIONS: { value: EnabledSignalType; label: string; desc: string }[] = [
  { value: 'OBRetest',       label: 'OB Retest',      desc: 'Order Block retest' },
  { value: 'FVGFill',        label: 'FVG Fill',        desc: 'Fair Value Gap fill' },
  { value: 'StructureBreak', label: 'Structure Break', desc: 'Market structure break' },
  { value: 'LiquiditySweep', label: 'Liq. Sweep',     desc: 'Liquidity sweep reversal' },
];

function lastTradingDayDaysBack(): number {
  const day = new Date().getDay(); // 0=Sun, 6=Sat
  if (day === 0) return 2; // Sunday → Friday
  if (day === 1) return 3; // Monday → Friday
  if (day === 6) return 1; // Saturday → Friday
  return 1;                // Tue–Fri → yesterday
}

export function BacktestForm({ onStart, onCancel, isRunning }: Props) {
  const [symbols, setSymbols] = useState('SPY,QQQ');
  const [daysBack, setDaysBack] = useState(730);
  const [durationKey, setDurationKey] = useState('2Y');
  const [timeframes, setTimeframes] = useState<BacktestTimeframe[]>(
    ['OneMinute', 'FiveMinute', 'FifteenMinute', 'OneHour', 'FourHour']
  );
  const [exitStrategy, setExitStrategy] = useState<ExitStrategy>('All');
  const [fixedRRatio, setFixedRRatio] = useState(2.0);
  // OpenKZ and CloseKZ can be selected together (maps to BothKZ on submit)
  const [openKZ, setOpenKZ] = useState(false);
  const [closeKZ, setCloseKZ] = useState(false);

  const resolvedSession = (): SessionFilter => {
    if (openKZ && closeKZ) return 'BothKZ';
    if (openKZ) return 'OpenKZ';
    if (closeKZ) return 'CloseKZ';
    return 'Full';
  };
  // null means "all enabled"; a subset means only those are tested
  const [signalTypes, setSignalTypes] = useState<EnabledSignalType[] | null>(['OBRetest', 'FVGFill']);

  const toggleTf = (tf: BacktestTimeframe) =>
    setTimeframes(prev => prev.includes(tf) ? prev.filter(t => t !== tf) : [...prev, tf]);

  const allSignalsEnabled = signalTypes === null;
  const isSignalEnabled = (t: EnabledSignalType) => signalTypes === null || signalTypes.includes(t);
  const toggleSignal = (t: EnabledSignalType) => {
    if (signalTypes === null) {
      // Deselect one — switch from "all" to all-except-this
      setSignalTypes(SIGNAL_OPTIONS.map(o => o.value).filter(v => v !== t));
    } else if (signalTypes.includes(t)) {
      const next = signalTypes.filter(v => v !== t);
      setSignalTypes(next.length === 0 ? null : next);
    } else {
      const next = [...signalTypes, t];
      setSignalTypes(next.length === SIGNAL_OPTIONS.length ? null : next);
    }
  };

  const handleSubmit = () => {
    const parsedSymbols = symbols.split(',').map(s => s.trim().toUpperCase()).filter(Boolean);
    if (parsedSymbols.length === 0 || timeframes.length === 0) return;
    onStart({ symbols: parsedSymbols, daysBack, timeframes, exitStrategy, fixedRRatio, sessionFilter: resolvedSession(), signalTypes });
  };

  return (
    <div className="bg-slate-900 border border-slate-800 rounded-xl p-6">
      <h2 className="text-lg font-semibold text-slate-100 mb-5">Backtest Parameters</h2>

      <div className="grid grid-cols-2 gap-5">
        {/* Symbols */}
        <Field label="Symbols (comma separated)">
          <input
            value={symbols}
            onChange={e => setSymbols(e.target.value)}
            disabled={isRunning}
            placeholder="SPY,QQQ"
            className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-sm text-slate-100
              placeholder-slate-600 focus:outline-none focus:border-blue-600 transition-colors
              disabled:opacity-50 disabled:cursor-not-allowed"
          />
        </Field>

        {/* Duration */}
        <Field label="History">
          <div className="flex gap-2 flex-wrap">
            {([
              { key: '1D',   label: '1D',  getDays: lastTradingDayDaysBack },
              { key: '1W',   label: '1W',  getDays: () => 7   },
              { key: '2W',   label: '2W',  getDays: () => 14  },
              { key: '30D',  label: '30D', getDays: () => 30  },
              { key: '60D',  label: '60D', getDays: () => 60  },
              { key: '90D',  label: '90D', getDays: () => 90  },
              { key: '6M',   label: '6M',  getDays: () => 180 },
              { key: '1Y',   label: '1Y',  getDays: () => 365 },
              { key: '2Y',   label: '2Y',  getDays: () => 730 },
              { key: '3Y',   label: '3Y',  getDays: () => 1095 },
              { key: '4Y',   label: '4Y',  getDays: () => 1460 },
              { key: '5Y',   label: '5Y',  getDays: () => 1825 },
            ]).map(({ key, label, getDays }) => (
              <button
                key={key}
                onClick={() => { setDurationKey(key); setDaysBack(getDays()); }}
                disabled={isRunning}
                className={`px-3 py-1 rounded-lg text-xs font-semibold transition-all duration-150
                  active:scale-95 disabled:cursor-not-allowed disabled:opacity-50
                  ${durationKey === key
                    ? 'bg-blue-700 text-white hover:bg-blue-600'
                    : 'bg-slate-800 text-slate-400 hover:bg-slate-700 hover:text-slate-200'
                  }`}
              >
                {label}
              </button>
            ))}
          </div>
        </Field>

        {/* Timeframes */}
        <Field label="Timeframes to test">
          <div className="flex gap-2 flex-wrap">
            {TF_OPTIONS.map(({ value, label }) => (
              <button
                key={value}
                onClick={() => toggleTf(value)}
                disabled={isRunning}
                className={`px-3 py-1 rounded-lg text-xs font-semibold transition-all duration-150
                  active:scale-95 disabled:cursor-not-allowed disabled:opacity-50
                  ${timeframes.includes(value)
                    ? 'bg-blue-700 text-white hover:bg-blue-600'
                    : 'bg-slate-800 text-slate-400 hover:bg-slate-700 hover:text-slate-200'
                  }`}
              >
                {label}
              </button>
            ))}
          </div>
        </Field>

        {/* Exit strategy */}
        <Field label="Exit strategy">
          <div className="flex gap-2 flex-wrap">
            {([
              { value: 'All',           label: 'All Strategies' },
              { value: 'FixedR',        label: 'Fixed R' },
              { value: 'BreakevenStop', label: 'Breakeven Stop' },
              { value: 'TrailingStop',  label: 'Trailing Stop' },
              { value: 'NextLiquidity', label: 'Next Liquidity' },
            ] as { value: ExitStrategy; label: string }[]).map(({ value, label }) => (
              <button
                key={value}
                onClick={() => setExitStrategy(value)}
                disabled={isRunning}
                className={`px-3 py-1 rounded-lg text-xs font-semibold transition-all duration-150
                  active:scale-95 disabled:cursor-not-allowed disabled:opacity-50
                  ${exitStrategy === value
                    ? 'bg-blue-700 text-white hover:bg-blue-600'
                    : 'bg-slate-800 text-slate-400 hover:bg-slate-700 hover:text-slate-200'
                  }`}
              >
                {label}
              </button>
            ))}
          </div>
        </Field>

        {/* Fixed R ratio */}
        {(exitStrategy === 'FixedR' || exitStrategy === 'BreakevenStop' || exitStrategy === 'TrailingStop' || exitStrategy === 'All') && (
          <Field label={`Fixed R ratio: ${fixedRRatio.toFixed(1)}R`}>
            <input
              type="range" min={1} max={5} step={0.5}
              value={fixedRRatio}
              onChange={e => setFixedRRatio(Number(e.target.value))}
              disabled={isRunning}
              className="w-full accent-blue-600 disabled:opacity-50"
            />
            <div className="flex justify-between text-[11px] text-slate-600 mt-1">
              <span>1R</span><span>2R</span><span>3R</span><span>4R</span><span>5R</span>
            </div>
          </Field>
        )}

        {/* Session filter */}
        <Field label="Session filter">
          <div className="flex gap-2 flex-wrap">
            {([
              { key: 'full',    label: 'Full Session',   desc: '9:30–4:00pm',  isOn: !openKZ && !closeKZ, toggle: () => { setOpenKZ(false); setCloseKZ(false); } },
              { key: 'open',    label: 'NY Open',        desc: '9:30–11:00am', isOn: openKZ,              toggle: () => setOpenKZ(v => !v) },
              { key: 'close',   label: 'PM Power Hour',  desc: '2:00–4:00pm',  isOn: closeKZ,             toggle: () => setCloseKZ(v => !v) },
            ]).map(({ key, label, desc, isOn, toggle }) => (
              <button
                key={key}
                onClick={toggle}
                disabled={isRunning}
                className={`px-3 py-1.5 rounded-lg text-xs font-semibold transition-all duration-150
                  active:scale-95 disabled:cursor-not-allowed disabled:opacity-50 text-left
                  ${isOn
                    ? 'bg-blue-700 text-white hover:bg-blue-600'
                    : 'bg-slate-800 text-slate-400 hover:bg-slate-700 hover:text-slate-200'
                  }`}
              >
                <div>{label}</div>
                <div className={`text-[10px] font-normal mt-0.5 ${isOn ? 'text-blue-200' : 'text-slate-600'}`}>{desc}</div>
              </button>
            ))}
          </div>
          {openKZ && closeKZ && (
            <p className="text-[11px] text-blue-400 mt-1.5">Both power sessions selected</p>
          )}
        </Field>

        {/* Signal type filter */}
        <Field label={`Signal types ${allSignalsEnabled ? '(all)' : `(${signalTypes!.length} selected)`}`}>
          <div className="flex gap-2 flex-wrap">
            {SIGNAL_OPTIONS.map(({ value, label, desc }) => (
              <button
                key={value}
                onClick={() => toggleSignal(value)}
                disabled={isRunning}
                title={desc}
                className={`px-3 py-1 rounded-lg text-xs font-semibold transition-all duration-150
                  active:scale-95 disabled:cursor-not-allowed disabled:opacity-50
                  ${isSignalEnabled(value)
                    ? 'bg-blue-700 text-white hover:bg-blue-600'
                    : 'bg-slate-800 text-slate-400 hover:bg-slate-700 hover:text-slate-200'
                  }`}
              >
                {label}
              </button>
            ))}
          </div>
        </Field>
      </div>

      {/* Large run warning */}
      {daysBack >= 365 && timeframes.includes('OneMinute') && (
        <div className="mt-4 px-3.5 py-2.5 bg-orange-950 border border-amber-800 rounded-lg text-xs text-amber-300">
          ⚠ 2+ years of 1M data requires ~400K bars per symbol. Expect this run to take several minutes.
        </div>
      )}

      {isRunning ? (
        <button
          onClick={onCancel}
          className="mt-5 w-full py-2.5 rounded-lg text-sm font-bold transition-all duration-150
            bg-red-800 text-white hover:bg-red-700 active:bg-red-900 active:scale-[0.99]"
        >
          Cancel Backtest
        </button>
      ) : (
        <button
          onClick={handleSubmit}
          disabled={timeframes.length === 0}
          className="mt-5 w-full py-2.5 rounded-lg text-sm font-bold transition-all duration-150
            bg-blue-700 text-white hover:bg-blue-600 active:bg-blue-800 active:scale-[0.99]
            disabled:bg-slate-800 disabled:text-slate-500 disabled:cursor-not-allowed disabled:scale-100"
        >
          Run Backtest
        </button>
      )}
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="block text-[11px] font-semibold text-slate-400 uppercase tracking-wide mb-2">
        {label}
      </label>
      {children}
    </div>
  );
}
