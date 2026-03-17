import { useEffect, useRef, useState } from 'react';
import type { BacktestProgressUpdate } from '../../types/backtest';

interface Props {
  progress: BacktestProgressUpdate | null;
}

export function BacktestProgress({ progress }: Props) {
  const pct = progress ? Math.min(100, Math.max(0, progress.percentComplete)) : 0;
  const [elapsed, setElapsed] = useState(0);
  const startRef = useRef(Date.now());

  // Reset timer when progress first appears
  useEffect(() => {
    if (progress) startRef.current = Date.now();
    setElapsed(0);
  }, [!!progress]);

  // Tick every second while running
  useEffect(() => {
    if (!progress || pct >= 100) return;
    const id = setInterval(() => setElapsed(Math.floor((Date.now() - startRef.current) / 1000)), 1000);
    return () => clearInterval(id);
  }, [!!progress, pct]);

  const elapsedStr = elapsed > 0
    ? elapsed >= 60 ? `${Math.floor(elapsed / 60)}m ${elapsed % 60}s` : `${elapsed}s`
    : null;

  return (
    <div className="bg-slate-900 border border-slate-800 rounded-xl px-6 py-5">
      <div className="flex justify-between items-center mb-2.5">
        <div>
          <div className="flex items-center gap-2">
            <span className="text-sm font-semibold text-slate-100">
              {progress?.stage ?? 'Starting…'}
            </span>
            {pct < 100 && (
              <span className="flex gap-0.5">
                {[0, 1, 2].map(i => (
                  <span
                    key={i}
                    className="w-1 h-1 rounded-full bg-blue-500 animate-bounce"
                    style={{ animationDelay: `${i * 0.15}s` }}
                  />
                ))}
              </span>
            )}
          </div>
          <p className="text-xs text-slate-500 mt-0.5">{progress?.detail ?? ''}</p>
        </div>
        <div className="text-right">
          <div className="text-sm text-slate-400">{pct}%</div>
          {elapsedStr && <div className="text-xs text-slate-600 mt-0.5">{elapsedStr}</div>}
        </div>
      </div>

      <div className="bg-slate-800 rounded h-2 overflow-hidden">
        <div
          className={`h-full rounded transition-all duration-300 ${pct === 100 ? 'bg-green-500' : 'bg-blue-700'}`}
          style={{ width: `${pct}%` }}
        />
      </div>
    </div>
  );
}
