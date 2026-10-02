// TremorScope 用センサー（参考実装）
//   ESP32 + MPU6050（3軸加速度）→ Wi-Fi → Azure IoT Hub（MQTT over TLS）
//
//   ・50Hz で 3 軸の加速度を測り、0.5 秒（25 点）ごとに 1 通のメッセージにまとめて送る
//   ・送る形（TremorScope の「新形式・整数」）:
//       {"v":2,"device":"tremor-01","seq":1200,"fs":50,"scale":0.001,"ax":[...],"ay":[...],"az":[...]}
//     値は mg の整数（× scale で g）。小数で送るより約半分の大きさになり、
//     IoT Hub の Free レベル（0.5KB ごとに 1 メッセージと数える）でも 1 通 = 1 メッセージに収まる。
//     seq は先頭の点の通し番号で、届かなかったメッセージがあってもアプリ側で「何点欠けたか」を数えられる。
//   ・測るのは専用のタスク（コア 1）、送るのは loop（コア 0 側の Wi-Fi と同じ）に分けて、
//     通信が遅れても測る間隔がずれないようにしている。
//
// 必要なもの: Arduino IDE + 「esp32 by Espressif」ボード、ライブラリ「PubSubClient」
// 配線: MPU6050 VCC→3V3, GND→GND, SDA→GPIO21, SCL→GPIO22
//
// 医療機器ではありません。測定の補助として使う試作です。

#include <WiFi.h>
#include <WiFiClientSecure.h>
#include <PubSubClient.h>
#include <Wire.h>
#include <time.h>
#include <mbedtls/md.h>
#include <mbedtls/base64.h>
#include "secrets.h"

// ---- 設定
static const int SAMPLE_RATE_HZ = 50;
static const int SAMPLES_PER_MESSAGE = 25;          // 0.5 秒ぶん
static const uint32_t SAS_LIFETIME_SEC = 60 * 60;   // 接続用トークンの有効期間（1 時間）
static const uint8_t MPU_ADDR = 0x68;

// Azure IoT Hub のサーバー証明書を確かめるためのルート証明書（DigiCert Global Root G2）
static const char* ROOT_CA =
"-----BEGIN CERTIFICATE-----\n" \
"MIIDjjCCAnagAwIBAgIQAzrx5qcRqaC7KGSxHQn65TANBgkqhkiG9w0BAQsFADBh\n" \
"MQswCQYDVQQGEwJVUzEVMBMGA1UEChMMRGlnaUNlcnQgSW5jMRkwFwYDVQQLExB3\n" \
"d3cuZGlnaWNlcnQuY29tMSAwHgYDVQQDExdEaWdpQ2VydCBHbG9iYWwgUm9vdCBH\n" \
"MjAeFw0xMzA4MDExMjAwMDBaFw0zODAxMTUxMjAwMDBaMGExCzAJBgNVBAYTAlVT\n" \
"MRUwEwYDVQQKEwxEaWdpQ2VydCBJbmMxGTAXBgNVBAsTEHd3dy5kaWdpY2VydC5j\n" \
"b20xIDAeBgNVBAMTF0RpZ2lDZXJ0IEdsb2JhbCBSb290IEcyMIIBIjANBgkqhkiG\n" \
"9w0BAQEFAAOCAQ8AMIIBCgKCAQEAuzfNNNx7a8myaJCtSnX/RrohCgiN9RlUyfuI\n" \
"2/Ou8jqJkTx65qsGGmvPrC3oXgkkRLpimn7Wo6h+4FR1IAWsULecYxpsMNzaHxmx\n" \
"1x7e/dfgy5SDN67sH0NO3Xss0r0upS/kqbitOtSZpLYl6ZtrAGCSYP9PIUkY92eQ\n" \
"q2EGnI/yuum06ZIya7XzV+hdG82MHauVBJVJ8zUtluNJbd134/tJS7SsVQepj5Wz\n" \
"tCO7TG1F8PapspUwtP1MVYwnSlcUfIKdzXOS0xZKBgyMUNGPHgm+F6HmIcr9g+UQ\n" \
"vIOlCsRnKPZzFBQ9RnbDhxSJITRNrw9FDKZJobq7nMWxM4MphQIDAQABo0IwQDAP\n" \
"BgNVHRMBAf8EBTADAQH/MA4GA1UdDwEB/wQEAwIBhjAdBgNVHQ4EFgQUTiJUIBiV\n" \
"5uNu5g/6+rkS7QYXjzkwDQYJKoZIhvcNAQELBQADggEBAGBnKJRvDkhj6zHd6mcY\n" \
"1Yl9PMWLSn/pvtsrF9+wX3N3KjITOYFnQoQj8kVnNeyIv/iPsGEMNKSuIEyExtv4\n" \
"NeF22d+mQrvHRAiGfzZ0JFrabA0UWTW98kndth/Jsw1HKj2ZL7tcu7XUIOGZX1NG\n" \
"Fdtom/DzMNU+MeKNhJ7jitralj41E6Vf8PlwUHBHQRFXGU7Aj64GxJUTFy8bJZ91\n" \
"8rGOmaFvE7FBcf6IKshPECBV1/MUReXgRPTqh5Uykw7+U0b6LJ3/iyK5S9kJRaTe\n" \
"pLiaWN0bfVKfjllDiIGknibVb63dDcY3fe0Dkhvld1927jyNxF1WW6LZZm6zNTfl\n" \
"MrY=\n" \
"-----END CERTIFICATE-----\n" \
;

