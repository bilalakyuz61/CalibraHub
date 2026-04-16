import { motion } from 'framer-motion'
import { Activity, Building2, User, Shield } from 'lucide-react'

export default function StatusBar({ collapsed }) {
  return (
    <div
      className="flex-shrink-0 flex items-center gap-4 px-4 py-2 border-t border-white/[0.05]"
      style={{
        background: 'rgba(8, 11, 22, 0.7)',
        backdropFilter: 'blur(20px)',
        WebkitBackdropFilter: 'blur(20px)',
      }}
    >
      {/* System status */}
      <div className="flex items-center gap-2">
        <div className="relative flex items-center justify-center">
          <motion.div
            animate={{ scale: [1, 1.6, 1], opacity: [0.6, 0.15, 0.6] }}
            transition={{ duration: 2.5, repeat: Infinity, ease: 'easeInOut' }}
            className="absolute w-2 h-2 rounded-full bg-emerald-400"
          />
          <div className="w-2 h-2 rounded-full bg-emerald-400 relative z-10" />
        </div>
        <span className="text-[11px] text-white/35 font-medium">Hazir</span>
      </div>

      <div className="w-px h-3.5 bg-white/[0.06]" />

      {/* Company */}
      <div className="flex items-center gap-1.5">
        <Building2 size={12} className="text-white/20" />
        <span className="text-[11px] text-white/35 font-medium">Adamar</span>
      </div>

      <div className="w-px h-3.5 bg-white/[0.06]" />

      {/* User */}
      <div className="flex items-center gap-1.5">
        <User size={12} className="text-white/20" />
        <span className="text-[11px] text-white/35 font-medium">Sistem Admin</span>
      </div>

      {/* Spacer */}
      <div className="flex-1" />

      {/* Copyright */}
      <span className="text-[10px] text-white/15 font-mono">&copy; 2026 CalibraHub</span>
    </div>
  )
}
