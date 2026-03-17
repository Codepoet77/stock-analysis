export type BacktestTimeframe = 'OneMinute' | 'FiveMinute' | 'FifteenMinute' | 'OneHour' | 'FourHour';
export type ExitStrategy = 'FixedR' | 'BreakevenStop' | 'TrailingStop' | 'NextLiquidity' | 'All';
export type EnabledSignalType = 'OBRetest' | 'FVGFill' | 'LiquiditySweep' | 'StructureBreak';
export type SessionFilter = 'Full' | 'OpenKZ' | 'CloseKZ' | 'BothKZ';
export type BacktestOutcome = 'Win' | 'Loss' | 'Scratch';
export type SignalDirection = 'Long' | 'Short';
export type SignalType = 'OBRetest' | 'FVGFill' | 'LiquiditySweep' | 'StructureBreak' | 'KillZoneConfluence';

export interface BacktestParameters {
  symbols: string[];
  daysBack: number;
  timeframes: BacktestTimeframe[];
  exitStrategy: ExitStrategy;
  fixedRRatio: number;
  sessionFilter: SessionFilter;
  signalTypes: EnabledSignalType[] | null;  // null = all enabled
}

export interface BacktestProgressUpdate {
  stage: string;
  detail: string;
  percentComplete: number;
}

export interface BacktestSignalOutcome {
  symbol: string;
  timeframeLabel: string;
  combinationLabel: string | null;
  signalType: SignalType;
  direction: SignalDirection;
  exitStrategy: ExitStrategy;
  entryTime: string;
  entryPrice: number;
  target: number;
  invalidation: number;
  outcome: BacktestOutcome;
  actualRR: number;
  barsToOutcome: number;
  duringKillZone: boolean;
}

export interface SignalTypeStats {
  total: number;
  wins: number;
  losses: number;
  winRate: number;
  averageRR: number;
}

export interface BacktestStats {
  label: string;
  symbol: string;
  exitStrategy: ExitStrategy;
  totalSignals: number;
  wins: number;
  losses: number;
  scratches: number;
  winRate: number;
  averageRR: number;
  profitFactor: number;
  expectedValue: number;
  bySignalType: Record<string, SignalTypeStats>;
  bySession: Record<string, SignalTypeStats>;
}

export interface BacktestResult {
  parameters: BacktestParameters;
  startedAt: string;
  completedAt: string;
  individualStats: BacktestStats[];
  combinationStats: BacktestStats[];
  sampleSignals: BacktestSignalOutcome[];
  totalSignalsAnalyzed: number;
  bestIndividual: BacktestStats | null;
  bestCombination: BacktestStats | null;
}

export interface BacktestHistoryEntry {
  id: string;
  createdAt: string;
  completedAt: string;
  parameters: BacktestParameters;
  totalSignalsAnalyzed: number;
  bestIndividual: BacktestStats | null;
}
