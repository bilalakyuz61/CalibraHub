/**
 * Tiptap inline Mark — Mod 1 (secili bolum E2E sifreleme).
 *
 * DOM temsili:
 *   <span class="nw-encrypted" data-ct="..." data-hint="..." data-open="0">🔒 Sifreli</span>
 *
 * Attributes:
 *   ct      : JSON string payload (encryption.encryptText ciktisi)
 *   hint    : Opsiyonel sifre ipucu (plaintext)
 *   open    : "1" ise kullanici bu oturumda acmis → duz metin render edilir
 *             "0" ise mask (🔒 Sifreli) gosterilir
 *
 * Etkilesim: NotesWorkspace bilesenindeki onClick handler modal acar,
 * sifre dogrulandiysa bu mark'in open attribute'u "1" yapilir.
 *
 * Storage (DB'de): <span class="nw-encrypted" data-ct="{...}" data-hint="...">...</span>
 * "open" sadece runtime; DB'ye gitmez ({renderHTML}'de hazircevaplanir).
 */
import { Mark, mergeAttributes } from '@tiptap/core'

export const EncryptedMark = Mark.create({
  name: 'encrypted',
  inclusive: false,
  keepOnSplit: false,

  addAttributes() {
    return {
      ct:   { default: null, parseHTML: el => el.getAttribute('data-ct'),   renderHTML: attrs => attrs.ct   ? { 'data-ct': attrs.ct } : {} },
      hint: { default: null, parseHTML: el => el.getAttribute('data-hint'), renderHTML: attrs => attrs.hint ? { 'data-hint': attrs.hint } : {} },
      // Runtime only — HTML'e yazilmaz (DB'ye gitmesin)
      open: { default: '0', parseHTML: () => '0', renderHTML: () => ({}) },
    }
  },

  parseHTML() {
    return [{ tag: 'span.nw-encrypted' }]
  },

  renderHTML({ HTMLAttributes }) {
    return ['span', mergeAttributes(HTMLAttributes, { class: 'nw-encrypted', 'data-open': '0' }), 0]
  },

  addCommands() {
    return {
      setEncrypted: (attrs) => ({ commands }) => commands.setMark(this.name, attrs),
      unsetEncrypted: () => ({ commands }) => commands.unsetMark(this.name),
    }
  },
})

export default EncryptedMark
