# Handbrake Mapper

ハンドブレーキ専用の最小構成マッパーです。物理USBハンドブレーキの **1本のアナログ軸**（必要なら1個のボタン）だけを vJoy に渡し、HidHide で元デバイスをゲームから隠せます。

主用途は Assetto Corsa Rally のように「Windowsでは正常なのにゲーム側だけ物理ハンドブレーキのHID入力を誤解釈する」ケースの回避ですが、ゲーム固有コードは入れていないため他のレースシムでも利用できます。

## 構成

```text
Physical USB Handbrake
        |
        v
Handbrake Mapper
  - Device select
  - Auto axis detect
  - Min/Max calibration
  - Deadzone
  - Invert
  - Optional Button 1 pass-through
        |
        v
vJoy Device 1 / X Axis
        |
        v
Any racing sim

Physical device -- HidHide --> hidden from games
HandbrakeMapper.exe ----------> whitelisted by HidHide
```

## 操作

1. Device で物理ハンドブレーキを選択。
2. `軸を自動検出` を押して2秒以内にレバーを大きく引く、またはAxisを手動選択。
3. レバーを離した状態で `現在値を 0%`。
4. 最大まで引いた状態で `現在値を 100%`。軸の生値が逆向きでもそのまま校正できます。
5. 必要なら Deadzone / 軸反転を設定。
6. SRT-HB-5のように不要なボタン信号を捨てたい場合は `ボタン入力も出力する` をOFFのままにする。
7. `元の物理デバイスをHidHideで隠す` をON。
8. START。
9. ゲームでは `vJoy Device` の `X Axis` をハンドブレーキに割り当てる。

HidHide設定を初めて変更するときだけUACが表示されます。アプリは `pnputil /restart-device` で再認識を試みますが、ゲームから元デバイスがまだ見える場合はUSBを一度抜き差ししてください。

## 必要環境

- Windows 10/11 x64
- vJoy 2.2.2.0 (BrunnerInnovation signed build)
- HidHide 1.5.230

配布用インストーラーは上記2つの公式署名済みセットアップを同梱する前提です。

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

スクリプトは公式GitHubリリースから vJoy/HidHide のセットアップを取得し、アプリをpublishして、最後に `HandbrakeMapper_Setup_0.1.0.exe` を生成します。

## 注意

- vJoy Device 1を別アプリが利用中の場合はSTARTできません。
- 既存のHidHide設定全体をOFFにはしません。このアプリが追加したデバイスパスだけを追加/解除します。
- 初期版はハンドブレーキ1台、軸1本、任意ボタン1個のみです。
- Windowsの古いWinMMジョイスティックAPIで認識されない特殊デバイスは対象外です。`joy.cpl` に表示される一般的なUSBゲームコントローラーを対象にしています。
