import { useState } from 'react';
import type { SignalDirection, AddRuleRequest } from '../../types/strategy';
import type { ExitStrategy, EnabledSignalType } from '../../types/backtest';

interface Props {
  onAdd: (strategyId: string | null, newName: string | null, req: AddRuleRequest) => void;
  onClose: () => void;
}

const SYMBOLS = ['SPY', 'QQQ'];

const TIMEFRAME_OPTIONS: { label: string; value: string }[] = [
  { label: '4H', value: '4H' },
  { label: '1H', value: '1H' },
  { label: '15M', value: '15M' },
  { label: '5M', value: '5M' },
  { label: '1M', value: '1M' },
  { label: '4H + 1H', value: '4H+1H' },
  { label: '4H + 15M', value: '4H+15M' },
  { label: '4H + 5M', value: '4H+5M' },
  { label: '1H + 15M', value: '1H+15M' },
  { label: '1H + 5M', value: '1H+5M' },
  { label: '1H + 1M', value: '1H+1M' },
  { label: '15M + 5M', value: '15M+5M' },
  { label: '15M + 1M', value: '15M+1M' },
  { label: '4H + 1H + 15M', value: '4H+1H+15M' },
  { label: '4H + 1H + 5M', value: '4H+1H+5M' },
  { label: '4H + 1H + 1M', value: '4H+1H+1M' },
  { label: '1H + 15M + 1M', value: '1H+15M+1M' },
];

const EXIT_STRATEGIES: { label: string; value: ExitStrategy }[] = [
  { label: 'Trailing Stop', value: 'TrailingStop' },
  { label: 'Breakeven Stop', value: 'BreakevenStop' },
  { label: 'Fixed R', value: 'FixedR' },
  { label: 'Next Liquidity', value: 'NextLiquidity' },
];

const SIGNAL_TYPES: { label: string; value: EnabledSignalType }[] = [
  { label: 'OB Retest', value: 'OBRetest' },
  { label: 'FVG Fill', value: 'FVGFill' },
  { label: 'Liquidity Sweep', value: 'LiquiditySweep' },
  { label: 'Structure Break', value: 'StructureBreak' },
];

