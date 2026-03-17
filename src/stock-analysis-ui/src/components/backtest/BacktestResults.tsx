import { useState } from 'react';
import type { BacktestResult, BacktestStats, BacktestSignalOutcome } from '../../types/backtest';
import type { Strategy, AddRuleRequest } from '../../types/strategy';
import { AddToStrategyModal } from '../strategy/AddToStrategyModal';

interface Props {
  result: BacktestResult;
  strategies: Strategy[];
  onAddToStrategy: (strategyId: string | null, newName: string | null, req: AddRuleRequest) => void;
}

type Tab = 'summary' | 'individual' | 'combinations' | 'comparison' | 'signals';

export function BacktestResults({ result, strategies, onAddToStrategy }: Props) {
  const [tab, setTab] = useState<Tab>('summary');
  const [addTarget, setAddTarget] = useState<BacktestStats | null>(null);
  const duration = Math.round(
    (new Date(result.completedAt).getTime() - new Date(result.startedAt).getTime()) / 1000
  );

  return (
    <>
    <div className="bg-slate-900 border border-slate-800 rounded-xl overflow-hidden">
      {/* Header */}
      <div className="px-6 py-4 border-b border-slate-800 bg-card-dark flex justify-between items-center">
        <div>
          <h2 className="text-lg font-semibold text-slate-100">Backtest Results</h2>
          <p className="text-xs text-slate-600 mt-1">
            {result.totalSignalsAnalyzed.toLocaleString()} signals analysed &nbsp;·&nbsp;
            {result.parameters.symbols.join(', ')} &nbsp;·&nbsp;
            {fmtDuration(result.parameters.daysBack)} &nbsp;·&nbsp;
            {fmtSignalTypes(result.parameters.signalTypes)} &nbsp;·&nbsp;
            completed in {duration}s
          </p>
        </div>
        {result.bestIndividual && (
          <div className="text-right">
            <div className="text-[11px] text-slate-400 uppercase tracking-wide">Best Setup</div>
            <div className="text-green-500 text-base font-bold">
              {result.bestIndividual.label} · {(result.bestIndividual.winRate * 100).toFixed(1)}% WR
            </div>
          </div>
        )}
      </div>

      {/* Tabs */}
      <div className="flex border-b border-slate-800 bg-card-dark">
        {(['summary', 'individual', 'combinations', 'comparison', 'signals'] as Tab[]).map(t => (
          <button
            key={t}
            onClick={() => setTab(t)}
            className={`px-4 py-2.5 text-xs font-medium capitalize transition-colors duration-150 border-b-2
              ${tab === t
                ? 'text-slate-100 border-blue-600'
                : 'text-slate-600 border-transparent hover:text-slate-300'
              }`}
          >
            {t}
          </button>
        ))}
      </div>

      <div className="p-6">
        {tab === 'summary'      && <SummaryTab result={result} onAddRow={setAddTarget} />}
        {tab === 'individual'   && <StatsTable stats={result.individualStats} title="Individual Timeframes" onAddRow={setAddTarget} />}
        {tab === 'combinations' && <StatsTable stats={result.combinationStats} title="Timeframe Combinations" onAddRow={setAddTarget} />}
        {tab === 'comparison'   && <ComparisonTab result={result} />}
        {tab === 'signals'      && <SignalsTab signals={result.sampleSignals} />}
      </div>
    </div>

    {addTarget && (
      <AddToStrategyModal
        stats={addTarget}
        parameters={result.parameters}
        strategies={strategies}
        onAdd={(sid, name, req) => { onAddToStrategy(sid, name, req); setAddTarget(null); }}
        onClose={() => setAddTarget(null)}
      />
    )}
    </>
  );
}

// ── Summary ──────────────────────────────────────────────────────────────────

function SummaryTab({ result, onAddRow }: { result: BacktestResult; onAddRow: (s: BacktestStats) => void }) {
  const topIndividual   = [...result.individualStats].sort((a, b) => b.expectedValue - a.expectedValue).slice(0, 5);
  const topCombinations = [...result.combinationStats].sort((a, b) => b.expectedValue - a.expectedValue).slice(0, 5);

  return (
    <div className="grid grid-cols-2 gap-6">
      <div>
        <SectionHeader>Top Individual Timeframes</SectionHeader>
        <RankTable rows={topIndividual} onAddRow={onAddRow} />
      </div>
      <div>
        <SectionHeader>Top Combinations</SectionHeader>
        <RankTable rows={topCombinations} onAddRow={onAddRow} />
      </div>
    </div>
  );
}

