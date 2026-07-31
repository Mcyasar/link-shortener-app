import http from 'k6/http';
import { check, sleep } from 'k6';

// 1. TEST YAPILANDIRMASI (Ramping Load Test)
export const options = {
  discardResponseBodies: true, // Yanıt gövdelerini saklama, soket yükünü hafifletir
  stages: [
    { duration: '30s', target: 50 },   // 30 saniyede 50 kullanıcıya çık (Ramp-up)
    { duration: '1m',  target: 200 },  // 1 dakika boyunca 500 anlık kullanıcıyla yükle
    { duration: '2m',  target: 500 }, // 2 dakika boyunca 1000 anlık kullanıcıya kadar zorla (Peak)
    { duration: '30s', target: 0 },    // 30 saniyede yükü sıfırla (Ramp-down)
  ],
  thresholds: {
    // Performans Kriterlerimiz (SLA):
    http_req_duration: ['p(95)<100'], // İsteklerin %95'i 100 ms'nin altında yanıt vermeli!
    http_req_failed: ['rate<0.01'],   // Hata oranı %1'in altında olmalı!
  },
};

const BASE_URL = 'http://linkshortener.local'; // Veya Ingress adresiniz

export default function () {
  // Test edilecek örnek kısa kod
  const shortCode = 'EZ1t88V';

  const params = {
    redirects: 0, // 302 Redirect yanıtını doğrudan yakalamak için takibi kapatıyoruz
    timeout: '5s', // 5 saniye içinde yanıt gelmezse soketi açık bırakma
  };

  // HTTP GET İstegi (Yönlendirme / Tıklama Senaryosu)
  const res = http.get(`${BASE_URL}/api/links/${shortCode}`, params);

  // BAŞARI KONTROLLERİ (Assertions)
  check(res, {
    'Status code is 302 or 200': (r) => r.status === 302 || r.status === 200,
    'Response time < 100ms': (r) => r.timings.duration < 100,
  });

  // Gerçekçi kullanıcı davranışı simülasyonu (0.1sn bekleme)
  sleep(0.1);
}