# Handbrake Mapper

USBハンドブレーキの入力を整理して、レースシムで使いやすくするWindows用ツールです。

特に、**レバーを引くと「アナログ軸」と「ボタン」が同時に入力されるタイプのハンドブレーキ**で、ゲーム側が正しく認識しない場合に役立ちます。

Handbrake Mapperでは、**軸入力とボタン入力をそれぞれ個別にON / OFFできます**。

たとえば次のような場合に使用できます。

- ハンドブレーキのアナログ入力は正常に使える
- しかし同時に送られるボタン入力のせいで、ゲーム内ではON / OFFを繰り返すなどの誤動作が起きる
- ボタン入力だけを無効にして、アナログ軸だけを使いたい

`軸入力` と `ボタン入力` は独立して設定できます。

- 軸入力 ON / ボタン入力 OFF → アナログ軸だけを出力
- 軸入力 ON / ボタン入力 ON → アナログ軸とボタンを両方出力
- 軸入力 OFF / ボタン入力 ON → ボタンだけを出力
- 軸入力 OFF / ボタン入力 OFF → 入力がないためSTARTできません

主に Assetto Corsa Rally のように、Windows上では正常なのにゲーム側だけ物理ハンドブレーキのHID入力を正しく扱えないケースの回避を目的としています。

ゲーム固有の処理は入れていないため、ほかのレースシムでも使用できます。

## 主な機能

- USBハンドブレーキを一覧から選択
- 実際のデバイス名を表示
- ハンドブレーキを動かすだけで対象デバイスと使用する軸を自動設定
- 0% / 100% キャリブレーション
- デッドゾーン設定
- 軸反転
- 軸入力のON / OFF
- ボタン入力のON / OFF
- vJoyへ仮想コントローラーとして出力
- Windowsのゲームコントローラー一覧では `HandbrakeMapper(vJoy Device)` と表示
- HidHideで元の物理ハンドブレーキをゲームから隠す
- vJoy自体は入力デバイス一覧から除外

## 仕組み

```text
Physical USB Handbrake
        |
        v
Handbrake Mapper
  - Device select
  - Auto device / axis detection
  - Min / Max calibration
  - Deadzone
  - Invert
  - Axis input ON / OFF
  - Button input ON / OFF
        |
        v
      vJoy
        |
        v
   Racing Sim

Physical handbrake -- HidHide --> hidden from games
HandbrakeMapper.exe ----------> can still read the device
```

## 使い方

1. `デバイスを動かして自動設定` を押し、2秒以内にハンドブレーキを大きく引きます。使用するデバイスとAxisが自動で選択されます。
2. 自動設定を使わない場合は、`Device` と `Axis` を手動で選択します。
3. レバーを完全に離した状態で `現在値を 0%` を押します。
4. レバーを最大まで引いた状態で `現在値を 100%` を押します。
5. 必要に応じて `Deadzone` や `軸を反転` を設定します。
6. `軸入力` と `ボタン入力` から、ゲームへ送りたい入力方式を選びます。
7. `元の物理デバイスをHidHideで隠す` をONにして `START` を押します。
8. レースシム側のハンドブレーキ設定で、`HandbrakeMapper(vJoy Device)` の入力を割り当てます。

HidHideの設定を初めて変更するときは、WindowsのUAC確認が表示されます。

ゲームから元の物理ハンドブレーキがまだ見えている場合は、USBを一度抜き差ししてください。

## 確認済みハンドブレーキ

### RT31

- 軸入力 + ボタン入力を同時に出力するタイプ
- 軸のみ、軸 + ボタン、ボタンのみの各モードで使用可能

ほかの一般的なUSBゲームコントローラー形式のハンドブレーキでも使用できるように設計しています。

## 必要環境

- Windows 10 / 11 x64
- vJoy 2.2.2.0
- HidHide 1.5.230

配布用インストーラーには、vJoyとHidHideのインストール処理も含まれています。

## ビルド（Windows）

### アプリのみ

```powershell
dotnet publish .\src\HandbrakeMapper\HandbrakeMapper.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -o .\dist\app
```

### インストーラーまで

Inno Setup 6をインストールしてから:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

ビルドすると `HandbrakeMapper_Setup_0.1.7.exe` が生成されます。

## HidHideの緊急解除（v0.1.7）

誤ってハンドルコントローラーなどを非表示にした場合、Handbrake Mapperの「HidHideの非表示を緊急解除」を押してください。デバイスが入力一覧から消えていても操作できます。確認後、管理者権限の確認（UAC）に応じると、このアプリが記録した非表示パスを解除します。必要ならUSBを再接続して「再検索」を押します。

- v0.1.6では「HidHide登録一覧から個別解除」を追加しました。HidHideの実際の非表示登録（`--dev-list`）から対象を選び、他のデバイスを変更せずに解除します。既存のsettings.jsonに記録がない場合にも対応できます。表示名が不明な場合はインスタンスパスを確認してから解除してください。\n- この操作はHidHide全体の有効・無効を変更しません。`Enable device hiding` がOFFのままであれば、必要なデバイスの登録解除後に自分でONに戻してください。\n- START時は対象デバイス名を確認してから実行します。
- START失敗時は今回設定した非表示対象の自動解除を試みます。解除に失敗した場合も復旧記録は保持します。
- 記録は `%LOCALAPPDATA%\\HandbrakeMapper\\settings.json` の `HidHidePaths` と `HidHidePendingPaths` に保存されます。誤って削除しないでください。
- v0.1.4以前で設定済みの `HidHidePaths` も解除対象になります。ただし、設定ファイルを削除していた場合などアプリに記録がない非表示対象はHidHide Configuration Clientで解除してください。
- 他アプリのHidHide登録を一括解除する操作は実装していません。

