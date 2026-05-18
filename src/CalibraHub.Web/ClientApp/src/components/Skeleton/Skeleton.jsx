/**
 * Skeleton component (rapor §2.9 cozumu).
 *
 * Buyuk grid'ler/list'ler yuklenirken bos tablo + spinner yerine satir
 * iskeleti gosterir — "yapi yukleniyor, az kaldi" hissi verir.
 *
 * CSS: site.css'te .cb-skeleton, .cb-skeleton-row, .cb-skeleton-rect zaten
 * tanimli; bu component sadece JSX wrapper'i.
 *
 * Kullanim:
 *   import { SkeletonRow, SkeletonRect, SkeletonList } from '../Skeleton/Skeleton'
 *   {loading ? <SkeletonList rows={5} /> : <RealGrid data={data} />}
 *
 *   // Tek satir
 *   <SkeletonRow width="60%" />
 *
 *   // Custom block (kart icin)
 *   <SkeletonRect height={80} width="100%" />
 */
import React from 'react'

export function SkeletonRow({ width = '100%', height = 12, style = {} }) {
    return (
        <div
            className="cb-skeleton cb-skeleton-row"
            style={{ width, height, ...style }}
            aria-hidden="true"
        />
    )
}

export function SkeletonRect({ width = '100%', height = 60, style = {} }) {
    return (
        <div
            className="cb-skeleton cb-skeleton-rect"
            style={{ width, height, ...style }}
            aria-hidden="true"
        />
    )
}

/**
 * Tekrarli satirlar — typical grid loading state.
 *
 * @param {number} rows  Kac iskelet satiri (default 5)
 * @param {number} gap   Satirlar arasi mesafe (default 12px)
 */
export function SkeletonList({ rows = 5, gap = 12, rowHeight = 14 }) {
    return (
        <div
            role="status"
            aria-label="Yukleniyor"
            style={{ display: 'flex', flexDirection: 'column', gap, padding: '8px 0' }}
        >
            {Array.from({ length: rows }).map((_, i) => (
                <SkeletonRow
                    key={i}
                    height={rowHeight}
                    width={`${75 + Math.random() * 20}%`}
                />
            ))}
            <span className="sr-only" style={{
                position: 'absolute', width: 1, height: 1, padding: 0,
                margin: -1, overflow: 'hidden', clip: 'rect(0,0,0,0)',
                whiteSpace: 'nowrap', border: 0
            }}>Yukleniyor</span>
        </div>
    )
}

/**
 * Grid satirlari icin — kolonlu skeleton.
 * Ornek: <SkeletonGrid columns={5} rows={4} />
 */
export function SkeletonGrid({ columns = 4, rows = 5, cellHeight = 14, gap = 8 }) {
    return (
        <div role="status" aria-label="Yukleniyor" style={{ width: '100%' }}>
            {Array.from({ length: rows }).map((_, r) => (
                <div
                    key={r}
                    style={{
                        display: 'grid',
                        gridTemplateColumns: `repeat(${columns}, 1fr)`,
                        gap,
                        padding: '10px 0',
                        borderBottom: '1px solid rgba(148,163,184,0.12)'
                    }}
                >
                    {Array.from({ length: columns }).map((_, c) => (
                        <SkeletonRow
                            key={c}
                            height={cellHeight}
                            width={`${60 + Math.random() * 35}%`}
                        />
                    ))}
                </div>
            ))}
        </div>
    )
}

export default { SkeletonRow, SkeletonRect, SkeletonList, SkeletonGrid }
