import { useState } from 'react';
import type { BacktestStats, BacktestParameters, EnabledSignalType, ExitStrategy } from '../../types/backtest';
import type { Strategy, SignalDirection, AddRuleRequest } from '../../types/strategy';

interface Props {
  stats: BacktestStats;
  parameters: BacktestParameters;
  strategies: Strategy[];
  onAdd: (strategyId: string | null, newName: string | null, req: AddRuleRequest) => void;
  onClose: () => void;
}

function isDuplicateRule(
  strategy: Strategy,
  symbol: string, label: string, exitStrategy: string, direction: SignalDirection,
  windowStart: string | null, windowEnd: string | null
) {
  return strategy.rules.some(r =>
    r.symbol === symbol &&
    r.label === label &&
    r.exitStrategy === exitStrategy &&
    (r.direction === direction || r.direction === 'Both' || direction === 'Both') &&
    fmtWindowKey(r.tradingWindowStart) === fmtWindowKey(windowStart) &&
    fmtWindowKey(r.tradingWindowEnd)   === fmtWindowKey(windowEnd)
  );
}

// Normalise to "HH:MM" for comparison ("09:30:00" → "09:30", null → "")
function fmtWindowKey(ts: string | null | undefined): string {
  if (!ts) return '';
  return ts.slice(0, 5);
}

const EXIT_LABELS: Record<ExitStrategy, string> = {
  TrailingStop: 'Trailing', BreakevenStop: 'BE', FixedR: 'FixedR', NextLiquidity: 'NextLiq', All: 'All',
};
const SIGNAL_SHORT: Record<EnabledSignalType, string> = {
  OBRetest: 'OB', FVGFill: 'FVG', LiquiditySweep: 'Liq', StructureBreak: 'BOS',
};

