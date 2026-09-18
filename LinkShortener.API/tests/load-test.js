import http from 'k6/http';
import { check, sleep } from 'k6';

// 1. TEST YAPILANDIRMASI (Ramping Load Test)
export const options = {
  redirects: 0,
  noConnectionReuse: false,
  stages: [
    { duration: '2s', target: 10 },  // 2 saniyede 10 VU'ya çık (Socket pool ısınsın)
    { duration: '1m', target: 100 }, // 1 dakika sabit kal
    { duration: '1m', target: 200 }, // 1 dakika sabit kal
    { duration: '1m', target: 500 }, // 1 dakika sabit kal
    { duration: '3m', target: 0 },
  ],
  discardResponseBodies: true, // Yanıt gövdelerini saklama, soket yükünü hafifletir
  // CI/CD kapısı: Bu eşikler aşılırsa k6 non-zero exit code döner, pipeline durur
  thresholds: {
    http_req_failed: ['rate<0.01'],      // Hata oranı %1'in altında olmalı
    http_req_duration: ['p(95)<150'],    // İsteklerin %95'i 150ms altında yanıt vermeli
  },
};

const BASE_URL = __ENV.TARGET_BASE_URL || 'http://linkshortener.test';
const SHORT_CODE = __ENV.TEST_SHORT_CODE || 'iZr6N9c';
const HOST_HEADER = __ENV.TARGET_HOST_HEADER || 'linkshortener.test';

export default function () {
  const params = {
    redirects: 0,
    timeout: '30s',
    headers: {},
  };

  // Eğer Ingress IP'sine gidiliyorsa Host header'ı ekle
  if (HOST_HEADER) {
    params.headers['Host'] = HOST_HEADER;
  }

  const res = http.get(`${BASE_URL}/api/links/${SHORT_CODE}`, params);

  if (res.error_code !== 0 || res.status === 0) {
    console.log(`❌ HATA ALINDI! 
      VU: ${__VU} | Iteration: ${__ITER}
      Error Code: ${res.error_code}
      Error Msg : ${res.error}
      Status    : ${res.status}
      Duration  : ${res.timings.duration} ms`);
  }

  check(res, {
    'Status code is 302 or 200': (r) => r.status === 302 || r.status === 200,
    'Response time < 100ms': (r) => r.timings.duration < 100,
  });

  sleep(0.1);
}