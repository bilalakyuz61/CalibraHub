import { useState } from 'react'
import Sidebar from './Sidebar'
import StatusBar from './StatusBar'

export default function DashboardLayout({ children, activePath, onNavigate }) {
  const [collapsed, setCollapsed] = useState(false)

  return (
    <div className="h-screen w-screen flex flex-col overflow-hidden bg-mesh">
      <div className="flex flex-1 min-h-0">
        {/* Sidebar */}
        <Sidebar
          collapsed={collapsed}
          onToggleCollapse={() => setCollapsed(prev => !prev)}
          activePath={activePath}
          onNavigate={onNavigate}
        />

        {/* Main content */}
        <main className="flex-1 min-w-0 min-h-0 overflow-hidden">
          {children}
        </main>
      </div>

      {/* Status bar */}
      <StatusBar collapsed={collapsed} />
    </div>
  )
}