function RankTable({ rows, onAddRow }: { rows: BacktestStats[]; onAddRow: (s: BacktestStats) => void }) {
  if (rows.length === 0) return <p className="text-slate-600 text-xs">No data</p>;

  return (
    <div className="flex flex-col gap-2">
      {rows.map((r, i) => (
        <div key={i} className="bg-slate-800 rounded-lg px-3.5 py-2.5 grid items-center gap-3"
          style={{ gridTemplateColumns: '24px 1fr auto auto auto auto' }}>
          <span className="text-slate-600 text-xs">#{i + 1}</span>
          <div>
            <div className="text-slate-100 font-semibold text-sm">{r.label}</div>
            <div className="text-slate-600 text-[11px]">{r.symbol} · {r.exitStrategy}</div>
          </div>
          <Stat label="WR" value={`${(r.winRate * 100).toFixed(1)}%`} cls={wrCls(r.winRate)} />
          <Stat label="Avg R:R" value={`${r.averageRR.toFixed(2)}R`} cls="text-slate-400" />
          <Stat label="EV" value={r.expectedValue.toFixed(2)} cls={r.expectedValue > 0 ? 'text-green-500' : 'text-red-500'} />
          <button
            onClick={() => onAddRow(r)}
            className="px-2 py-1 rounded text-[10px] font-semibold text-blue-400 border border-blue-800
              hover:bg-blue-900/50 hover:text-blue-300 transition-colors duration-150 whitespace-nowrap"
          >
            + Strategy
          </button>
        </div>
      ))}
    </div>
  );
}

// ── Stats Table ───────────────────────────────────────────────────────────────

