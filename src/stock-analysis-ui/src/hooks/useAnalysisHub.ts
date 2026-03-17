import { useEffect, useRef, useState, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import type { AnalysisResult, Bar } from '../types/analysis';
import type { BacktestParameters, BacktestProgressUpdate, BacktestResult, BacktestHistoryEntry } from '../types/backtest';
import type { Strategy, LiveSignal, AddRuleRequest } from '../types/strategy';

const HUB_URL = `${import.meta.env.VITE_API_URL || ''}/hubs/analysis`;

export function useAnalysisHub() {
  const connectionRef = useRef<signalR.HubConnection | null>(null);
  const [connected, setConnected] = useState(false);
  const [results, setResults] = useState<Record<string, AnalysisResult>>({});
  const [liveBar, setLiveBar] = useState<Bar | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Strategy state
  const [strategies, setStrategies] = useState<Strategy[]>([]);

  // Backtest state
  const [backtestProgress, setBacktestProgress] = useState<BacktestProgressUpdate | null>(null);
  const [backtestResult, setBacktestResult] = useState<BacktestResult | null>(null);
  const [backtestError, setBacktestError] = useState<string | null>(null);
  const [backtestRunning, setBacktestRunning] = useState(false);
  const [backtestHistory, setBacktestHistory] = useState<BacktestHistoryEntry[]>([]);
  const [loadedResult, setLoadedResult] = useState<BacktestResult | null>(null);

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect()
      .build();

    connection.on('ReceiveAnalysis', (result: AnalysisResult) => {
      setResults(prev => ({ ...prev, [result.symbol]: result }));
    });

    connection.on('ReceiveBar', (bar: Bar) => {
      setLiveBar(bar);
    });

    connection.on('BacktestProgress', (update: BacktestProgressUpdate) => {
      setBacktestProgress(update);
      setBacktestRunning(true);
    });

    connection.on('BacktestComplete', (result: BacktestResult) => {
      setBacktestResult(result);
      setBacktestProgress(null);
      setBacktestRunning(false);
      setBacktestError(null);
      connection.invoke('GetBacktestHistory').catch(console.error);
    });

    connection.on('BacktestCancelled', () => {
      setBacktestProgress(null);
      setBacktestRunning(false);
    });

    connection.on('BacktestError', (message: string) => {
      setBacktestError(message);
      setBacktestProgress(null);
      setBacktestRunning(false);
    });

    connection.on('BacktestHistory', (entries: BacktestHistoryEntry[]) => {
      setBacktestHistory(entries);
    });

    connection.on('BacktestResultLoaded', (result: BacktestResult) => {
      setLoadedResult(result);
    });

    connection.on('StrategiesLoaded', (data: Strategy[]) => {
      setStrategies(data);
    });

    connection.on('StrategyCreated', (strategy: Strategy) => {
      setStrategies(prev => [strategy, ...prev]);
    });

    connection.on('StrategyDeleted', (id: string) => {
      setStrategies(prev => prev.filter(s => s.id !== id));
    });

    connection.on('StrategyToggled', ({ strategyId, isActive }: { strategyId: string; isActive: boolean }) => {
      setStrategies(prev => prev.map(s => s.id === strategyId ? { ...s, isActive } : s));
    });

    connection.on('RuleAdded', (data: { strategyId: string; signalTypesJson: string } & Omit<Strategy['rules'][0], 'signalTypes'>) => {
      let signalTypes: Strategy['rules'][0]['signalTypes'] = [];
      try { signalTypes = JSON.parse(data.signalTypesJson) ?? []; } catch { /* empty */ }
      const rule: Strategy['rules'][0] = { ...data, signalTypes };
      setStrategies(prev => prev.map(s => s.id === data.strategyId
        ? { ...s, rules: [...s.rules, rule] }
        : s));
    });

    connection.on('RuleRemoved', (ruleId: string) => {
      setStrategies(prev => prev.map(s => ({
        ...s,
        rules: s.rules.filter(r => r.id !== ruleId),
      })));
    });

    connection.on('LiveSignalFired', (signal: LiveSignal) => {
      setStrategies(prev => prev.map(s => s.id === signal.strategyId
        ? { ...s, recentSignals: [signal, ...s.recentSignals].slice(0, 50) }
        : s));
    });

    connection.on('LiveSignalResolved', (signal: LiveSignal) => {
      setStrategies(prev => prev.map(s => s.id === signal.strategyId
        ? {
            ...s,
            recentSignals: s.recentSignals.map(sig => sig.id === signal.id ? signal : sig),
          }
        : s));
    });

    connection.onreconnecting(() => setConnected(false));
    connection.onreconnected(() => {
      setConnected(true);
      connection.invoke('RequestAnalysisBoth').catch(console.error);
      connection.invoke('GetStrategies').catch(console.error);
    });
    connection.onclose(() => setConnected(false));

    connection.start()
      .then(() => {
        setConnected(true);
        setError(null);
        connection.invoke('RequestAnalysisBoth').catch(console.error);
        connection.invoke('GetBacktestHistory').catch(console.error);
        connection.invoke('GetStrategies').catch(console.error);
        // Server will replay current backtest state via OnConnectedAsync
      })
      .catch(err => {
        setError(`Connection failed: ${err.message}`);
      });

    connectionRef.current = connection;
    return () => { connection.stop(); };
  }, []);

  const requestAnalysis = useCallback((symbol: string) => {
    connectionRef.current?.invoke('RequestAnalysis', symbol).catch(console.error);
  }, []);

  const requestBoth = useCallback(() => {
    connectionRef.current?.invoke('RequestAnalysisBoth').catch(console.error);
  }, []);

  const startBacktest = useCallback((params: BacktestParameters) => {
    setBacktestResult(null);
    setBacktestError(null);
    setBacktestProgress(null);
    setBacktestRunning(true);
    connectionRef.current?.invoke('StartBacktest', params).catch(err => {
      setBacktestError(`Failed to start: ${err.message}`);
      setBacktestRunning(false);
    });
  }, []);

  const cancelBacktest = useCallback(() => {
    connectionRef.current?.invoke('CancelBacktest').catch(console.error);
    setBacktestRunning(false);
    setBacktestProgress(null);
  }, []);

  const getBacktestHistory = useCallback(() => {
    connectionRef.current?.invoke('GetBacktestHistory').catch(console.error);
  }, []);

  const loadBacktestResult = useCallback((id: string) => {
    setLoadedResult(null);
    connectionRef.current?.invoke('GetBacktestResult', id).catch(console.error);
  }, []);

  const clearLoadedResult = useCallback(() => setLoadedResult(null), []);

  const getStrategies = useCallback(() => {
    connectionRef.current?.invoke('GetStrategies').catch(console.error);
  }, []);

  const createStrategy = useCallback((name: string) => {
    connectionRef.current?.invoke('CreateStrategy', name).catch(console.error);
  }, []);

  const addRule = useCallback((strategyId: string, req: AddRuleRequest) => {
    connectionRef.current?.invoke('AddRule', strategyId, req).catch(console.error);
  }, []);

  const removeRule = useCallback((ruleId: string) => {
    connectionRef.current?.invoke('RemoveRule', ruleId).catch(console.error);
  }, []);

  const toggleStrategy = useCallback((strategyId: string, isActive: boolean) => {
    connectionRef.current?.invoke('ToggleStrategy', strategyId, isActive).catch(console.error);
  }, []);

  const deleteStrategy = useCallback((strategyId: string) => {
    connectionRef.current?.invoke('DeleteStrategy', strategyId).catch(console.error);
  }, []);

  const subscribeToStrategy = useCallback((strategyId: string) => {
    connectionRef.current?.invoke('SubscribeToStrategy', strategyId).catch(console.error);
  }, []);

  return {
    connected, results, liveBar, error,
    requestAnalysis, requestBoth,
    backtestRunning, backtestProgress, backtestResult, backtestError,
    startBacktest, cancelBacktest,
    backtestHistory, loadedResult,
    getBacktestHistory, loadBacktestResult, clearLoadedResult,
    strategies,
    getStrategies, createStrategy, addRule, removeRule,
    toggleStrategy, deleteStrategy, subscribeToStrategy,
  };
}