### HidHideのアクセス拒否 (0x0005) とUACの対策（v0.1.7）

- **HidHide Configuration Clientを完全に終了**してからHandbrakeMapperを使用してください。HidHideの制御デバイスは複数の管理クライアントによる同時アクセスを受け付けない場合があります。管理者権限だけでは解消できません。
- START前と個別解除・緊急解除の処理前にCLIで接続確認を行い、アクセス拒否ならUACの前に停止して具体的な操作を案内します。
- 非表示処理で失敗した場合は、同一の管理者PowerShell内で今回追加した非表示登録の解除を試み、2回目のUAC表示を抑止します。失敗時の復旧記録は保持します。
- HidHideを操作するほかの常駐ソフトがある場合もアクセス拒否の原因になり得ます。不要な管理画面を閉じて再実行してください。

## 注意

- vJoy Device 1を別のアプリが使用中の場合はSTARTできないことがあります。
- HidHide全体の設定をOFFにはせず、Handbrake Mapperが追加した対象デバイスだけを管理します。
- 現在はハンドブレーキ1台、軸1本、任意のボタン入力1個を対象としています。
- `joy.cpl` に表示される一般的なUSBゲームコントローラーを対象にしています。

## ライセンス

Handbrake Mapperは [MIT License](LICENSE) で公開しています。

vJoyおよびHidHideはそれぞれのライセンスに従います。詳細は `licenses/THIRD_PARTY_NOTICES.txt` を参照してください。

---

# English

## About Handbrake Mapper

Handbrake Mapper is a small Windows utility that makes USB handbrake input easier to use with racing simulators.

It is especially useful for handbrakes that send **an analog axis and a button at the same time** when the lever is pulled.

Some racing games may misinterpret this combination even though the handbrake works normally in Windows. This can cause symptoms such as the handbrake rapidly switching ON and OFF by itself.

Handbrake Mapper lets you independently enable or disable the analog axis input and the button input.

For example:

- The analog handbrake axis works correctly.
- The handbrake also sends a button signal at the same time.
- That extra button signal causes incorrect behavior in a game.
- You want to remove only the button signal and keep the analog handbrake.

**Axis Input** and **Button Input** can be configured independently.

- Axis ON / Button OFF → analog axis only
- Axis ON / Button ON → analog axis + button
- Axis OFF / Button ON → button only
- Axis OFF / Button OFF → START is blocked because no input is enabled

The original motivation for this project was to work around handbrake input problems in games such as Assetto Corsa Rally, where the device may work correctly in Windows but be interpreted incorrectly by the game.

There is no game-specific code, so Handbrake Mapper can also be used with other racing simulators.

## Features

- Select a USB handbrake from a device list
- Display actual device names
- Automatically select both the handbrake device and active axis by moving the lever
- 0% / 100% calibration
- Deadzone adjustment
- Axis inversion
- Axis input ON / OFF
- Button input ON / OFF
- Output through a virtual vJoy controller
- Shown as `HandbrakeMapper(vJoy Device)` in Windows Game Controllers
- Hide the original physical handbrake from games with HidHide
- Exclude vJoy itself from the input-device list

## How it works

```text
Physical USB Handbrake
        |
        v
Handbrake Mapper
  - Device select
  - Auto device / axis detection
  - Min / Max calibration
  - Deadzone
  - Invert
  - Button input ON / OFF
        |
        v
      vJoy
        |
        v
   Racing Sim

Physical handbrake -- HidHide --> hidden from games
HandbrakeMapper.exe ----------> can still read the device
```

## How to use

1. Click `デバイスを動かして自動設定` / Auto Configure by Moving Device, then pull the handbrake firmly within two seconds. The device and active axis are selected automatically.
2. If you prefer, you can select `Device` and `Axis` manually.
3. Fully release the lever and click `現在値を 0%` / Set Current Value to 0%.
4. Pull the lever fully and click `現在値を 100%` / Set Current Value to 100%.
5. Adjust `Deadzone` or `Invert Axis` if necessary.
6. Choose the input mode you want to send to the game with `Axis Input` and `Button Input`.
7. Enable `Hide physical device with HidHide` and click `START`.
8. In your racing simulator's handbrake settings, assign the input from `HandbrakeMapper(vJoy Device)`.

Windows may display a UAC prompt the first time HidHide settings are changed.

If the original physical handbrake is still visible to the game, disconnect and reconnect its USB cable once.

## Tested handbrakes

### RT31

- Sends both analog axis and button input
- Supports axis-only, axis + button, and button-only output modes

The application is designed to work with other standard USB game-controller-style handbrakes as well.

## Requirements

- Windows 10 / 11 x64
- vJoy 2.2.2.0
- HidHide 1.5.230

The bundled installer can install vJoy and HidHide together with Handbrake Mapper.

## Build on Windows

### Application only

```powershell
dotnet publish .\src\HandbrakeMapper\HandbrakeMapper.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -o .\dist\app
```

### Build the installer

Install Inno Setup 6 first, then run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

This generates `HandbrakeMapper_Setup_0.1.6.exe`.

## Notes

- START may fail if vJoy Device 1 is already being used by another application.
- Handbrake Mapper does not disable HidHide globally. It manages only the physical device entries added by this application.
- The current version supports one handbrake, one analog axis, and one optional button input.
- Standard USB game controllers visible in `joy.cpl` are the primary target.

## License

Handbrake Mapper is released under the [MIT License](LICENSE).

vJoy and HidHide are distributed under their respective licenses. See `licenses/THIRD_PARTY_NOTICES.txt` for details.
