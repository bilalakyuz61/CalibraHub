/**
 * widgetRegistry — Dashboard widget tipi → React bileseni + meta eslesmesi.
 *
 * Her meta:
 *   { component, title, icon, defaultSize, iconColor, settingsSchema }
 *   - component:      widget React bileseni
 *   - title:          WidgetFrame baslik (varsayilan; settings.customTitle override eder)
 *   - icon:           Lucide ikon adi (DynamicWidgetFactory.resolveIcon ile cozulur)
 *   - defaultSize:    katalogdan eklenince varsayilan boyut ('sm'|'md'|'lg')
 *   - iconColor:      header ikon rengi
 *   - settingsSchema: per-widget ayar alanlari (WidgetSettingsModal icin)
 *       { key, type:'text'|'multiselect'|'select', label, options?, default? }
 */
import WelcomeWidget from './widgets/WelcomeWidget'
import QuickLinksWidget from './widgets/QuickLinksWidget'
import PendingApprovalsWidget from './widgets/PendingApprovalsWidget'
import ExchangeRatesWidget from './widgets/ExchangeRatesWidget'
import RecentDocumentsWidget from './widgets/RecentDocumentsWidget'
import WorkOrdersWidget from './widgets/WorkOrdersWidget'
import SalesQuotesWidget from './widgets/SalesQuotesWidget'
import StockAlertsWidget from './widgets/StockAlertsWidget'
import CalendarWidget from './widgets/CalendarWidget'

var CURRENCY_OPTIONS = [
  { value: 'USD', label: 'USD – Amerikan Doları' },
  { value: 'EUR', label: 'EUR – Euro' },
  { value: 'GBP', label: 'GBP – İngiliz Sterlini' },
  { value: 'CHF', label: 'CHF – İsviçre Frangı' },
  { value: 'JPY', label: 'JPY – Japon Yeni' },
  { value: 'RUB', label: 'RUB – Rus Rublesi' },
  { value: 'SAR', label: 'SAR – Suudi Riyali' },
  { value: 'AED', label: 'AED – BAE Dirhemi' },
  { value: 'CNY', label: 'CNY – Çin Yuanı' },
]

export var WIDGET_REGISTRY = {
  'welcome-card': {
    component: WelcomeWidget,
    title: 'Hoş Geldiniz',
    icon: 'UserCircle',
    iconColor: 'indigo',
    defaultSize: 'lg',
    settingsSchema: [],
  },
  'quick-links': {
    component: QuickLinksWidget,
    title: 'Kısayollar',
    icon: 'Zap',
    iconColor: 'amber',
    defaultSize: 'md',
    settingsSchema: [],
  },
  'pending-approvals': {
    component: PendingApprovalsWidget,
    title: 'Onayda Bekleyenler',
    icon: 'Inbox',
    iconColor: 'rose',
    defaultSize: 'sm',
    settingsSchema: [],
  },
  'exchange-rates': {
    component: ExchangeRatesWidget,
    title: 'Döviz Kurları',
    icon: 'DollarSign',
    iconColor: 'emerald',
    defaultSize: 'md',
    settingsSchema: [
      {
        key: 'codes',
        type: 'multiselect',
        label: 'Görüntülenecek Dövizler',
        options: CURRENCY_OPTIONS,
        default: ['USD', 'EUR', 'GBP'],
      },
    ],
  },
  'recent-documents': {
    component: RecentDocumentsWidget,
    title: 'Son Belgeler',
    icon: 'Files',
    iconColor: 'blue',
    defaultSize: 'md',
    settingsSchema: [
      {
        key: 'take',
        type: 'select',
        label: 'Gösterilecek Belge Sayısı',
        options: [
          { value: 5,  label: '5 belge' },
          { value: 10, label: '10 belge' },
          { value: 20, label: '20 belge' },
        ],
        default: 8,
      },
    ],
  },
  'work-orders': {
    component: WorkOrdersWidget,
    title: 'İş Emirleri Özeti',
    icon: 'ClipboardList',
    iconColor: 'violet',
    defaultSize: 'md',
    settingsSchema: [],
  },
  'sales-quotes': {
    component: SalesQuotesWidget,
    title: 'Satış Teklifleri',
    icon: 'FileText',
    iconColor: 'indigo',
    defaultSize: 'md',
    settingsSchema: [],
  },
  'stock-alerts': {
    component: StockAlertsWidget,
    title: 'Stok Uyarıları',
    icon: 'PackageX',
    iconColor: 'amber',
    defaultSize: 'md',
    settingsSchema: [],
  },
  'calendar': {
    component: CalendarWidget,
    title: 'Takvim',
    icon: 'CalendarDays',
    iconColor: 'violet',
    defaultSize: 'lg',
    defaultHeight: 3,
    settingsSchema: [],
  },
}

/** Tipe ait meta'yi dondur; yoksa null. */
export function getWidgetMeta(type) {
  if (!type) return null
  return WIDGET_REGISTRY[type] || null
}

/** Boyut → grid kolon span eslesmesi. */
export function sizeToSpan(size) {
  if (size === 'sm') return 1
  if (size === 'lg') return 3
  return 2
}

export default WIDGET_REGISTRY