// ---- 測った値を送る側に渡すための入れ物
struct Block {
  uint64_t seq;
  float ax[SAMPLES_PER_MESSAGE];
  float ay[SAMPLES_PER_MESSAGE];
  float az[SAMPLES_PER_MESSAGE];
};
static QueueHandle_t blockQueue;

WiFiClientSecure tls;
PubSubClient mqtt(tls);
static time_t sasExpiry = 0;
static char mqttUser[160];
static char mqttPassword[400];
static char topic[96];

// ---------------------------------------------------------------- MPU6050

static void mpuWrite(uint8_t reg, uint8_t value) {
  Wire.beginTransmission(MPU_ADDR);
  Wire.write(reg);
  Wire.write(value);
  Wire.endTransmission();
}

static bool mpuBegin() {
  Wire.begin();
  Wire.setClock(400000);
  mpuWrite(0x6B, 0x01);  // スリープ解除、ジャイロの時計を使う
  delay(50);
  mpuWrite(0x1A, 0x04);  // 内蔵の低域フィルタ 約 21Hz（50Hz で測るときの折り返しを防ぐ）
  mpuWrite(0x1C, 0x00);  // 加速度の範囲 ±2g（16384 LSB/g）
  Wire.beginTransmission(MPU_ADDR);
  Wire.write(0x75);      // WHO_AM_I
  Wire.endTransmission(false);
  Wire.requestFrom(MPU_ADDR, (uint8_t)1);
  return Wire.available() && Wire.read() == 0x68;
}

static bool mpuRead(float& x, float& y, float& z) {
  Wire.beginTransmission(MPU_ADDR);
  Wire.write(0x3B);
  if (Wire.endTransmission(false) != 0) return false;
  if (Wire.requestFrom(MPU_ADDR, (uint8_t)6) != 6) return false;
  int16_t rx = (Wire.read() << 8) | Wire.read();
  int16_t ry = (Wire.read() << 8) | Wire.read();
  int16_t rz = (Wire.read() << 8) | Wire.read();
  x = rx / 16384.0f;
  y = ry / 16384.0f;
  z = rz / 16384.0f;
  return true;
}

// 20ms ごとに 1 点測る。25 点たまったら送る側へ渡す（送る側が詰まっていたら捨てる → アプリが欠けとして数える）
static void samplingTask(void*) {
  Block block{};
  uint64_t seq = 0;
  int n = 0;
  float px = 0, py = 0, pz = 1;  // 読み取りに失敗したときは直前の値を使う（0 を入れると大きな段差になるため）
  TickType_t last = xTaskGetTickCount();
  const TickType_t period = pdMS_TO_TICKS(1000 / SAMPLE_RATE_HZ);
  for (;;) {
    vTaskDelayUntil(&last, period);
    float x, y, z;
    if (mpuRead(x, y, z)) { px = x; py = y; pz = z; } else { x = px; y = py; z = pz; }
    if (n == 0) block.seq = seq;
    block.ax[n] = x; block.ay[n] = y; block.az[n] = z;
    seq++;
    if (++n == SAMPLES_PER_MESSAGE) {
      xQueueSend(blockQueue, &block, 0);
      n = 0;
    }
  }
}

// ---------------------------------------------------------------- Azure IoT Hub

static String urlEncode(const char* s) {
  String out;
  const char* hex = "0123456789ABCDEF";
  for (; *s; s++) {
    char c = *s;
    if (isalnum((unsigned char)c) || c == '-' || c == '_' || c == '.' || c == '~') out += c;
    else { out += '%'; out += hex[(c >> 4) & 0xF]; out += hex[c & 0xF]; }
  }
  return out;
}

