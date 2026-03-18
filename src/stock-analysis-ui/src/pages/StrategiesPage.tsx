import { useState, useEffect } from 'react';
import type { Strategy, LiveSignal, LiveSignalStatus, AddRuleRequest } from '../types/strategy';
import { CreateStrategyModal } from '../components/strategy/CreateStrategyModal';

interface Props {
  strategies: Strategy[];
  onToggle: (strategyId: string, isActive: boolean) => void;
  onDelete: (strategyId: string) => void;
  onRemoveRule: (ruleId: string) => void;
  onSubscribe: (strategyId: string) => void;
  onAddToStrategy: (strategyId: string | null, newName: string | null, req: AddRuleRequest) => void;
}

export function StrategiesPage({ strategies, onToggle, onDelete, onRemoveRule, onSubscribe, onAddToStrategy }: Props) {
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null);
  const [showCreateModal, setShowCreateModal] = useState(false);

  // Subscribe to strategy groups when expanded
  useEffect(() => {
    if (expandedId) onSubscribe(expandedId);
  }, [expandedId, onSubscribe]);

  if (strategies.length === 0) {
    return (
      <div className="max-w-[1400px] mx-auto">
        <PageHeader onNew={() => setShowCreateModal(true)} />
        <div className="bg-slate-900 border border-slate-800 rounded-xl px-6 py-16 text-center">
          <p className="text-slate-500 text-sm mb-1">No strategies yet.</p>
          <p className="text-slate-600 text-xs">
            Click <span className="text-blue-400">New Strategy</span> above or create one from the Backtester results.
          </p>
        </div>
        {showCreateModal && (
          <CreateStrategyModal
            onAdd={(sid, name, req) => { onAddToStrategy(sid, name, req); setShowCreateModal(false); }}
            onClose={() => setShowCreateModal(false)}
          />
        )}
      </div>
    );
  }

  function handleDelete(id: string) {
    if (confirmDelete === id) {
      onDelete(id);
      setConfirmDelete(null);
      if (expandedId === id) setExpandedId(null);
    } else {
      setConfirmDelete(id);
    }
  }

  return (
    <div className="max-w-[1400px] mx-auto">
      <PageHeader onNew={() => setShowCreateModal(true)} />

      {showCreateModal && (
        <CreateStrategyModal
          onAdd={(sid, name, req) => { onAddToStrategy(sid, name, req); setShowCreateModal(false); }}
          onClose={() => setShowCreateModal(false)}
        />
      )}

      <div className="flex flex-col gap-3">
        {strategies.map(s => {
          const isExpanded = expandedId === s.id;
          const perf = s.performance;
          const openCount = s.recentSignals.filter(sig => sig.status === 'Open').length;

          return (
            <div key={s.id} className="bg-slate-900 border border-slate-800 rounded-xl overflow-hidden">
              {/* Strategy header row */}
              <div
                className="flex items-center gap-3 px-5 py-3.5 cursor-pointer hover:bg-slate-800/50 transition-colors duration-150"
                onClick={() => setExpandedId(isExpanded ? null : s.id)}
              >
                {/* Toggle active */}
                <button
                  onClick={e => { e.stopPropagation(); onToggle(s.id, !s.isActive); }}
                  className={`w-8 h-4 rounded-full transition-colors duration-200 flex-shrink-0 relative
                    ${s.isActive ? 'bg-green-700' : 'bg-slate-700'}`}
                  title={s.isActive ? 'Active — click to pause' : 'Paused — click to activate'}
                >
                  <span className={`absolute top-0.5 w-3 h-3 rounded-full bg-white transition-all duration-200
                    ${s.isActive ? 'left-4' : 'left-0.5'}`} />
                </button>

                {/* Name */}
                <div className="flex-1 min-w-0">
                  <span className="text-slate-100 font-semibold text-sm">{s.name}</span>
                  <span className="ml-2 text-slate-600 text-xs">{s.rules.length} rule{s.rules.length !== 1 ? 's' : ''}</span>
                  {openCount > 0 && (
                    <span className="ml-2 bg-amber-900/60 border border-amber-700/50 text-amber-300 text-[10px] font-semibold
                      rounded-full px-2 py-0.5">
                      {openCount} open
                    </span>
                  )}
                </div>

                {/* Performance summary */}
                {perf.totalSignals > 0 && (
                  <div className="flex gap-4 items-center">
                    <PerfStat label="Signals" value={perf.totalSignals.toString()} cls="text-slate-400" />
                    <PerfStat label="Win %" value={`${(perf.winRate * 100).toFixed(1)}%`} cls={perf.winRate >= 0.5 ? 'text-green-500' : 'text-amber-400'} />
                    <PerfStat label="Avg R:R" value={`${perf.averageRR.toFixed(2)}R`} cls="text-slate-400" />
                    <PerfStat label="EV" value={perf.expectedValue.toFixed(2)} cls={perf.expectedValue > 0 ? 'text-green-500' : 'text-red-500'} />
                    <PerfStat label="PF" value={perf.profitFactor >= 999 ? '∞' : perf.profitFactor.toFixed(2)} cls={perf.profitFactor >= 1.5 ? 'text-green-500' : 'text-amber-400'} />
                  </div>
                )}

                {/* Delete */}
                <button
                  onClick={e => { e.stopPropagation(); handleDelete(s.id); }}
                  className={`px-2.5 py-1 rounded text-[11px] font-semibold transition-colors duration-150
                    ${confirmDelete === s.id
                      ? 'bg-red-700 text-white hover:bg-red-600'
                      : 'text-slate-600 hover:text-red-400 hover:bg-red-950/30'
                    }`}
                >
                  {confirmDelete === s.id ? 'Confirm?' : 'Delete'}
                </button>

                {/* Chevron */}
                <span className={`text-slate-600 text-xs transition-transform duration-150 ${isExpanded ? 'rotate-180' : ''}`}>▼</span>
              </div>

              {/* Expanded detail */}
              {isExpanded && (
                <div className="border-t border-slate-800 px-5 py-4 space-y-5">
                  {/* Rules */}
                  {s.rules.length > 0 && (
                    <div>
                      <SectionLabel>Rules</SectionLabel>
                      <div className="flex flex-col gap-1.5">
                        {s.rules.map(r => (
                          <div key={r.id} className="bg-slate-800 rounded-lg px-3.5 py-2 flex items-center gap-3">
                            <div className="flex-1">
                              <span className="text-slate-100 text-sm font-semibold">{r.label}</span>
                              <span className="ml-2 text-slate-500 text-xs">{r.symbol} · {r.exitStrategy} · {r.direction}</span>
                              {r.killZoneOnly && <span className="ml-2 text-amber-500 text-[10px] font-semibold">KZ</span>}
                              {r.tradingWindowStart && r.tradingWindowEnd && (
                                <span className="ml-2 text-slate-500 text-[10px]">
                                  {fmtWindow(r.tradingWindowStart)}–{fmtWindow(r.tradingWindowEnd)} ET
                                </span>
                              )}
                            </div>
                            <div className="flex gap-1 flex-wrap">
                              {r.signalTypes.map(t => (
                                <span key={t} className="bg-slate-700 rounded px-1.5 py-0.5 text-[10px] text-slate-400">{t}</span>
                              ))}
                            </div>
                            <button
                              onClick={() => onRemoveRule(r.id)}
                              className="text-slate-600 hover:text-red-400 text-xs transition-colors duration-150 ml-1"
                            >
                              ✕
                            </button>
                          </div>
                        ))}
                      </div>
                    </div>
                  )}

                  {/* Performance detail */}
                  {perf.totalSignals > 0 && (
                    <div>
                      <SectionLabel>Performance</SectionLabel>
                      <div className="grid grid-cols-4 gap-2 mb-3">
                        <MetricCard label="Total Signals" value={perf.totalSignals.toString()} cls="text-slate-100" />
                        <MetricCard label="Win Rate" value={`${(perf.winRate * 100).toFixed(1)}%`} cls={perf.winRate >= 0.5 ? 'text-green-500' : 'text-amber-400'} />
                        <MetricCard label="Avg R:R" value={`${perf.averageRR.toFixed(2)}R`} cls="text-slate-400" />
                        <MetricCard label="Profit Factor" value={perf.profitFactor >= 999 ? '∞' : perf.profitFactor.toFixed(2)} cls={perf.profitFactor >= 1.5 ? 'text-green-500' : 'text-amber-400'} />
                        <MetricCard label="Expected Value" value={perf.expectedValue.toFixed(2)} cls={perf.expectedValue > 0 ? 'text-green-500' : 'text-red-500'} />
                        <MetricCard label="Wins" value={perf.wins.toString()} cls="text-green-500" />
                        <MetricCard label="Losses" value={perf.losses.toString()} cls="text-red-500" />
                        <MetricCard label="Scratches" value={perf.scratches.toString()} cls="text-slate-400" />
                      </div>
                    </div>
                  )}

                  {/* Recent signals */}
                  {s.recentSignals.length > 0 && (
                    <div>
                      <SectionLabel>Recent Signals</SectionLabel>
                      <SignalTable signals={s.recentSignals} />
                    </div>
                  )}

                  {s.rules.length === 0 && s.recentSignals.length === 0 && (
                    <p className="text-slate-600 text-xs">No rules added yet. Add a rule from the Backtester results.</p>
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

// ── Signal table ──────────────────────────────────────────────────────────────

function SignalTable({ signals }: { signals: LiveSignal[] }) {
  return (
    <div className="overflow-x-auto max-h-[400px] overflow-y-auto">
      <table className="w-full border-collapse text-xs">
        <thead className="sticky top-0 bg-slate-900">
          <tr className="text-slate-600 border-b border-slate-800">
            {['Rule','Symbol','Dir','Type','Entry Time','Exit Time','Entry','Target','Stop','Exit','Status','R:R'].map(h => (
              <th key={h} className="px-2.5 py-2 text-left font-semibold text-[11px] uppercase whitespace-nowrap">{h}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {signals.map(sig => (
            <tr key={sig.id} className={`border-b border-slate-900
              ${sig.status === 'Win' ? 'bg-green-950/40'
                : sig.status === 'Loss' ? 'bg-red-950/40'
                : sig.status === 'Open' ? 'bg-amber-950/20'
                : ''}`}>
              <td className="px-2.5 py-1.5 text-slate-400 whitespace-nowrap">{sig.ruleLabel}</td>
              <td className="px-2.5 py-1.5 text-slate-400">{sig.symbol}</td>
              <td className={`px-2.5 py-1.5 font-semibold ${sig.direction === 'Long' ? 'text-green-500' : 'text-red-500'}`}>{sig.direction}</td>
              <td className="px-2.5 py-1.5 text-slate-500">{sig.signalType}</td>
              <td className="px-2.5 py-1.5 text-slate-600 whitespace-nowrap">{fmtTime(sig.entryTime)}</td>
              <td className="px-2.5 py-1.5 text-slate-600 whitespace-nowrap">{sig.outcomeTime ? fmtTime(sig.outcomeTime) : '—'}</td>
              <td className="px-2.5 py-1.5 text-slate-400">${sig.entryPrice.toFixed(2)}</td>
              <td className="px-2.5 py-1.5 text-slate-400">${sig.target.toFixed(2)}</td>
              <td className="px-2.5 py-1.5 text-slate-500">${sig.currentStop.toFixed(2)}</td>
              <td className="px-2.5 py-1.5 text-slate-600">{sig.exitStrategy}</td>
              <td className="px-2.5 py-1.5">
                <StatusBadge status={sig.status} />
              </td>
              <td className={`px-2.5 py-1.5 font-semibold ${sig.actualRR !== null ? (sig.actualRR > 0 ? 'text-green-500' : 'text-red-500') : 'text-slate-600'}`}>
                {sig.actualRR !== null ? `${sig.actualRR.toFixed(2)}R` : '—'}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function StatusBadge({ status }: { status: LiveSignalStatus }) {
  const cls = status === 'Open' ? 'bg-amber-900/60 border-amber-700/50 text-amber-300'
    : status === 'Win' ? 'bg-green-900/60 border-green-700/50 text-green-300'
    : status === 'Loss' ? 'bg-red-900/60 border-red-700/50 text-red-300'
    : 'bg-slate-800 border-slate-700 text-slate-400';
  return (
    <span className={`border rounded-full px-2 py-0.5 text-[10px] font-semibold ${cls}`}>{status}</span>
  );
}

// ── Small helpers ─────────────────────────────────────────────────────────────

function PageHeader({ onNew }: { onNew: () => void }) {
  return (
    <div className="mb-6 flex justify-between items-center">
      <div>
        <h2 className="text-xl font-bold text-slate-100 mb-1">Live Strategies</h2>
        <p className="text-sm text-slate-500">Active signal detectors running against real-time market data</p>
      </div>
      <button
        onClick={onNew}
        className="px-4 py-2 rounded-lg text-sm font-semibold text-white bg-blue-700 hover:bg-blue-600
          active:bg-blue-800 active:scale-95 transition-all duration-150"
      >
        New Strategy
      </button>
    </div>
  );
}

function SectionLabel({ children }: { children: React.ReactNode }) {
  return <h4 className="text-[11px] font-semibold text-slate-500 uppercase tracking-wider mb-2">{children}</h4>;
}

function PerfStat({ label, value, cls }: { label: string; value: string; cls: string }) {
  return (
    <div className="text-center">
      <div className="text-[10px] text-slate-600">{label}</div>
      <div className={`text-xs font-bold ${cls}`}>{value}</div>
    </div>
  );
}

function MetricCard({ label, value, cls }: { label: string; value: string; cls: string }) {
  return (
    <div className="bg-slate-800 rounded-lg px-3 py-2">
      <div className="text-[10px] text-slate-600 uppercase tracking-wide mb-0.5">{label}</div>
      <div className={`text-base font-bold ${cls}`}>{value}</div>
    </div>
  );
}

// "09:30:00" → "9:30"
function fmtWindow(ts: string) {
  const [h, m] = ts.split(':');
  return `${parseInt(h)}:${m}`;
}

function fmtTime(iso: string) {
  const d = new Date(iso);
  return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' }) + ' ' +
    d.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', hour12: false });
}