export function AddToStrategyModal({ stats, parameters, strategies, onAdd, onClose }: Props) {
  const [mode, setMode] = useState<'existing' | 'new'>('new');
  const [selectedStrategyId, setSelectedStrategyId] = useState<string>(strategies[0]?.id ?? '');
  const [direction, setDirection] = useState<SignalDirection>('Both');
  const defaultWindow = parameters.sessionFilter === 'OpenKZ'  ? ['09:30', '11:00']
                      : parameters.sessionFilter === 'CloseKZ' ? ['14:00', '16:00']
                      : ['09:30', '16:00'];
  const [windowStart, setWindowStart] = useState(defaultWindow[0]);
  const [windowEnd, setWindowEnd] = useState(defaultWindow[1]);
  const [windowEnabled, setWindowEnabled] = useState(true);

  const enabledSignalTypes: EnabledSignalType[] = parameters.signalTypes && parameters.signalTypes.length > 0
    ? parameters.signalTypes
    : ['OBRetest', 'FVGFill', 'LiquiditySweep', 'StructureBreak'];

  const winStart = windowEnabled ? `${windowStart}:00` : null;
  const winEnd   = windowEnabled ? `${windowEnd}:00`   : null;

  const autoName = `${stats.label} · ${stats.symbol} · ${enabledSignalTypes.map(t => SIGNAL_SHORT[t]).join('+')} · ${EXIT_LABELS[stats.exitStrategy as ExitStrategy] ?? stats.exitStrategy}${direction !== 'Both' ? ` · ${direction}` : ''}`;

  const selectedStrategy = strategies.find(s => s.id === selectedStrategyId);
  const isDuplicate = mode === 'existing' && selectedStrategy
    ? isDuplicateRule(selectedStrategy, stats.symbol, stats.label, stats.exitStrategy, direction, winStart, winEnd)
    : false;

  function handleSubmit() {
    if (isDuplicate) return;
    const req: AddRuleRequest = {
      symbol: stats.symbol,
      label: stats.label,
      exitStrategy: stats.exitStrategy,
      fixedRRatio: parameters.fixedRRatio,
      killZoneOnly: parameters.sessionFilter === 'OpenKZ' || parameters.sessionFilter === 'BothKZ',
      direction,
      signalTypes: enabledSignalTypes,
      tradingWindowStart: winStart,
      tradingWindowEnd:   winEnd,
    };
    if (mode === 'existing') {
      onAdd(selectedStrategyId, null, req);
    } else {
      onAdd(null, autoName, req);
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60" onClick={onClose}>
      <div
        className="bg-slate-900 border border-slate-700 rounded-xl shadow-2xl w-[420px] p-6"
        onClick={e => e.stopPropagation()}
      >
        <div className="flex items-start justify-between mb-5">
          <div>
            <h2 className="text-base font-bold text-slate-100">Add to Strategy</h2>
            <p className="text-xs text-slate-500 mt-0.5">
              {stats.label} · {stats.symbol} · {stats.exitStrategy}
            </p>
          </div>
          <button onClick={onClose} className="text-slate-600 hover:text-slate-300 text-lg leading-none mt-0.5">&#10005;</button>
        </div>

        {/* Mode toggle */}
        {strategies.length > 0 && (
          <div className="flex gap-1 mb-4 bg-slate-800 rounded-lg p-1">
            {(['new', 'existing'] as const).map(m => (
              <button
                key={m}
                onClick={() => setMode(m)}
                className={`flex-1 py-1.5 rounded-md text-xs font-semibold transition-colors duration-150
                  ${mode === m ? 'bg-blue-700 text-white' : 'text-slate-400 hover:text-slate-200'}`}
              >
                {m === 'existing' ? 'Add to Existing' : 'Create New'}
              </button>
            ))}
          </div>
        )}

        {/* Auto-generated name (new mode) */}
        {mode === 'new' && (
          <div className="mb-4 bg-slate-800 rounded-lg px-3.5 py-2.5">
            <div className="text-[10px] text-slate-600 uppercase tracking-wide mb-1">Strategy Name</div>
            <div className="text-sm font-semibold text-slate-100">{autoName}</div>
          </div>
        )}

        {/* Existing strategy selector */}
        {mode === 'existing' && strategies.length > 0 && (
          <div className="mb-4">
            <label className="block text-[11px] text-slate-400 uppercase tracking-wide mb-1.5">Strategy</label>
            <select
              value={selectedStrategyId}
              onChange={e => setSelectedStrategyId(e.target.value)}
              className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-sm text-slate-100
                focus:outline-none focus:border-blue-600"
            >
              {strategies.map(s => {
                const dup = isDuplicateRule(s, stats.symbol, stats.label, stats.exitStrategy, direction, winStart, winEnd);
                return (
                  <option key={s.id} value={s.id}>
                    {dup ? `\u26A0 ${s.name} (already added)` : s.name}
                  </option>
                );
              })}
            </select>
          </div>
        )}

        {/* Direction */}
        <div className="mb-5">
          <label className="block text-[11px] text-slate-400 uppercase tracking-wide mb-1.5">Trade Direction</label>
          <div className="flex gap-1">
            {(['Long', 'Short', 'Both'] as SignalDirection[]).map(d => (
              <button
                key={d}
                onClick={() => setDirection(d)}
                className={`flex-1 py-1.5 rounded-lg text-xs font-semibold transition-colors duration-150 border
                  ${direction === d
                    ? d === 'Long' ? 'bg-green-900 border-green-700 text-green-300'
                      : d === 'Short' ? 'bg-red-900 border-red-700 text-red-300'
                      : 'bg-blue-900 border-blue-700 text-blue-300'
                    : 'bg-slate-800 border-slate-700 text-slate-500 hover:text-slate-300'
                  }`}
              >
                {d}
              </button>
            ))}
          </div>
        </div>

        {/* Signal types summary */}
        <div className="mb-5 bg-slate-800 rounded-lg px-3 py-2.5">
          <div className="text-[11px] text-slate-400 uppercase tracking-wide mb-1.5">Signal Types</div>
          <div className="flex flex-wrap gap-1">
            {enabledSignalTypes.map(t => (
              <span key={t} className="bg-slate-700 rounded px-2 py-0.5 text-[11px] text-slate-300">{t}</span>
            ))}
          </div>
        </div>

        {/* Trading window */}
        <div className="mb-5">
          <div className="flex items-center justify-between mb-2">
            <label className="text-[11px] text-slate-400 uppercase tracking-wide">Trading Window (ET)</label>
            <button
              onClick={() => setWindowEnabled(v => !v)}
              className={`w-8 h-4 rounded-full transition-colors duration-200 relative flex-shrink-0
                ${windowEnabled ? 'bg-green-700' : 'bg-slate-700'}`}
            >
              <span className={`absolute top-0.5 w-3 h-3 rounded-full bg-white transition-all duration-200
                ${windowEnabled ? 'left-4' : 'left-0.5'}`} />
            </button>
          </div>
          {windowEnabled ? (
            <div className="flex items-center gap-2">
              <input
                type="time"
                value={windowStart}
                onChange={e => setWindowStart(e.target.value)}
                className="flex-1 bg-slate-800 border border-slate-700 rounded-lg px-3 py-1.5 text-sm text-slate-100
                  focus:outline-none focus:border-blue-600"
              />
              <span className="text-slate-600 text-xs">to</span>
              <input
                type="time"
                value={windowEnd}
                onChange={e => setWindowEnd(e.target.value)}
                className="flex-1 bg-slate-800 border border-slate-700 rounded-lg px-3 py-1.5 text-sm text-slate-100
                  focus:outline-none focus:border-blue-600"
              />
            </div>
          ) : (
            <p className="text-xs text-slate-600">No time restriction — signals fire at any hour</p>
          )}
        </div>

        {/* Duplicate warning */}
        {isDuplicate && (
          <div className="mb-4 bg-amber-950/50 border border-amber-800/60 rounded-lg px-3 py-2.5 text-xs text-amber-300">
            This rule (<strong>{stats.symbol} {stats.label} {stats.exitStrategy} {direction}</strong>) already exists in this strategy.
          </div>
        )}

        {/* Actions */}
        <div className="flex gap-2 justify-end">
          <button
            onClick={onClose}
            className="px-4 py-1.5 rounded-lg text-xs font-semibold text-slate-400 bg-slate-800 hover:bg-slate-700
              hover:text-slate-200 transition-colors duration-150"
          >
            Cancel
          </button>
          <button
            onClick={handleSubmit}
            disabled={isDuplicate}
            className="px-4 py-1.5 rounded-lg text-xs font-semibold text-white bg-blue-700 hover:bg-blue-600
              disabled:bg-slate-700 disabled:text-slate-500 disabled:cursor-not-allowed transition-colors duration-150"
          >
            {mode === 'new' ? 'Create & Add Rule' : 'Add Rule'}
          </button>
        </div>
      </div>
    </div>
  );
}
