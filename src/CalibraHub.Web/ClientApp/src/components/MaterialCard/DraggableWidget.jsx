import { useSortable } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { motion } from 'framer-motion'
import WidgetTooltip from './WidgetTooltip'
import './MaterialCard.css'

const KNOWN_COLORS = ['emerald', 'amber', 'blue', 'violet', 'cyan', 'rose', 'slate', 'indigo', 'teal', 'orange']

export default function DraggableWidget({ widget }) {
  var id = widget.id
  var Icon = widget.icon
  var label = widget.label
  var value = widget.value
  var detail = widget.detail || ''
  var color = KNOWN_COLORS.includes(widget.color) ? widget.color : 'blue'

  var sortable = useSortable({ id: id })
  var style = {
    transform: CSS.Transform.toString(sortable.transform),
    transition: sortable.transition,
    zIndex: sortable.isDragging ? 50 : 1,
  }

  if (!Icon) return null

  return (
    <div ref={sortable.setNodeRef} style={style} {...sortable.attributes} {...sortable.listeners}>
      <WidgetTooltip label={label} value={value} detail={detail}>
        <motion.div
          layout
          animate={sortable.isDragging
            ? { scale: 1.06, rotate: 1, boxShadow: '0 12px 40px rgba(0,0,0,0.35)' }
            : { scale: 1, rotate: 0, boxShadow: '0 2px 8px rgba(0,0,0,0.08)' }
          }
          transition={{ type: 'spring', stiffness: 350, damping: 26, mass: 0.7 }}
          className="mc-chip flex items-center gap-2 px-2.5 py-1.5 rounded-xl cursor-grab active:cursor-grabbing select-none whitespace-nowrap"
          data-color={color}
        >
          <div
            className="mc-chip w-6 h-6 rounded-lg flex items-center justify-center flex-shrink-0"
            data-color={color}
          >
            <Icon size={13} className="mc-chip-icon" strokeWidth={1.8} />
          </div>
          <div className="flex flex-col min-w-0 leading-none">
            <span className="text-[9px] font-semibold uppercase tracking-wider text-slate-500 dark:text-white/30">{label}</span>
            <span className="mc-chip-value text-xs font-bold leading-tight tracking-tight">{value}</span>
          </div>
        </motion.div>
      </WidgetTooltip>
    </div>
  )
}
