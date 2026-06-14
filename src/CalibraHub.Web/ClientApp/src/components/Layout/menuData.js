import {
  LayoutDashboard, ClipboardCheck, Warehouse, ShoppingCart,
  TrendingUp, Factory, Landmark, Settings2, Package,
  Puzzle, TreePine, Users, Layers, FlaskConical,
} from 'lucide-react'

const menuData = [
  {
    id: 'general',
    label: 'Genel',
    icon: LayoutDashboard,
    href: '/Dashboard',
  },
  {
    id: 'approvals',
    label: 'Onay Surecleri',
    icon: ClipboardCheck,
    href: '/Approvals',
  },
  {
    id: 'logistics',
    label: 'Lojistik',
    icon: Warehouse,
    children: [
      {
        id: 'logistics-definitions',
        label: 'Sabit Tanimlamalar',
        icon: Layers,
        children: [
          { id: 'material-cards', label: 'Malzeme Kartlari', icon: Package, href: '/Logistics/MaterialCards' },
        ],
      },
    ],
  },
  {
    id: 'purchasing',
    label: 'Satin Alma',
    icon: ShoppingCart,
    href: '/Purchasing',
  },
  {
    id: 'sales',
    label: 'Satis',
    icon: TrendingUp,
    href: '/Sales/Quotes',
  },
  {
    id: 'production',
    label: 'Uretim',
    icon: Factory,
    children: [
      { id: 'product-tree', label: 'Urun Agaci', icon: TreePine, href: '/Production/BOM' },
    ],
  },
  {
    id: 'arge',
    label: 'AR-GE',
    icon: FlaskConical,
    href: '/Arge/Projects',
  },
  {
    id: 'finance',
    label: 'Finans',
    icon: Landmark,
    children: [
      { id: 'accounts', label: 'Cari Hesaplar', icon: Users, href: '/Finance/Accounts' },
    ],
  },
  {
    id: 'definitions',
    label: 'Genel Tanimlamalar',
    icon: Settings2,
    href: '/Definitions',
  },
]

export default menuData
