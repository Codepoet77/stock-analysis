import type { BacktestParameters, BacktestProgressUpdate, BacktestResult } from '../types/backtest';
import type { Strategy, AddRuleRequest } from '../types/strategy';
import { BacktestForm } from '../components/backtest/BacktestForm';
import { BacktestProgress } from '../components/backtest/BacktestProgress';
import { BacktestResults } from '../components/backtest/BacktestResults';

interface Props {
  connected: boolean;
  isRunning: boolean;
  progress: BacktestProgressUpdate | null;
  result: BacktestResult | null;
  error: string | null;
  onStart: (params: BacktestParameters) => void;
  onCancel: () => void;
  strategies: Strategy[];
  onAddToStrategy: (strategyId: string | null, newName: string | null, req: AddRuleRequest) => void;
}

export function BacktestPage({ connected, isRunning, progress, result, error, onStart, onCancel, strategies, onAddToStrategy }: Props) {
  return (
    <div className="max-w-[1400px] mx-auto">
      <div className="mb-6">
        <h2 className="text-xl font-bold text-slate-100 mb-1">Strategy Backtester</h2>
        <p className="text-sm text-slate-500">
          Walk-forward test of ICT signal types across timeframes and exit strategies using Polygon historical data
        </p>
      </div>

      {!connected && (
        <div className="bg-stone-950 border border-amber-900 rounded-lg px-4 py-3 mb-4 text-amber-300 text-sm">
          Not connected to backend — connect before running a backtest.
        </div>
      )}

      {error && (
        <div className="bg-red-950 border border-red-700 rounded-lg px-4 py-3 mb-4 text-red-300 text-sm">
          {error}
        </div>
      )}

      <BacktestForm onStart={onStart} onCancel={onCancel} isRunning={isRunning} />

      {(isRunning || progress) && (
        <div className="mt-5">
          <BacktestProgress progress={progress} />
        </div>
      )}

      {result && !isRunning && (
        <div className="mt-7">
          <BacktestResults result={result} strategies={strategies} onAddToStrategy={onAddToStrategy} />
        </div>
      )}
    </div>
  );
}
