/**
 * GuideSettingsModal — Widget tanım formundaki rehber özelleştirme.
 *
 * Bu component artık ortak `GuideCustomizationModal`'ın widget tarafı için
 * ince wrapper'ıdır. Tüm UI ve davranış (Distinct filtre, kolon seçimi,
 * SQL kısıtı) Common/GuideCustomizationModal'da yaşar — değişiklik orada
 * yapılır, hem widget hem standart-alan tarafı aynı anda yenilenir.
 *
 * Props:
 *   isOpen        bool
 *   onClose       fn
 *   onSaved       fn(config)
 *   fieldLabel    string
 *   initialConfig { viewCode, columns, constraint } | null
 */
import GuideCustomizationModal from '../Common/GuideCustomizationModal'

export default function GuideSettingsModal(props) {
  // Widget tanim formundan acilinca her rehberde DÖNÜŞ + GÖRÜNÜM toggle'lari gosterilir;
  // tip 1 (standart) bile olsa admin override yapabilmeli. Field rehber popup'i ise
  // tip 1'de sadece DÖNÜŞ kuralina uyar (GuideCustomizationModal default davranisi).
  return <GuideCustomizationModal {...props} forceShowDisplayColumn={true} />
}
