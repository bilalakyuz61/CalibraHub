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
  // 2026-06-04: CLAUDE.md kuralina geri donuldu —
  //   "Widget tanım formundan (Alan Rehberi) her zaman DÖNÜŞ+GÖRÜNÜM
  //    (forceShowDisplayColumn). Field popup'ta tip 1 sadece DÖNÜŞ"
  //
  // 2026-05-22'de DisplayColumn admin UI'dan kaldirilmisti (Tip 1 standart
  // rehberlerde Code/Name varsayilani yeterli kabul edildi), ancak admin'in
  // Tip 2 ozel rehberlerde de DÖNÜŞ + GÖRÜNÜM secebilmesi gerekiyor — uzerinde
  // calistigi rehber Tip 1 mi Tip 2 mi belirsiz (admin yeni view ekleyebilir).
  // Kural: widget tanim formundan her zaman ikisini de goster, admin karar versin.
  return <GuideCustomizationModal {...props} forceShowDisplayColumn={true} />
}
