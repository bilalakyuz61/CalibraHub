import { Node, mergeAttributes } from '@tiptap/core'
import { ReactNodeViewRenderer } from '@tiptap/react'
import { DrawingNodeView } from './DrawingNodeView'

export var DrawingNodeExtension = Node.create({
  name: 'drawingCanvas',
  group: 'block',
  atom: true,
  draggable: false,
  selectable: true,

  addAttributes: function () {
    return {
      snapshot: {
        default: null,
        parseHTML: function (el) { return el.getAttribute('data-snapshot') || null },
        renderHTML: function (attrs) {
          return attrs.snapshot ? { 'data-snapshot': attrs.snapshot } : {}
        },
      },
      height: {
        default: 460,
        parseHTML: function (el) { return parseInt(el.getAttribute('data-height'), 10) || 460 },
        renderHTML: function (attrs) { return { 'data-height': attrs.height || 460 } },
      },
    }
  },

  parseHTML: function () {
    return [{ tag: 'div[data-drawing-canvas]' }]
  },

  renderHTML: function (ref) {
    return ['div', mergeAttributes({ 'data-drawing-canvas': '' }, ref.HTMLAttributes)]
  },

  addNodeView: function () {
    return ReactNodeViewRenderer(DrawingNodeView)
  },

  addCommands: function () {
    return {
      insertDrawingCanvas: function () {
        return function (ref) {
          return ref.commands.insertContent({ type: 'drawingCanvas' })
        }
      },
    }
  },
})
