export type MarketSession = 'PreMarket' | 'RegularHours' | 'AfterHours' | 'Closed';
export type StructureBias = 'Bullish' | 'Bearish' | 'Neutral';
export type FvgType = 'Bullish' | 'Bearish';
export type OrderBlockType = 'Bullish' | 'Bearish';
export type LiquidityType = 'BuySide' | 'SellSide';
export type SwingType = 'High' | 'Low';

export interface Bar {
  symbol: string;
  time: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

export interface SwingPoint {
  type: SwingType;
  price: number;
  time: string;
}

export interface MarketStructure {
  bias: StructureBias;
  swingPoints: SwingPoint[];
  structureBreak: boolean;
  structureBreakDescription: string;
}

export interface FairValueGap {
  type: FvgType;
  top: number;
  bottom: number;
  formedAt: string;
  isFilled: boolean;
}

export interface OrderBlock {
  type: OrderBlockType;
  top: number;
  bottom: number;
  formedAt: string;
  isValid: boolean;
}

export interface LiquidityLevel {
  type: LiquidityType;
  price: number;
  label: string;
  isSwept: boolean;
  sweptAt: string | null;
}

export interface AnalysisResult {
  symbol: string;
  analyzedAt: string;
  session: MarketSession;
  currentPrice: number;
  previousDayHigh: number;
  previousDayLow: number;
  previousDayClose: number;
  marketStructure: MarketStructure;
  fairValueGaps: FairValueGap[];
  orderBlocks: OrderBlock[];
  liquidityLevels: LiquidityLevel[];
  isNyKillZone: boolean;
  recentBars: Bar[];
}
