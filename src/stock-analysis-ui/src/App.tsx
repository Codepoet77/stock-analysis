import { useEffect, useRef } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { useAnalysisHub } from './hooks/useAnalysisHub';
import { useAuth } from './hooks/useAuth';
import { SymbolCard } from './components/SymbolCard';
import { BacktestPage } from './pages/BacktestPage';
import { HistoryPage } from './pages/HistoryPage';
import { StrategiesPage } from './pages/StrategiesPage';
import { LoginPage } from './components/LoginPage';
import type { AddRuleRequest } from './types/strategy';

type Page = 'dashboard' | 'backtest' | 'history' | 'strategies';

const SYMBOLS = ['SPY', 'QQQ'];

export default function App() {
  const { user, loading, login, logout } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const page: Page = location.pathname === '/backtest' ? 'backtest'
    : location.pathname === '/history' ? 'history'
    : location.pathname === '/strategies' ? 'strategies'
    : 'dashboard';
  const setPage = (p: Page) => navigate(
    p === 'dashboard' ? '/' : p === 'backtest' ? '/backtest' : p === 'history' ? '/history' : '/strategies'
  );

  const {
    connected, results, error, requestAnalysis, requestBoth,
    backtestRunning, backtestProgress, backtestResult, backtestError,
    startBacktest, cancelBacktest,
    backtestHistory, loadedResult,
    getBacktestHistory, loadBacktestResult, clearLoadedResult,
    strategies, createStrategy, addRule, removeRule,
    toggleStrategy, deleteStrategy, subscribeToStrategy,
  } = useAnalysisHub();

  // Refresh history when navigating to the History tab
  useEffect(() => {
    if (page === 'history') getBacktestHistory();
  }, [page]); // eslint-disable-line react-hooks/exhaustive-deps

  // Pending rule: create strategy then auto-add rule when StrategyCreated fires
  const pendingRuleRef = useRef<{ name: string; req: AddRuleRequest } | null>(null);
  const prevStrategiesRef = useRef(strategies);

  useEffect(() => {
    const prev = prevStrategiesRef.current;
    const pending = pendingRuleRef.current;
    if (pending && strategies.length > prev.length) {
      // Find the newly created strategy (matches name + is newest)
      const newStrategy = strategies.find(s => s.name === pending.name && !prev.some(p => p.id === s.id));
      if (newStrategy) {
        addRule(newStrategy.id, pending.req);
        pendingRuleRef.current = null;
      }
    }
    prevStrategiesRef.current = strategies;
  }, [strategies, addRule]);

  function handleAddToStrategy(strategyId: string | null, newName: string | null, req: AddRuleRequest) {
    if (strategyId) {
      addRule(strategyId, req);
    } else if (newName) {
      pendingRuleRef.current = { name: newName, req };
      createStrategy(newName);
    }
  }

  if (loading) return (
    <div className="min-h-screen bg-app flex items-center justify-center text-slate-500 text-sm">
      Checking authentication…
    </div>
  );

  if (!user) return <LoginPage onLogin={login} />;

  const activeStrategiesCount = strategies.filter(s => s.isActive).length;

  return (
    <div className="min-h-screen bg-app text-slate-100 p-6">
      {/* Header */}
      <div className="flex justify-between items-center mb-6">
        <div className="flex items-center gap-8">
          <div>
            <h1 className="text-xl font-bold text-slate-100">ICT Analysis</h1>
            <p className="text-xs text-slate-500 mt-0.5">SPY & QQQ</p>
          </div>

          {/* Nav tabs */}
          <nav className="flex gap-1">
            {(['dashboard', 'backtest', 'history', 'strategies'] as Page[]).map(p => (
              <button
                key={p}
                onClick={() => setPage(p)}
                className={`px-4 py-1.5 rounded-lg text-sm font-semibold transition-colors duration-150
                  ${page === p
                    ? 'bg-blue-700 text-white'
                    : 'bg-transparent text-slate-500 hover:text-slate-300 hover:bg-slate-800'
                  }`}
              >
                {p === 'dashboard' ? 'Dashboard'
                  : p === 'backtest' ? 'Backtester'
                  : p === 'history' ? 'History'
                  : (
                    <span className="flex items-center gap-1.5">
                      Strategies
                      {activeStrategiesCount > 0 && (
                        <span className="bg-green-700 text-white text-[10px] font-bold rounded-full px-1.5 py-0.5 leading-none">
                          {activeStrategiesCount}
                        </span>
                      )}
                    </span>
                  )}
              </button>
            ))}
          </nav>
        </div>

        <div className="flex items-center gap-3">
          {/* Connection status */}
          <div className="flex items-center gap-1.5">
            <span className={`w-2 h-2 rounded-full inline-block ${connected ? 'bg-green-500' : 'bg-red-500'}`} />
            <span className="text-xs text-slate-400">{connected ? 'Connected' : 'Disconnected'}</span>
          </div>

          {/* Analyze Both (dashboard only) */}
          {page === 'dashboard' && (
            <button
              onClick={requestBoth}
              disabled={!connected}
              className="px-4 py-2 rounded-lg text-sm font-semibold transition-all duration-150
                bg-blue-700 text-white hover:bg-blue-600 active:bg-blue-800 active:scale-95
                disabled:bg-slate-800 disabled:text-slate-500 disabled:cursor-not-allowed disabled:scale-100"
            >
              Analyze Both
            </button>
          )}

          {/* User */}
          <div className="flex items-center gap-2.5 ml-2 pl-3 border-l border-slate-800">
            {user.picture && (
              <img src={user.picture} alt={user.name} className="w-7 h-7 rounded-full" />
            )}
            <span className="text-xs text-slate-400">{user.name}</span>
            <button
              onClick={logout}
              className="px-2.5 py-1 rounded-md text-xs text-slate-500 border border-slate-700
                hover:border-slate-500 hover:text-slate-300 transition-colors duration-150 active:scale-95"
            >
              Sign out
            </button>
          </div>
        </div>
      </div>

      {/* Error banner */}
      {error && (
        <div className="bg-red-950 border border-red-700 rounded-lg px-4 py-3 mb-4 text-red-300 text-sm">
          {error}
        </div>
      )}

      {/* Dashboard */}
      {page === 'dashboard' && (
        <div className="grid gap-6" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(600px, 1fr))' }}>
          {SYMBOLS.map(sym => {
            const result = results[sym];
            return result ? (
              <SymbolCard key={sym} result={result} onRefresh={() => requestAnalysis(sym)} />
            ) : (
              <div key={sym} className="bg-slate-900 border border-slate-800 rounded-xl p-10 flex items-center justify-center text-slate-500 text-sm min-h-[200px]">
                {connected ? `Loading ${sym}...` : 'Waiting for connection...'}
              </div>
            );
          })}
        </div>
      )}

      {/* Backtest */}
      {page === 'backtest' && (
        <BacktestPage
          connected={connected}
          isRunning={backtestRunning}
          progress={backtestProgress}
          result={backtestResult}
          error={backtestError}
          onStart={startBacktest}
          onCancel={cancelBacktest}
          strategies={strategies}
          onAddToStrategy={handleAddToStrategy}
        />
      )}

      {/* History */}
      {page === 'history' && (
        <HistoryPage
          history={backtestHistory}
          loadedResult={loadedResult}
          onLoad={loadBacktestResult}
          onClear={clearLoadedResult}
          strategies={strategies}
          onAddToStrategy={handleAddToStrategy}
        />
      )}

      {/* Strategies */}
      {page === 'strategies' && (
        <StrategiesPage
          strategies={strategies}
          onToggle={toggleStrategy}
          onDelete={deleteStrategy}
          onRemoveRule={removeRule}
          onSubscribe={subscribeToStrategy}
        />
      )}
    </div>
  );
}