// デバイスの鍵から、期限つきの接続用トークン（SAS）を作る
static bool makeSasToken() {
  time_t now = time(nullptr);
  if (now < 1700000000) return false;  // 時刻がまだ合っていない
  sasExpiry = now + SAS_LIFETIME_SEC;

  String resource = String(IOT_HUB_HOST) + "/devices/" + IOT_DEVICE_ID;
  String encodedResource = urlEncode(resource.c_str());
  String toSign = encodedResource + "\n" + String((unsigned long)sasExpiry);

  unsigned char key[64]; size_t keyLen = 0;
  if (mbedtls_base64_decode(key, sizeof key, &keyLen, (const unsigned char*)IOT_DEVICE_KEY, strlen(IOT_DEVICE_KEY)) != 0) return false;

  unsigned char hmac[32];
  const mbedtls_md_info_t* info = mbedtls_md_info_from_type(MBEDTLS_MD_SHA256);
  if (mbedtls_md_hmac(info, key, keyLen, (const unsigned char*)toSign.c_str(), toSign.length(), hmac) != 0) return false;
  memset(key, 0, sizeof key);

  unsigned char sig[64]; size_t sigLen = 0;
  if (mbedtls_base64_encode(sig, sizeof sig - 1, &sigLen, hmac, sizeof hmac) != 0) return false;
  sig[sigLen] = 0;

  snprintf(mqttUser, sizeof mqttUser, "%s/%s/?api-version=2021-04-12", IOT_HUB_HOST, IOT_DEVICE_ID);
  snprintf(mqttPassword, sizeof mqttPassword, "SharedAccessSignature sr=%s&sig=%s&se=%lu",
           encodedResource.c_str(), urlEncode((const char*)sig).c_str(), (unsigned long)sasExpiry);
  return true;
}

static void ensureConnected() {
  if (WiFi.status() != WL_CONNECTED) {
    Serial.println("Wi-Fi に再接続します");
    WiFi.disconnect();
    WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
    for (int i = 0; i < 40 && WiFi.status() != WL_CONNECTED; i++) delay(250);
    if (WiFi.status() != WL_CONNECTED) return;
  }
  // トークンの期限が近ければ作り直して、つなぎ直す
  if (mqtt.connected() && time(nullptr) < sasExpiry - 300) return;
  mqtt.disconnect();
  if (!makeSasToken()) { Serial.println("接続用トークンを作れません（時刻か鍵を確認）"); delay(1000); return; }
  if (mqtt.connect(IOT_DEVICE_ID, mqttUser, mqttPassword)) Serial.println("IoT Hub に接続しました");
  else { Serial.printf("IoT Hub に接続できません（状態 %d）\n", mqtt.state()); delay(2000); }
}

static void appendArray(String& s, const char* name, const float* v) {
  s += ",\""; s += name; s += "\":[";
  for (int i = 0; i < SAMPLES_PER_MESSAGE; i++) {
    if (i) s += ',';
    s += String((long)lroundf(v[i] * 1000.0f));  // mg の整数
  }
  s += ']';
}

// ---------------------------------------------------------------- setup / loop

void setup() {
  Serial.begin(115200);
  delay(200);
  if (!mpuBegin()) {
    Serial.println("MPU6050 が見つかりません。配線を確認してください");
  }

  WiFi.mode(WIFI_STA);
  WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
  configTime(0, 0, "pool.ntp.org", "time.google.com");  // トークンの期限に正しい時刻が必要

  tls.setCACert(ROOT_CA);
  mqtt.setServer(IOT_HUB_HOST, 8883);
  mqtt.setBufferSize(2048);
  mqtt.setKeepAlive(60);
  snprintf(topic, sizeof topic, "devices/%s/messages/events/", IOT_DEVICE_ID);

  blockQueue = xQueueCreate(8, sizeof(Block));  // 4 秒ぶんまでためられる
  xTaskCreatePinnedToCore(samplingTask, "sampling", 4096, nullptr, 3, nullptr, 1);
}

void loop() {
  ensureConnected();
  mqtt.loop();

  Block block;
  while (mqtt.connected() && xQueueReceive(blockQueue, &block, pdMS_TO_TICKS(50)) == pdTRUE) {
    String json;
    json.reserve(600);
    json += "{\"v\":2,\"device\":\"" IOT_DEVICE_ID "\",\"seq\":";
    json += String((unsigned long long)block.seq);
    json += ",\"fs\":";
    json += SAMPLE_RATE_HZ;
    json += ",\"scale\":0.001";
    appendArray(json, "ax", block.ax);
    appendArray(json, "ay", block.ay);
    appendArray(json, "az", block.az);
    json += '}';
    if (!mqtt.publish(topic, json.c_str())) {
      Serial.println("送信できませんでした");
      break;
    }
  }
}
