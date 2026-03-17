import type { AnalysisResult } from '../types/analysis';

interface Props {
  result: AnalysisResult;
}

function fmt(n: number) { return n.toFixed(2); }
function timeLabel(iso: string) {
  return new Date(iso).toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' });
}

const biasCls = {
  Bullish: 'text-green-500',
  Bearish: 'text-red-500',
  Neutral: 'text-slate-400',
};

export function AnalysisPanel({ result }: Props) {
  const { marketStructure: ms, fairValueGaps, orderBlocks, liquidityLevels } = result;

  return (
    <div className="flex flex-col gap-4">

      {/* Market Structure */}
      <section>
        <SectionLabel>Market Structure</SectionLabel>
        <div className="flex gap-4 items-center">
          <span className="text-2xl font-bold text-slate-100">${fmt(result.currentPrice)}</span>
          <span className={`font-semibold text-sm ${biasCls[ms.bias]}`}>{ms.bias}</span>
        </div>
        {ms.structureBreak && (
          <p className="mt-1.5 text-amber-400 text-xs">⚠ {ms.structureBreakDescription}</p>
        )}
      </section>

      {/* Previous Day Levels */}
      <section>
        <SectionLabel>Previous Day Levels</SectionLabel>
        <div className="grid grid-cols-3 gap-2">
          <LevelBox label="PDH" value={result.previousDayHigh} cls="text-amber-400" />
          <LevelBox label="PDL" value={result.previousDayLow}  cls="text-violet-400" />
          <LevelBox label="PDC" value={result.previousDayClose} cls="text-slate-400" />
        </div>
      </section>

      {/* Fair Value Gaps */}
      <section>
        <SectionLabel>Fair Value Gaps ({fairValueGaps.filter(f => !f.isFilled).length} active)</SectionLabel>
        {fairValueGaps.length === 0
          ? <p className="text-slate-600 text-xs">No active FVGs</p>
          : <div className="flex flex-col gap-1.5">
              {fairValueGaps.map((fvg, i) => (
                <div key={i} className={`bg-slate-800 px-2.5 py-1.5 rounded text-xs
                  border-l-[3px] ${fvg.type === 'Bullish' ? 'border-green-500' : 'border-red-500'}
                  ${fvg.isFilled ? 'opacity-50' : ''}`}>
                  <span className={`font-semibold ${fvg.type === 'Bullish' ? 'text-green-500' : 'text-red-500'}`}>
                    {fvg.type}
                  </span>
                  {' '}{fmt(fvg.bottom)} – {fmt(fvg.top)}
                  <span className="text-slate-600 ml-2">{timeLabel(fvg.formedAt)}</span>
                  {fvg.isFilled && <span className="text-slate-600 ml-2">[filled]</span>}
                </div>
              ))}
            </div>
        }
      </section>

      {/* Order Blocks */}
      <section>
        <SectionLabel>Order Blocks ({orderBlocks.filter(ob => ob.isValid).length} valid)</SectionLabel>
        {orderBlocks.length === 0
          ? <p className="text-slate-600 text-xs">No order blocks detected</p>
          : <div className="flex flex-col gap-1.5">
              {orderBlocks.map((ob, i) => (
                <div key={i} className={`bg-slate-800 px-2.5 py-1.5 rounded text-xs
                  border-l-[3px] ${ob.type === 'Bullish' ? 'border-green-500' : 'border-red-500'}
                  ${ob.isValid ? '' : 'opacity-40'}`}>
                  <span className={`font-semibold ${ob.type === 'Bullish' ? 'text-green-500' : 'text-red-500'}`}>
                    {ob.type} OB
                  </span>
                  {' '}{fmt(ob.bottom)} – {fmt(ob.top)}
                  <span className="text-slate-600 ml-2">{timeLabel(ob.formedAt)}</span>
                  {!ob.isValid && <span className="text-red-500 ml-2">[violated]</span>}
                </div>
              ))}
            </div>
        }
      </section>

      {/* Liquidity Levels */}
      <section>
        <SectionLabel>Liquidity Levels</SectionLabel>
        {liquidityLevels.length === 0
          ? <p className="text-slate-600 text-xs">No liquidity levels</p>
          : <div className="flex flex-col gap-1.5">
              {liquidityLevels.map((lvl, i) => (
                <div key={i} className={`bg-slate-800 px-2.5 py-1.5 rounded text-xs
                  border-l-[3px] ${lvl.type === 'BuySide' ? 'border-green-500' : 'border-red-500'}
                  ${lvl.isSwept ? 'opacity-50' : ''}`}>
                  <span className={`font-semibold ${lvl.type === 'BuySide' ? 'text-green-500' : 'text-red-500'}`}>
                    {lvl.type === 'BuySide' ? 'BSL' : 'SSL'}
                  </span>
                  {' '}{lvl.label} @ ${fmt(lvl.price)}
                  {lvl.isSwept && <span className="text-slate-600 ml-2">[swept]</span>}
                </div>
              ))}
            </div>
        }
      </section>

    </div>
  );
}

function SectionLabel({ children }: { children: React.ReactNode }) {
  return (
    <h3 className="text-xs font-semibold text-slate-400 uppercase tracking-wide mb-2">{children}</h3>
  );
}

function LevelBox({ label, value, cls }: { label: string; value: number; cls: string }) {
  return (
    <div className="bg-slate-800 p-2 rounded-lg text-center">
      <div className={`${cls} text-[11px] font-semibold mb-0.5`}>{label}</div>
      <div className="text-slate-100 text-sm font-bold">${value.toFixed(2)}</div>
    </div>
  );
}