function StatsTable({ stats, title, onAddRow }: { stats: BacktestStats[]; title: string; onAddRow: (s: BacktestStats) => void }) {
  if (stats.length === 0) return <p className="text-slate-600 text-sm">No data available.</p>;

  return (
    <div>
      <SectionHeader>{title}</SectionHeader>
      <div className="overflow-x-auto">
        <table className="w-full border-collapse text-xs">
          <thead>
            <tr className="text-slate-600 border-b border-slate-800">
              {['Timeframe','Symbol','Exit','Signals','Wins','Losses','Win %','Avg R:R','PF','EV',''].map(h => (
                <th key={h} className="px-2.5 py-2 text-left font-semibold text-[11px] uppercase whitespace-nowrap">{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {[...stats].sort((a, b) => b.expectedValue - a.expectedValue).map((s, i) => (
              <tr key={i} className={`border-b border-slate-800 ${i % 2 !== 0 ? 'bg-card-dark' : ''}`}>
                <td className="px-2.5 py-2"><strong className="text-slate-100">{s.label}</strong></td>
                <td className="px-2.5 py-2 text-slate-400">{s.symbol}</td>
                <td className="px-2.5 py-2"><Tag>{s.exitStrategy}</Tag></td>
                <td className="px-2.5 py-2 text-slate-400">{s.totalSignals.toLocaleString()}</td>
                <td className="px-2.5 py-2 text-green-500">{s.wins.toLocaleString()}</td>
                <td className="px-2.5 py-2 text-red-500">{s.losses.toLocaleString()}</td>
                <td className={`px-2.5 py-2 font-semibold ${wrCls(s.winRate)}`}>{(s.winRate * 100).toFixed(1)}%</td>
                <td className="px-2.5 py-2 text-slate-400">{s.averageRR.toFixed(2)}R</td>
                <td className={`px-2.5 py-2 ${s.profitFactor >= 1.5 ? 'text-green-500' : 'text-amber-400'}`}>{s.profitFactor.toFixed(2)}</td>
                <td className={`px-2.5 py-2 font-bold ${s.expectedValue > 0 ? 'text-green-500' : 'text-red-500'}`}>{s.expectedValue.toFixed(2)}</td>
                <td className="px-2.5 py-2">
                  <button
                    onClick={() => onAddRow(s)}
                    className="px-2 py-0.5 rounded text-[10px] font-semibold text-blue-400 border border-blue-800
                      hover:bg-blue-900/50 hover:text-blue-300 transition-colors duration-150 whitespace-nowrap"
                  >
                    + Strategy
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

// ── Comparison ────────────────────────────────────────────────────────────────

function ComparisonTab({ result }: { result: BacktestResult }) {
  const all   = [...result.individualStats, ...result.combinationStats];
  const fixed = all.filter(s => s.exitStrategy === 'FixedR');
  const liq   = all.filter(s => s.exitStrategy === 'NextLiquidity');

  const avg = (arr: BacktestStats[], key: keyof BacktestStats) =>
    arr.length ? arr.reduce((sum, s) => sum + (s[key] as number), 0) / arr.length : 0;

  return (
    <div>
      <SectionHeader>Fixed R vs Next Liquidity Level</SectionHeader>
      <div className="grid grid-cols-2 gap-4 mb-6">
        <CompCard title="Fixed R"        color="border-blue-700"   avgWR={avg(fixed,'winRate')} avgRR={avg(fixed,'averageRR')} avgPF={avg(fixed,'profitFactor')} avgEV={avg(fixed,'expectedValue')} />
        <CompCard title="Next Liquidity" color="border-violet-500" avgWR={avg(liq,  'winRate')} avgRR={avg(liq,  'averageRR')} avgPF={avg(liq,  'profitFactor')} avgEV={avg(liq,  'expectedValue')} />
      </div>

      <SectionHeader>Signal Type Breakdown</SectionHeader>
      <SignalTypeBreakdown stats={all} />
    </div>
  );
}

function CompCard({ title, color, avgWR, avgRR, avgPF, avgEV }:
  { title: string; color: string; avgWR: number; avgRR: number; avgPF: number; avgEV: number }) {
  return (
    <div className={`bg-slate-800 rounded-xl p-4 border-t-[3px] ${color}`}>
      <h3 className="text-sm font-semibold text-slate-100 mb-3.5">{title}</h3>
      <div className="grid grid-cols-2 gap-2.5">
        <MetricBox label="Avg Win Rate"   value={`${(avgWR*100).toFixed(1)}%`} cls={avgWR>=0.5?'text-green-500':'text-amber-400'} />
        <MetricBox label="Avg R:R"        value={`${avgRR.toFixed(2)}R`}       cls="text-slate-400" />
        <MetricBox label="Profit Factor"  value={avgPF.toFixed(2)}             cls={avgPF>=1.5?'text-green-500':'text-amber-400'} />
        <MetricBox label="Expected Value" value={avgEV.toFixed(2)}             cls={avgEV>0?'text-green-500':'text-red-500'} />
      </div>
    </div>
  );
}

function MetricBox({ label, value, cls }: { label: string; value: string; cls: string }) {
  return (
    <div className="bg-slate-900 rounded-lg px-2.5 py-2">
      <div className="text-[10px] text-slate-600 uppercase tracking-wide mb-0.5">{label}</div>
      <div className={`text-lg font-bold ${cls}`}>{value}</div>
    </div>
  );
}

function SignalTypeBreakdown({ stats }: { stats: BacktestStats[] }) {
  const types = ['OBRetest', 'FVGFill', 'LiquiditySweep', 'StructureBreak'];
  return (
    <div className="overflow-x-auto">
      <table className="w-full border-collapse text-xs">
        <thead>
          <tr className="text-slate-600 border-b border-slate-800">
            {['Signal Type','Total','Win %','Avg R:R'].map(h => (
              <th key={h} className="px-2.5 py-2 text-left font-semibold text-[11px] uppercase">{h}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {types.map(type => {
            const all = stats.flatMap(s => s.bySignalType[type] ? [s.bySignalType[type]] : []);
            const total = all.reduce((s, t) => s + t.total, 0);
            const wins  = all.reduce((s, t) => s + t.wins, 0);
            const wr    = total > 0 ? wins / total : 0;
            const avgRR = all.length > 0 ? all.reduce((s, t) => s + t.averageRR, 0) / all.length : 0;
            return (
              <tr key={type} className="border-b border-slate-800">
                <td className="px-2.5 py-2 font-semibold text-slate-100">{type}</td>
                <td className="px-2.5 py-2 text-slate-400">{total.toLocaleString()}</td>
                <td className={`px-2.5 py-2 font-semibold ${wrCls(wr)}`}>{(wr*100).toFixed(1)}%</td>
                <td className="px-2.5 py-2 text-slate-400">{avgRR.toFixed(2)}R</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

// ── Signals ───────────────────────────────────────────────────────────────────

function SignalsTab({ signals }: { signals: BacktestSignalOutcome[] }) {
  const [filter, setFilter] = useState<'All' | 'Win' | 'Loss'>('All');
  const filtered = signals.filter(s => filter === 'All' || s.outcome === filter);

  return (
    <div>
      <div className="flex gap-2 mb-4 items-center">
        {(['All', 'Win', 'Loss'] as const).map(f => (
          <button
            key={f}
            onClick={() => setFilter(f)}
            className={`px-3.5 py-1 rounded-lg text-xs font-semibold transition-all duration-150 active:scale-95
              ${filter === f
                ? 'bg-blue-700 text-white hover:bg-blue-600'
                : 'bg-slate-800 text-slate-400 hover:bg-slate-700 hover:text-slate-200'
              }`}
          >
            {f}
          </button>
        ))}
        <span className="text-xs text-slate-600 ml-2">Showing last 500 signals</span>
      </div>

      <div className="overflow-x-auto max-h-[500px] overflow-y-auto">
        <table className="w-full border-collapse text-xs">
          <thead className="sticky top-0 bg-slate-900">
            <tr className="text-slate-600 border-b border-slate-800">
              {['Symbol','TF','Combo','Type','Dir','Exit','Entry','Target','Stop','R:R','Bars','KZ','Outcome'].map(h => (
                <th key={h} className="px-2.5 py-2 text-left font-semibold text-[11px] uppercase whitespace-nowrap">{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {filtered.slice(0, 200).map((s, i) => (
              <tr key={i} className={`border-b border-slate-900
                ${s.outcome === 'Win' ? 'bg-green-950/40' : s.outcome === 'Loss' ? 'bg-red-950/40' : ''}`}>
                <td className="px-2.5 py-1.5 text-slate-400">{s.symbol}</td>
                <td className="px-2.5 py-1.5 text-slate-400">{s.timeframeLabel}</td>
                <td className="px-2.5 py-1.5 text-slate-600">{s.combinationLabel ?? '—'}</td>
                <td className="px-2.5 py-1.5 text-slate-400">{s.signalType}</td>
                <td className={`px-2.5 py-1.5 font-semibold ${s.direction === 'Long' ? 'text-green-500' : 'text-red-500'}`}>{s.direction}</td>
                <td className="px-2.5 py-1.5 text-slate-600">{s.exitStrategy}</td>
                <td className="px-2.5 py-1.5 text-slate-400">${s.entryPrice.toFixed(2)}</td>
                <td className="px-2.5 py-1.5 text-slate-400">${s.target.toFixed(2)}</td>
                <td className="px-2.5 py-1.5 text-slate-400">${s.invalidation.toFixed(2)}</td>
                <td className={`px-2.5 py-1.5 font-semibold ${s.actualRR > 0 ? 'text-green-500' : 'text-red-500'}`}>{s.actualRR.toFixed(2)}R</td>
                <td className="px-2.5 py-1.5 text-slate-400">{s.barsToOutcome}</td>
                <td className={`px-2.5 py-1.5 ${s.duringKillZone ? 'text-amber-400' : 'text-slate-700'}`}>{s.duringKillZone ? '✓' : '—'}</td>
                <td className={`px-2.5 py-1.5 font-bold ${s.outcome === 'Win' ? 'text-green-500' : s.outcome === 'Loss' ? 'text-red-500' : 'text-slate-400'}`}>{s.outcome}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function SectionHeader({ children }: { children: React.ReactNode }) {
  return (
    <h3 className="text-[11px] font-semibold text-slate-400 uppercase tracking-wider mb-3">{children}</h3>
  );
}

function Stat({ label, value, cls }: { label: string; value: string; cls: string }) {
  return (
    <div className="text-center">
      <div className="text-[10px] text-slate-600">{label}</div>
      <div className={`text-xs font-bold ${cls}`}>{value}</div>
    </div>
  );
}

function Tag({ children }: { children: React.ReactNode }) {
  return (
    <span className="bg-slate-800 border border-slate-700 rounded px-1.5 py-0.5 text-[11px] text-slate-400">
      {children}
    </span>
  );
}

const wrCls = (wr: number) => wr >= 0.5 ? 'text-green-500' : wr >= 0.4 ? 'text-amber-400' : 'text-red-500';

const SIGNAL_LABELS: Record<string, string> = {
  OBRetest: 'OB Retest',
  FVGFill: 'FVG Fill',
  LiquiditySweep: 'Liq. Sweep',
  StructureBreak: 'Structure Break',
};

function fmtSignalTypes(types: string[] | null): string {
  if (!types || types.length === 0) return 'All signals';
  return types.map(t => SIGNAL_LABELS[t] ?? t).join(', ');
}

function fmtDuration(daysBack: number): string {
  if (daysBack === 1) return '1 day';
  if (daysBack <= 3) return 'last trading day';
  if (daysBack === 7) return '1 week';
  if (daysBack === 14) return '2 weeks';
  if (daysBack === 180) return '6 months';
  if (daysBack >= 365) {
    const y = Math.round(daysBack / 365);
    return `${y} year${y !== 1 ? 's' : ''}`;
  }
  return `${daysBack} days`;
}
