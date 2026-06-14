/**
 * NodeDataCtx — custom node componentlerinin (nodeTypes.jsx) parent designer'in
 * controlled `nodes` state'ini guncelleyebilmesi icin context.
 *
 * Designer Provider value: updateNodeData(nodeId, patch).
 * useReactFlow().setNodes controlled mode'da dis state'le senkron olmadigi icin
 * bu context gerekli.
 */
import { createContext, useContext } from 'react'

export var NodeDataCtx = createContext(null)

export function useUpdateNodeData() {
  return useContext(NodeDataCtx)
}

// Alt tuşu basili oldugunda tum handle'lar isConnectable=false olur — React Flow
// connection mekanizmasi devre disi kalir, sadece DraggableHandle Alt+drag mode'u calisir.
export var AltKeyCtx = createContext(false)
export function useAltKeyDown() { return useContext(AltKeyCtx) }
