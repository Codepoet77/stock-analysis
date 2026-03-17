import type { MarketSession } from '../types/analysis';

interface Props {
  session: MarketSession;
  isKillZone: boolean;
}

const sessionConfig: Record<MarketSession, { label: string; cls: string }> = {
  RegularHours: { label: 'Market Open',   cls: 'bg-green-500' },
  PreMarket:    { label: 'Pre-Market',    cls: 'bg-amber-500' },
  AfterHours:   { label: 'After Hours',   cls: 'bg-violet-500' },
  Closed:       { label: 'Market Closed', cls: 'bg-slate-500' },
};

export function MarketStatusBadge({ session, isKillZone }: Props) {
  const { label, cls } = sessionConfig[session] ?? { label: String(session), cls: 'bg-slate-500' };

  return (
    <div className="flex gap-2 items-center">
      <span className={`${cls} text-white px-2.5 py-0.5 rounded-full text-xs font-semibold`}>
        {label}
      </span>
      {isKillZone && (
        <span className="bg-red-600 text-white px-2.5 py-0.5 rounded-full text-xs font-semibold">
          NY Kill Zone
        </span>
      )}
    </div>
  );
}
