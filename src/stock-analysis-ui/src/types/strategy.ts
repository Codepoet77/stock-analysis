import type { EnabledSignalType, ExitStrategy } from './backtest';

export type SignalDirection = 'Long' | 'Short' | 'Both';
export type LiveSignalStatus = 'Open' | 'Win' | 'Loss' | 'Scratch' | 'Expired';

export interface StrategyRule {
  id: string;
  symbol: string;
  label: string;
  direction: SignalDirection;
  exitStrategy: ExitStrategy;
  fixedRRatio: number;
  killZoneOnly: boolean;
  signalTypes: EnabledSignalType[];
  tradingWindowStart: string | null;  // "HH:MM:SS" e.g. "09:30:00"
  tradingWindowEnd: string | null;
  isActive: boolean;
  createdAt: string;
}

export interface LiveSignal {
  id: string;
  symbol: string;
  direction: 'Long' | 'Short';
  signalType: string;
  timeframeLabel: string;
  entryTime: string;
  entryPrice: number;
  target: number;
  stop: number;
  currentStop: number;
  exitStrategy: ExitStrategy;
  status: LiveSignalStatus;
  outcomeTime: string | null;
  actualExitPrice: number | null;
  actualRR: number | null;
  breakevenActivated: boolean;
  strategyId: string;
  ruleId: string;
  ruleLabel: string;
}

export interface StrategyPerformance {
  totalSignals: number;
  openSignals: number;
  wins: number;
  losses: number;
  scratches: number;
  winRate: number;
  averageRR: number;
  expectedValue: number;
  profitFactor: number;
  lastSignalAt: string | null;
}

export interface Strategy {
  id: string;
  name: string;
  isActive: boolean;
  createdAt: string;
  rules: StrategyRule[];
  performance: StrategyPerformance;
  recentSignals: LiveSignal[];
}

export interface AddRuleRequest {
  symbol: string;
  label: string;
  exitStrategy: ExitStrategy;
  fixedRRatio: number;
  killZoneOnly: boolean;
  direction: SignalDirection;
  signalTypes: EnabledSignalType[] | null;
  tradingWindowStart: string | null;  // "HH:MM:SS"
  tradingWindowEnd: string | null;
}
