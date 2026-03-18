import { useEffect, useRef, useCallback } from 'react';
import {
  createChart,
  ColorType,
  type IChartApi,
  type ISeriesApi,
  type UTCTimestamp,
  LineSeries,
  CandlestickSeries,
} from 'lightweight-charts';
import type { AnalysisResult, Bar } from '../types/analysis';

const API = import.meta.env.VITE_API_URL || '';

interface Props {
  result: AnalysisResult;
}

interface CandleData {
  time: UTCTimestamp;
  open: number;
  high: number;
  low: number;
  close: number;
}

export function CandlestickChart({ result }: Props) {
  const containerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const candleSeriesRef = useRef<ISeriesApi<'Candlestick'> | null>(null);
  const pdhSeriesRef = useRef<ISeriesApi<'Line'> | null>(null);
  const pdlSeriesRef = useRef<ISeriesApi<'Line'> | null>(null);

  // Track all loaded candles and fetch state
  const allCandlesRef = useRef<CandleData[]>([]);
  const oldestTimeRef = useRef<Date | null>(null);
  const fetchingRef = useRef(false);
  const fullyLoadedRef = useRef(false);
  const initialFitDoneRef = useRef(false);

  const toTs = (iso: string) => Math.floor(new Date(iso).getTime() / 1000) as UTCTimestamp;

  const fetchOlderBars = useCallback(async () => {
    if (fetchingRef.current || fullyLoadedRef.current || !oldestTimeRef.current) return;
    fetchingRef.current = true;

    try {
      const to = oldestTimeRef.current.toISOString();
      // Fetch 2 hours earlier
      const from = new Date(oldestTimeRef.current.getTime() - 2 * 60 * 60 * 1000).toISOString();

      const res = await fetch(`${API}/api/analysis/${result.symbol}/bars?from=${from}&to=${to}`);
      if (!res.ok) return;

      const bars: Bar[] = await res.json();
      if (bars.length === 0) {
        fullyLoadedRef.current = true;
        return;
      }

      const newCandles: CandleData[] = bars.map(bar => ({
        time: toTs(bar.time),
        open: bar.open,
        high: bar.high,
        low: bar.low,
        close: bar.close,
      }));

      // Filter out duplicates
      const existingTimes = new Set(allCandlesRef.current.map(c => c.time));
      const unique = newCandles.filter(c => !existingTimes.has(c.time));

      if (unique.length === 0) {
        fullyLoadedRef.current = true;
        return;
      }

      allCandlesRef.current = [...unique, ...allCandlesRef.current].sort((a, b) => a.time - b.time);
      oldestTimeRef.current = new Date(allCandlesRef.current[0].time * 1000);

      // Update chart
      if (candleSeriesRef.current && pdhSeriesRef.current && pdlSeriesRef.current) {
        const times = allCandlesRef.current.map(c => c.time);
        candleSeriesRef.current.setData(allCandlesRef.current);
        pdhSeriesRef.current.setData(times.map(t => ({ time: t, value: result.previousDayHigh })));
        pdlSeriesRef.current.setData(times.map(t => ({ time: t, value: result.previousDayLow })));
      }
    } finally {
      fetchingRef.current = false;
    }
  }, [result.symbol, result.previousDayHigh, result.previousDayLow]);

  // Create chart and series once
  useEffect(() => {
    if (!containerRef.current) return;

    // Reset state on mount
    allCandlesRef.current = [];
    oldestTimeRef.current = null;
    fullyLoadedRef.current = false;
    initialFitDoneRef.current = false;

    const chart = createChart(containerRef.current, {
      layout: {
        background: { type: ColorType.Solid, color: '#0f172a' },
        textColor: '#94a3b8',
      },
      grid: {
        vertLines: { color: '#1e293b' },
        horzLines: { color: '#1e293b' },
      },
      crosshair: { mode: 1 },
      timeScale: {
        timeVisible: true,
        secondsVisible: false,
      },
      localization: {
        timeFormatter: (time: number) => {
          const d = new Date(time * 1000);
          return d.toLocaleString('en-US', {
            month: 'short', day: 'numeric',
            hour: '2-digit', minute: '2-digit', hour12: false,
            timeZone: 'America/New_York',
          });
        },
      },
      width: containerRef.current.clientWidth,
      height: 350,
    });

    candleSeriesRef.current = chart.addSeries(CandlestickSeries, {
      upColor: '#22c55e',
      downColor: '#ef4444',
      borderVisible: false,
      wickUpColor: '#22c55e',
      wickDownColor: '#ef4444',
    });

    pdhSeriesRef.current = chart.addSeries(LineSeries, {
      color: '#f59e0b',
      lineWidth: 1,
      lineStyle: 2,
      title: 'PDH',
    });

    pdlSeriesRef.current = chart.addSeries(LineSeries, {
      color: '#8b5cf6',
      lineWidth: 1,
      lineStyle: 2,
      title: 'PDL',
    });

    chartRef.current = chart;

    const resizeObserver = new ResizeObserver(() => {
      if (containerRef.current)
        chart.applyOptions({ width: containerRef.current.clientWidth });
    });
    resizeObserver.observe(containerRef.current);

    return () => {
      resizeObserver.disconnect();
      chart.remove();
      chartRef.current = null;
      candleSeriesRef.current = null;
      pdhSeriesRef.current = null;
      pdlSeriesRef.current = null;
    };
  }, []);

  // Detect scroll to left edge
  useEffect(() => {
    const chart = chartRef.current;
    if (!chart) return;

    const handler = () => {
      const range = chart.timeScale().getVisibleLogicalRange();
      if (range && range.from < 5) {
        fetchOlderBars();
      }
    };

    chart.timeScale().subscribeVisibleLogicalRangeChange(handler);
    return () => chart.timeScale().unsubscribeVisibleLogicalRangeChange(handler);
  }, [fetchOlderBars]);

  // Update data when result changes
  useEffect(() => {
    if (!candleSeriesRef.current || !pdhSeriesRef.current || !pdlSeriesRef.current) return;
    if (result.recentBars.length === 0) return;

    const newCandles: CandleData[] = result.recentBars.map(bar => ({
      time: toTs(bar.time),
      open: bar.open,
      high: bar.high,
      low: bar.low,
      close: bar.close,
    }));

    // Merge: new candles overwrite existing ones at the same timestamp
    const map = new Map<number, CandleData>();
    for (const c of allCandlesRef.current) map.set(c.time, c);
    for (const c of newCandles) map.set(c.time, c);
    const merged = [...map.values()].sort((a, b) => a.time - b.time);

    allCandlesRef.current = merged;
    if (merged.length > 0) {
      oldestTimeRef.current = new Date(merged[0].time * 1000);
    }

    const times = merged.map(c => c.time);
    candleSeriesRef.current.setData(merged);
    pdhSeriesRef.current.setData(times.map(t => ({ time: t, value: result.previousDayHigh })));
    pdlSeriesRef.current.setData(times.map(t => ({ time: t, value: result.previousDayLow })));

    // First load: fit all content. After that: keep view, scroll to latest.
    if (!initialFitDoneRef.current) {
      chartRef.current?.timeScale().fitContent();
      initialFitDoneRef.current = true;
    } else {
      chartRef.current?.timeScale().scrollToRealTime();
    }
  }, [result]);

  return <div ref={containerRef} style={{ width: '100%' }} />;
}
