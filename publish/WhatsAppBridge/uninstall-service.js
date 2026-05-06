/**
 * Windows Service kaldırma scripti.
 * Çalıştırma: node uninstall-service.js
 */

import { Service } from 'node-windows';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const __filename = fileURLToPath(import.meta.url);
const __dirname  = dirname(__filename);

const svc = new Service({
    name: 'CalibraHubWhatsAppBridge',
    script: join(__dirname, 'index.js'),
});

svc.on('uninstall', () => {
    console.log('[uninstall-service] Servis kaldirildi.');
    process.exit(0);
});

svc.on('error', (err) => {
    console.error('[uninstall-service] Hata:', err);
    process.exit(1);
});

console.log('[uninstall-service] Servis kaldiriliyor...');
svc.uninstall();
