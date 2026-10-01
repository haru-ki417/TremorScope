// このファイルを「secrets.h」という名前でコピーし、値を書き換えてください。
// secrets.h は .gitignore に入っているので、Git には登録されません（公開しないこと）。
#pragma once

#define WIFI_SSID      "your-wifi-ssid"
#define WIFI_PASSWORD  "your-wifi-password"

// Azure Portal → IoT Hub → デバイス → このデバイスの「主キー」
// ※ IoT Hub 全体の鍵（iothubowner など）ではなく、デバイスごとの鍵を使います
#define IOT_HUB_HOST   "your-hub.azure-devices.net"
#define IOT_DEVICE_ID  "tremor-01"
#define IOT_DEVICE_KEY "base64-device-primary-key"
