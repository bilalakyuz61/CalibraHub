/**
 * Windows Service kayıt scripti — node-windows üzerinden.
 * Çalıştırma: node install-service.js
 *
 * Servis adı: CalibraHubWhatsAppBridge
 * Otomatik başlama: evet
 * Çökme durumunda yeniden başlatma: 3 kere, 5 sn arayla
 *
 * Setup wizard tarafından elevated context'te çağrılır.
 */

import { Service } from 'node-windows';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const __filename = fileURLToPath(import.meta.url);
const __dirname  = dirname(__filename);

const svc = new Service({
    name: 'CalibraHubWhatsAppBridge',
    description: 'CalibraHub WhatsApp Bridge — telefon WhatsApp Web ile köprü kurar, CalibraHub.Web HTTP üzerinden mesaj gönderir.',
    script: join(__dirname, 'index.js'),
    nodeOptions: [],
    workingDirectory: __dirname,

    // Cokme durumunda 3 yeniden baslatma denemesi
    grow: 0.25,
    wait: 2,
    maxRestarts: 3,
});

svc.on('install', () => {
    console.log('[install-service] Servis basariyla kuruldu.');
    svc.start();
});

svc.on('alreadyinstalled', () => {
    console.log('[install-service] Servis zaten kayitli — once kaldirip tekrar kurun (uninstall-service.js).');
});

svc.on('start', () => {
    console.log('[install-service] Servis basladi.');
    process.exit(0);
});

svc.on('error', (err) => {
    console.error('[install-service] Hata:', err);
    process.exit(1);
});

console.log('[install-service] CalibraHubWhatsAppBridge servisi kuruluyor...');
svc.install();
