/**
 * themeService
 *
 * Tema Razor backend tarafindan yonetilir (body.app-theme-light / body.app-theme-dark).
 * React sadece okuyarak kendi Tailwind `dark` class'ini html tag'ine senkronize eder.
 *
 * Bu servis localStorage veya fetch cagrisi YAPMAZ — tek kaynak Razor layout'udur.
 * Tema degistirme islemi _Layout.cshtml icindeki profil dropdown'unda yapilir
 * ve /Account/SaveInterfacePreferences endpoint'ine POST edilir.
 */

/**
 * Mevcut temayi body class'indan oku.
 * @returns {'light'|'dark'}
 */
export function loadTheme() {
  try {
    if (document.body && document.body.classList.contains('app-theme-light')) {
      return 'light'
    }
    // Varsayilan: dark (app-theme-dark, app-theme-midnight, vb. koyu tema kodlari)
    return 'dark'
  } catch (e) {
    return 'dark'
  }
}

/**
 * Tailwind `dark` class'ini html tag'inde uygular/kaldirir.
 * @param {'light'|'dark'} theme
 */
export function applyTheme(theme) {
  try {
    var html = document.documentElement
    if (theme === 'dark') {
      html.classList.add('dark')
    } else {
      html.classList.remove('dark')
    }
  } catch (e) {}
}

/**
 * Uygulama baslangicinda: body class'indan tema okunur, html'e uygulanir.
 */
export function initTheme() {
  var theme = loadTheme()
  applyTheme(theme)
  return theme
}