export function CreateStrategyModal({ onAdd, onClose }: Props) {
  const [symbol, setSymbol] = useState('QQQ');
  const [timeframe, setTimeframe] = useState('4H');
  const [exitStrategy, setExitStrategy] = useState<ExitStrategy>('TrailingStop');
  const [fixedRR, setFixedRR] = useState('2');
  const [direction, setDirection] = useState<SignalDirection>('Both');
  const [selectedSignalTypes, setSelectedSignalTypes] = useState<Set<EnabledSignalType>>(
    new Set(['OBRetest', 'FVGFill'])
  );
  const [windowEnabled, setWindowEnabled] = useState(true);
  const [windowStart, setWindowStart] = useState('09:30');
  const [windowEnd, setWindowEnd] = useState('16:00');

  const EXIT_LABELS: Record<ExitStrategy, string> = {
    TrailingStop: 'Trailing', BreakevenStop: 'BE', FixedR: 'FixedR', NextLiquidity: 'NextLiq', All: 'All',
  };
  const SIGNAL_SHORT: Record<EnabledSignalType, string> = {
    OBRetest: 'OB', FVGFill: 'FVG', LiquiditySweep: 'Liq', StructureBreak: 'BOS',
  };
  const autoName = `${timeframe} · ${symbol} · ${[...selectedSignalTypes].map(t => SIGNAL_SHORT[t]).join('+')} · ${EXIT_LABELS[exitStrategy]}${direction !== 'Both' ? ` · ${direction}` : ''}`;

  function toggleSignalType(t: EnabledSignalType) {
    setSelectedSignalTypes(prev => {
      const next = new Set(prev);
      if (next.has(t)) {
        if (next.size > 1) next.delete(t);
      } else {
        next.add(t);
      }
      return next;
    });
  }

  function handleSubmit() {
    if (selectedSignalTypes.size === 0) return;

    const req: AddRuleRequest = {
      symbol,
      label: timeframe,
      exitStrategy,
      fixedRRatio: parseFloat(fixedRR) || 2,
      killZoneOnly: false,
      direction,
      signalTypes: [...selectedSignalTypes],
      tradingWindowStart: windowEnabled ? `${windowStart}:00` : null,
      tradingWindowEnd: windowEnabled ? `${windowEnd}:00` : null,
    };

    onAdd(null, autoName, req);
  }

  const canSubmit = selectedSignalTypes.size > 0;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60" onClick={onClose}>
      <div
        className="bg-slate-900 border border-slate-700 rounded-xl shadow-2xl w-[460px] max-h-[90vh] overflow-y-auto p-6"
        onClick={e => e.stopPropagation()}
      >
        <div className="flex items-start justify-between mb-5">
          <div>
            <h2 className="text-base font-bold text-slate-100">Create Strategy</h2>
            <p className="text-xs text-slate-500 mt-0.5">Configure a new live signal detection rule</p>
          </div>
          <button onClick={onClose} className="text-slate-600 hover:text-slate-300 text-lg leading-none mt-0.5">&#10005;</button>
        </div>

        {/* Auto-generated name preview */}
        <div className="mb-4 bg-slate-800 rounded-lg px-3.5 py-2.5">
          <div className="text-[10px] text-slate-600 uppercase tracking-wide mb-1">Strategy Name</div>
          <div className="text-sm font-semibold text-slate-100">{autoName}</div>
        </div>

        {/* Symbol */}
        <div className="mb-4">
          <Label>Symbol</Label>
          <div className="flex gap-1">
            {SYMBOLS.map(s => (
              <button
                key={s}
                onClick={() => setSymbol(s)}
                className={`flex-1 py-1.5 rounded-lg text-xs font-semibold transition-colors duration-150 border
                  ${symbol === s
                    ? 'bg-blue-900 border-blue-700 text-blue-300'
                    : 'bg-slate-800 border-slate-700 text-slate-500 hover:text-slate-300'
                  }`}
              >
                {s}
              </button>
            ))}
          </div>
        </div>

        {/* Timeframe */}
        <div className="mb-4">
          <Label>Timeframe</Label>
          <select
            value={timeframe}
            onChange={e => setTimeframe(e.target.value)}
            className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-sm text-slate-100
              focus:outline-none focus:border-blue-600"
          >
            {TIMEFRAME_OPTIONS.map(t => (
              <option key={t.value} value={t.value}>{t.label}</option>
            ))}
          </select>
        </div>

        {/* Signal types */}
        <div className="mb-4">
          <Label>Signal Types</Label>
          <div className="flex flex-wrap gap-1.5">
            {SIGNAL_TYPES.map(t => (
              <button
                key={t.value}
                onClick={() => toggleSignalType(t.value)}
                className={`px-3 py-1.5 rounded-lg text-xs font-semibold transition-colors duration-150 border
                  ${selectedSignalTypes.has(t.value)
                    ? 'bg-blue-900 border-blue-700 text-blue-300'
                    : 'bg-slate-800 border-slate-700 text-slate-500 hover:text-slate-300'
                  }`}
              >
                {t.label}
              </button>
            ))}
          </div>
        </div>

        {/* Exit strategy */}
        <div className="mb-4">
          <Label>Exit Strategy</Label>
          <div className="flex flex-wrap gap-1.5">
            {EXIT_STRATEGIES.map(e => (
              <button
                key={e.value}
                onClick={() => setExitStrategy(e.value)}
                className={`px-3 py-1.5 rounded-lg text-xs font-semibold transition-colors duration-150 border
                  ${exitStrategy === e.value
                    ? 'bg-blue-900 border-blue-700 text-blue-300'
                    : 'bg-slate-800 border-slate-700 text-slate-500 hover:text-slate-300'
                  }`}
              >
                {e.label}
              </button>
            ))}
          </div>
        </div>

        {/* Fixed R:R ratio */}
        {exitStrategy === 'FixedR' && (
          <div className="mb-4">
            <Label>R:R Ratio</Label>
            <input
              type="number"
              value={fixedRR}
              onChange={e => setFixedRR(e.target.value)}
              min="0.5"
              max="10"
              step="0.5"
              className="w-24 bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-sm text-slate-100
                focus:outline-none focus:border-blue-600"
            />
          </div>
        )}

        {/* Direction */}
        <div className="mb-5">
          <Label>Trade Direction</Label>
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

        {/* Trading window */}
        <div className="mb-5">
          <div className="flex items-center justify-between mb-2">
            <Label noMargin>Trading Window (ET)</Label>
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
            disabled={!canSubmit}
            className="px-4 py-1.5 rounded-lg text-xs font-semibold text-white bg-blue-700 hover:bg-blue-600
              disabled:bg-slate-700 disabled:text-slate-500 disabled:cursor-not-allowed transition-colors duration-150"
          >
            Create Strategy
          </button>
        </div>
      </div>
    </div>
  );
}

function Label({ children, noMargin }: { children: React.ReactNode; noMargin?: boolean }) {
  return (
    <label className={`block text-[11px] text-slate-400 uppercase tracking-wide ${noMargin ? '' : 'mb-1.5'}`}>
      {children}
    </label>
  );
}
