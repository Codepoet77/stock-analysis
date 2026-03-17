import type { AnalysisResult } from '../types/analysis';
import { CandlestickChart } from './CandlestickChart';
import { AnalysisPanel } from './AnalysisPanel';
import { MarketStatusBadge } from './MarketStatusBadge';

interface Props {
  result: AnalysisResult;
  onRefresh: () => void;
}

export function SymbolCard({ result, onRefresh }: Props) {
  const analyzedAt = new Date(result.analyzedAt).toLocaleTimeString('en-US', {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });

  return (
    <div className="bg-slate-900 border border-slate-800 rounded-xl overflow-hidden flex flex-col">
      {/* Header */}
      <div className="px-5 py-4 border-b border-slate-800 bg-card-dark flex items-center justify-between">
        <div className="flex items-center gap-4">
          <h2 className="text-xl font-bold text-slate-100">{result.symbol}</h2>
          <MarketStatusBadge session={result.session} isKillZone={result.isNyKillZone} />
        </div>
        <div className="flex items-center gap-3">
          <span className="text-xs text-slate-600">Updated {analyzedAt}</span>
          <button
            onClick={onRefresh}
            className="px-3.5 py-1.5 bg-blue-700 text-white rounded-lg text-xs font-semibold
              hover:bg-blue-600 active:bg-blue-800 active:scale-95 transition-all duration-150"
          >
            Refresh
          </button>
        </div>
      </div>

      {/* Chart */}
      <div className="px-5 py-4 border-b border-slate-800">
        <CandlestickChart result={result} />
      </div>

      {/* Analysis */}
      <div className="px-5 py-4">
        <AnalysisPanel result={result} />
      </div>
    </div>
  );
}
