# 導入の手順

センサーがなくても、手順 1 だけで操作を試せます（模擬センサー）。

## 1. アプリを起動する

- 配布版: [Releases](../../releases) の `TremorScope-x.y.z-win-x64.zip` を展開し、`TremorScope.exe` を起動します（.NET のインストールは不要）。
- 開発版: .NET 10 SDK を入れて `dotnet run --project src/TremorScope.App`。

初回起動で `%LOCALAPPDATA%\TremorScope` に次のファイルができます。

| ファイル | 中身 |
| --- | --- |
| `tremorscope.db` | 患者・測定（SQLite） |
| `settings.json` | 秘密ではない設定 |
| `secrets.bin` | 接続文字列・キー・施設の鍵（DPAPI で暗号化） |

## 2. センサーを作る（ESP32 + MPU6050）

1. 配線: MPU6050 の VCC→3V3、GND→GND、SDA→GPIO21、SCL→GPIO22。
2. Arduino IDE に「esp32 by Espressif」ボードと「PubSubClient」ライブラリを入れます。
3. `firmware/TremorSensor/secrets.example.h` を `secrets.h` にコピーし、Wi-Fi とデバイスの情報を書きます（このファイルは Git に入りません）。
4. 書き込んでシリアルモニター（115200）に「IoT Hub に接続しました」と出れば完了です。

センサーは手の甲（中手骨のあたり）にテープやバンドで固定します。向きはどちらでも構いません（3 軸を合成して解析するため）。

## 3. Azure IoT Hub を用意する

1. IoT Hub を作ります。試すだけなら Free レベル（1 日 8,000 メッセージ、0.5KB ごとに 1 メッセージと数える）で足ります。
   センサーは 0.5 秒ごとに約 0.4KB を送るので、**電源を入れている時間が 1 日あたり約 66 分まで**です。測定するときだけ電源を入れてください。
   毎日たくさん測る施設では S1 レベル（1 日 40 万メッセージ）にします。
2. 「デバイス」でデバイスを追加し（例: `tremor-01`）、**そのデバイスの主キー**を `secrets.h` に書きます。
3. アプリの設定画面に入れる接続文字列は、「組み込みのエンドポイント」の **イベント ハブ互換エンドポイント**です。
   共有アクセス ポリシーは **`service`** を選んでください（受信だけの権限）。`iothubowner` はすべての権限を持つため使いません。
4. 複数の PC で同じ IoT Hub を使う場合は、PC ごとに「コンシューマー グループ」を作って設定します。

## 4. Azure Cosmos DB（任意）

1. Cosmos DB（NoSQL）を作ります。無料枠（1,000 RU/s・25 GB）で足ります。
2. 「キー」からエンドポイントと主キーを設定画面に入れ、「接続を確認」を押します。データベース・コンテナーがなければ自動で作ります（パーティション キー `/pseudonymId`）。
3. 送るのは仮名 ID と測定値だけです。患者を削除すると、クラウドの同じ仮名 ID の記録も削除します。

## 5. PACS（任意）

1. 設定画面で PACS のホスト・ポート・AE タイトルを入れ、「接続を確認（C-ECHO）」を押します。
2. 患者 ID の入れ方を選びます。院外の PACS や研究用なら「仮名 ID」、院内の PACS で患者にひも付けるなら「カルテ番号」。
3. 結果画面の「PACS へ送信」で、レポートを DICOM 画像（Secondary Capture）として送ります。

試すだけなら、無料の PACS「[Orthanc](https://www.orthanc-server.com/)」を同じ PC で動かすと簡単です（AE タイトル `ORTHANC`、ポート 4242）。
