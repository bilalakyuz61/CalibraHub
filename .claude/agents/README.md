# CalibraHub Ajan Takımı

Bu klasör CalibraHub uzman ajanlarını tanımlar. Her tanım **iki modda** çalışır:
- **Subagent** (`Agent` tool): uzman işi yapar, **lidere** rapor verir. Config gerektirmez, her ortamda çalışır.
- **Agent Team teammate**: uzman tam bağımsız bir Claude session'ı olur, **birbirleriyle ve seninle** doğrudan mesajlaşır, paylaşılan görev listesinden iş çeker. `CLAUDE_CODE_EXPERIMENTAL_AGENT_TEAMS=1` gerektirir (`settings.local.json`'da açık).

> Teammate modunda tanımın `tools` allowlist'i ve `model`'i onurlandırılır, gövdesi sistem prompt'una eklenir. `skills`/`mcpServers` frontmatter'ı teammate modda uygulanmaz (skill'ler proje/kullanıcı ayarından yüklenir).

## Kadro

**2026-07-16 itibarıyla tüm takım Claude Fable 5'te** (kullanıcı kararı — maliyet bilinçli kabul edildi). Ajan prompt'ları Fable için de-prescribe edildi: sınırlar + proje kuralları kısıt olarak kaldı, adım-adım tarifler kaldırıldı, kapsam/otonomi/raporlama tarzı blokları eklendi.

| Ajan | Model | Sahiplendiği katman |
|------|-------|---------------------|
| 🧠 **Lider** = ana session | Fable 5 | Görev bölme, delegasyon, entegrasyon, **build/run/restart/commit**, kural denetimi |
| ⚙️ `calibrahub-backend` | Fable 5 | 7 .NET projesi: Domain/App/**Infrastructure**/**Worker**/Persistence(Repos)/**Tests**/Controllers + audit + yetki |
| 🎨 `calibrahub-frontend` | Fable 5 | `.cshtml` (201) + `.jsx` (149) + CSS / C-Grid / sekmeli-form / tema |
| 🗄️ `calibrahub-db` | Fable 5 | SQL / DDL / migration / naming / **kod↔DB senkron denetimi** (en yüksek risk rolü) |
| 🔍 `calibrahub-review` | Fable 5 | Diff inceleme / kural + tema + güvenlik audit (salt-okuma, **kapsam-öncelikli** bulgu raporlar) |
| 📱 `calibrahub-mobile` | Fable 5 | Kotlin/Compose Android app / Retrofit cookie auth / Modül Seçici + Depo + WhatsApp / `/api/mobile/*` sözleşmesi |

**Fable notları (lider için):** ① Zor işlerde tek istek dakikalarca sürebilir — normal, bekle. ② Güvenlik-komşusu bir görevde ajan `refusal` ile durursa görevi **Agent çağrısında `model: "opus"` override'ı ile** yeniden koş (ajan tanımını değiştirme). ③ Görev prompt'unda hedef + kısıt ver, adımları sayma — Fable'da aşırı reçete kaliteyi düşürür.

## Lider protokolü (ana session bunu izler)

1. **Analiz et** → isteği somut işlere böl.
2. **Dosya sahipliğine göre paylaştır** → aynı dosyayı iki uzmana verme (çakışma = üzerine yazma). Katman sınırları: backend `Domain/Application/Infrastructure/Worker/Persistence(Repos)/Tests/Web(Controllers,Models)`, frontend `Views/ClientApp/wwwroot/css`, db `Persistence/Database + şema/migration`, mobil `mobile/CalibraHubAndroid`. **İki koordinasyon dikişi (aynı dosya değil ama anlaşma şart):** ① Persistence + kolon adları → backend↔db; ② `/api/mobile/*` sözleşmesi (`MobileApiController` ↔ `CalibraApi.kt` DTO'ları) → mobil↔backend. **Lokalizasyon** (çok-dilli etiket) kesişen konudur, ayrı ajan yok: plumbing (`UiCatalog`/`UiTextService`/`UiConfigurationService`/`UiLabelTranslation`) backend, tablo db, etiket-key + `Appearance` UI frontend, tutarlılık review.
3. **Delege et:**
   - Subagent modu: bağımsız işler için tek mesajda birden çok `Agent` çağrısı (paralel).
   - Team modu: "`calibrahub-backend` tipinde bir teammate ile şunu yap…" diye spawn et; riskli işte plan onayı iste.
4. **Entegre et** → uzman çıktılarını birleştir.
5. **Derle & çalıştır** → `dotnet build` / `npm run build` + port 61001 restart **yalnız lider** (paralel port/`bin` çakışmasını önlemek için). Uzmanlar server başlatmaz.
6. **Doğrulat** → `calibrahub-review`'a incelet; kritik akışta `verify` skill.
7. **Commit** → curated commit lider işi.

## Kullanım örnekleri

**Subagent (bu ortamda bugün):**
> "Depo transfer ekranına ek alan desteği ekle" → lider backend + frontend + db subagent'larını paralel çağırır, review ile doğrular.

**Agent Team (interaktif `claude` CLI, Rider terminali):**
> "3 teammate spawn et: biri PR #142 güvenlik, biri performans, biri test kapsamı; bulgularını paylaşıp tartışsınlar."

## Ortam notları
- **Flag yeni session'da etkinleşir.** `settings.local.json`'a eklendi; açık takım için Claude Code'u yeniden başlat.
- **Windows + Rider → in-process mod.** Split-pane (tmux/iTerm2) desteklenmez; teammate'ler ana terminalde ajan panelinde listelenir.
- **Deneysel kısıtlar:** in-process teammate'lerde `/resume` yok, görev durumu geç güncellenebilir, kapanış yavaş olabilir, iç içe takım yok (teammate teammate açamaz).
