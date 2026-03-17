import { useState } from 'react';
import type { BacktestHistoryEntry, BacktestResult } from '../types/backtest';
import type { Strategy, AddRuleRequest } from '../types/strategy';
import { BacktestResults } from '../components/backtest/BacktestResults';

interface Props {
  history: BacktestHistoryEntry[];
  loadedResult: BacktestResult | null;
  onLoad: (id: string) => void;
  onClear: () => void;
  strategies: Strategy[];
  onAddToStrategy: (strategyId: string | null, newName: string | null, req: AddRuleRequest) => void;
}

export function HistoryPage({ history, loadedResult, onLoad, onClear, strategies, onAddToStrategy }: Props) {
  const [selectedId, setSelectedId] = useState<string | null>(null);

  function handleSelect(id: string) {
    if (selectedId === id) {
      setSelectedId(null);
      onClear();
    } else {
      setSelectedId(id);
      onLoad(id);
    }
  }

  if (history.length === 0) {
    return (
      <div className="max-w-[1400px] mx-auto">
        <div className="mb-6">
          <h2 className="text-xl font-bold text-slate-100 mb-1">Backtest History</h2>
          <p className="text-sm text-slate-500">Past completed backtests</p>
        </div>
        <div className="bg-slate-900 border border-slate-800 rounded-xl px-6 py-12 text-center text-slate-600 text-sm">
          No completed backtests yet.
        </div>
      </div>
    );
  }

  return (
    <div className="max-w-[1400px] mx-auto">
      <div className="mb-6">
        <h2 className="text-xl font-bold text-slate-100 mb-1">Backtest History</h2>
        <p className="text-sm text-slate-500">Click a row to view full results</p>
      </div>

      <div className="flex flex-col gap-2">
        {history.map(entry => {
          const isSelected = selectedId === entry.id;
          const runDuration = entry.completedAt
            ? Math.round((new Date(entry.completedAt).getTime() - new Date(entry.createdAt).getTime()) / 1000)
            : null;
          const daysBack = entry.parameters?.daysBack ?? 730;
          const rangeEnd = new Date(entry.completedAt ?? entry.createdAt);
          const rangeStart = new Date(rangeEnd.getTime() - daysBack * 86400_000);
          const fmtDate = (d: Date) => d.toLocaleDateString(undefined, { month: 'short', year: 'numeric' });
          const durationLabel = daysBack >= 365
            ? `${Math.round(daysBack / 365)}Y`
            : daysBack === 180 ? '6M'
            : daysBack === 14  ? '2W'
            : daysBack === 7   ? '1W'
            : daysBack <= 3    ? '1D'
            : `${daysBack}D`;

          return (
            <div key={entry.id} className="bg-slate-900 border border-slate-800 rounded-xl overflow-hidden">
              {/* Row header */}
              <button
                className={`w-full px-5 py-4 flex items-center gap-4 text-left hover:bg-slate-800 transition-colors duration-150 ${isSelected ? 'bg-slate-800' : ''}`}
                onClick={() => handleSelect(entry.id)}
              >
                {/* Date */}
                <div className="w-36 shrink-0">
                  <div className="text-xs font-semibold text-slate-100">
                    {new Date(entry.createdAt).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })}
                  </div>
                  <div className="text-[11px] text-slate-600 mt-0.5">
                    {new Date(entry.createdAt).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })}
                  </div>
                </div>

                {/* Params */}
                <div className="flex-1 min-w-0">
                  <div className="flex flex-wrap gap-1.5 mb-1.5">
                    {entry.parameters?.symbols?.map(s => (
                      <span key={s} className="bg-slate-700 text-slate-200 text-[11px] font-semibold px-1.5 py-0.5 rounded">{s}</span>
                    ))}
                    {entry.parameters?.timeframes?.map(tf => (
                      <span key={tf} className="bg-slate-800 border border-slate-700 text-slate-400 text-[11px] px-1.5 py-0.5 rounded">{tf.replace('Minute', 'M').replace('Hour', 'H').replace('One', '1').replace('Five', '5').replace('Fifteen', '15').replace('Four', '4')}</span>
                    ))}
                  </div>
                  <div className="text-[11px] text-slate-500">
                    <span className="font-semibold text-slate-400">{durationLabel}</span>
                    {' · '}
                    {fmtDate(rangeStart)} – {fmtDate(rangeEnd)}
                  </div>
                </div>

                {/* Signals */}
                <div className="text-right shrink-0 w-24">
                  <div className="text-sm font-semibold text-slate-100">{entry.totalSignalsAnalyzed.toLocaleString()}</div>
                  <div className="text-[11px] text-slate-600">signals</div>
                </div>

                {/* Best setup */}
                {entry.bestIndividual ? (
                  <div className="text-right shrink-0 w-40">
                    <div className="text-xs font-semibold text-green-500">{entry.bestIndividual.label}</div>
                    <div className="text-[11px] text-slate-600">{(entry.bestIndividual.winRate * 100).toFixed(1)}% WR · {entry.bestIndividual.expectedValue.toFixed(2)} EV</div>
                  </div>
                ) : (
                  <div className="w-40 shrink-0" />
                )}

                {/* Run duration */}
                <div className="text-right shrink-0 w-16">
                  {runDuration !== null && <div className="text-[11px] text-slate-600">{runDuration >= 60 ? `${Math.floor(runDuration/60)}m ${runDuration%60}s` : `${runDuration}s`}</div>}
                </div>

                {/* Chevron */}
                <div className="shrink-0 text-slate-600">
                  <svg className={`w-4 h-4 transition-transform duration-200 ${isSelected ? 'rotate-180' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
                  </svg>
                </div>
              </button>

              {/* Expanded result */}
              {isSelected && (
                <div className="border-t border-slate-800 p-5">
                  {loadedResult && loadedResult.parameters ? (
                    <BacktestResults result={loadedResult} strategies={strategies} onAddToStrategy={onAddToStrategy} />
                  ) : (
                    <div className="flex items-center gap-2 text-slate-500 text-sm py-4">
                      <span className="flex gap-0.5">
                        {[0,1,2].map(i => (
                          <span key={i} className="w-1 h-1 rounded-full bg-blue-500 animate-bounce" style={{ animationDelay: `${i*0.15}s` }} />
                        ))}
                      </span>
                      Loading results…
                    </div>
                  )}
                </div>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
