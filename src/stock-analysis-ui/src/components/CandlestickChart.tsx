import { useEffect, useRef } from 'react';
import {
  createChart,
  ColorType,
  type IChartApi,
  type ISeriesApi,
  type UTCTimestamp,
  LineSeries,
  CandlestickSeries,
} from 'lightweight-charts';
import type { AnalysisResult } from '../types/analysis';

interface Props {
  result: AnalysisResult;
}

export function CandlestickChart({ result }: Props) {
  const containerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const candleSeriesRef = useRef<ISeriesApi<'Candlestick'> | null>(null);
  const pdhSeriesRef = useRef<ISeriesApi<'Line'> | null>(null);
  const pdlSeriesRef = useRef<ISeriesApi<'Line'> | null>(null);

  // Create chart and series once
  useEffect(() => {
    if (!containerRef.current) return;

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

  // Update data when result changes
  useEffect(() => {
    if (!candleSeriesRef.current || !pdhSeriesRef.current || !pdlSeriesRef.current) return;
    if (result.recentBars.length === 0) return;

    const toTs = (iso: string) => Math.floor(new Date(iso).getTime() / 1000) as UTCTimestamp;

    const candles = result.recentBars.map(bar => ({
      time: toTs(bar.time),
      open: bar.open,
      high: bar.high,
      low: bar.low,
      close: bar.close,
    }));

    const times = candles.map(c => c.time);

    candleSeriesRef.current.setData(candles);
    pdhSeriesRef.current.setData(times.map(t => ({ time: t, value: result.previousDayHigh })));
    pdlSeriesRef.current.setData(times.map(t => ({ time: t, value: result.previousDayLow })));

    chartRef.current?.timeScale().fitContent();
  }, [result]);

  return <div ref={containerRef} style={{ width: '100%' }} />;
}
